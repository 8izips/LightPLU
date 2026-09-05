using UnityEngine;

public static class PhysicalExposure
{
    /// <summary>
    /// Photographic EV100: log2((N^2 / t) * (100 / ISO)).
    /// N is the f-number, t is shutter time in seconds.
    /// </summary>
    public static float CalculateEV100(float aperture, float shutterSeconds, float iso)
    {
        aperture = Mathf.Max(aperture, 0.0001f);
        shutterSeconds = Mathf.Max(shutterSeconds, 0.000001f);
        iso = Mathf.Max(iso, 0.0001f);

        float value =
            (aperture * aperture / shutterSeconds) *
            (100.0f / iso);

        return Mathf.Log(value, 2.0f);
    }

    /// <summary>
    /// Multiplier applied to physical light intensity before URP renders it.
    /// This keeps physically large light values in a practical HDR range.
    /// </summary>
    public static float GetPreExposureMultiplier(float referenceEV100)
    {
        return Mathf.Pow(2.0f, -referenceEV100);
    }

    /// <summary>
    /// Number of exposure stops required after reference pre-exposure.
    /// Positive exposure compensation makes the image brighter.
    /// </summary>
    public static float GetRelativeExposureStops(
        float referenceEV100,
        float cameraEV100,
        float exposureCompensationStops = 0.0f)
    {
        return referenceEV100 - cameraEV100 + exposureCompensationStops;
    }

    /// <summary>
    /// Multiplier required after reference pre-exposure to represent the camera EV100.
    /// Combining this with GetPreExposureMultiplier(referenceEV100) yields
    /// 2^(-cameraEV100 + exposureCompensationStops).
    /// </summary>
    public static float GetRelativeExposureMultiplier(
        float referenceEV100,
        float cameraEV100,
        float exposureCompensationStops = 0.0f)
    {
        return Mathf.Pow(
            2.0f,
            GetRelativeExposureStops(
                referenceEV100,
                cameraEV100,
                exposureCompensationStops));
    }

    /// <summary>
    /// Direct exposure multiplier for a scene-linear luminance value.
    /// </summary>
    public static float GetDirectExposureMultiplier(
        float cameraEV100,
        float exposureCompensationStops = 0.0f)
    {
        return Mathf.Pow(2.0f, -cameraEV100 + exposureCompensationStops);
    }

    /// <summary>
    /// Converts log2 luminance measured from the pre-exposed scene color into
    /// a physical camera EV100 which maps that luminance to the requested middle gray.
    ///
    /// The measured scene has already been multiplied by 2^-ReferenceEV100, so
    /// ReferenceEV100 is added back before solving B = L * 2^-EV for EV.
    /// </summary>
    public static float CalculateAutoExposureEV100FromPreExposedLogLuminance(
        float preExposedLog2Luminance,
        float referenceEV100,
        float middleGray,
        float minEV100,
        float maxEV100)
    {
        middleGray = Mathf.Max(middleGray, 0.000001f);

        float physicalLog2Luminance =
            preExposedLog2Luminance + referenceEV100;

        float targetEV100 =
            physicalLog2Luminance - Mathf.Log(middleGray, 2.0f);

        if (minEV100 > maxEV100)
            (minEV100, maxEV100) = (maxEV100, minEV100);

        return Mathf.Clamp(targetEV100, minEV100, maxEV100);
    }
}
