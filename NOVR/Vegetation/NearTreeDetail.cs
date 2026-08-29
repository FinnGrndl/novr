using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Effects;
using UnityEngine;

namespace NOVR.Vegetation;

/// <summary>The meshes and materials every near tree is drawn with, built once.</summary>
internal sealed class NearTreeAssets
{
    internal Mesh[] Full = null!;
    internal Mesh Reduced = null!;
    internal NearTreeMaterials Materials = null!;

    internal static NearTreeAssets? TryBuild(Material gameTreeMaterial)
    {
        var materials = NearTreeMaterials.TryBuild(gameTreeMaterial, NearTreeConfig.Lush);
        if (materials == null) return null;

        var full = new Mesh[NearTreeModel.FullVariants];
        for (var v = 0; v < full.Length; v++) full[v] = NearTreeModel.Build(v, full: true);

        return new NearTreeAssets
        {
            Full = full,
            Reduced = NearTreeModel.Build(0, full: false),
            Materials = materials,
        };
    }

    internal void Destroy()
    {
        foreach (var mesh in Full)
        {
            if (mesh != null) Object.Destroy(mesh);
        }

        if (Reduced != null) Object.Destroy(Reduced);
        Materials.Destroy();
    }
}

/// <summary>
/// Drives the near-tree fields — one per <see cref="TreeRenderer"/> in the
/// scene — and owns the assets they share.
///
/// <para>It runs at execution order 7, one ahead of <see cref="DetailRenderer"/>
/// at 8, so the frame's sentinel writes are in the buffer before the game queues
/// the cull that reads it. Getting that backwards is not fatal — it would cost
/// one frame of drawing a handful of entering trees twice — but there is no
/// reason to.</para>
///
/// <para>The camera comes out of <see cref="DetailRenderer"/>'s own private field
/// rather than from <c>Camera.main</c>, because in VR that field is not
/// <c>Camera.main</c>: <c>DetailRendererVrCameraFix</c> rebinds it to the tracked
/// camera, and the near set has to agree with the cull it is hiding trees
/// from.</para>
/// </summary>
[DefaultExecutionOrder(7)]
internal sealed class NearTreeDetail : MonoBehaviour
{
    private static readonly Dictionary<TreeRenderer, NearTreeField> Fields =
        new Dictionary<TreeRenderer, NearTreeField>();

    // Resolved once: AccessTools.Field is a reflection lookup every time it is
    // called, and this one is on the per-frame path.
    private static readonly System.Reflection.FieldInfo? DetailCameraField =
        AccessTools.Field(typeof(DetailRenderer), "camera");

    private DetailRenderer? _detail;
    private NearTreeAssets? _assets;
    private readonly List<NearTreeField> _active = new List<NearTreeField>();
    private bool _running;
    private float _nextLog;

    internal static void Attach(DetailRenderer detail)
    {
        if (!NearTreeConfig.Enabled) return;
        if (detail.gameObject.GetComponent<NearTreeDetail>() != null) return;

        var component = detail.gameObject.AddComponent<NearTreeDetail>();
        component._detail = detail;
    }

    private void Start()
    {
        if (_detail == null) return;

        var renderers = AccessTools.Field(typeof(DetailRenderer), "treeRenderers")?.GetValue(_detail)
            as TreeRenderer[];
        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogWarning("[NOVR.NearTrees] DetailRenderer.treeRenderers is missing or empty; "
                             + "near trees have nothing to work from.");
            enabled = false;
            return;
        }

        Material? gameMaterial = null;
        foreach (var renderer in renderers)
        {
            gameMaterial = AccessTools.Field(typeof(TreeRenderer), "material")?.GetValue(renderer) as Material;
            if (gameMaterial != null) break;
        }

        _assets = NearTreeAssets.TryBuild(gameMaterial!);
        if (_assets == null)
        {
            enabled = false;
            return;
        }

        foreach (var renderer in renderers)
        {
            var field = NearTreeField.TryCreate(renderer, _assets);
            if (field == null) continue;
            _active.Add(field);
            Fields[renderer] = field;
        }

