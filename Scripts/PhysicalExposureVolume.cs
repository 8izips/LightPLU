using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum PhysicalExposureMode
{
    EV100 = 0,
    PhysicalCamera = 1
}

[Serializable]
public sealed class PhysicalExposureModeParameter : VolumeParameter<PhysicalExposureMode>
{
    public PhysicalExposureModeParameter(
        PhysicalExposureMode value,
        bool overrideState = false)
        : base(value, overrideState)
    {
    }
}

[Serializable]
[VolumeComponentMenu("LightPLU/Physical Exposure")]
[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public sealed class PhysicalExposureVolume : VolumeComponent, IPostProcessComponent
{
    [Tooltip("How the camera EV100 is specified.")]
    public PhysicalExposureModeParameter mode =
        new PhysicalExposureModeParameter(PhysicalExposureMode.EV100);

    [Tooltip("Manual photographic EV100 used when Mode is EV100.")]
    public ClampedFloatParameter ev100 =
        new ClampedFloatParameter(15.0f, -20.0f, 30.0f);

    [Tooltip("Lens f-number used when Mode is Physical Camera.")]
    public ClampedFloatParameter aperture =
        new ClampedFloatParameter(16.0f, 0.5f, 64.0f);

    [Tooltip("Shutter time in seconds used when Mode is Physical Camera. Example: 1/125 s = 0.008 s.")]
    public ClampedFloatParameter shutterSeconds =
        new ClampedFloatParameter(1.0f / 125.0f, 1.0f / 8000.0f, 30.0f);

    [Tooltip("ISO sensitivity used when Mode is Physical Camera.")]
    public ClampedFloatParameter iso =
        new ClampedFloatParameter(100.0f, 25.0f, 204800.0f);

    [Tooltip("Reference EV100 used to pre-expose LightPLU lights. This must match the Reference EV100 on the lights.")]
    public ClampedFloatParameter referenceEV100 =
        new ClampedFloatParameter(15.0f, -20.0f, 30.0f);

    [Tooltip("Exposure compensation in stops. Positive values brighten the image.")]
    public ClampedFloatParameter exposureCompensation =
        new ClampedFloatParameter(0.0f, -10.0f, 10.0f);

    public float CameraEV100 => mode.value switch
    {
        PhysicalExposureMode.PhysicalCamera =>
            PhysicalExposure.CalculateEV100(
                aperture.value,
                shutterSeconds.value,
                iso.value),
        _ => ev100.value
    };

    public float RelativeExposureStops =>
        PhysicalExposure.GetRelativeExposureStops(
            referenceEV100.value,
            CameraEV100,
            exposureCompensation.value);

    public float ExposureMultiplier =>
        PhysicalExposure.GetRelativeExposureMultiplier(
            referenceEV100.value,
            CameraEV100,
            exposureCompensation.value);

    public bool IsActive()
    {
        return active && Mathf.Abs(RelativeExposureStops) > 0.0001f;
    }

    [Obsolete("Unused. #from(2023.1)")]
    public bool IsTileCompatible() => false;
}
