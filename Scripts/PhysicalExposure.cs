using UnityEngine;

public static class PhysicalExposure
{
    /// <summary>
    /// Photographic EV100: log2((N^2 / t) * (100 / ISO)).
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
    /// Multiplier applied before rendering to keep physical light values in a safe HDR range.
    /// </summary>
    public static float GetPreExposureMultiplier(float referenceEV100)
    {
        return Mathf.Pow(2.0f, -referenceEV100);
    }

    /// <summary>
    /// Multiplier required after reference pre-exposure to represent a camera EV100.
    /// Combining this with GetPreExposureMultiplier(referenceEV100) yields 2^-cameraEV100.
    /// </summary>
    public static float GetRelativeExposureMultiplier(float referenceEV100, float cameraEV100)
    {
        return Mathf.Pow(2.0f, referenceEV100 - cameraEV100);
    }

    /// <summary>
    /// Direct physical exposure multiplier for a scene-linear luminance value.
    /// </summary>
    public static float GetDirectExposureMultiplier(float cameraEV100)
    {
        return Mathf.Pow(2.0f, -cameraEV100);
    }
}