        if (_active.Count == 0)
        {
            Debug.LogWarning("[NOVR.NearTrees] No tree renderer could be taken over; near trees are off.");
            _assets.Destroy();
            _assets = null;
            enabled = false;
            return;
        }

        _running = true;
        Debug.Log($"[NOVR.NearTrees] Active on {_active.Count} tree renderer(s), "
                  + $"{NearTreeConfig.Radius:F0} m radius, {NearTreeConfig.DetailRadius:F0} m full detail, "
                  + $"budget {NearTreeConfig.Budget}.");
    }

    private void LateUpdate()
    {
        if (!_running) return;

        // Switched off in the config while flying: hand every clump back to the
        // game and stop. Switching it on again needs a mission load, because the
        // position index is built once when the scene's vegetation is set up.
        if (!NearTreeConfig.Enabled)
        {
            foreach (var field in _active) field.Restore();
            return;
        }

        var camera = DetailCameraField?.GetValue(_detail) as Camera;
        if (camera == null) camera = Camera.main;
        if (camera == null) return;

        var position = camera.transform.position;
        foreach (var field in _active)
        {
            field.Update(position);
            field.Render();
        }

        if (!NearTreeConfig.Debug || Time.unscaledTime < _nextLog) return;
        _nextLog = Time.unscaledTime + 1f;

        var trees = 0;
        var hidden = 0;
        var ms = 0f;
        var nearest = float.MaxValue;
        var global = Vector3.zero;
        foreach (var field in _active)
        {
            trees += field.TreesDrawn;
            hidden += field.ClumpsHidden;
            ms = Mathf.Max(ms, field.LastRebuildMs);
            nearest = Mathf.Min(nearest, field.NearestDistance);
            global = field.CameraGlobal;
        }

        // The camera's global position and the nearest clump are here so that an
        // empty set is diagnosable from the log alone: over water, over a runway,
        // or a coordinate conversion that is wrong all look identical otherwise.
        Debug.Log($"[NOVR.NearTrees] {trees} trees drawn, {hidden} clumps hidden from the game, "
                  + $"last rebuild {ms:F2} ms; camera global "
                  + $"({global.x:F0}, {global.y:F0}, {global.z:F0}), nearest clump {nearest:F0} m.");
    }

    private void OnDestroy()
    {
        foreach (var field in _active) field.Restore();
        _active.Clear();
        Fields.Clear();
        _assets?.Destroy();
        _assets = null;
        _running = false;
    }

    /// <summary>
    /// A blast is about to clear trees on the GPU; record it so those clumps are
    /// never restored (and stop drawing them ourselves).
    /// </summary>
    internal static void NoteBlast(TreeRenderer renderer, BlastManager.DetailBlast blast)
    {
        if (Fields.TryGetValue(renderer, out var field))
        {
            field.NoteBlast(blast.Position.AsVector3(), blast.Radius);
        }
    }
}

/// <summary>
/// Attaches the near-tree driver once the game's vegetation has been set up —
/// after <c>DetailRenderer.Start</c>, because that is where
/// <c>TreeRenderer.CommandSetup</c> creates the position buffer the driver takes
/// over.
/// </summary>
[HarmonyPatch(typeof(DetailRenderer), "Start")]
internal static class DetailRendererNearTreesPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(DetailRenderer __instance) => NearTreeDetail.Attach(__instance);
}

/// <summary>
/// Records blast clearing before the game scales the radius. A prefix rather
/// than a postfix on purpose: <c>ClearTrees</c> multiplies <c>blast.Radius</c> in
/// place, and reading it afterwards would depend on whether Harmony hands a
/// postfix the original argument or the mutated local.
/// </summary>
[HarmonyPatch(typeof(TreeRenderer), nameof(TreeRenderer.ClearTrees))]
internal static class TreeRendererClearTreesNearTreesPatch
{
    [HarmonyPrefix]
    private static void Prefix(TreeRenderer __instance, BlastManager.DetailBlast blast) =>
        NearTreeDetail.NoteBlast(__instance, blast);
}
