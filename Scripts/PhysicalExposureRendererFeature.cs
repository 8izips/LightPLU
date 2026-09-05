using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Applies LightPLU's camera exposure before URP post-processing.
///
/// LightPLU lights are pre-exposed by 2^-ReferenceEV100. This pass applies
/// 2^(ReferenceEV100 - CameraEV100 + Compensation) so the combined result is
/// equivalent to direct physical exposure 2^(-CameraEV100 + Compensation).
///
/// Auto Exposure uses a lightweight 16x16 log-luminance meter. The GPU reduces
/// the 256 samples to one log-average luminance value and asynchronously returns
/// that single float. Exposure adaptation remains on the CPU in EV space, so
/// Speed Up / Speed Down are expressed directly in stops per second.
/// </summary>
public sealed class PhysicalExposureRendererFeature : ScriptableRendererFeature
{
    private const string ShaderName = "Hidden/LightPLU/PhysicalExposure";
    private const string ExposureShaderResourceName = "LightPLUPhysicalExposure";
    private const string AutoExposureResourceName = "LightPLUPhysicalExposureAuto";

    [SerializeField]
    [Tooltip("Full-screen shader used to multiply scene-linear color by the physical exposure multiplier. If empty, LightPLU loads it from Resources automatically.")]
    private Shader exposureShader;

    [SerializeField]
    [Tooltip("Compute shader used by the lightweight Auto Exposure meter. If empty, LightPLU loads it from Resources automatically.")]
    private ComputeShader autoExposureCompute;

    [SerializeField]
    [Tooltip("Physical exposure should normally run immediately before URP post-processing so Bloom and other HDR effects see the exposed scene color.")]
    private RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

    [SerializeField]
    [Tooltip("Apply the Volume exposure to the Scene View camera as well as Game cameras.")]
    private bool applyToSceneView = true;

    private readonly Dictionary<int, AutoExposureState> _autoStates = new();

    private Material _material;
    private PhysicalExposurePass _pass;
    private bool _warnedAutoUnsupported;

    public override void Create()
    {
        if (exposureShader == null)
        {
            exposureShader = Resources.Load<Shader>(ExposureShaderResourceName);

            if (exposureShader == null)
                exposureShader = Shader.Find(ShaderName);
        }

        if (autoExposureCompute == null)
        {
            autoExposureCompute =
                Resources.Load<ComputeShader>(AutoExposureResourceName);
        }

        CoreUtils.Destroy(_material);
        _material = exposureShader != null
            ? CoreUtils.CreateEngineMaterial(exposureShader)
            : null;

        _pass = new PhysicalExposurePass(_material, autoExposureCompute)
        {
            renderPassEvent = injectionPoint
        };
    }

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        if (_pass == null || _material == null)
            return;

        CameraData cameraData = renderingData.cameraData;

        // Apply once, to the camera that resolves the final stack target.
        if (!cameraData.resolveFinalTarget)
            return;

        if (cameraData.cameraType == CameraType.Preview ||
            cameraData.cameraType == CameraType.Reflection)
        {
            return;
        }

        if (!applyToSceneView && cameraData.cameraType == CameraType.SceneView)
            return;

        PhysicalExposureVolume volume =
            VolumeManager.instance.stack?.GetComponent<PhysicalExposureVolume>();

        if (volume == null || !volume.IsActive())
        {
            SetCameraAutoInactive(cameraData.camera);
            return;
        }

        bool autoRequested = volume.autoExposure.value;
        bool autoSupported =
            autoRequested &&
            autoExposureCompute != null &&
            SystemInfo.supportsComputeShaders &&
            SystemInfo.supportsAsyncGPUReadback &&
            !cameraData.xrRendering;

        if (autoRequested && !autoSupported && !_warnedAutoUnsupported)
        {
            _warnedAutoUnsupported = true;
            Debug.LogWarning(
                "[LightPLU] Auto Exposure is enabled but its lightweight meter " +
                "is unavailable on this camera/device. Falling back to the manual " +
                "EV100/Physical Camera value. Auto metering currently requires " +
                "compute shaders, Async GPU Readback, and a non-XR camera.");
        }

        float cameraEV100 = volume.ManualCameraEV100;
        AutoExposureState autoState = null;

        if (autoSupported)
        {
            int cameraId = cameraData.camera.GetInstanceID();

            if (!_autoStates.TryGetValue(cameraId, out autoState))
            {
                autoState = new AutoExposureState();
                _autoStates.Add(cameraId, autoState);
            }

            autoState.BeginOrUpdate(
                volume.ManualCameraEV100,
                volume.minEV100.value,
                volume.maxEV100.value,
                volume.speedUp.value,
                volume.speedDown.value,
                GetAdaptationDeltaTime());

            cameraEV100 = autoState.CurrentEV100;
        }
        else
        {
            SetCameraAutoInactive(cameraData.camera);
        }

