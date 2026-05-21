using HarmonyLib;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace NOVR.VrUi.HarmonyPatches;

internal static class HUDBombingStateViewPositionPatch
{
    private const float HudDistance = 1000.0f;

    private static readonly FieldInfo AlignmentBarField = AccessTools.Field(typeof(global::HUDBombingState), "alignmentBar");
    private static readonly FieldInfo CcipPipperField = AccessTools.Field(typeof(global::HUDBombingState), "ccipPipper");
    private static readonly FieldInfo CcipLineField = AccessTools.Field(typeof(global::HUDBombingState), "ccipLine");
    private static readonly FieldInfo CcipFallTimeField = AccessTools.Field(typeof(global::HUDBombingState), "ccipFallTime");
    private static readonly FieldInfo CcrpFallTimeField = AccessTools.Field(typeof(global::HUDBombingState), "ccrpFallTime");
    private static readonly FieldInfo DropCountdownField = AccessTools.Field(typeof(global::HUDBombingState), "dropCountdown");
    private static readonly FieldInfo CcrpCircleField = AccessTools.Field(typeof(global::HUDBombingState), "ccrpCircle");
    private static readonly FieldInfo AverageTargetPositionField = AccessTools.Field(typeof(global::HUDBombingState), "averageTargetPosition");
    private static readonly FieldInfo CcipImpactPointSmoothedField = AccessTools.Field(typeof(global::HUDBombingState), "ccipImpactPointSmoothed");

    [HarmonyPatch(typeof(global::HUDBombingState), nameof(global::HUDBombingState.UpdateWeaponDisplay))]
    private static class UpdateWeaponDisplayPatch
    {
        [HarmonyPostfix]
        private static void Postfix(global::HUDBombingState __instance, Aircraft aircraft)
        {
            var mainCamera = EventBus.MainCamera;
            var cockpitHudCamera = EventBus.CockpitHudCamera;
            if (mainCamera == null || cockpitHudCamera == null || aircraft == null)
                return;

            __instance.transform.localPosition = Vector3.zero;
            __instance.transform.rotation = cockpitHudCamera.transform.rotation;

            UpdateCcrpDisplay(__instance, aircraft);
            UpdateCcipDisplay(__instance);
        }
    }

    private static void UpdateCcrpDisplay(global::HUDBombingState state, Aircraft aircraft)
    {
        var alignmentBar = (Image)AlignmentBarField.GetValue(state);
        var ccrpCircle = (Image)CcrpCircleField.GetValue(state);
        var dropCountdown = (Text)DropCountdownField.GetValue(state);
        var ccrpFallTime = (Text)CcrpFallTimeField.GetValue(state);
        var cockpitHudCamera = EventBus.CockpitHudCamera;
        if (alignmentBar == null || cockpitHudCamera == null || !alignmentBar.gameObject.activeSelf)
            return;

        var averageTargetPosition = (GlobalPosition)AverageTargetPositionField.GetValue(state);
        var targetDelta = averageTargetPosition - aircraft.GlobalPosition();
        var horizontalTargetDelta = new Vector3(targetDelta.x, 0.0f, targetDelta.z);
        var verticalOffset = -targetDelta.y + Vector3.Project(horizontalTargetDelta, aircraft.transform.forward).y;
        var upperWorldPosition = averageTargetPosition.ToLocalPosition() + Vector3.up * verticalOffset;
        var lowerWorldPosition = averageTargetPosition.ToLocalPosition() + Vector3.up * verticalOffset * 0.9f;

        if (!TryProjectToCockpitHud(upperWorldPosition, out var upperHudPosition) ||
            !TryProjectToCockpitHud(lowerWorldPosition, out var lowerHudPosition))
        {
            alignmentBar.gameObject.SetActive(false);
            return;
        }

        alignmentBar.transform.position = upperHudPosition;
        alignmentBar.transform.rotation = GetRotationAlongHudSegment(upperHudPosition, lowerHudPosition, cockpitHudCamera);

        if (ccrpCircle != null)
            ccrpCircle.transform.rotation = cockpitHudCamera.transform.rotation;

        if (dropCountdown != null)
            dropCountdown.transform.rotation = cockpitHudCamera.transform.rotation;

        if (ccrpFallTime != null)
            ccrpFallTime.transform.rotation = cockpitHudCamera.transform.rotation;
    }

    private static void UpdateCcipDisplay(global::HUDBombingState state)
    {
        var ccipPipper = (Image)CcipPipperField.GetValue(state);
        var ccipLine = (Image)CcipLineField.GetValue(state);
        var ccipFallTime = (Text)CcipFallTimeField.GetValue(state);
        var velocityVector = SceneSingleton<FlightHud>.i.velocityVector;
        var cockpitHudCamera = EventBus.CockpitHudCamera;
        if (ccipPipper == null || ccipLine == null || cockpitHudCamera == null || velocityVector == null || !ccipPipper.enabled)
            return;

        var ccipImpactPointSmoothed = (Vector3)CcipImpactPointSmoothedField.GetValue(state);
        var impactWorldPosition = ccipImpactPointSmoothed + Datum.origin.position;
        if (!TryProjectToCockpitHud(impactWorldPosition, out var pipperHudPosition))
        {
            ccipPipper.enabled = false;
            ccipLine.enabled = false;
            return;
        }

        ccipPipper.transform.position = pipperHudPosition;
        ccipPipper.transform.rotation = cockpitHudCamera.transform.rotation;

        var velocityHudPosition = velocityVector.transform.position;
        var lineDirection = velocityHudPosition - pipperHudPosition;
        if (lineDirection.sqrMagnitude <= Mathf.Epsilon)
        {
            ccipLine.enabled = false;
            return;
        }

        var normalizedLineDirection = lineDirection.normalized;
        var lineStart = pipperHudPosition + normalizedLineDirection * 22.0f;
        var lineEnd = velocityHudPosition - normalizedLineDirection * 8.0f;
        var lineVector = lineEnd - lineStart;
        if (Vector3.Dot(lineDirection, lineVector) < 0.0f)
        {
            ccipLine.enabled = false;
            return;
        }

        ccipLine.transform.position = lineStart;
        ccipLine.transform.rotation = Quaternion.LookRotation(
            cockpitHudCamera.transform.forward,
            lineVector.sqrMagnitude > Mathf.Epsilon ? lineVector.normalized : cockpitHudCamera.transform.up);
        ccipLine.transform.localScale = new Vector3(1.0f, lineVector.magnitude, 1.0f);

        if (ccipFallTime != null)
            ccipFallTime.transform.rotation = cockpitHudCamera.transform.rotation;
    }

    private static Quaternion GetRotationAlongHudSegment(Vector3 start, Vector3 end, Camera cockpitHudCamera)
    {
        var segment = end - start;
        if (segment.sqrMagnitude <= Mathf.Epsilon)
            return cockpitHudCamera.transform.rotation;

        return Quaternion.LookRotation(cockpitHudCamera.transform.forward, segment.normalized);
    }

    private static bool TryProjectToCockpitHud(Vector3 worldPosition, out Vector3 hudPosition)
    {
        hudPosition = Vector3.zero;
        var mainCamera = EventBus.MainCamera;
        var cockpitHudCamera = EventBus.CockpitHudCamera;
        if (mainCamera == null || cockpitHudCamera == null)
            return false;

        var mainCameraLocal = mainCamera.transform.InverseTransformPoint(worldPosition);
        if (mainCameraLocal.z <= 0.0f)
            return false;

        hudPosition = cockpitHudCamera.transform.TransformPoint(mainCameraLocal).normalized * HudDistance;
        return true;
    }
}
