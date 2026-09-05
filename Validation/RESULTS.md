# Unity 6 URP validation results

Date: 2026-09-05

## Outcome

The play-mode validation harness was run in Unity 6 URP. The user reported that every PASS/FAIL validation stage passed after the exposure test was changed from a legacy `Graphics.Blit` path to a direct HDR fragment calculation.

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

## Important scope

This does **not** claim that every URP BRDF, tonemapper or post-processing effect is identical to HDRP, Unreal Engine or Blender. The purpose of the harness is to isolate physical-unit transport and exposure math from renderer-specific artistic choices.

The final production exposure renderer feature still needs to be implemented and validated in the intended URP injection point.
