using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NOVR;

public static class LayerHelper
{
    
    
    // Check me on game updates!!
    [Flags]
    public enum Layers
    {
        
        // Base game
        Default = 1 << 0,
        TransparentFX = 1 << 1,
        IgnoreRaycast = 1 << 2,
        Cockpit = 1 << 3,
        Water = 1 << 4,
        UI = 1 << 5,
        Statics = 1 << 6,
        PP = 1 << 7,
        TargetCamPP = 1 << 8,
        HUD = 1 << 9,
        Effects = 1 << 10,
        Ship = 1 << 11,
        Sun = 1 << 12,
        ExclusionZones = 1 << 13,
        CockpitAndExternal = 1 << 14,
        IgnoreCollision = 1 << 15,
        PreviewRender = 1 << 16,
        EditorSelectOnly = 1 << 17,
        GrassBlockerProxy = 1 << 18,
        
        
        
        // Ours
        VrUi = 1 << 30,
        VrUiCapture = 1 << 31,
    }

    public static Layers GetVrUiLayer() => Layers.VrUi;

    public static Layers GetVrUiCaptureLayer() => Layers.VrUiCapture;

    public static void SetLayerRecursive(Transform transform, Layers layer)
    {
        transform.gameObject.layer = (int)layer;

        // Not using the usual foreach Transform etc because it fails in silly il2cpp.
        for (var index = 0; index < transform.childCount; index++)
        {
            var child = transform.GetChild(index);
            SetLayerRecursive(child, layer);
        }
    }
}
