using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace NOVR;

/// <summary>
/// Where the frame goes, written to the log every few seconds of a normal flight.
///
/// <para><b>Why it exists.</b> Multiplayer sessions were reported as unplayably
/// laggy on a fast machine, and the log carried no frame times at all, so every
/// theory about the cause was a guess. This answers the question from one
/// ordinary flight, with no A/B test: how long the frames are, which part of
/// Unity's player loop the main thread spends them in (physics, scripts, canvas
/// rebuilds, rendering, the XR frame wait), which cameras cost what to submit,
/// and how much of it is NOVR's own per-frame code.</para>
///
/// <para><b>How.</b> A timestamp marker is inserted between every subsystem of
/// the player loop, so each subsystem's time is the gap between the markers on
/// either side of it. That covers the game, every mod and Unity itself without
/// patching any of them. Cameras are timed from URP's begin/end camera
/// callbacks. NOVR's own hot paths report through <see cref="Begin"/> and
/// <see cref="End"/>. The cost is two timestamps per subsystem per frame, a few
/// microseconds, and one log line per section per interval.</para>
/// </summary>
public class PerfMonitor : MonoBehaviour
{
    // ------------------------------------------------------------ NOVR scopes

    /// <summary>Starts a timed NOVR scope; pass the result to <see cref="End"/>.</summary>
    public static long Begin() => Stopwatch.GetTimestamp();

    /// <summary>Adds the time since <paramref name="start"/> to a NOVR slot.</summary>
    public static void End(PerfSlot slot, long start)
    {
        if (!_active) return;
        var i = (int)slot;
        SlotTicks[i] += Stopwatch.GetTimestamp() - start;
        SlotCalls[i]++;
    }

    private static readonly long[] SlotTicks = new long[(int)PerfSlot.Count];
    private static readonly int[] SlotCalls = new int[(int)PerfSlot.Count];

    // ------------------------------------------------------------ player loop

    /// <summary>Marker type the inserted loop systems carry, so they are recognisable in the loop.</summary>
    private struct NovrPerfMarker
    {
    }

    private static bool _active;
    private static bool _loopInstalled;
    private static long _lastMarker;
    private static string[] _loopNames = Array.Empty<string>();
    private static long[] _loopTicks = Array.Empty<long>();
    private static long[] _frameLoopTicks = Array.Empty<long>();
    private static long[] _spikeLoopTicks = Array.Empty<long>();
    private static int _gapIndex = -1;
    private static int _fixedSteps;

    // ------------------------------------------------------------ slow frames

    private static long _lastFrameStart;
    private static long _spikeTicks = long.MaxValue;
    private static int _spikeFrames;
    private static long _spikeTotalTicks;
    private static int _spikeGc;
    private static int _frameFixedSteps;
    private static int _lastGcCount;
    private static float _windowStartStatic;
    private static readonly List<(double Ms, string Text)> WorstSpikes = new(4);
    private static readonly List<float> SpikeTimes = new(64);
    private const int WorstSpikesKept = 3;
    private const int SpikeTimesKept = 40;

    private static void InstallPlayerLoopMarkers()
    {
        if (_loopInstalled) return;

        try
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();
            if (root.subSystemList == null) return;

            var names = new List<string>();
            var phases = root.subSystemList;
            for (var p = 0; p < phases.Length; p++)
            {
                var phase = phases[p];
                var subs = phase.subSystemList;
                if (subs == null || subs.Length == 0) continue;

                var phaseName = phase.type != null ? phase.type.Name : $"Phase{p}";
                var wrapped = new List<PlayerLoopSystem>(subs.Length * 2 + 1);
                for (var s = 0; s < subs.Length; s++)
                {
                    // Marker s closes the subsystem before it (if any) and opens
                    // subsystem s. The very first marker of the loop also closes
                    // the time spent outside the player loop and starts a frame.
                    var startsFrame = names.Count == 0;
                    var closes = s == 0 ? -1 : names.Count - 1;
                    var opensFixed = s == 0 && phaseName == "FixedUpdate";
                    wrapped.Add(Marker(closes, opensFixed, startsFrame));

                    var sub = subs[s];
                    names.Add($"{phaseName}.{(sub.type != null ? sub.type.Name : $"#{s}")}");
                    wrapped.Add(sub);
                }

                wrapped.Add(Marker(names.Count - 1, false, false));
                phase.subSystemList = wrapped.ToArray();
                phases[p] = phase;
            }

            root.subSystemList = phases;
            _gapIndex = names.Count;
            names.Add("outside the player loop");
            _loopNames = names.ToArray();
            _loopTicks = new long[_loopNames.Length];
            _frameLoopTicks = new long[_loopNames.Length];
            _spikeLoopTicks = new long[_loopNames.Length];
            PlayerLoop.SetPlayerLoop(root);
            _loopInstalled = true;
            Debug.Log($"[NOVR-PERF] Timing markers placed around {_gapIndex} player loop subsystems.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NOVR-PERF] Could not time the player loop ({e.Message}); frame times and cameras are still logged.");
        }
    }

