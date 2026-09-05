using System;
using UnityEngine;
using UnityEngine.Rendering;

public static class PhysicalLightUnitConverter
{
    public static LightUnit GetDefaultAuthoringUnit(LightType lightType)
    {
        return lightType switch
        {
            LightType.Directional => LightUnit.Lux,
            LightType.Point => LightUnit.Lumen,
            LightType.Spot => LightUnit.Lumen,
            LightType.Rectangle => LightUnit.Lumen,
            LightType.Disc => LightUnit.Lumen,
            LightType.Tube => LightUnit.Lumen,
            _ => LightUnitUtils.GetNativeLightUnit(lightType)
        };
    }

    public static bool TryToNativeIntensity(
        Light light,
        float physicalIntensity,
        LightUnit physicalUnit,
        out float nativeIntensity)
    {
        nativeIntensity = 0.0f;

        if (light == null || physicalIntensity < 0.0f)
            return false;

        if (!LightUnitUtils.IsLightUnitSupported(light.type, physicalUnit))
            return false;

        try
        {
            LightUnit nativeUnit = LightUnitUtils.GetNativeLightUnit(light.type);
            nativeIntensity = LightUnitUtils.ConvertIntensity(
                light,
                physicalIntensity,
                physicalUnit,
                nativeUnit);

            return IsFinite(nativeIntensity) && nativeIntensity >= 0.0f;
        }
        catch (ArgumentException)
        {
            nativeIntensity = 0.0f;
            return false;
        }
    }

    public static bool TryToPreExposedNativeIntensity(
        Light light,
        float physicalIntensity,
        LightUnit physicalUnit,
        float referenceEV100,
        out float nativePhysicalIntensity,
        out float preExposedNativeIntensity)
    {
        nativePhysicalIntensity = 0.0f;
        preExposedNativeIntensity = 0.0f;

        if (!TryToNativeIntensity(
                light,
                physicalIntensity,
                physicalUnit,
                out nativePhysicalIntensity))
        {
            return false;
        }

        preExposedNativeIntensity =
            nativePhysicalIntensity * PhysicalExposure.GetPreExposureMultiplier(referenceEV100);

        return IsFinite(preExposedNativeIntensity) && preExposedNativeIntensity >= 0.0f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
