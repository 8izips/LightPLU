# LightPLU

Physical Light Units and physical camera exposure for Unity 6 URP.

> Status: experimental 2.0. The legacy empirical Lux/Lumen coefficients have been removed.

## Model

Unity 6 provides `UnityEngine.Rendering.LightUnitUtils`, including native-unit definitions and physical Lumen/Candela/Lux conversions. LightPLU now uses Unity's conversion math instead of hand-tuned coefficients.

```text
Physical PLU
    -> Unity LightUnitUtils
    -> native unit (Lux for Directional, Candela for Point/Spot)
    -> Reference EV100 pre-exposure
    -> URP Light.intensity
```

The camera side applies the inverse relative exposure before URP post-processing:

```text
light pre-exposure = 2^(-ReferenceEV100)

camera multiplier =
    2^(ReferenceEV100 - CameraEV100 + ExposureCompensation)

combined =
    2^(-CameraEV100 + ExposureCompensation)
```

This lets physically large light values remain in a practical HDR range while preserving predictable EV behavior.

## Requirements

- Unity 6
- Universal Render Pipeline
- Render Graph enabled (Unity 6 default)
- Linear color space strongly recommended and required by the included validation harness

The current production exposure Renderer Feature targets the Unity 6 Render Graph path. URP Compatibility Mode (Render Graph Disabled) is not currently supported by that feature.

## Light setup

1. Add `LightPLU` to a Unity `Light`.
2. Enter the physical intensity and unit.
3. Keep `Reference EV100` at `15` unless the whole lighting/exposure setup intentionally uses another reference.
4. LightPLU converts the authored value to Unity's native light unit using `LightUnitUtils` and writes a pre-exposed value to `Light.intensity`.

Examples:

- Directional: `100000 Lux`
- Point: `1000 Lumen`
- Spot: `1000 Lumen` (Spot solid angle is handled by Unity's conversion when the Spot reflector is enabled)

## Physical Exposure setup

### 1. Add the Renderer Feature

Open the Universal Renderer Data used by the camera and add:

`Physical Exposure Renderer Feature`

Keep its injection point at:

`Before Rendering Post Processing`

The feature uses `Shaders/PhysicalExposure.shader`. It normally resolves this shader automatically. The shader can also be assigned explicitly in the Renderer Feature inspector.

### 2. Add the Volume component

Add a Global Volume (or use an existing Volume Profile), then add:

`LightPLU > Physical Exposure`

The component supports two modes.

#### EV100

Set the camera exposure directly, for example:

```text
Mode: EV100
EV100: 15
Reference EV100: 15
Exposure Compensation: 0
```

With matching EV15 values the relative exposure multiplier is `1`, because the lights are already pre-exposed to EV15.

Changing camera EV works in photographic stops:

```text
EV14 -> x2 brighter than EV15
EV15 -> x1
EV16 -> x0.5
```

#### Physical Camera

Set:

- Aperture (f-number)
- Shutter Seconds
- ISO

LightPLU calculates:

```text
EV100 = log2((N^2 / t) * (100 / ISO))
```

Example for 1/125 second:

```text
Aperture: 16
Shutter Seconds: 0.008
ISO: 100
```

`Exposure Compensation` is measured in stops. Positive values brighten the image.

### 3. Match Reference EV100

`Reference EV100` on the Physical Exposure Volume must match the value used by the LightPLU lights. The default is `15` on both sides.

### 4. Avoid double exposure

If URP `Color Adjustments` is also enabled, keep its `Post Exposure` at `0` unless an additional artistic offset is intentional.

LightPLU's Physical Exposure runs before URP post-processing, so Bloom and subsequent HDR effects receive the exposed scene color. Tonemapping such as ACES remains URP's responsibility.

## Validation

Run:

`Tools > LightPLU Validation > Create or Reset Validation Scene`

Open the generated scene, press Play, and read the Console. The runner creates its camera, float HDR render targets, materials, lights and measurement surface at runtime.

The current harness checks:

- Float HDR GPU readback
- `LightUnitUtils` Lumen/Candela/Lux conversions
- Directional Lux against a physical Lambert reference
- Point Candela and inverse-square attenuation at 1m / 2m / 4m
- Point Lumen -> Candela -> URP
- Spot Lumen and solid-angle conversion
- Exposure stop math `2^-EV100`
- `100000 lux + 18% Lambert + EV15 ~= 0.17485` before tonemapping
- Light-side Reference EV pre-exposure
- URP direct-diffuse normalization as an informational diagnostic

The validation run used during the Unity 6 migration passed all numeric tests after the validation shaders were updated to the current URP cluster-light-loop API.

See [`Validation/RESULTS.md`](Validation/RESULTS.md) for the validated outcome and interpretation.

## Legacy sample

The old GreyScene/HDRP visual comparison was removed because it encoded the previous empirical intensity coefficients. The automated validation scene replaces it with a reproducible numeric reference.

## License

MIT. See [LICENSE](LICENSE).