    private static PlayerLoopSystem Marker(int closesIndex, bool opensFixedStep, bool startsFrame)
    {
        return new PlayerLoopSystem
        {
            type = typeof(NovrPerfMarker),
            updateDelegate = () =>
            {
                var now = Stopwatch.GetTimestamp();
                var closes = startsFrame ? _gapIndex : closesIndex;
                if (_active && _lastMarker != 0 && closes >= 0 && closes < _loopTicks.Length)
                {
                    var dt = now - _lastMarker;
                    _loopTicks[closes] += dt;
                    _frameLoopTicks[closes] += dt;
                }

                if (startsFrame) OnFrameStart(now);
                if (opensFixedStep)
                {
                    _fixedSteps++;
                    _frameFixedSteps++;
                }

                _lastMarker = now;
            },
        };
    }

    /// <summary>
    /// Closes the frame that just ended. A frame over the spike threshold is
    /// counted, and the worst few are kept with where their time went, so the
    /// periodic stalls can be told apart from the steady load.
    /// </summary>
    private static void OnFrameStart(long now)
    {
        var spike = false;
        if (_active && _lastFrameStart != 0)
        {
            // A collection in the frame that just ended shows up here as a changed count.
            var gc = GC.CollectionCount(0);
            var hadGc = gc != _lastGcCount;
            _lastGcCount = gc;

            var frameTicks = now - _lastFrameStart;
            spike = frameTicks > _spikeTicks;
            if (spike) RecordSpike(frameTicks, hadGc);
        }

        ScriptProfiler.OnFrameBoundary(spike);
        _lastFrameStart = now;
        _frameFixedSteps = 0;
        Array.Clear(_frameLoopTicks, 0, _frameLoopTicks.Length);
    }

    private static void RecordSpike(long frameTicks, bool hadGc)
    {
        _spikeFrames++;
        _spikeTotalTicks += frameTicks;
        for (var i = 0; i < _frameLoopTicks.Length; i++) _spikeLoopTicks[i] += _frameLoopTicks[i];
        if (hadGc) _spikeGc++;

        if (SpikeTimes.Count < SpikeTimesKept) SpikeTimes.Add(Time.unscaledTime - _windowStartStatic);

        var ms = Ms(frameTicks);
        if (WorstSpikes.Count >= WorstSpikesKept && ms <= WorstSpikes[WorstSpikes.Count - 1].Ms) return;

        // Only the few worst frames of a window get here, so the allocation is rare.
        var text = $"{ms:F0} ms{(hadGc ? " with a GC" : "")}, physics steps {_frameFixedSteps}: " +
                   $"{Top(_frameLoopTicks, 1, 4)} | scripts: {ScriptProfiler.DescribeFrame(4)}";
        var at = 0;
        while (at < WorstSpikes.Count && WorstSpikes[at].Ms >= ms) at++;
        WorstSpikes.Insert(at, (ms, text));
        if (WorstSpikes.Count > WorstSpikesKept) WorstSpikes.RemoveAt(WorstSpikes.Count - 1);
    }

    // ------------------------------------------------------------ cameras

    private sealed class CameraStat
    {
        public string Name = "";
        public long Ticks;
        public int Renders;
        public long OpenedAt;
    }

    private static readonly Dictionary<int, CameraStat> Cameras = new();

    private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (!_active || camera == null) return;
        var id = camera.GetInstanceID();
        if (!Cameras.TryGetValue(id, out var stat))
        {
            stat = new CameraStat { Name = camera.name };
            Cameras[id] = stat;
        }

