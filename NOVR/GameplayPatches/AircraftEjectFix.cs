using System;
using HarmonyLib;
using NaturalPoint.TrackIR;
using NOVR.VrUi.HarmonyPatches;
using UnityEngine;

namespace NOVR.HarmonyPatches;

internal static class AircraftEjectFix
{
    [HarmonyPatch(typeof(PilotDismounted), "Setup")]
    private static class SetupPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PilotDismounted __instance)
        {
            try
            {
                if (!UnitRegistry.TryGetUnit<Aircraft>(__instance.parentUnit, out var unit)) return;
                if (unit != SceneSingleton<CameraStateManager>.i.followingUnit ||
                    __instance.pilotNumber != (byte)0) return;
                var head = __instance.transform.Find("pilot/pilot_armature/pelvis/chest/neck/head");
                var helmetCamPoint = head?.Find("helmet_cam_point");
                if (helmetCamPoint == null) throw new Exception("helmetCamPoint is null");
                SceneSingleton<CameraStateManager>.i.SetCameraPosition(new GlobalPosition(helmetCamPoint.transform.position), Quaternion.identity);
                head?.gameObject.SetActive(false);
                
                LayerHelper.SetLayerRecursive(__instance.transform, LayerHelper.Layers.CockpitAndExternal);
            }
            catch (Exception e)
            {
                Debug.Log(e);
            }

        }
    }
}