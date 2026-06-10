using HarmonyLib;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOVR.VrUi.HarmonyPatches;

internal static class TMPDropdownVrLayerPatch
{
    private static readonly FieldInfo DropdownField = AccessTools.Field(typeof(TMP_Dropdown), "m_Dropdown");

    [HarmonyPatch(typeof(TMP_Dropdown), nameof(TMP_Dropdown.Show))]
    private static class ShowPatch
    {
        [HarmonyPostfix]
        private static void Postfix(TMP_Dropdown __instance)
        {
            if (__instance == null || !IsInVrLayerHierarchy(__instance.transform))
                return;

            var dropdown = (GameObject)DropdownField.GetValue(__instance);
            if (dropdown == null)
                return;
            
            LayerHelper.SetLayerRecursive(dropdown.transform, LayerHelper.Layers.VrUi);
            
            var mask = dropdown.gameObject.GetComponentInChildren<Mask>();
            if (mask) mask.enabled = false;
        }
    }

    private static bool IsInVrLayerHierarchy(Transform transform)
    {
        var vrLayer = LayerHelper.Layers.VrUi;
        while (transform != null)
        {
            if (transform.gameObject.layer == (int)vrLayer)
                return true;
            
            transform = transform.parent;
        }

        return false;
    }
}