        stat.OpenedAt = Stopwatch.GetTimestamp();
    }

    private static void OnEndCamera(ScriptableRenderContext context, Camera camera)
    {
        if (!_active || camera == null) return;
        if (!Cameras.TryGetValue(camera.GetInstanceID(), out var stat) || stat.OpenedAt == 0) return;
        stat.Ticks += Stopwatch.GetTimestamp() - stat.OpenedAt;
        stat.Renders++;
        stat.OpenedAt = 0;
    }

    // ------------------------------------------------------------ frames

    private readonly List<float> _frameMs = new(2048);
    private readonly FrameTiming[] _timing = new FrameTiming[1];
    private double _cpuMainMs, _cpuRenderMs, _gpuMs;
    private int _timingSamples;
    private float _windowStart;
    private int _gcAtStart;
    private int _window;

    private void Awake()
    {
        InstallPlayerLoopMarkers();
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
        ResetWindow();
    }

    private void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        _active = false;
    }

    private void Update()
    {
        var enabledNow = PerfMonitorConfig.Enabled?.Value ?? true;
        if (enabledNow != _active)
        {
            _active = enabledNow;
            ResetWindow();
        }

        ScriptProfiler.Active = _active;
        if (!_active) return;

        var threshold = Mathf.Clamp(PerfMonitorConfig.SpikeThresholdMs?.Value ?? 40f, 15f, 1000f);
        _spikeTicks = (long)(threshold / 1000.0 * Stopwatch.Frequency);
        ScriptProfiler.Tick();

        _frameMs.Add(Time.unscaledDeltaTime * 1000f);
        SampleFrameTiming();

        var interval = Mathf.Clamp(PerfMonitorConfig.Interval?.Value ?? 10f, 2f, 300f);
        if (Time.unscaledTime - _windowStart < interval) return;

        try
        {
            Report(Time.unscaledTime - _windowStart);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NOVR-PERF] Report failed: {e.Message}");
        }

        ResetWindow();
    }

    private void SampleFrameTiming()
    {
        try
        {
            if (!FrameTimingManager.IsFeatureEnabled()) return;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timing) < 1) return;

            var t = _timing[0];
            if (t.cpuMainThreadFrameTime <= 0 && t.gpuFrameTime <= 0) return;
            _cpuMainMs += t.cpuMainThreadFrameTime;
            _cpuRenderMs += t.cpuRenderThreadFrameTime;
            _gpuMs += t.gpuFrameTime;
            _timingSamples++;
        }
        catch (Exception)
        {
            // Not available on this build; the frame interval still says enough.
        }
    }

    private void ResetWindow()
    {
        _frameMs.Clear();
        _cpuMainMs = _cpuRenderMs = _gpuMs = 0;
        _timingSamples = 0;
        _windowStart = Time.unscaledTime;
        _windowStartStatic = _windowStart;
        _gcAtStart = GC.CollectionCount(0);
        _lastGcCount = _gcAtStart;
        _fixedSteps = 0;
        Array.Clear(_loopTicks, 0, _loopTicks.Length);
        Array.Clear(_spikeLoopTicks, 0, _spikeLoopTicks.Length);
        _spikeFrames = 0;
        _spikeTotalTicks = 0;
        _spikeGc = 0;
        WorstSpikes.Clear();
        SpikeTimes.Clear();
        ScriptProfiler.ResetWindow();
        UiRebuildProfiler.ResetWindow();
        Array.Clear(SlotTicks, 0, SlotTicks.Length);
        Array.Clear(SlotCalls, 0, SlotCalls.Length);
        // Cameras that did not render in the last window are dropped, so a
        // capture camera rebuilt many times does not leave its ghosts behind.
        StaleCameras.Clear();
        foreach (var pair in Cameras)
        {
            if (pair.Value.Renders == 0 && pair.Value.OpenedAt == 0) StaleCameras.Add(pair.Key);
            pair.Value.Ticks = 0;
            pair.Value.Renders = 0;
        }

        foreach (var id in StaleCameras) Cameras.Remove(id);
    }

    private static readonly List<int> StaleCameras = new();

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private void Report(float seconds)
    {
        var frames = _frameMs.Count;
        if (frames == 0) return;
        _window++;

        _frameMs.Sort();
        double sum = 0;
        var over20 = 0;
        var over50 = 0;
        foreach (var ms in _frameMs)
        {
            sum += ms;
            if (ms > 20f) over20++;
            if (ms > 50f) over50++;
        }

        var avg = sum / frames;
        var p95 = _frameMs[Mathf.Clamp((int)(frames * 0.95f), 0, frames - 1)];
        var max = _frameMs[frames - 1];

        var line = new StringBuilder(256);
        line.Append($"[NOVR-PERF] {seconds:F0}s {frames} frames ({frames / seconds:F0} fps): ");
        line.Append($"avg {avg:F1} ms, p95 {p95:F1}, max {max:F1}, >20ms {over20}, >50ms {over50}");
        if (_timingSamples > 0)
        {
            line.Append($" | cpu main {_cpuMainMs / _timingSamples:F1} ms, render thread {_cpuRenderMs / _timingSamples:F1}, gpu {_gpuMs / _timingSamples:F1}");
        }

        line.Append($" | physics steps/frame {(double)_fixedSteps / frames:F2}, GC {GC.CollectionCount(0) - _gcAtStart}, heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB");
        Debug.Log(line.ToString());

        if (_loopInstalled) Debug.Log(TopLoop(frames));
        if (_loopInstalled && _spikeFrames > 0) Debug.Log(SpikeReport());
        Debug.Log(TopCameras(frames));
        Debug.Log(NovrSlots(frames));
        foreach (var scriptLine in ScriptProfiler.Report(frames, _spikeFrames)) Debug.Log(scriptLine);
        foreach (var uiLine in UiRebuildProfiler.Report(frames)) Debug.Log(uiLine);

        // Allocating scans, so rarely: they are here to catch leaks (render
        // targets or cameras that pile up) and to put a unit count next to the
        // numbers above.
        if (_window % 6 == 1) Debug.Log(SceneCounts());
    }

    /// <summary>The <paramref name="count"/> largest entries of a per-subsystem tick array, in ms divided by <paramref name="divisor"/>.</summary>
    private static string Top(long[] ticks, int divisor, int count)
    {
        var sb = new StringBuilder(256);
        var used = new List<int>(count);
        for (var k = 0; k < count; k++)
        {
            var best = -1;
            for (var i = 0; i < ticks.Length; i++)
            {
                if (ticks[i] <= 0 || used.Contains(i)) continue;
                if (best < 0 || ticks[i] > ticks[best]) best = i;
            }

            if (best < 0) break;
            used.Add(best);
            if (k > 0) sb.Append(", ");
            sb.Append($"{_loopNames[best]} {Ms(ticks[best]) / divisor:F1}");
        }

        return sb.ToString();
    }

    private static string SpikeReport()
    {
        var sb = new StringBuilder(1024);
        var threshold = Ms(_spikeTicks);
        sb.Append($"[NOVR-PERF] slow frames (>{threshold:F0} ms): {_spikeFrames}, avg {Ms(_spikeTotalTicks) / _spikeFrames:F0} ms, {_spikeGc} with a GC");
        if (ScriptProfiler.SettingUp) sb.Append(" (script timing still being set up, which causes slow frames of its own)");
        sb.Append("; ");
        sb.Append($"per slow frame: {Top(_spikeLoopTicks, _spikeFrames, 6)}");
        sb.Append(" | at s: ");
        for (var i = 0; i < SpikeTimes.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(SpikeTimes[i].ToString("F1"));
        }

        for (var i = 0; i < WorstSpikes.Count; i++)
        {
            sb.Append($"\n[NOVR-PERF] slow frame #{i + 1}: {WorstSpikes[i].Text}");
        }

        return sb.ToString();
    }

    private string TopLoop(int frames)
    {
        var order = new List<int>(_loopTicks.Length);
        double total = 0;
        for (var i = 0; i < _loopTicks.Length; i++)
        {
            if (i != _gapIndex) total += _loopTicks[i];
            if (_loopTicks[i] > 0) order.Add(i);
        }

        order.Sort((a, b) => _loopTicks[b].CompareTo(_loopTicks[a]));

        var sb = new StringBuilder(512);
        sb.Append($"[NOVR-PERF] main thread {Ms((long)total) / frames:F1} ms/frame in the player loop, top: ");
        for (var k = 0; k < order.Count && k < 10; k++)
        {
            var i = order[k];
            if (k > 0) sb.Append(", ");
            sb.Append($"{_loopNames[i]} {Ms(_loopTicks[i]) / frames:F2}");
        }

        return sb.ToString();
    }

    private static string TopCameras(int frames)
    {
        var stats = new List<CameraStat>();
        foreach (var stat in Cameras.Values)
        {
            if (stat.Renders > 0) stats.Add(stat);
        }

        stats.Sort((a, b) => b.Ticks.CompareTo(a.Ticks));

        var sb = new StringBuilder(512);
        sb.Append($"[NOVR-PERF] cameras ({stats.Count} rendering), ms/frame and renders/frame: ");
        for (var k = 0; k < stats.Count && k < 10; k++)
        {
            if (k > 0) sb.Append(", ");
            var s = stats[k];
            sb.Append($"{s.Name} {Ms(s.Ticks) / frames:F2} ({(double)s.Renders / frames:F1}x)");
        }

        return sb.ToString();
    }

    private static string NovrSlots(int frames)
    {
        var sb = new StringBuilder(256);
        double total = 0;
        for (var i = 0; i < SlotTicks.Length; i++) total += SlotTicks[i];
        sb.Append($"[NOVR-PERF] NOVR code {Ms((long)total) / frames:F2} ms/frame: ");
        for (var i = 0; i < SlotTicks.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"{(PerfSlot)i} {Ms(SlotTicks[i]) / frames:F2}");
            if (SlotCalls[i] > frames) sb.Append($" ({(double)SlotCalls[i] / frames:F0} calls)");
        }

        return sb.ToString();
    }

    private static string SceneCounts()
    {
        var units = 0;
        var canvases = 0;
        var renderTextures = 0;
        try
        {
            units = FindObjectsOfType<global::Unit>().Length;
            canvases = FindObjectsOfType<Canvas>().Length;
            renderTextures = Resources.FindObjectsOfTypeAll<RenderTexture>().Length;
        }
        catch (Exception)
        {
            // Counts are context, never worth failing the report over.
        }

        var sb = new StringBuilder(512);
        sb.Append($"[NOVR-PERF] scene: units {units}, active canvases {canvases}, render textures {renderTextures}, cameras {Camera.allCamerasCount}: ");
        var cams = Camera.allCameras;
        for (var i = 0; i < cams.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(cams[i].name);
        }

        return sb.ToString();
    }
}

