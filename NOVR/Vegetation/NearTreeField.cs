using System;
using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Effects;
using UnityEngine;
using UnityEngine.Rendering;

namespace NOVR.Vegetation;

/// <summary>
/// One <see cref="TreeRenderer"/>'s worth of near trees: which clumps are close
/// enough to replace, hiding those from the game's culler, and drawing the
/// replacements.
///
/// <para><b>Why hiding is possible at all.</b> The game's cull is a compute
/// shader with an outer radius and no inner one, so a second renderer added
/// alongside it would draw every near tree twice. Rather than ship a compute
/// shader of our own — this mod ships one PNG and no shaders, and a
/// <c>.compute</c> would need an AssetBundle built in a matching editor — the
/// near clumps are removed from the game's <i>input</i>.
/// <c>TreeRenderer.CommandSetup</c> fills <c>treePositionBuffer</c> exactly once,
/// from the position TextAsset, and nothing ever refills it; so writing a
/// position far below the map over the handful of entries that entered the near
/// set makes the game's own distance test reject them, and writing the original
/// back puts them where they were. Each write is twelve bytes at a known index,
/// not a re-upload of the 6.6 MB array.</para>
///
/// <para><b>The one correctness trap</b> is that the game writes to that buffer
/// too: <c>BlastClearPositions</c> zeroes every tree within a blast, and
/// restoring a position it has since zeroed would resurrect a destroyed forest.
/// <see cref="NoteBlast"/> keeps the same set on the CPU from a prefix on
/// <c>ClearTrees</c> and simply never restores what a blast took — cheaper and
/// stall-free compared with reading the buffer back.</para>
///
/// <para>Positions in that buffer are global map coordinates, not scene
/// coordinates: the game runs a floating origin that moves every root object
/// whenever the camera drifts past 1024 m. Scene position = global +
/// <c>Datum.originPosition</c>, which is the conversion the game's own
/// <c>ToLocalPosition</c> uses, so the instance matrices are rebuilt whenever
/// that origin moves.</para>
/// </summary>
internal sealed class NearTreeField
{
    private const byte FlagHidden = 1;
    private const byte FlagCleared = 2;
    private const byte FlagNear = 4;

    /// <summary>
    /// Where a hidden clump is parked. Far enough below the map that the game's
    /// distance test rejects it whatever the player's tree-range multiplier is
    /// (the widest is 8 km), and its 100 m frustum sphere cannot reach back.
    /// </summary>
    private static readonly Vector3 Sentinel = new Vector3(0f, -100000f, 0f);

    /// <summary>512 m: about 24 clumps per cell on the shipped maps, so a 300 m
    /// query touches nine cells and tests a couple of hundred candidates.</summary>
    private const float CellSize = 512f;

    /// <summary>Instances per draw call. <c>RenderMeshInstanced</c> does not
    /// document a limit the way <c>DrawMeshInstanced</c> does, so this stays at
    /// the familiar one rather than trusting an absence.</summary>
    private const int MaxPerCall = 1023;

    private readonly GraphicsBuffer _positionBuffer;
    private readonly NearTreeAssets _assets;

    private readonly Vector3[] _clumps;      // global coordinates, our own copy
    private readonly byte[] _flags;
    private readonly Vector4[] _islands;     // per tree: offset inside the clump, and height
    private readonly float _clearMultiplier;

    // Uniform grid over the clumps, in the counting-sort layout: _cellItems is
    // every clump index grouped by cell, _cellStart says where each group starts.
    private readonly Vector3 _gridMin;
    private readonly int _gridX;
    private readonly int _gridZ;
    private readonly int[] _cellStart;
    private readonly int[] _cellItems;

    private readonly List<int> _hiddenIds = new List<int>(512);
    private readonly List<int> _candidates = new List<int>(512);
    private readonly Vector3[] _write = new Vector3[1];

    private int[] _nearIds = new int[256];
    private float[] _nearDist = new float[256];
    private int _nearCount;

