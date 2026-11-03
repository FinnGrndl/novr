using System.Reflection;
using HarmonyLib;
using NuclearOption.Effects;
using UnityEngine;

namespace NOVR.GameplayPatches;

/// <summary>
/// Keeps Nuclear Option's GPU detail culling attached to NOVR's tracked camera.
/// </summary>
internal static class DetailRendererVrCameraFix
{
    private static readonly FieldInfo? DetailCameraField =
        AccessTools.Field(typeof(DetailRenderer), "camera");

    private static DetailRenderer? _boundRenderer;
    private static Camera? _boundCamera;
    private static bool _loggedMissingField;

    internal static void BindToTrackedCamera(Camera trackedCamera)
    {
        var detailRenderer = DetailRenderer.i;
        if (trackedCamera == null || detailRenderer == null)
        {
            return;
        }

        if (ReferenceEquals(_boundRenderer, detailRenderer) &&
            ReferenceEquals(_boundCamera, trackedCamera))
        {
            return;
        }

        if (DetailCameraField == null)
        {
            if (!_loggedMissingField)
            {
                Debug.LogError("[NOVR.TreeFix] Could not find DetailRenderer.camera; GPU detail culling was not rebound.");
                _loggedMissingField = true;
            }

            return;
        }

        DetailCameraField.SetValue(detailRenderer, trackedCamera);
        _boundRenderer = detailRenderer;
        _boundCamera = trackedCamera;

        Debug.Log(
            $"[NOVR.TreeFix] Bound DetailRenderer GPU detail culling to camera '{trackedCamera.gameObject.name}'.");
    }
}

/// <summary>
/// The GPU grass/tree culling planes come from <c>GeometryUtility.CalculateFrustumPlanes(camera)</c>,
/// which under XR is the mono desktop-FOV frustum, so detail only survives in a narrow cone in the
/// middle of the headset view. Replace them with the union of both eye frustums plus a margin
/// for head motion between LateUpdate and rendering.
/// </summary>
[HarmonyPatch(typeof(ShaderGlobalManager), nameof(ShaderGlobalManager.SetCameraPlanes))]
internal static class DetailCullingStereoFrustumPatch
{
    // Extra angle added on every side of the combined eye frustum.
    private const float MarginDegrees = 10f;
    private const float MaxHalfAngleDegrees = 85f;

    private static readonly Plane[] Planes = new Plane[6];
    private static readonly Vector4[] PlaneVectors = new Vector4[6];
    private static bool _loggedActive;

    [HarmonyPostfix]
    private static void Postfix(Camera camera)
    {
        if (camera == null || !camera.stereoEnabled)
        {
            return;
        }

        var cameraView = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * camera.transform.worldToLocalMatrix;
        float left = float.MaxValue, right = float.MinValue, bottom = float.MaxValue, top = float.MinValue;
        if (!AccumulateEye(camera, Camera.StereoscopicEye.Left, cameraView, ref left, ref right, ref bottom, ref top) ||
            !AccumulateEye(camera, Camera.StereoscopicEye.Right, cameraView, ref left, ref right, ref bottom, ref top))
        {
            return;
        }

        left = -WidenTangent(-left);
        right = WidenTangent(right);
        bottom = -WidenTangent(-bottom);
        top = WidenTangent(top);

        var near = camera.nearClipPlane;
        var far = camera.farClipPlane;
        var projection = Matrix4x4.Frustum(left * near, right * near, bottom * near, top * near, near, far);
        GeometryUtility.CalculateFrustumPlanes(projection * cameraView, Planes);
        for (var i = 0; i < 6; i++)
        {
            var normal = Planes[i].normal;
            PlaneVectors[i] = new Vector4(normal.x, normal.y, normal.z, Planes[i].distance);
        }

        Shader.SetGlobalVectorArray(ShaderGlobalManager.ID_Global_FrustumPlanes, PlaneVectors);

        if (!_loggedActive)
        {
            _loggedActive = true;
            Debug.Log(
                $"[NOVR.TreeFix] Detail culling uses the stereo frustum: horizontal " +
                $"{Mathf.Atan(-left) * Mathf.Rad2Deg:F0}/{Mathf.Atan(right) * Mathf.Rad2Deg:F0} deg, vertical " +
                $"{Mathf.Atan(-bottom) * Mathf.Rad2Deg:F0}/{Mathf.Atan(top) * Mathf.Rad2Deg:F0} deg " +
                $"(desktop FOV {camera.fieldOfView:F0} deg).");
        }
    }

    // Rotates the eye's frustum edge directions into the centre camera's view space and grows the
    // tangent bounds to cover them. Handles asymmetric and canted eye projections. Works on
    // directions read straight off the projection matrix: inverting the full view-projection
    // loses all precision with the game's far plane.
    private static bool AccumulateEye(
        Camera camera,
        Camera.StereoscopicEye eye,
        Matrix4x4 cameraView,
        ref float left,
        ref float right,
        ref float bottom,
        ref float top)
    {
        var projection = camera.GetStereoProjectionMatrix(eye);
        if (Mathf.Approximately(projection.m00, 0f) || Mathf.Approximately(projection.m11, 0f))
        {
            return false;
        }

        var eyeLeft = (projection.m02 - 1f) / projection.m00;
        var eyeRight = (projection.m02 + 1f) / projection.m00;
        var eyeBottom = (projection.m12 - 1f) / projection.m11;
        var eyeTop = (projection.m12 + 1f) / projection.m11;

        var eyeToCameraView = cameraView * camera.GetStereoViewMatrix(eye).inverse;
        for (var corner = 0; corner < 4; corner++)
        {
            var eyeDirection = new Vector3(
                (corner & 1) == 0 ? eyeLeft : eyeRight,
                (corner & 2) == 0 ? eyeBottom : eyeTop,
                -1f);
            var point = eyeToCameraView.MultiplyVector(eyeDirection);
            if (point.z >= 0f)
            {
                return false;
            }

            var tanX = point.x / -point.z;
            var tanY = point.y / -point.z;
            left = Mathf.Min(left, tanX);
            right = Mathf.Max(right, tanX);
            bottom = Mathf.Min(bottom, tanY);
            top = Mathf.Max(top, tanY);
        }

        return true;
    }

    private static float WidenTangent(float tangent)
    {
        var angle = Mathf.Atan(tangent) * Mathf.Rad2Deg + MarginDegrees;
        return Mathf.Tan(Mathf.Min(angle, MaxHalfAngleDegrees) * Mathf.Deg2Rad);
    }
}
