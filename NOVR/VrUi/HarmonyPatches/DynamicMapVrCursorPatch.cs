using HarmonyLib;
using UnityEngine;
using System;

namespace NOVR.VrUi.HarmonyPatches;

internal static class DynamicMapVrCursorPatch
{
    [HarmonyPatch(typeof(global::DynamicMap), "SelectFromMap")]
    private static class SelectFromMapPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(global::DynamicMap __instance)
        {
            if (NOUIManager.I == null || APIBus.CockpitHudCamera == null)
            {
                return true;
            }
            if (VrUiCursor.Instance != null && VrUiCursor.Instance.IsActive)
            {
                var cursorScreenPoint = VrUiCursor.Instance.GetScreenPoint();
                var icons = UnityEngine.Object.FindObjectsOfType<global::UnitMapIcon>();
                
                global::UnitMapIcon? closestIcon = null;
                float closestSqrDistance = float.MaxValue;
                
                foreach (var icon in icons)
                {
                    if (icon == null || icon.unit == null) continue;
                    if (icon.iconImage == null || !icon.iconImage.raycastTarget) continue;
                    if (global::SceneSingleton<global::TargetListSelector>.i != null &&
                        global::SceneSingleton<global::TargetListSelector>.i.CheckExclusions(icon.unit))
                    {
                        continue;
                    }
                    
                    Vector3 iconWorldPosition = icon.transform.position;
                    Vector2 iconScreenPoint = APIBus.CockpitHudCamera.WorldToScreenPoint(iconWorldPosition);
                    
                    float sqrDistance = (iconScreenPoint - cursorScreenPoint).sqrMagnitude;
                    if (sqrDistance < closestSqrDistance)
                    {
                        closestSqrDistance = sqrDistance;
                        closestIcon = icon;
                    }
                }
                
                if (closestIcon != null && closestSqrDistance <= 10000f)
                {
                    closestIcon.ClickIcon((global::MapIcon.ClickSource)1);
                }
                
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(global::DynamicMap), "IsCursorInMapRectangle")]
    private static class IsCursorInMapRectanglePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(global::DynamicMap __instance, ref bool __result)
        {
            if (NOUIManager.I == null || APIBus.CockpitHudCamera == null)
            {
                return true;
            }
            if (VrUiCursor.Instance != null && VrUiCursor.Instance.IsActive)
            {
                var screenPoint = VrUiCursor.Instance.GetScreenPoint();
                __result = RectTransformUtility.RectangleContainsScreenPoint(
                    __instance.mapBackground.rectTransform,
                    screenPoint,
                    APIBus.CockpitHudCamera
                );
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(global::DynamicMap), "GetCursorCoordinates")]
    private static class GetCursorCoordinatesPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(global::DynamicMap __instance, ref global::GlobalPosition __result)
        {
            if (NOUIManager.I == null || APIBus.CockpitHudCamera == null)
            {
                return true;
            }
            if (VrUiCursor.Instance != null && VrUiCursor.Instance.IsActive)
            {
                var screenPoint = VrUiCursor.Instance.GetScreenPoint();
                var mapImageRect = __instance.mapImage.GetComponent<RectTransform>();
                
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    mapImageRect,
                    screenPoint,
                    APIBus.CockpitHudCamera,
                    out var localPoint
                );
                
                float scaleFactor = __instance.mapDimension / 900.0f;
                Vector2 worldCoords = localPoint * scaleFactor;
                
                __result = new global::GlobalPosition(worldCoords.x, 0f, worldCoords.y);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(global::MapWaypoint), MethodType.Constructor, new Type[] { typeof(Vector3), typeof(Vector3), typeof(GameObject), typeof(GameObject) })]
    private static class MapWaypointConstructorPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ref Vector3 __0)
        {
            if (VrUiCursor.Instance != null && VrUiCursor.Instance.IsActive)
            {
                __0 = VrUiCursor.Instance.CursorPosition;
            }
        }
    }
}
