using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NOVR;

/// <summary>
/// Times the scripts of the game and of every other mod, so the performance log
/// can name the code a slow frame was spent in.
///
/// <para><b>Why.</b> The player-loop timing in <see cref="PerfMonitor"/> says
/// how much of a frame goes to "scripts", "coroutines" or "physics", but not
/// whose. In a full multiplayer session the main thread ran at twice to four
/// times the headset's budget with NOVR's own code at a few percent of it, and
/// about two frames a second stalled for 50 ms or more in a regular rhythm. The
/// question left is which script does that, and that needs names.</para>
///
/// <para><b>How.</b> About 20 s after start, every <c>Update</c>,
/// <c>LateUpdate</c> and <c>FixedUpdate</c> of a MonoBehaviour, every
/// compiler-generated iterator (coroutines among them) and every Harmony
/// prefix, postfix and finalizer another mod installed gets a timing prefix and
/// postfix, a few per frame so the patching itself does not freeze the game.
/// The game's assemblies, the networking and input libraries and all other
/// plugins are covered; Unity, the runtime, BepInEx, Harmony and NOVR itself
/// are not. Times are inclusive: a method that calls another timed method
/// carries its time too.</para>
///
/// <para><b>Cost.</b> Roughly 0.1 µs per timed call. In a session with
/// thousands of units that can add up to a millisecond per frame, which is why
/// it can be switched off on its own.</para>
/// </summary>
internal static class ScriptProfiler
{
    private const string HarmonyId = "deltawing.novr.perf";
    private const float StartDelaySeconds = 20f;
    private const float RescanIntervalSeconds = 60f;
    private const int PatchesPerFrame = 6;
    private const int MaxPatches = 5000;

    /// <summary>
    /// A method called more often than this per frame is unpatched again after
    /// its first report: the timer's own cost would then be a sizable part of
    /// the frame it is measuring.
    /// </summary>
    private const int MaxCallsPerFrame = 3000;

    private static readonly string[] ExcludedAssemblyPrefixes =
    {
        "UnityEngine", "Unity.", "System", "mscorlib", "Mono.", "netstandard", "Microsoft.",
        "BepInEx", "0Harmony", "HarmonyX", "MonoMod", "Mono.Cecil", "NOVR", "Newtonsoft",
        "Valve.", "SteamVR", "Oculus", "Meta.", "__Generated", "HarmonySharedState",
    };

    private static readonly HashSet<string> OwnHarmonyIds = new()
    {
        HarmonyId, "deltawing.novr", "novr.vrui.behavioradder",
    };

    private static Harmony? _harmony;
    private static HarmonyMethod? _prefix;
    private static HarmonyMethod? _postfix;
    private static int _mainThread;

    // Keyed by the method handle rather than the MethodBase: the instance
    // Harmony hands the postfix need not be the one reflection returned here.
    private static readonly Dictionary<long, int> Index = new();
    private static readonly HashSet<MethodBase> Seen = new();
    private static readonly List<string> Names = new();
    private static readonly List<MethodBase> Methods = new();
    private static readonly Queue<(MethodBase Method, string Name)> Pending = new();
    private static readonly Queue<int> ToUnpatch = new();
    private static bool[] _dropped = new bool[512];

    private static long[] _ticks = new long[512];
    private static long[] _max = new long[512];
    private static long[] _frame = new long[512];
    private static long[] _spike = new long[512];
    private static int[] _calls = new int[512];
    private static int[] _stamp = new int[512];
    private static int _frameId = 1;
    private static readonly List<int> Touched = new(256);

    private static bool _scanned;
    private static float _nextRescan;
    private static int _failed;
    private static bool _reportedDone;

    /// <summary>Set by <see cref="PerfMonitor"/>; nothing is recorded while false.</summary>
    internal static bool Active;

    internal static bool Enabled => PerfMonitorConfig.Scripts?.Value ?? true;

