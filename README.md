# LightPLU

Physical Light Units for Unity 6 URP, with an explicit pre-exposure model.

> Status: experimental 2.0 core. The legacy empirical Lux/Lumen coefficients have been removed.

## Why this changed

The original LightPLU converted physical units to URP intensity with hand-tuned coefficients. Unity 6 now ships `UnityEngine.Rendering.LightUnitUtils`, including native-unit definitions and physically-defined Lumen/Candela/Lux conversions. A play-mode validation harness in this repository confirmed that Unity 6 URP can transport those native values consistently for Directional, Point and Spot lights, including inverse-square attenuation and Spot solid-angle conversion.

LightPLU therefore no longer treats the old coefficients as physical conversions. The 2.0 core uses:

```text
Physical PLU
    -> Unity LightUnitUtils
    -> native unit (Lux for Directional, Candela for Point/Spot)
    -> reference pre-exposure
    -> URP Light.intensity
```

Camera exposure is represented separately:

```text
relative exposure = 2^(ReferenceEV100 - CameraEV100)
```

The production URP exposure renderer feature is not part of this commit yet. `PhysicalExposure` contains the shared math and the validation harness verifies the exposure equation independently.

## Requirements

- Unity 6
- Universal Render Pipeline
- Linear color space recommended and required by the included validation harness

## Usage

1. Add `LightPLU` to a Unity `Light`.
2. Enter the physical intensity and unit on the component.
3. Keep `Reference EV100` at 15 unless you intentionally use another pre-exposure reference.
4. LightPLU converts the authored value to Unity's native light unit using `LightUnitUtils`, then applies the reference pre-exposure before writing `Light.intensity`.

Examples:

- Directional: `100000 Lux`
- Point: `1000 Lumen`
- Spot: `1000 Lumen` (conversion uses the Spot angle when the Spot reflector is enabled)

## Validation

Run:

`Tools > LightPLU Validation > Create or Reset Validation Scene`

Open the generated scene, press Play, and read the Console. The scene is otherwise empty; the runner creates its camera, HDR render targets, materials, lights and measurement surface at runtime.

The current harness checks:

- Float HDR GPU readback
- `LightUnitUtils` Lumen/Candela/Lux conversions
- Directional Lux against a physical Lambert reference
- Point Candela and inverse-square attenuation at 1m / 2m / 4m
- Point Lumen -> Candela -> URP
- Spot Lumen and solid-angle conversion
- Exposure stop math `2^-EV100`
- `100000 lux + 18% Lambert + EV15 ~= 0.17485` before tonemapping
- Light-side reference pre-exposure
- URP direct-diffuse normalization as an informational diagnostic

See [`Validation/RESULTS.md`](Validation/RESULTS.md) for the validated outcome and interpretation.

## Legacy sample

The old GreyScene/HDRP visual comparison was removed because it encoded the previous empirical intensity coefficients. The automated validation scene replaces it with a reproducible numeric reference.

## License

MIT. See [LICENSE](LICENSE).