    // Last frame's set, kept explicitly: the flag byte still describes it while
    // the new one is being built (the hysteresis reads it), so it cannot double
    // as the record of what to clear afterwards.
    private int[] _prevIds = new int[256];
    private int _prevCount;

    // Instance matrices, split by model variant and sorted near-to-far inside
    // each, so the detail-radius split is a binary search rather than a rebuild.
    private readonly Matrix4x4[][] _matrices = new Matrix4x4[NearTreeModel.FullVariants][];
    private readonly float[][] _matrixDist = new float[NearTreeModel.FullVariants][];
    private readonly int[][] _matrixId = new int[NearTreeModel.FullVariants][];
    private readonly int[] _matrixCount = new int[NearTreeModel.FullVariants];

    private Vector3 _lastOrigin = new Vector3(float.NaN, 0f, 0f);
    private Vector3 _lastBuildCamera = new Vector3(float.NaN, 0f, 0f);
    private bool _setDirty = true;

    internal int TreesDrawn { get; private set; }
    internal int ClumpsHidden => _hiddenIds.Count;
    internal float LastRebuildMs { get; private set; }

    /// <summary>Distance to the closest clump on the map, whether or not it was
    /// taken. An empty near set with this at 4 km means the aircraft is over
    /// water or a runway, not that the lookup is broken.</summary>
    internal float NearestDistance { get; private set; }

    internal Vector3 CameraGlobal { get; private set; }

