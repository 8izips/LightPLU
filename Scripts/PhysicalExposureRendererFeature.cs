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
/// </summary>
public sealed class PhysicalExposureRendererFeature : ScriptableRendererFeature
{
    private const string ShaderName = "Hidden/LightPLU/PhysicalExposure";

    [SerializeField]
    [Tooltip("Full-screen shader used to multiply scene-linear color by the physical exposure multiplier.")]
    private Shader exposureShader;

    [SerializeField]
    [Tooltip("Physical exposure should normally run immediately before URP post-processing so Bloom and other HDR effects see the exposed scene color.")]
    private RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

    [SerializeField]
    [Tooltip("Apply the Volume exposure to the Scene View camera as well as Game cameras.")]
    private bool applyToSceneView = true;

    private Material _material;
    private PhysicalExposurePass _pass;

    public override void Create()
    {
        if (exposureShader == null)
            exposureShader = Shader.Find(ShaderName);

        CoreUtils.Destroy(_material);
        _material = exposureShader != null
            ? CoreUtils.CreateEngineMaterial(exposureShader)
            : null;

        _pass = new PhysicalExposurePass(_material)
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

        ref CameraData cameraData = ref renderingData.cameraData;

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
            return;

        _pass.Setup(volume.ExposureMultiplier);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass = null;
        CoreUtils.Destroy(_material);
        _material = null;
    }

    private sealed class PhysicalExposurePass : ScriptableRenderPass
    {
        private const string PassName = "LightPLU Physical Exposure";
        private static readonly int ExposureMultiplierId =
            Shader.PropertyToID("_ExposureMultiplier");

        private readonly Material _material;
        private float _exposureMultiplier = 1.0f;

        public PhysicalExposurePass(Material material)
        {
            _material = material;
        }

        public void Setup(float exposureMultiplier)
        {
            _exposureMultiplier = exposureMultiplier;

            // The pass samples camera color, therefore the active color target must
            // be an intermediate texture rather than a direct backbuffer target.
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(
            RenderGraph renderGraph,
            ContextContainer frameData)
        {
            if (_material == null ||
                Mathf.Abs(_exposureMultiplier - 1.0f) <= 0.000001f)
            {
                return;
            }

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

            _material.SetFloat(
                ExposureMultiplierId,
                _exposureMultiplier);

            TextureHandle source = resourceData.activeColorTexture;
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
                passName: PassName);

            // Make the exposed texture the camera color for all following passes,
            // including URP Bloom, color grading and tonemapping.
            resourceData.cameraColor = destination;
        }
    }
}
