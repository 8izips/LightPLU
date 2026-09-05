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

The production exposure Renderer Feature targets the Unity 6 Render Graph path. URP Compatibility Mode (Render Graph Disabled) is not currently supported by that feature.

Auto Exposure additionally requires:

- Compute shader support
- Async GPU Readback support
- A non-XR camera for the current lightweight metering implementation

If Auto Exposure is requested but those requirements are unavailable, LightPLU falls back to the manual EV100/Physical Camera value.

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

The lightweight Auto Exposure meter is loaded automatically from:

`Resources/LightPLUPhysicalExposureAuto.compute`

### 2. Add the Volume component

Add a Global Volume (or use an existing Volume Profile), then add:

`LightPLU > Physical Exposure`

### Manual EV100

Set the camera exposure directly, for example:

```text
Auto Exposure: Off
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

### Physical Camera

With Auto Exposure off, set:

- Aperture (f-number)
- Shutter Seconds
- ISO

LightPLU calculates:

```text
EV100 = log2((N^2 / t) * (100 / ISO))
```

Example for 1/125 second:

```text
Auto Exposure: Off
Mode: Physical Camera
Aperture: 16
Shutter Seconds: 0.008
ISO: 100
```

The manual EV/Physical Camera value is also used as the initial EV when Auto Exposure is switched on.

## Auto Exposure

Enable the `Auto Exposure` toggle in `LightPLU > Physical Exposure`.

The current implementation is intentionally a lightweight mode comparable in purpose to Unreal's Auto Exposure Basic rather than a full histogram implementation. Every metering update samples a uniform 16 x 16 grid of the scene color, calculates log-average luminance on the GPU, and asynchronously returns one float. The camera then adapts in EV space.

Controls:

- `Min EV100`: darkest allowed camera EV limit
- `Max EV100`: brightest-scene camera EV limit
- `Middle Gray`: target display-linear gray; default `0.18`
- `Speed Up`: stops/second when moving from a dark environment to a bright environment (EV increases)
- `Speed Down`: stops/second when moving from a bright environment to a dark environment (EV decreases)
- `Exposure Compensation`: artistic offset in stops after metering; positive values brighten the result

The auto target is solved from the pre-exposed scene luminance. If `Lp` is the measured pre-exposed luminance and `R` is Reference EV100:

```text
physical log luminance = log2(Lp) + R

target EV100 =
    physical log luminance - log2(MiddleGray)
```

The result is clamped to `Min EV100` / `Max EV100` and followed using Speed Up / Speed Down.

This design deliberately keeps the meter before the Physical Exposure pass, so it observes scene-linear color that has only received LightPLU's reference pre-exposure.

### Current Auto Exposure scope

The first implementation uses log-average Basic metering. It does not yet implement Unreal-style histogram percentiles, metering masks, compensation curves, or local exposure. Those can be layered on without changing the physical exposure model.

## Match Reference EV100

`Reference EV100` on the Physical Exposure Volume must match the value used by the LightPLU lights. The default is `15` on both sides.

## Avoid double exposure

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

The production Renderer Feature and new Auto Exposure meter should be integration-checked in the target project after adding the Renderer Feature and Volume override.

See [`Validation/RESULTS.md`](Validation/RESULTS.md) for the validated outcome and interpretation.

## Legacy sample

The old GreyScene/HDRP visual comparison was removed because it encoded the previous empirical intensity coefficients. The automated validation scene replaces it with a reproducible numeric reference.

## License

MIT. See [LICENSE](LICENSE).