    private NearTreeField(
        GraphicsBuffer positionBuffer, NearTreeAssets assets,
        Vector3[] clumps, Vector4[] islands, float clearMultiplier)
    {
        _positionBuffer = positionBuffer;
        _assets = assets;
        _clumps = clumps;
        _islands = islands;
        _clearMultiplier = clearMultiplier;
        _flags = new byte[clumps.Length];

        var min = clumps[0];
        var max = clumps[0];
        for (var i = 1; i < clumps.Length; i++)
        {
            min = Vector3.Min(min, clumps[i]);
            max = Vector3.Max(max, clumps[i]);
        }

        _gridMin = min;
        _gridX = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / CellSize) + 1);
        _gridZ = Mathf.Max(1, Mathf.CeilToInt((max.z - min.z) / CellSize) + 1);

        _cellStart = new int[_gridX * _gridZ + 1];
        _cellItems = new int[clumps.Length];
        for (var i = 0; i < clumps.Length; i++) _cellStart[Cell(clumps[i]) + 1]++;
        for (var c = 1; c < _cellStart.Length; c++) _cellStart[c] += _cellStart[c - 1];
        var fill = new int[_gridX * _gridZ];
        for (var i = 0; i < clumps.Length; i++)
        {
            var c = Cell(clumps[i]);
            _cellItems[_cellStart[c] + fill[c]++] = i;
        }

        for (var v = 0; v < NearTreeModel.FullVariants; v++)
        {
            _matrices[v] = new Matrix4x4[1024];
            _matrixDist[v] = new float[1024];
            _matrixId[v] = new int[1024];
        }
    }

    /// <summary>
    /// Reads everything the field needs out of a <see cref="TreeRenderer"/> that
    /// has already been set up, or returns null having said what was missing.
    /// Every one of these is a private field of a class in the game's assembly:
    /// if the game renames one, this feature switches itself off and the rest of
    /// the mod is unaffected.
    /// </summary>
    internal static NearTreeField? TryCreate(TreeRenderer renderer, NearTreeAssets assets)
    {
        var buffer = AccessTools.Field(typeof(TreeRenderer), "treePositionBuffer")?.GetValue(renderer)
            as GraphicsBuffer;
        if (buffer == null)
        {
            Debug.LogWarning("[NOVR.NearTrees] TreeRenderer.treePositionBuffer is missing or null; "
                             + "this renderer keeps its own trees.");
            return null;
        }

        var positions = renderer.PositionData;
        if (positions == null || positions.bytes.Length < 12)
        {
            Debug.LogWarning("[NOVR.NearTrees] TreeRenderer has no position data; skipping it.");
            return null;
        }

        var clumps = ReadPositions(positions.bytes);
        var clumpMesh = AccessTools.Field(typeof(TreeRenderer), "mesh")?.GetValue(renderer) as Mesh;
        var islands = ClumpLayout(clumpMesh, out var source);

        var multiplierField = AccessTools.Field(typeof(TreeRenderer), "clearTreeRadiusMultiplier");
        var multiplier = multiplierField != null ? (float)multiplierField.GetValue(renderer) : 0.3f;

        Debug.Log($"[NOVR.NearTrees] {clumps.Length} clumps x {islands.Length} trees ({source}), "
                  + $"blast clear x{multiplier:F2}.");

        return new NearTreeField(buffer, assets, clumps, islands, multiplier);
    }

    private static unsafe Vector3[] ReadPositions(byte[] bytes)
    {
        var count = bytes.Length / 12;
        var result = new Vector3[count];
        fixed (byte* src = bytes)
        {
            var typed = (Vector3*)src;
            for (var i = 0; i < count; i++) result[i] = typed[i];
        }

        return result;
    }

    /// <summary>
    /// Where the 36 trees sit inside one clump, and how tall they are, taken
    /// from the game's own <c>TreeClump1</c> so the mod's trees stand exactly
    /// where the ones they replace stood. The mesh splits into connected islands
    /// of six vertices — one crossed pair each — so a union over the triangles
    /// finds them without knowing the count in advance.
    ///
    /// <para>Falls back to a jittered grid over the mesh bounds when the mesh was
    /// imported without read/write access, which is the normal setting for a mesh
    /// nothing was ever meant to read on the CPU. That fallback puts trees in the
    /// right area but not on the same spots, so the crossover shifts them.</para>
    /// </summary>
    private static Vector4[] ClumpLayout(Mesh? mesh, out string source)
    {
        if (mesh != null && mesh.isReadable)
        {
            var verts = mesh.vertices;
            var tris = mesh.triangles;
            var parent = new int[verts.Length];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;

            for (var t = 0; t < tris.Length; t += 3)
            {
                Union(parent, tris[t], tris[t + 1]);
                Union(parent, tris[t + 1], tris[t + 2]);
            }

            var groups = new Dictionary<int, (Vector3 sum, int n, float minY, float maxY)>(64);
            for (var v = 0; v < verts.Length; v++)
            {
                var root = Find(parent, v);
                groups.TryGetValue(root, out var g);
                if (g.n == 0) g = (Vector3.zero, 0, float.MaxValue, float.MinValue);
                groups[root] = (g.sum + verts[v], g.n + 1,
                    Mathf.Min(g.minY, verts[v].y), Mathf.Max(g.maxY, verts[v].y));
            }

            if (groups.Count > 0)
            {
                var offsets = new Vector4[groups.Count];
                var index = 0;
                foreach (var g in groups.Values)
                {
                    var centre = g.sum / g.n;
                    // The base of the trunk, not the middle of the card: the mod's
                    // model is generated with its base at the origin.
                    offsets[index++] = new Vector4(centre.x, g.minY, centre.z, g.maxY - g.minY);
                }

                source = $"read from {mesh.name}";
                return offsets;
            }
        }

        // The shipped TreeClump1 is not CPU-readable, which is the normal import
        // setting for a mesh nothing was meant to read back, so this is the path
        // that actually runs. The table is the same measurement taken offline.
        source = "baked TreeClump1 layout";
        return NearTreeClumpLayout.TreeClump1;
    }

    private static int Find(int[] parent, int a)
    {
        while (parent[a] != a) a = parent[a] = parent[parent[a]];
        return a;
    }

    private static void Union(int[] parent, int a, int b)
    {
        a = Find(parent, a);
        b = Find(parent, b);
        if (a != b) parent[b] = a;
    }

    private int Cell(Vector3 global)
    {
        var cx = Mathf.Clamp((int)((global.x - _gridMin.x) / CellSize), 0, _gridX - 1);
        var cz = Mathf.Clamp((int)((global.z - _gridMin.z) / CellSize), 0, _gridZ - 1);
        return cz * _gridX + cx;
    }

    /// <summary>
    /// A stable per-clump random in [0,1), from the clump index. Used to fray the
    /// two radii: a hard radius puts every appearing tree on one circle centred
    /// on the pilot, which is far more visible than the pop it is made of.
    /// </summary>
    private static float Jitter(int id, uint salt)
    {
        var h = (uint)id * 2654435761u ^ salt;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    internal void Update(Vector3 cameraScene)
    {
        var origin = Datum.originPosition;
        var cameraGlobal = cameraScene - origin;
        CameraGlobal = cameraGlobal;

        var radius = NearTreeConfig.Radius;
        var maxClumps = Mathf.Max(1, NearTreeConfig.Budget / _islands.Length);

        CollectCandidates(cameraGlobal, radius);
        SelectNear(cameraGlobal, radius, maxClumps);
        ApplyHiding();

        var movedFar = (cameraScene - _lastBuildCamera).sqrMagnitude > 25f * 25f;
        if (_setDirty || movedFar || origin != _lastOrigin)
        {
            var started = Time.realtimeSinceStartup;
            BuildMatrices(cameraGlobal, origin);
            LastRebuildMs = (Time.realtimeSinceStartup - started) * 1000f;
            _lastBuildCamera = cameraScene;
            _lastOrigin = origin;
            _setDirty = false;
        }
    }

    private void CollectCandidates(Vector3 cameraGlobal, float radius)
    {
        _candidates.Clear();
        var cx = (int)((cameraGlobal.x - _gridMin.x) / CellSize);
        var cz = (int)((cameraGlobal.z - _gridMin.z) / CellSize);
        var reach = Mathf.CeilToInt(radius / CellSize);

        for (var z = cz - reach; z <= cz + reach; z++)
        {
            if (z < 0 || z >= _gridZ) continue;
            for (var x = cx - reach; x <= cx + reach; x++)
            {
                if (x < 0 || x >= _gridX) continue;
                var cell = z * _gridX + x;
                for (var i = _cellStart[cell]; i < _cellStart[cell + 1]; i++) _candidates.Add(_cellItems[i]);
            }
        }
    }

    private void SelectNear(Vector3 cameraGlobal, float radius, int maxClumps)
    {
        if (_nearIds.Length < _candidates.Count)
        {
            _nearIds = new int[Mathf.NextPowerOfTwo(_candidates.Count)];
            _nearDist = new float[_nearIds.Length];
            _prevIds = new int[_nearIds.Length];
            _prevCount = 0;
        }

        var count = 0;
        var nearest = float.MaxValue;
        foreach (var id in _candidates)
        {
            if ((_flags[id] & FlagCleared) != 0) continue;

            var delta = _clumps[id] - cameraGlobal;
            var distance = delta.magnitude;
            if (distance < nearest) nearest = distance;

            // Frayed radius, plus hysteresis so a clump sitting on the boundary
            // does not flicker between the two renderers every frame.
            var threshold = radius * (0.85f + 0.15f * Jitter(id, 0x51ED270Bu));
            if ((_flags[id] & FlagNear) != 0) threshold *= 1.06f;
            if (distance > threshold) continue;

            _nearIds[count] = id;
            _nearDist[count] = distance;
            count++;
        }

        NearestDistance = nearest;
        Array.Sort(_nearDist, _nearIds, 0, count);
        if (count > maxClumps) count = maxClumps;

        // Membership change decides whether the instance matrices are rebuilt, and
        // it has to be read while the flags still describe last frame.
        if (count != _prevCount) _setDirty = true;
        else
        {
            for (var i = 0; i < count; i++)
            {
                if ((_flags[_nearIds[i]] & FlagNear) != 0) continue;
                _setDirty = true;
                break;
            }
        }

        for (var i = 0; i < _prevCount; i++) _flags[_prevIds[i]] &= unchecked((byte)~FlagNear);
        for (var i = 0; i < count; i++) _flags[_nearIds[i]] |= FlagNear;

        Array.Copy(_nearIds, _prevIds, count);
        _prevCount = count;
        _nearCount = count;
    }

    private void ApplyHiding()
    {
        for (var i = 0; i < _nearCount; i++)
        {
            var id = _nearIds[i];
            if ((_flags[id] & FlagHidden) != 0) continue;
            _write[0] = Sentinel;
            _positionBuffer.SetData(_write, 0, id, 1);
            _flags[id] |= FlagHidden;
            _hiddenIds.Add(id);
        }

        for (var i = _hiddenIds.Count - 1; i >= 0; i--)
        {
            var id = _hiddenIds[i];
            if ((_flags[id] & FlagNear) != 0) continue;

            // A blast took it while we had it hidden: the game has already zeroed
            // this slot, and putting the tree back would undestroy it.
            if ((_flags[id] & FlagCleared) == 0)
            {
                _write[0] = _clumps[id];
                _positionBuffer.SetData(_write, 0, id, 1);
            }

            _flags[id] &= unchecked((byte)~FlagHidden);
            _hiddenIds[i] = _hiddenIds[_hiddenIds.Count - 1];
            _hiddenIds.RemoveAt(_hiddenIds.Count - 1);
            _setDirty = true;
        }
    }

    /// <summary>
    /// Rebuilds the per-instance matrices, ordered near-to-far inside each model
    /// variant so the detail split downstream is a binary search.
    ///
    /// <para>Two passes, and the reason is measured. Sorting the matrices
    /// directly means an <c>Array.Sort</c> that swaps 64-byte structs, and at
    /// the default radius this method ran in 1.19 ms — every frame while moving,
    /// because a clump entering or leaving marks the set dirty. Sorting a float
    /// key against an int index moves four bytes instead of sixty-four, and the
    /// transform is then built once, in order.</para>
    /// </summary>
    private void BuildMatrices(Vector3 cameraGlobal, Vector3 origin)
    {
        for (var v = 0; v < NearTreeModel.FullVariants; v++) _matrixCount[v] = 0;

        // Pass one: distance and variant only. The packed id is the clump's slot
        // in the near list times 64 plus the tree's index in the clump, which
        // fits because a clump holds 36.
        for (var c = 0; c < _nearCount; c++)
        {
            var clumpId = _nearIds[c];
            var clump = _clumps[clumpId];

            for (var t = 0; t < _islands.Length; t++)
            {
                var island = _islands[t];
                var global = clump + new Vector3(island.x, island.y, island.z);
                var seed = (uint)clumpId * 0x9E3779B9u + (uint)t;
                var variant = (int)(seed >> 28) % NearTreeModel.FullVariants;

                var count = _matrixCount[variant];
                if (count == _matrices[variant].Length)
                {
                    Array.Resize(ref _matrices[variant], count * 2);
                    Array.Resize(ref _matrixDist[variant], count * 2);
                    Array.Resize(ref _matrixId[variant], count * 2);
                }

                // The detail split runs on a frayed distance so the change of
                // model does not happen on a circle centred on the pilot either.
                _matrixDist[variant][count] =
                    (global - cameraGlobal).magnitude * (0.88f + 0.24f * Jitter((int)seed, 0x165667B1u));
                _matrixId[variant][count] = c * 64 + t;
                _matrixCount[variant] = count + 1;
            }
        }

        var drawn = 0;
        for (var v = 0; v < NearTreeModel.FullVariants; v++)
        {
            var count = _matrixCount[v];
            Array.Sort(_matrixDist[v], _matrixId[v], 0, count);

            var matrices = _matrices[v];
            var ids = _matrixId[v];
            for (var i = 0; i < count; i++)
            {
                var clumpId = _nearIds[ids[i] >> 6];
                var t = ids[i] & 63;
                var island = _islands[t];
                var global = _clumps[clumpId] + new Vector3(island.x, island.y, island.z);
                var seed = (uint)clumpId * 0x9E3779B9u + (uint)t;

                // The clump already carries six distinct sizes between 15 m and
                // 31 m, so this only has to break ties inside a size class.
                var height = island.w * (0.92f + 0.16f * Jitter((int)seed, 0x2545F491u));
                var width = height * (0.85f + 0.3f * Jitter((int)seed, 0x9E3779B1u));
                var yaw = Jitter((int)seed, 0x85EBCA6Bu) * 360f;
                var lean = new Vector3(
                    (Jitter((int)seed, 0xC2B2AE35u) - 0.5f) * 8f,
                    0f,
                    (Jitter((int)seed, 0x27D4EB2Fu) - 0.5f) * 8f);

                matrices[i] = Matrix4x4.TRS(
                    global + origin,
                    Quaternion.Euler(lean.x, yaw, lean.z),
                    new Vector3(width, height, width));
            }

            drawn += count;
        }

        TreesDrawn = drawn;
        _detailSplitValid = false;
        _detailRadiusUsed = NearTreeConfig.DetailRadius;
    }

    private bool _detailSplitValid;
    private float _detailRadiusUsed;
    private readonly int[] _split = new int[NearTreeModel.FullVariants];

    internal void Render()
    {
        if (TreesDrawn == 0) return;

        var detail = NearTreeConfig.DetailRadius;
        if (!_detailSplitValid || !Mathf.Approximately(detail, _detailRadiusUsed))
        {
            for (var v = 0; v < NearTreeModel.FullVariants; v++)
            {
                _split[v] = UpperBound(_matrixDist[v], _matrixCount[v], detail);
            }

            _detailRadiusUsed = detail;
            _detailSplitValid = true;
        }

        for (var v = 0; v < NearTreeModel.FullVariants; v++)
        {
            Draw(_assets.Full[v], _matrices[v], 0, _split[v]);
            Draw(_assets.Reduced, _matrices[v], _split[v], _matrixCount[v] - _split[v]);
        }
    }

    private void Draw(Mesh mesh, Matrix4x4[] matrices, int start, int count)
    {
        if (count <= 0) return;

        var canopy = _assets.Materials.CanopyParams;
        var trunk = _assets.Materials.TrunkParams;
        for (var offset = 0; offset < count; offset += MaxPerCall)
        {
            var batch = Mathf.Min(MaxPerCall, count - offset);
            Graphics.RenderMeshInstanced(
                in canopy, mesh, NearTreeModel.SubmeshCanopy, matrices, batch, start + offset);
            Graphics.RenderMeshInstanced(
                in trunk, mesh, NearTreeModel.SubmeshTrunk, matrices, batch, start + offset);
        }
    }

    private static int UpperBound(float[] sorted, int count, float value)
    {
        int lo = 0, hi = count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (sorted[mid] <= value) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }

    /// <summary>
    /// Records what a blast is about to clear. Called from a prefix on
    /// <c>TreeRenderer.ClearTrees</c>, i.e. before the game scales the radius by
    /// its own multiplier, so the same scaling is applied here.
    /// </summary>
    internal void NoteBlast(Vector3 globalPosition, float radius)
    {
        var scaled = radius * _clearMultiplier;
        CollectCandidates(globalPosition, scaled);
        foreach (var id in _candidates)
        {
            if ((_clumps[id] - globalPosition).sqrMagnitude > scaled * scaled) continue;
            if ((_flags[id] & FlagCleared) != 0) continue;
            _flags[id] |= FlagCleared;
            _setDirty = true;
        }
    }

    /// <summary>
    /// Puts every hidden clump back. Called when the feature is switched off or
    /// the renderer goes away, so a mod that stops running does not leave holes
    /// in the forest — the buffer outlives this object by exactly as long as the
    /// scene does.
    /// </summary>
    internal void Restore()
    {
        foreach (var id in _hiddenIds)
        {
            _flags[id] &= unchecked((byte)~FlagHidden);
            if ((_flags[id] & FlagCleared) != 0) continue;
            _write[0] = _clumps[id];
            try
            {
                _positionBuffer.SetData(_write, 0, id, 1);
            }
            catch (Exception)
            {
                // The buffer is released with the scene; if we are being torn
                // down after it, there is nothing left to restore into.
                return;
            }
        }

        _hiddenIds.Clear();
    }
}
