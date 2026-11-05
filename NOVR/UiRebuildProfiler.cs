using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace NOVR;

/// <summary>
/// Which canvases rebuild their UI every frame, and what that costs.
///
/// <para><b>Why.</b> In a full multiplayer session the performance log put
/// <c>PostLateUpdate.PlayerUpdateCanvases</c> at about 2.9 ms a frame with
/// NOVR's captured HUD, against under 0.4 ms with NOVR 0.4.3. That subsystem is
/// the UI's own rebuild pass (layout, and regenerating the mesh of every
/// graphic whose vertices were dirtied) plus Unity's native batching of the
/// canvases that changed. The script timing in <see cref="ScriptProfiler"/>
/// leaves UnityEngine.UI out, so this names the canvases and elements instead.</para>
///
/// <para><b>How.</b> A prefix and postfix on <c>Graphic.Rebuild</c>, on
/// TextMeshPro's override of it, on <c>LayoutRebuilder.Rebuild</c> and on the
/// registry's whole <c>PerformUpdate</c>. Each rebuild is charged to its root
/// canvas and to the element itself. What the whole pass costs minus what these
/// account for is the native batching.</para>
/// </summary>
internal static class UiRebuildProfiler
{
    private sealed class Stat
    {
        public string Name = "";
        public long Ticks;
        public int Graphics;
        public int Layouts;
    }

    private static readonly Dictionary<int, Stat> ByCanvas = new();
    private static readonly Dictionary<int, Stat> ByElement = new();
    private const int MaxElementsTracked = 20000;

    private static AccessTools.FieldRef<LayoutRebuilder, RectTransform>? _layoutTarget;
    private static long _passTicks;
    private static int _passes;
    private static bool _installed;

    internal static void Install(Harmony harmony)
    {
        if (_installed) return;
        _installed = true;

        var installed = 0;
        try
        {
            _layoutTarget = AccessTools.FieldRefAccess<LayoutRebuilder, RectTransform>("m_ToRebuild");
        }
        catch (Exception)
        {
            _layoutTarget = null;
        }

        installed += TryPatch(harmony, AccessTools.Method(typeof(CanvasUpdateRegistry), "PerformUpdate"),
            nameof(PassPrefix), nameof(PassPostfix));
        installed += TryPatch(harmony, AccessTools.Method(typeof(Graphic), nameof(Graphic.Rebuild)),
            nameof(TimerPrefix), nameof(GraphicPostfix));
        installed += TryPatch(harmony, AccessTools.Method(typeof(TMPro.TextMeshProUGUI), nameof(Graphic.Rebuild)),
            nameof(TimerPrefix), nameof(GraphicPostfix));
        installed += TryPatch(harmony, AccessTools.Method(typeof(LayoutRebuilder), nameof(LayoutRebuilder.Rebuild)),
            nameof(TimerPrefix), nameof(LayoutPostfix));

        Debug.Log($"[NOVR-PERF] UI rebuild timing on {installed} of 4 methods.");
    }

