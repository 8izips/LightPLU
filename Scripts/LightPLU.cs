using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
public sealed class LightPLU : MonoBehaviour
{
    [SerializeField] private Light targetLight;
    [SerializeField] private LightUnit physicalUnit = LightUnit.Lux;
    [SerializeField, Min(0.0f)] private float physicalIntensity = 100000.0f;
    [SerializeField] private float referenceEV100 = 15.0f;
    [SerializeField] private bool applyAutomatically = true;

    public Light TargetLight => targetLight;
    public LightUnit PhysicalUnit => physicalUnit;
    public float PhysicalIntensity => physicalIntensity;
    public float ReferenceEV100 => referenceEV100;

    public float NativePhysicalIntensity { get; private set; }
    public float PreExposedNativeIntensity { get; private set; }

    private void Reset()
    {
        targetLight = GetComponent<Light>();
        SetDefaultsForLightType();
        ApplyPhysicalLight();
    }

    private void OnEnable()
    {
        EnsureTargetLight();

        if (applyAutomatically)
            ApplyPhysicalLight();
    }

    private void OnValidate()
    {
        EnsureTargetLight();
        physicalIntensity = Mathf.Max(0.0f, physicalIntensity);
        EnsureSupportedUnit();

        if (applyAutomatically)
            ApplyPhysicalLight();
    }

    public bool ApplyPhysicalLight()
    {
        if (!EnsureTargetLight())
            return false;

        EnsureSupportedUnit();

        if (!PhysicalLightUnitConverter.TryToPreExposedNativeIntensity(
                targetLight,
                physicalIntensity,
                physicalUnit,
                referenceEV100,
                out float nativePhysicalIntensity,
                out float preExposedNativeIntensity))
        {
            return false;
        }

        NativePhysicalIntensity = nativePhysicalIntensity;
        PreExposedNativeIntensity = preExposedNativeIntensity;

        // Keep Light.lightUnit truthful about the unit stored in Light.intensity.
        // The authored physical unit remains on this component.
        targetLight.lightUnit = LightUnitUtils.GetNativeLightUnit(targetLight.type);
        targetLight.intensity = preExposedNativeIntensity;

        return true;
    }

    private bool EnsureTargetLight()
    {
        if (targetLight == null)
            targetLight = GetComponent<Light>();

        return targetLight != null;
    }

    private void EnsureSupportedUnit()
    {
        if (targetLight == null)
            return;

        if (!LightUnitUtils.IsLightUnitSupported(targetLight.type, physicalUnit))
            physicalUnit = PhysicalLightUnitConverter.GetDefaultAuthoringUnit(targetLight.type);
    }

    private void SetDefaultsForLightType()
    {
        if (targetLight == null)
            return;

        physicalUnit = PhysicalLightUnitConverter.GetDefaultAuthoringUnit(targetLight.type);

        physicalIntensity = targetLight.type switch
        {
            LightType.Directional => 100000.0f,
            LightType.Point => 1000.0f,
            LightType.Spot => 1000.0f,
            _ => 1000.0f
        };

        referenceEV100 = 15.0f;
    }
}