        float exposureMultiplier =
            PhysicalExposure.GetRelativeExposureMultiplier(
                volume.referenceEV100.value,
                cameraEV100,
                volume.exposureCompensation.value);

        _pass.Setup(
            exposureMultiplier,
            autoSupported,
            autoState,
            volume.referenceEV100.value,
            volume.middleGray.value,
            volume.minEV100.value,
            volume.maxEV100.value);

        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass = null;
        _autoStates.Clear();

        CoreUtils.Destroy(_material);
        _material = null;
    }

    private void SetCameraAutoInactive(Camera camera)
    {
        if (camera == null)
            return;

        if (_autoStates.TryGetValue(camera.GetInstanceID(), out AutoExposureState state))
            state.SetInactive();
    }

    private static float GetAdaptationDeltaTime()
    {
        if (!Application.isPlaying)
            return 1.0f / 60.0f;

        return Mathf.Clamp(Time.unscaledDeltaTime, 0.0f, 0.25f);
    }

    private sealed class AutoExposureState
    {
        public float CurrentEV100 { get; private set; }
        public float TargetEV100 { get; private set; }
        public bool ReadbackPending { get; set; }

        private bool _initialized;
        private bool _activeLastFrame;

        public void BeginOrUpdate(
            float initialEV100,
            float minEV100,
            float maxEV100,
            float speedUp,
            float speedDown,
            float deltaTime)
        {
            NormalizeRange(ref minEV100, ref maxEV100);
            initialEV100 = Mathf.Clamp(initialEV100, minEV100, maxEV100);

            if (!_initialized || !_activeLastFrame)
            {
                CurrentEV100 = initialEV100;
                TargetEV100 = initialEV100;
                _initialized = true;
                _activeLastFrame = true;
                return;
            }

            TargetEV100 = Mathf.Clamp(TargetEV100, minEV100, maxEV100);

            float speed = TargetEV100 > CurrentEV100
                ? speedUp
                : speedDown;

            CurrentEV100 = Mathf.MoveTowards(
                CurrentEV100,
                TargetEV100,
                Mathf.Max(0.0f, speed) * Mathf.Max(0.0f, deltaTime));

            _activeLastFrame = true;
        }

        public void SetTargetEV100(float targetEV100)
        {
            if (float.IsNaN(targetEV100) || float.IsInfinity(targetEV100))
                return;

            TargetEV100 = targetEV100;
        }

        public void SetInactive()
        {
            _activeLastFrame = false;
        }

        private static void NormalizeRange(ref float minEV100, ref float maxEV100)
        {
            if (minEV100 > maxEV100)
                (minEV100, maxEV100) = (maxEV100, minEV100);
        }
    }

    private sealed class PhysicalExposurePass : ScriptableRenderPass
    {
        private const string ExposurePassName = "LightPLU Physical Exposure";
        private const string MeterPassName = "LightPLU Auto Exposure Meter";
        private const string ReadbackPassName = "LightPLU Auto Exposure Readback";
        private const string MeterKernelName = "MeterLogLuminance";

        private static readonly int ExposureMultiplierId =
            Shader.PropertyToID("_ExposureMultiplier");
        private static readonly int SourceTextureId =
            Shader.PropertyToID("_SourceTexture");
        private static readonly int ResultBufferId =
            Shader.PropertyToID("_Result");

        private readonly Material _material;
        private readonly ComputeShader _autoExposureCompute;
        private readonly int _meterKernel;

        private float _exposureMultiplier = 1.0f;
        private bool _autoExposureEnabled;
        private AutoExposureState _autoState;
        private float _referenceEV100;
        private float _middleGray;
        private float _minEV100;
        private float _maxEV100;

        public PhysicalExposurePass(
            Material material,
            ComputeShader autoExposureCompute)
        {
            _material = material;
            _autoExposureCompute = autoExposureCompute;
            _meterKernel = _autoExposureCompute != null
                ? _autoExposureCompute.FindKernel(MeterKernelName)
                : -1;
        }

        public void Setup(
            float exposureMultiplier,
            bool autoExposureEnabled,
            AutoExposureState autoState,
            float referenceEV100,
            float middleGray,
            float minEV100,
            float maxEV100)
        {
            _exposureMultiplier = exposureMultiplier;
            _autoExposureEnabled = autoExposureEnabled;
            _autoState = autoState;
            _referenceEV100 = referenceEV100;
            _middleGray = middleGray;
            _minEV100 = minEV100;
            _maxEV100 = maxEV100;

            // Both exposure and auto metering sample camera color. The active target
            // therefore has to be an intermediate texture, not a direct backbuffer.
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(
            RenderGraph renderGraph,
            ContextContainer frameData)
        {
            if (_material == null)
                return;

            UniversalResourceData resourceData =
                frameData.Get<UniversalResourceData>();

            if (resourceData.isActiveTargetBackBuffer)
            {
                Debug.LogWarning(
                    "[LightPLU] Physical Exposure skipped because the active " +
                    "camera target is the backbuffer. The pass requires an " +
                    "intermediate color texture.");
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;

            if (_autoExposureEnabled &&
                _autoExposureCompute != null &&
                _meterKernel >= 0 &&
                _autoState != null &&
                !_autoState.ReadbackPending)
            {
                RecordAutoExposureMeter(renderGraph, source);
            }

            if (Mathf.Abs(_exposureMultiplier - 1.0f) <= 0.000001f)
                return;

            _material.SetFloat(
                ExposureMultiplierId,
                _exposureMultiplier);

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "_LightPLUPhysicalExposureColor";
            destinationDesc.clearBuffer = false;

            TextureHandle destination =
                renderGraph.CreateTexture(destinationDesc);

            RenderGraphUtils.BlitMaterialParameters parameters =
                new RenderGraphUtils.BlitMaterialParameters(
                    source,
                    destination,
                    _material,
                    0);

            renderGraph.AddBlitPass(
                parameters,
                passName: ExposurePassName);

            // Make the exposed texture the camera color for all following passes,
            // including URP Bloom, color grading and tonemapping.
            resourceData.cameraColor = destination;
        }

        private void RecordAutoExposureMeter(
            RenderGraph renderGraph,
            TextureHandle source)
        {
            BufferDesc resultDesc = new BufferDesc
            {
                name = "_LightPLUAutoExposureLogLuminance",
                count = 1,
                stride = sizeof(float),
                target = GraphicsBuffer.Target.Structured
            };

            BufferHandle resultBuffer =
                renderGraph.CreateBuffer(resultDesc);

            using (IComputeRenderGraphBuilder builder =
                renderGraph.AddComputePass<AutoMeterPassData>(
                    MeterPassName,
                    out AutoMeterPassData passData))
            {
                passData.computeShader = _autoExposureCompute;
                passData.kernel = _meterKernel;
                passData.source = source;
                passData.result = resultBuffer;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseBuffer(resultBuffer, AccessFlags.Write);

                builder.SetRenderFunc(static (
                    AutoMeterPassData data,
                    ComputeGraphContext context) =>
                {
                    context.cmd.SetComputeTextureParam(
                        data.computeShader,
                        data.kernel,
                        SourceTextureId,
                        data.source);

                    context.cmd.SetComputeBufferParam(
                        data.computeShader,
                        data.kernel,
                        ResultBufferId,
                        data.result);

                    context.cmd.DispatchCompute(
                        data.computeShader,
                        data.kernel,
                        1,
                        1,
                        1);
                });
            }

            _autoState.ReadbackPending = true;

            using (IUnsafeRenderGraphBuilder builder =
                renderGraph.AddUnsafePass<AutoReadbackPassData>(
                    ReadbackPassName,
                    out AutoReadbackPassData passData))
            {
                passData.result = resultBuffer;
                passData.state = _autoState;
                passData.referenceEV100 = _referenceEV100;
                passData.middleGray = _middleGray;
                passData.minEV100 = _minEV100;
                passData.maxEV100 = _maxEV100;

                builder.AllowPassCulling(false);
                builder.UseBuffer(resultBuffer, AccessFlags.Read);

                builder.SetRenderFunc(static (
                    AutoReadbackPassData data,
                    UnsafeGraphContext context) =>
                {
                    AutoExposureState state = data.state;
                    float referenceEV100 = data.referenceEV100;
                    float middleGray = data.middleGray;
                    float minEV100 = data.minEV100;
                    float maxEV100 = data.maxEV100;

                    context.cmd.RequestAsyncReadback(
                        data.result,
                        request =>
                        {
                            state.ReadbackPending = false;

                            if (request.hasError)
                                return;

                            var values = request.GetData<float>();
                            if (values.Length == 0)
                                return;

                            float targetEV100 =
                                PhysicalExposure.CalculateAutoExposureEV100FromPreExposedLogLuminance(
                                    values[0],
                                    referenceEV100,
                                    middleGray,
                                    minEV100,
                                    maxEV100);

                            state.SetTargetEV100(targetEV100);
                        });
                });
            }
        }

        private sealed class AutoMeterPassData
        {
            public ComputeShader computeShader;
            public int kernel;
            public TextureHandle source;
            public BufferHandle result;
        }

        private sealed class AutoReadbackPassData
        {
            public BufferHandle result;
            public AutoExposureState state;
            public float referenceEV100;
            public float middleGray;
            public float minEV100;
            public float maxEV100;
        }
    }
}