    /// <summary>True while methods are still being patched; that work makes frames slow by itself.</summary>
    internal static bool SettingUp => _harmony != null && (Pending.Count > 0 || ToUnpatch.Count > 0);

    /// <summary>Called every frame from <see cref="PerfMonitor"/>.</summary>
    internal static void Tick()
    {
        var now = Time.unscaledTime;
        if (!_scanned)
        {
            // Decided once: patches already in place stay until the next start.
            if (!Enabled || now < StartDelaySeconds) return;
            _scanned = true;
            _nextRescan = now + RescanIntervalSeconds;
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            try
            {
                _harmony = new Harmony(HarmonyId);
                _prefix = new HarmonyMethod(typeof(ScriptProfiler), nameof(Prefix)) { priority = Priority.First };
                _postfix = new HarmonyMethod(typeof(ScriptProfiler), nameof(Postfix)) { priority = Priority.Last };
                UiRebuildProfiler.Install(_harmony);
                ScanAssemblies();
                ScanPatches();
                Debug.Log($"[NOVR-PERF] Script timing: {Pending.Count} methods of the game and other mods queued, " +
                          $"patching {PatchesPerFrame} per frame.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NOVR-PERF] Script timing could not start: {e.Message}");
                _harmony = null;
            }
        }
        else if (_harmony != null && now >= _nextRescan)
        {
            // Mods that patch lazily, on a scene load or a first spawn, are
            // picked up here.
            _nextRescan = now + RescanIntervalSeconds;
            ScanPatches();
        }

        if (_harmony == null) return;

        if (ToUnpatch.Count > 0)
        {
            var i = ToUnpatch.Dequeue();
            try
            {
                _harmony.Unpatch(Methods[i], HarmonyPatchType.All, HarmonyId);
            }
            catch (Exception)
            {
                // It keeps its timer; its numbers are no longer reported.
            }
        }

        for (var n = 0; n < PatchesPerFrame && Pending.Count > 0; n++)
        {
            var (method, name) = Pending.Dequeue();
            PatchOne(method, name);
        }

        if (Pending.Count == 0 && !_reportedDone)
        {
            _reportedDone = true;
            Debug.Log($"[NOVR-PERF] Script timing active on {Names.Count} methods ({_failed} could not be patched).");
        }
        else if (Pending.Count > 0)
        {
            _reportedDone = false;
        }
    }

    // ------------------------------------------------------------ discovery

    private static void ScanAssemblies()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;
            var asmName = assembly.GetName().Name ?? "";
            if (IsExcluded(asmName)) continue;

            Type?[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types;
            }
            catch (Exception)
            {
                continue;
            }

