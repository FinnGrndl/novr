using System;
using NOVR.VrCamera;
using NOVR.VrUi.Capture;
using NOVR.VrUi.Native;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NOVR.VrUi;

public class NOUIManager : NOVRBehaviour
{
    private const float SmoothingFactor = 10f;
    private Camera? _cockpitHudCamera;
    private GameObject? _smoothedForwardReference;

    public static NOUIManager I { get; private set; }

    public Camera CockpitHudCamera => _cockpitHudCamera ??= CreateUiCamera("VrCockpitHudCamera", 100);
    public GameObject CockpitHudReference => _smoothedForwardReference ??= CreateSmoothedForwardReference();

    private GameObject CreateSmoothedForwardReference()
    {
        var go = new GameObject("SmoothedForwardReference");
        go.transform.SetParent(transform);
        
        var cam = CockpitHudCamera;
        go.transform.position = cam.transform.position;
        go.transform.localRotation = cam.transform.localRotation;
        return go;
    }

    private new void Awake()
    {
        base.Awake();
        APIBus.OnMainCameraChanged += OnMainCameraChanged;
    }

    private void OnDestroy()
    {
        APIBus.OnMainCameraChanged -= OnMainCameraChanged;
    }

    private void Start()
    {
        I = this;
        Create<UIBehaviorPatcher>(transform);
        UIBehaviorPatcher.DoPatching();
        Create<VrUiCursor>(transform);
        Create<MenuCaptureBackend>(transform);
        _imguiBegin = Create<ImguiCaptureBegin>(transform);
        _imguiEnd = Create<ImguiCaptureEnd>(transform);
        Create<FlightHudCaptureBackend>(transform);
        Create<HmdVisorBackend>(transform);
        Create<ViewLayerBackend>(transform);
        Create<Components.GazeDesignatorDriver>(transform);
        Create<NativeVrUiRoot>(transform);
        ConfigureUiCameras();
    }

    protected override void OnSettingChanged()
    {
        base.OnSettingChanged();
        ConfigureUiCameras();
    }

    private ImguiCaptureBegin? _imguiBegin;
    private ImguiCaptureEnd? _imguiEnd;

    private void Update()
    {
        var perfStart = PerfMonitor.Begin();
        UpdateCore();
        PerfMonitor.End(PerfSlot.UiManager, perfStart);
    }

    private void UpdateCore()
    {
        ConfigureUiCameras();
        UpdateSmoothedPosition();

        // An active OnGUI costs Layout and Repaint events every frame, so these
        // two only run while the F1 window is open.
        ImguiCapture.Tick();
        var imguiOpen = ImguiCapture.WindowOpen;
        if (_imguiBegin != null && _imguiBegin.enabled != imguiOpen) _imguiBegin.enabled = imguiOpen;
        if (_imguiEnd != null && _imguiEnd.enabled != imguiOpen) _imguiEnd.enabled = imguiOpen;
    }
    
    private void UpdateSmoothedPosition()
    {
        var smoothedForwardReference = CockpitHudReference;
        var cam = CockpitHudCamera;
        smoothedForwardReference.transform.position = cam.transform.position;
        smoothedForwardReference.transform.localRotation = Quaternion.Lerp(smoothedForwardReference.transform.localRotation, cam.transform.localRotation, Mathf.Clamp(Time.deltaTime * SmoothingFactor, 0, 1));
    }

    

    private Camera CreateUiCamera(string cameraName, float depth)
    {
        var poseDriver = Create<NOVRPoseDriver>(transform);
        poseDriver.name = cameraName;

        var camera = poseDriver.gameObject.AddComponent<Camera>();
        var additionalCameraData = poseDriver.gameObject.AddComponent<UniversalAdditionalCameraData>();
        VrCameraManager.IgnoredCameras.Add(camera);
        

        camera.stereoTargetEye = StereoTargetEyeMask.Both;
        camera.targetTexture = null;
        camera.clearFlags = CameraClearFlags.Depth;
        camera.backgroundColor = Color.clear;
        camera.depth = depth;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.cullingMask = 1 << (int)LayerHelper.GetVrUiLayer();
        additionalCameraData.renderType = CameraRenderType.Overlay;

        return camera;
    }

    private void OnMainCameraChanged(Camera? previous, Camera? newCam)
    {
        if (newCam == null)
        {
            return;
        }

        var cockpitHudCamera = CockpitHudCamera;
        var cameraStack = newCam.gameObject.GetComponent<UniversalAdditionalCameraData>()?.cameraStack;
        if (cameraStack != null && !cameraStack.Contains(cockpitHudCamera))
        {
            cameraStack.Add(cockpitHudCamera);
        }
    }

    private void ConfigureUiCameras()
    {
        ConfigureUiCamera(CockpitHudCamera);
    }

    private static void ConfigureUiCamera(Camera camera)
    {
        
        if (camera.clearFlags != CameraClearFlags.Depth) camera.clearFlags = CameraClearFlags.Depth;
        if (camera.backgroundColor != Color.clear) camera.backgroundColor = Color.clear;
        if (camera.targetTexture != null) camera.targetTexture = null;
        if (camera.nearClipPlane != 0.01f) camera.nearClipPlane = 0.01f;
        if (camera.farClipPlane != 10000f) camera.farClipPlane = 10000f;
        if (camera.rect != FullViewport) camera.rect = FullViewport;
    }

    private static readonly Rect FullViewport = new(0f, 0f, 1f, 1f);
}