    private static int TryPatch(Harmony harmony, System.Reflection.MethodInfo? target, string prefix, string postfix)
    {
        if (target == null) return 0;
        try
        {
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(UiRebuildProfiler), prefix) { priority = Priority.First },
                postfix: new HarmonyMethod(typeof(UiRebuildProfiler), postfix) { priority = Priority.Last });
            return 1;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NOVR-PERF] UI rebuild timing: {target.DeclaringType?.Name}.{target.Name} could not be patched: {e.Message}");
            return 0;
        }
    }

    // ------------------------------------------------------------ timers

    private static void PassPrefix(out long __state) => __state = Stopwatch.GetTimestamp();

    private static void PassPostfix(long __state)
    {
        if (!ScriptProfiler.Active || __state == 0) return;
        _passTicks += Stopwatch.GetTimestamp() - __state;
        _passes++;
    }

    private static void TimerPrefix(out long __state) => __state = Stopwatch.GetTimestamp();

    // The step is taken by position (__0): parameter names are not part of
    // the contract, and a rename would stop the patch from applying.
    private static void GraphicPostfix(Graphic __instance, CanvasUpdate __0, long __state)
    {
        // Graphics do their work in the PreRender step; the other steps are
        // empty calls and would only add noise and cost.
        if (!ScriptProfiler.Active || __state == 0 || __0 != CanvasUpdate.PreRender) return;
        if (__instance == null) return;

        var dt = Stopwatch.GetTimestamp() - __state;
        var canvas = __instance.canvas;
        Charge(canvas != null ? canvas.rootCanvas : null, dt, true);
        ChargeElement(__instance, dt);
    }

    private static void LayoutPostfix(LayoutRebuilder __instance, CanvasUpdate __0, long __state)
    {
        if (!ScriptProfiler.Active || __state == 0) return;
        var dt = Stopwatch.GetTimestamp() - __state;
        Canvas? root = null;
        var target = _layoutTarget != null ? _layoutTarget(__instance) : null;
        if (target != null)
        {
            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas != null) root = canvas.rootCanvas;
        }

        // Only the first step counts as a rebuild; the others are part of it.
        Charge(root, dt, __0 == CanvasUpdate.Layout ? false : (bool?)null);
    }

    private static void Charge(Canvas? root, long dt, bool? graphic)
    {
        var id = root != null ? root.GetInstanceID() : 0;
        if (!ByCanvas.TryGetValue(id, out var stat))
        {
            stat = new Stat { Name = root != null ? root.name : "(no canvas)" };
            ByCanvas[id] = stat;
        }

        stat.Ticks += dt;
        if (graphic == true) stat.Graphics++;
        else if (graphic == false) stat.Layouts++;
    }

    private static void ChargeElement(Graphic graphic, long dt)
    {
        var id = graphic.GetInstanceID();
        if (!ByElement.TryGetValue(id, out var stat))
        {
            if (ByElement.Count >= MaxElementsTracked) return;
            // The one allocation per element, the first time it rebuilds.
            var parent = graphic.transform.parent;
            stat = new Stat
            {
                Name = $"{(parent != null ? parent.name + "/" : "")}{graphic.name} ({graphic.GetType().Name})",
            };
            ByElement[id] = stat;
        }

        stat.Ticks += dt;
        stat.Graphics++;
    }

    // ------------------------------------------------------------ report

    internal static void ResetWindow()
    {
        _passTicks = 0;
        _passes = 0;
        foreach (var stat in ByCanvas.Values)
        {
            stat.Ticks = 0;
            stat.Graphics = 0;
            stat.Layouts = 0;
        }

        foreach (var stat in ByElement.Values)
        {
            stat.Ticks = 0;
            stat.Graphics = 0;
        }
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    internal static IEnumerable<string> Report(int frames)
    {
        if (!_installed || frames == 0 || _passes == 0) yield break;

        var canvases = new List<Stat>();
        long accounted = 0;
        foreach (var stat in ByCanvas.Values)
        {
            if (stat.Ticks <= 0) continue;
            canvases.Add(stat);
            accounted += stat.Ticks;
        }

        canvases.Sort((a, b) => b.Ticks.CompareTo(a.Ticks));
        var sb = new StringBuilder(1024);
        sb.Append($"[NOVR-PERF] UI rebuild pass {Ms(_passTicks) / frames:F2} ms/frame, of it graphics and layouts {Ms(accounted) / frames:F2}; ");
        sb.Append("by canvas, ms/frame (graphics, layouts per frame): ");
        for (var k = 0; k < canvases.Count && k < 10; k++)
        {
            var s = canvases[k];
            if (k > 0) sb.Append(", ");
            sb.Append($"{s.Name} {Ms(s.Ticks) / frames:F2} ({(double)s.Graphics / frames:F0}, {(double)s.Layouts / frames:F0})");
        }

        yield return sb.ToString();

        var elements = new List<Stat>();
        foreach (var stat in ByElement.Values)
        {
            if (stat.Graphics > 0) elements.Add(stat);
        }

        if (elements.Count == 0) yield break;

        // Ranked by how often, not how long: an element rebuilt every frame is
        // the one that keeps its canvas re-batching.
        elements.Sort((a, b) => b.Graphics != a.Graphics ? b.Graphics.CompareTo(a.Graphics) : b.Ticks.CompareTo(a.Ticks));
        var everyFrame = 0;
        foreach (var e in elements)
        {
            if (e.Graphics >= frames * 0.9) everyFrame++;
        }

        sb.Clear();
        sb.Append($"[NOVR-PERF] UI elements rebuilt: {elements.Count} in the window, {everyFrame} of them about every frame; most often (rebuilds/frame, ms/frame): ");
        for (var k = 0; k < elements.Count && k < 12; k++)
        {
            var e = elements[k];
            if (k > 0) sb.Append(", ");
            sb.Append($"{e.Name} {(double)e.Graphics / frames:F1} {Ms(e.Ticks) / frames:F2}");
        }

        yield return sb.ToString();
    }
}