            var shortName = asmName == "Assembly-CSharp" ? "game" : asmName;
            foreach (var type in types)
            {
                if (type == null) continue;
                try
                {
                    AddType(type, shortName);
                }
                catch (Exception)
                {
                    // A type whose base cannot be resolved is simply not timed.
                }
            }
        }
    }

    private static void AddType(Type type, string asm)
    {
        if (type.ContainsGenericParameters) return;

        if (typeof(MonoBehaviour).IsAssignableFrom(type))
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var name in new[] { "Update", "LateUpdate", "FixedUpdate" })
            {
                var method = type.GetMethod(name, flags, null, Type.EmptyTypes, null);
                if (method != null && !method.IsAbstract) Enqueue(method, $"{asm}:{type.Name}.{name}");
            }

            return;
        }

        // Compiler-generated iterator state machines of methods that return an
        // IEnumerator, which is what coroutines are. Iterators that return an
        // IEnumerable are left out: code loops over those, sometimes thousands
        // of times a frame, and they show up inside their caller anyway.
        if (type.IsNested && type.Name.StartsWith("<") &&
            typeof(IEnumerator).IsAssignableFrom(type) &&
            !typeof(IEnumerable).IsAssignableFrom(type) &&
            type.IsDefined(typeof(CompilerGeneratedAttribute), false))
        {
            var moveNext = type.GetMethod("MoveNext",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null);
            if (moveNext == null) return;

            var inner = type.Name;
            var end = inner.IndexOf('>');
            var outerName = type.DeclaringType != null ? type.DeclaringType.Name : "?";
            var iteratorName = end > 1 ? inner.Substring(1, end - 1) : inner;
            Enqueue(moveNext, $"{asm}:{outerName}.{iteratorName} (iterator)");
        }
    }

    private static void ScanPatches()
    {
        try
        {
            foreach (var target in Harmony.GetAllPatchedMethods())
            {
                var info = Harmony.GetPatchInfo(target);
                if (info == null) continue;
                AddPatches(info.Prefixes, target, "prefix");
                AddPatches(info.Postfixes, target, "postfix");
                AddPatches(info.Finalizers, target, "finalizer");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NOVR-PERF] Script timing: reading other mods' patches failed: {e.Message}");
        }
    }

    private static void AddPatches(IEnumerable<Patch> patches, MethodBase target, string kind)
    {
        foreach (var patch in patches)
        {
            if (patch == null || OwnHarmonyIds.Contains(patch.owner)) continue;
            var method = patch.PatchMethod;
            if (method == null) continue;
            var asm = method.DeclaringType?.Assembly.GetName().Name ?? "";
            if (asm.StartsWith("NOVR")) continue;

            Enqueue(method, $"{patch.owner}:{method.DeclaringType?.Name}.{method.Name} ({kind} on {target.DeclaringType?.Name}.{target.Name})");
        }
    }

    private static bool IsExcluded(string assemblyName)
    {
        foreach (var prefix in ExcludedAssemblyPrefixes)
        {
            if (assemblyName.StartsWith(prefix, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static void Enqueue(MethodBase method, string name)
    {
        if (Seen.Count >= MaxPatches) return;
        if (method.IsGenericMethodDefinition || method.ContainsGenericParameters) return;
        if (!Seen.Add(method)) return;
        Pending.Enqueue((method, name));
    }

    private static void PatchOne(MethodBase method, string name)
    {
        long key;
        try
        {
            key = method.MethodHandle.Value.ToInt64();
        }
        catch (Exception)
        {
            _failed++;
            return;
        }

        var index = Names.Count;
        EnsureCapacity(index + 1);
        Names.Add(name);
        Methods.Add(method);
        Index[key] = index;

        try
        {
            _harmony!.Patch(method, prefix: _prefix, postfix: _postfix);
        }
        catch (Exception)
        {
            _failed++;
            _dropped[index] = true;
        }
    }

    private static void EnsureCapacity(int count)
    {
        if (count <= _ticks.Length) return;
        var size = Math.Max(count, _ticks.Length * 2);
        Array.Resize(ref _ticks, size);
        Array.Resize(ref _max, size);
        Array.Resize(ref _frame, size);
        Array.Resize(ref _spike, size);
        Array.Resize(ref _calls, size);
        Array.Resize(ref _stamp, size);
        Array.Resize(ref _dropped, size);
    }

    // ------------------------------------------------------------ the timer

    private static void Prefix(out long __state)
    {
        __state = Stopwatch.GetTimestamp();
    }

    private static void Postfix(long __state, MethodBase __originalMethod)
    {
        if (!Active || __state == 0) return;
        // Iterators are also stepped on worker threads; only the main thread's
        // frame is being measured, and the counters are not thread-safe.
        if (Thread.CurrentThread.ManagedThreadId != _mainThread) return;
        if (__originalMethod == null || !Index.TryGetValue(__originalMethod.MethodHandle.Value.ToInt64(), out var i)) return;

        var dt = Stopwatch.GetTimestamp() - __state;
        _ticks[i] += dt;
        _calls[i]++;
        if (dt > _max[i]) _max[i] = dt;
        if (_stamp[i] != _frameId)
        {
            _stamp[i] = _frameId;
            Touched.Add(i);
        }

        _frame[i] += dt;
    }

    // ------------------------------------------------------------ frames and reports

    /// <summary>Close the frame that just ended; called at the top of each player loop.</summary>
    internal static void OnFrameBoundary(bool spike)
    {
        for (var k = 0; k < Touched.Count; k++)
        {
            var i = Touched[k];
            if (spike) _spike[i] += _frame[i];
            _frame[i] = 0;
        }

        Touched.Clear();
        _frameId++;
    }

    /// <summary>The slowest timed methods of the frame that just ended, before it is closed.</summary>
    internal static string DescribeFrame(int count)
    {
        if (Touched.Count == 0) return "no timed scripts";
        var order = new List<int>(Touched);
        order.Sort((a, b) => _frame[b].CompareTo(_frame[a]));
        var sb = new StringBuilder();
        for (var k = 0; k < order.Count && k < count; k++)
        {
            if (k > 0) sb.Append(", ");
            sb.Append($"{Names[order[k]]} {Ms(_frame[order[k]]):F1}");
        }

        return sb.ToString();
    }

    internal static void ResetWindow()
    {
        var n = Names.Count;
        Array.Clear(_ticks, 0, n);
        Array.Clear(_max, 0, n);
        Array.Clear(_spike, 0, n);
        Array.Clear(_calls, 0, n);
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    internal static IEnumerable<string> Report(int frames, int spikeFrames)
    {
        var n = Names.Count;
        if (n == 0 || frames == 0) yield break;

        var order = new List<int>(n);
        for (var i = 0; i < n; i++)
        {
            if (_calls[i] > 0 && !_dropped[i]) order.Add(i);
        }

        // Methods too frequent to time cheaply are reported once, with what
        // they cost, and unpatched.
        var droppedNow = new StringBuilder();
        foreach (var i in order)
        {
            if (_calls[i] <= (long)MaxCallsPerFrame * frames) continue;
            _dropped[i] = true;
            ToUnpatch.Enqueue(i);
            var entry = $"{Names[i]} {Ms(_ticks[i]) / frames:F2} ms ({_calls[i] / frames} calls)";
            if (droppedNow.Length > 0) droppedNow.Append(", ");
            droppedNow.Append(entry);
        }

        if (droppedNow.Length > 0)
        {
            yield return $"[NOVR-PERF] too frequent to keep timing, cost per frame before removal: {droppedNow}";
        }

        order.Sort((a, b) => _ticks[b].CompareTo(_ticks[a]));
        var sb = new StringBuilder(1024);
        sb.Append("[NOVR-PERF] scripts, inclusive ms/frame (calls/frame, slowest call ms): ");
        for (var k = 0; k < order.Count && k < 15; k++)
        {
            var i = order[k];
            if (k > 0) sb.Append(", ");
            sb.Append($"{Names[i]} {Ms(_ticks[i]) / frames:F2} ({(double)_calls[i] / frames:F1}, {Ms(_max[i]):F1})");
        }

        yield return sb.ToString();

        order.Sort((a, b) => _max[b].CompareTo(_max[a]));
        sb.Clear();
        sb.Append("[NOVR-PERF] slowest single calls, ms (calls in window): ");
        for (var k = 0; k < order.Count && k < 10; k++)
        {
            var i = order[k];
            if (k > 0) sb.Append(", ");
            sb.Append($"{Names[i]} {Ms(_max[i]):F1} ({_calls[i]})");
        }

        yield return sb.ToString();

        if (spikeFrames <= 0) yield break;

        order.Sort((a, b) => _spike[b].CompareTo(_spike[a]));
        sb.Clear();
        sb.Append($"[NOVR-PERF] scripts in the {spikeFrames} slow frames, ms per slow frame: ");
        for (var k = 0; k < order.Count && k < 12; k++)
        {
            var i = order[k];
            if (_spike[i] == 0) break;
            if (k > 0) sb.Append(", ");
            sb.Append($"{Names[i]} {Ms(_spike[i]) / spikeFrames:F2}");
        }

        yield return sb.ToString();
    }
}
