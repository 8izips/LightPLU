# Unity 6 URP validation results

Date: 2026-09-05

## Outcome

The play-mode validation harness was run in Unity 6 URP. Every numeric PASS/FAIL validation stage passed after the exposure test was changed from a legacy `Graphics.Blit` path to a direct HDR fragment calculation and the validation shaders were updated to the current URP cluster-light-loop API.

- GPU float-HDR readback: **PASS**
- Unity `LightUnitUtils` conversion checks: **PASS**
- Directional native Lux -> physical Lambert: **PASS**
- Point Candela at 1m / 2m / 4m: **PASS**
- Point inverse-square ratios: **PASS**
- Point Lumen -> Unity conversion -> URP: **PASS**
- Spot Lumen / solid-angle conversion: **PASS**
- Exposure stop math `sceneLinear * 2^-EV100`: **PASS**
- `100000 lux + 18% Lambert + EV15`: **PASS**
- Reference pre-exposure on the light side: **PASS**
- URP direct-diffuse comparison: completed as an informational diagnostic

The integration reference is:

```text
L = 100000 * 0.18 / PI
  = 5729.578... cd/m^2

B = L * 2^-15
  = 0.1748528...
```

The validation reproduced this value within the configured tolerance.

## What this establishes

For the tested Unity 6 URP configuration:

1. Unity's built-in `LightUnitUtils` can replace LightPLU's legacy fixed Lux/Lumen conversion coefficients.
2. Point-light attenuation follows the expected inverse-square relationship in the measured region.
3. Spot Lumen conversion can use Unity's solid-angle-aware conversion rather than a fixed coefficient.
4. A reference pre-exposure model is numerically equivalent to applying the same EV exposure after physical scene luminance is produced.
5. LightPLU can keep physical authoring values separate from the pre-exposed native value written to `Light.intensity`.

## Production exposure implementation

The repository now includes:

- `PhysicalExposureVolume`: manual EV100 / physical camera controls and a Basic Auto Exposure toggle.
- `PhysicalExposureRendererFeature`: a Unity 6 Render Graph pass injected at `BeforeRenderingPostProcessing`.
- `Resources/LightPLUPhysicalExposure.shader`: multiplies scene-linear camera color by the relative physical exposure multiplier.
- `Resources/LightPLUPhysicalExposureAuto.compute`: 16 x 16 log-luminance metering for Basic Auto Exposure.

The production pass uses the same validated equation:

```text
Light side:
    2^-ReferenceEV100

Camera side:
    2^(ReferenceEV100 - CameraEV100 + Compensation)

Combined:
    2^(-CameraEV100 + Compensation)
```

Basic Auto Exposure meters the scene before the Physical Exposure pass. Because the measured scene is already reference-pre-exposed, the auto target is solved as:

```text
physicalLog2Luminance = measuredPreExposedLog2Luminance + ReferenceEV100

targetEV100 = physicalLog2Luminance - log2(MiddleGray)
```

The target is clamped by Min/Max EV100 and adapted in EV space using Speed Up / Speed Down in stops per second.

## Production integration result

The production `Physical Exposure Renderer Feature` and Basic Auto Exposure path were subsequently checked in the target Unity 6 URP project and reported to operate correctly in Play mode.

This confirms the intended integration path at the current project configuration:

1. `Physical Exposure Renderer Feature` is present on the active Universal Renderer Data.
2. `LightPLU > Physical Exposure` is active in a Volume Profile.
3. Manual exposure responds correctly in Play mode.
4. Basic Auto Exposure adapts correctly in Play mode.

The automated harness still remains the numeric authority for PLU transport and exposure math; the production-feature result above is an integration/behavior confirmation rather than a pixel-exact automated test.

## Important scope

This does **not** claim that every URP BRDF, tonemapper or post-processing effect is identical to HDRP, Unreal Engine or Blender. The purpose of the harness is to isolate physical-unit transport and exposure math from renderer-specific artistic choices.