/// <summary>NOVR's own per-frame code paths, as reported by <see cref="PerfMonitor"/>.</summary>
public enum PerfSlot
{
    Cursor,
    MenuCapture,
    HudCapture,
    HmdVisor,
    ViewLayer,
    UiManager,
    CameraManager,
    OffscreenCuller,
    UnitMarkers,
    Count,
}

[ConfigSection(Order = 97)]
public static class PerfMonitorConfig
{
    public static ConfigEntry<bool>? Enabled;
    public static ConfigEntry<float>? Interval;
    public static ConfigEntry<bool>? Scripts;
    public static ConfigEntry<float>? SpikeThresholdMs;

    public static void Bind(ConfigFile config)
    {
        Enabled = config.Bind(
            "Debug",
            "Performance Log",
            true,
            "Write frame times to the log every few seconds: how long frames take, which part of the "
            + "game loop (physics, scripts, UI rebuilds, rendering, the headset's frame wait) the time "
            + "goes to, what each camera costs and how much of it is NOVR's own code. Costs a few "
            + "microseconds per frame. Lines start with [NOVR-PERF].");

        Interval = config.Bind(
            "Debug",
            "Performance Log Interval",
            10f,
            "Seconds between [NOVR-PERF] reports.");

        Scripts = config.Bind(
            "Debug",
            "Performance Log Scripts",
            true,
            "Also time the Update methods, coroutines and Harmony patches of the game and of other "
            + "mods, so the log names the scripts the time goes to and those behind slow frames. "
            + "Starts 20 s after launch and takes a few seconds to set up. Costs up to about a "
            + "millisecond per frame in a full session. Read at startup: switching it off takes "
            + "effect on the next start.");

        SpikeThresholdMs = config.Bind(
            "Debug",
            "Performance Log Spike Threshold",
            40f,
            "A frame longer than this many milliseconds counts as a slow frame; the log lists the "
            + "worst of them with what they were spent on.");
    }
}
