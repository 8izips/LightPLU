using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class LightPLUValidationRunner : MonoBehaviour
{
    private const int ValidationLayer = 30;
    private const int ValidationMask = 1 << ValidationLayer;

    [SerializeField, Min(64)] private int resolution = 256;
    [SerializeField, Range(2, 64)] private int centerRegionSize = 16;
    [SerializeField] private float readbackTolerancePercent = 0.1f;
    [SerializeField] private float directionalTolerancePercent = 1.0f;
    [SerializeField] private float punctualTolerancePercent = 2.0f;
    [SerializeField] private float exposureTolerancePercent = 0.1f;

    private readonly List<Result> results = new();
    private readonly List<LightState> oldLights = new();

    private GameObject root;
    private Camera validationCamera;
    private MeshRenderer surfaceRenderer;
    private RenderTexture renderTarget;
    private Material constantMaterial;
    private Material referenceMaterial;
    private Material exposureMaterial;
    private Material urpDiffuseMaterial;
    private Light activeLight;

    private AmbientMode oldAmbientMode;
    private Color oldAmbientLight;
    private float oldAmbientIntensity;
    private float oldReflectionIntensity;
    private Material oldSkybox;
    private bool oldFog;
    private bool environmentCaptured;
    private bool cleanedUp;

    private readonly struct Result
    {
        public readonly bool Pass;
        public readonly bool Info;
        public readonly string Category;
        public readonly string Name;
        public readonly float Expected;
        public readonly float Actual;
        public readonly float Error;
        public readonly float Tolerance;
        public readonly string Note;

        public Result(bool pass, bool info, string category, string name,
            float expected, float actual, float error, float tolerance, string note)
        {
            Pass = pass;
            Info = info;
            Category = category;
            Name = name;
            Expected = expected;
            Actual = actual;
            Error = error;
            Tolerance = tolerance;
            Note = note;
        }
    }

    private readonly struct LightState
    {
        public readonly Light Light;
        public readonly bool Enabled;

        public LightState(Light light, bool enabled)
        {
            Light = light;
            Enabled = enabled;
        }
    }

    private readonly struct Measurement
    {
        public readonly bool Success;
        public readonly Vector3 RGB;
        public readonly string Error;
        public float Gray => (RGB.x + RGB.y + RGB.z) / 3.0f;

        public Measurement(bool success, Vector3 rgb, string error)
        {
            Success = success;
            RGB = rgb;
            Error = error;
        }
    }

    private IEnumerator Start()
    {
        Debug.Log("\n============================================================\n" +
                  " LightPLU Physical Validation - Unity 6 URP\n" +
                  "============================================================");

        if (!CheckEnvironment() || !CreateMaterials())
        {
            PrintSummary();
            yield break;
        }

        CaptureEnvironment();
        CreateMeasurementEnvironment();
        yield return null;

        yield return RunReadbackCalibration();
        if (HasFailure("Measurement"))
        {
            Debug.LogError("[LightPLU Validation] Aborting: GPU readback calibration failed.");
            Cleanup();
            PrintSummary();
            yield break;
        }

        RunLightUnitMath();
        yield return RunDirectionalLux();
        yield return RunPointCandela();
        yield return RunPointLumen();
        yield return RunSpotLumen();
        yield return RunExposureStops();
        yield return RunExposureIntegration();
        yield return RunPreExposure();
        yield return RunUrpDiffuseDiagnostic();

        Cleanup();
        PrintSummary();
    }

    private void OnDestroy() => Cleanup();

    private bool CheckEnvironment()
    {
        bool urp = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset;
        LogSimple("Environment", "Active Render Pipeline is URP", urp,
            GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.name
                : "No SRP asset");
        if (!urp) return false;

        bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        LogSimple("Environment", "Color Space = Linear", linear,
            QualitySettings.activeColorSpace.ToString());
        if (!linear) return false;

        bool readback = SystemInfo.supportsAsyncGPUReadback;
        LogSimple("Environment", "Async GPU Readback supported", readback,
            SystemInfo.graphicsDeviceName);
        return readback;
    }

    private bool CreateMaterials()
    {
        Shader constant = Shader.Find("LightPLUValidation/Constant");
        Shader reference = Shader.Find("LightPLUValidation/ReferenceLambert");
        Shader exposure = Shader.Find("LightPLUValidation/ExposureSurface");
        Shader urpDiffuse = Shader.Find("LightPLUValidation/URPDirectDiffuse");

        bool found = constant && reference && exposure && urpDiffuse;
        LogSimple("Environment", "Validation shaders found", found, null);
        if (!found) return false;

        constantMaterial = new Material(constant) { hideFlags = HideFlags.HideAndDontSave };
        referenceMaterial = new Material(reference) { hideFlags = HideFlags.HideAndDontSave };
        exposureMaterial = new Material(exposure) { hideFlags = HideFlags.HideAndDontSave };
        urpDiffuseMaterial = new Material(urpDiffuse) { hideFlags = HideFlags.HideAndDontSave };
        return true;
    }

    private void CaptureEnvironment()
    {
        oldAmbientMode = RenderSettings.ambientMode;
        oldAmbientLight = RenderSettings.ambientLight;
        oldAmbientIntensity = RenderSettings.ambientIntensity;
        oldReflectionIntensity = RenderSettings.reflectionIntensity;
        oldSkybox = RenderSettings.skybox;
        oldFog = RenderSettings.fog;

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.ambientIntensity = 0.0f;
        RenderSettings.reflectionIntensity = 0.0f;
        RenderSettings.skybox = null;
        RenderSettings.fog = false;

        foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            oldLights.Add(new LightState(light, light.enabled));
            light.enabled = false;
        }

        environmentCaptured = true;
    }

    private void CreateMeasurementEnvironment()
    {
        root = new GameObject("__LightPLUValidation_Runtime") { hideFlags = HideFlags.DontSave };

        GameObject surface = new GameObject("Measurement Surface");
        surface.transform.SetParent(root.transform, false);
        surface.layer = ValidationLayer;
        surface.AddComponent<MeshFilter>().sharedMesh = CreateQuad();
        surfaceRenderer = surface.AddComponent<MeshRenderer>();
        surfaceRenderer.sharedMaterial = constantMaterial;

        GameObject cameraObject = new GameObject("Validation Camera");
        cameraObject.transform.SetParent(root.transform, false);
        cameraObject.transform.position = new Vector3(0, 0, 10);
        cameraObject.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        cameraObject.layer = ValidationLayer;

        validationCamera = cameraObject.AddComponent<Camera>();
        validationCamera.enabled = false;
        validationCamera.orthographic = true;
        validationCamera.orthographicSize = 1.5f;
        validationCamera.nearClipPlane = 0.1f;
        validationCamera.farClipPlane = 50.0f;
        validationCamera.clearFlags = CameraClearFlags.SolidColor;
        validationCamera.backgroundColor = Color.black;
        validationCamera.allowHDR = true;
        validationCamera.allowMSAA = false;
        validationCamera.cullingMask = ValidationMask;

        UniversalAdditionalCameraData cameraData = validationCamera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = false;
        cameraData.renderShadows = false;
        cameraData.antialiasing = AntialiasingMode.None;
        cameraData.dithering = false;
        cameraData.allowXRRendering = false;

        renderTarget = new RenderTexture(
            resolution, resolution, 24,
            RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear)
        {
            name = "LightPLU Validation HDR",
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave
        };
        renderTarget.Create();
        validationCamera.targetTexture = renderTarget;
    }

    private static Mesh CreateQuad()
    {
        Mesh mesh = new Mesh { name = "LightPLU Validation Quad", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[]
        {
            new Vector3(-3,-3,0), new Vector3(3,-3,0),
            new Vector3(3,3,0), new Vector3(-3,3,0)
        };
        mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        mesh.triangles = new[] { 0,1,2, 0,2,3 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private IEnumerator RunReadbackCalibration()
    {
        Stage("Stage 0 - GPU Linear HDR Readback");
        DestroyActiveLight();
        surfaceRenderer.sharedMaterial = constantMaterial;

        foreach (float value in new[] { 0.125f, 0.5f, 1.0f, 2.0f, 10.0f })
        {
            constantMaterial.SetFloat("_Value", value);
            Measurement m = default;
            yield return Measure(x => m = x);
            if (!m.Success) { LogSimple("Measurement", $"Linear {value}", false, m.Error); continue; }
            Validate("Measurement", $"Linear {value}", value, m.Gray, readbackTolerancePercent);
        }
    }

    private void RunLightUnitMath()
    {
        Stage("Stage 1 - LightUnitUtils CPU Math");
        float pointSolidAngle = LightUnitUtils.GetSolidAngleFromPointLight();
        Validate("LightUnitUtils", "Point 1000 lm -> cd",
            1000.0f / (4.0f * Mathf.PI),
            LightUnitUtils.LumenToCandela(1000.0f, pointSolidAngle), 0.001f);

        float spotSolidAngle = LightUnitUtils.GetSolidAngleFromSpotLight(60.0f);
        float expectedSolidAngle = 2.0f * Mathf.PI * (1.0f - Mathf.Cos(30.0f * Mathf.Deg2Rad));
        Validate("LightUnitUtils", "Spot 60deg solid angle", expectedSolidAngle, spotSolidAngle, 0.001f);
        Validate("LightUnitUtils", "1000 lux @2m -> cd", 4000.0f,
            LightUnitUtils.LuxToCandela(1000.0f, 2.0f), 0.001f);
    }

    private IEnumerator RunDirectionalLux()
    {
        Stage("Stage 2 - Directional Lux");
        const float lux = 10000.0f;
        const float reflectance = 0.18f;
        CreateDirectional(lux);
        referenceMaterial.SetFloat("_Reflectance", reflectance);
        surfaceRenderer.sharedMaterial = referenceMaterial;
        yield return null;

        Measurement m = default;
        yield return Measure(x => m = x);
        ValidateMeasurement("Directional", "10000 lux -> 18% Lambert",
            lux * reflectance / Mathf.PI, m, directionalTolerancePercent);
        DestroyActiveLight();
    }

    private IEnumerator RunPointCandela()
    {
        Stage("Stage 3 - Point Candela / Inverse Square");
        const float candela = 1000.0f;
        const float reflectance = 0.18f;
        referenceMaterial.SetFloat("_Reflectance", reflectance);
        surfaceRenderer.sharedMaterial = referenceMaterial;
        Light light = CreatePoint(candela, 1.0f);
        var measured = new Dictionary<float, float>();

        foreach (float distance in new[] { 1.0f, 2.0f, 4.0f })
        {
            light.transform.position = new Vector3(0, 0, distance);
            yield return null;
            Measurement m = default;
            yield return Measure(x => m = x);
            float expected = (candela / (distance * distance)) * reflectance / Mathf.PI;
            ValidateMeasurement("Point", $"1000 cd @ {distance}m", expected, m, punctualTolerancePercent);
            if (m.Success) measured[distance] = m.Gray;
        }

        if (measured.Count == 3)
        {
            Validate("Point Ratio", "1m / 2m", 4.0f, measured[1.0f] / measured[2.0f], punctualTolerancePercent);
            Validate("Point Ratio", "1m / 4m", 16.0f, measured[1.0f] / measured[4.0f], punctualTolerancePercent);
        }
        DestroyActiveLight();
    }

    private IEnumerator RunPointLumen()
    {
        Stage("Stage 4 - Point Lumen");
        const float lumen = 1000.0f;
        const float reflectance = 0.18f;
        Light light = CreatePoint(1.0f, 1.0f);
        light.enableSpotReflector = false;
        float cd = LightUnitUtils.ConvertIntensity(light, lumen, LightUnit.Lumen, LightUnit.Candela);
        light.intensity = cd;
        referenceMaterial.SetFloat("_Reflectance", reflectance);
        surfaceRenderer.sharedMaterial = referenceMaterial;
        yield return null;

        Measurement m = default;
        yield return Measure(x => m = x);
        ValidateMeasurement("Point Lumen", "1000 lm @1m",
            cd * reflectance / Mathf.PI, m, punctualTolerancePercent);
        DestroyActiveLight();
    }

    private IEnumerator RunSpotLumen()
    {
        Stage("Stage 5 - Spot Lumen / Solid Angle");
        const float lumen = 1000.0f;
        const float distance = 2.0f;
        const float reflectance = 0.18f;
        Light light = CreateSpot(1.0f, distance, 60.0f);
        light.enableSpotReflector = true;
        float cd = LightUnitUtils.ConvertIntensity(light, lumen, LightUnit.Lumen, LightUnit.Candela);
        light.intensity = cd;
        referenceMaterial.SetFloat("_Reflectance", reflectance);
        surfaceRenderer.sharedMaterial = referenceMaterial;
        yield return null;

        Measurement m = default;
        yield return Measure(x => m = x);
        float expected = (cd / (distance * distance)) * reflectance / Mathf.PI;
        ValidateMeasurement("Spot Lumen", "1000 lm, 60deg @2m center", expected, m, 5.0f);
        DestroyActiveLight();
    }

    private IEnumerator RunExposureStops()
    {
        Stage("Stage 6 - Exposure Stops");
        DestroyActiveLight();
        surfaceRenderer.sharedMaterial = exposureMaterial;
        exposureMaterial.SetFloat("_Value", 1.0f);

        foreach (float ev in new[] { -2.0f, -1.0f, 0.0f, 1.0f, 2.0f })
        {
            exposureMaterial.SetFloat("_EV100", ev);
            Measurement m = default;
            yield return Measure(x => m = x);
            ValidateMeasurement("Exposure", $"Input 1.0 @ EV {ev:+0;-0;0}",
                PhysicalExposure.GetDirectExposureMultiplier(ev), m, exposureTolerancePercent);
        }
    }

    private IEnumerator RunExposureIntegration()
    {
        Stage("Stage 7 - 100000 lux + 18% Lambert + EV15");
        const float luminance = 100000.0f * 0.18f / Mathf.PI;
        exposureMaterial.SetFloat("_Value", luminance);
        exposureMaterial.SetFloat("_EV100", 15.0f);
        surfaceRenderer.sharedMaterial = exposureMaterial;

        Measurement m = default;
        yield return Measure(x => m = x);
        ValidateMeasurement("Exposure Integration", "100000 lux, 18% Lambert, EV15",
            luminance * PhysicalExposure.GetDirectExposureMultiplier(15.0f),
            m, exposureTolerancePercent);
    }

    private IEnumerator RunPreExposure()
    {
        Stage("Stage 8 - Light-side Reference Pre-Exposure");
        const float lux = 100000.0f;
        const float reflectance = 0.18f;
        Light light = CreateDirectional(1.0f);
        bool converted = PhysicalLightUnitConverter.TryToPreExposedNativeIntensity(
            light, lux, LightUnit.Lux, 15.0f,
            out _, out float preExposed);

        if (!converted)
        {
            LogSimple("PreExposure", "100000 lux @ Reference EV15", false, "Conversion failed");
            DestroyActiveLight();
            yield break;
        }

        light.intensity = preExposed;
        referenceMaterial.SetFloat("_Reflectance", reflectance);
        surfaceRenderer.sharedMaterial = referenceMaterial;
        yield return null;

        Measurement m = default;
        yield return Measure(x => m = x);
        float expected = lux * reflectance / Mathf.PI * PhysicalExposure.GetPreExposureMultiplier(15.0f);
        ValidateMeasurement("PreExposure", "100000 lux pre-exposed at EV15",
            expected, m, directionalTolerancePercent);
        DestroyActiveLight();
    }

    private IEnumerator RunUrpDiffuseDiagnostic()
    {
        Stage("Stage 9 - URP Direct Diffuse Diagnostic");
        const float lux = 10000.0f;
        const float albedo = 0.18f;
        CreateDirectional(lux);

        referenceMaterial.SetFloat("_Reflectance", albedo);
        surfaceRenderer.sharedMaterial = referenceMaterial;
        yield return null;
        Measurement reference = default;
        yield return Measure(x => reference = x);

        urpDiffuseMaterial.SetFloat("_Albedo", albedo);
        surfaceRenderer.sharedMaterial = urpDiffuseMaterial;
        yield return null;
        Measurement urp = default;
        yield return Measure(x => urp = x);

        if (reference.Success && urp.Success && reference.Gray > 0.0f)
        {
            float ratio = urp.Gray / reference.Gray;
            LogInfo("URP Diagnostic", "URP direct diffuse / physical Lambert",
                Mathf.PI * 0.96f, ratio,
                $"Reference={reference.Gray:G9}, URP={urp.Gray:G9}");
        }
        else
        {
            LogSimple("URP Diagnostic", "URP direct diffuse / physical Lambert", false,
                reference.Error ?? urp.Error ?? "Reference was zero");
        }
        DestroyActiveLight();
    }

    private Light CreateDirectional(float intensity)
    {
        DestroyActiveLight();
        GameObject go = new GameObject("Validation Directional");
        go.transform.SetParent(root.transform, false);
        go.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        go.layer = ValidationLayer;
        Light light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = Color.white;
        light.intensity = intensity;
        light.lightUnit = LightUnit.Lux;
        light.shadows = LightShadows.None;
        light.cullingMask = ValidationMask;
        light.bounceIntensity = 0.0f;
        activeLight = light;
        return light;
    }

    private Light CreatePoint(float intensity, float distance)
    {
        DestroyActiveLight();
        GameObject go = new GameObject("Validation Point");
        go.transform.SetParent(root.transform, false);
        go.transform.position = new Vector3(0, 0, distance);
        go.layer = ValidationLayer;
        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Color.white;
        light.intensity = intensity;
        light.lightUnit = LightUnit.Candela;
        light.range = 1000.0f;
        light.renderMode = LightRenderMode.ForcePixel;
        light.shadows = LightShadows.None;
        light.cullingMask = ValidationMask;
        light.bounceIntensity = 0.0f;
        activeLight = light;
        return light;
    }

    private Light CreateSpot(float intensity, float distance, float angle)
    {
        DestroyActiveLight();
        GameObject go = new GameObject("Validation Spot");
        go.transform.SetParent(root.transform, false);
        go.transform.position = new Vector3(0, 0, distance);
        go.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        go.layer = ValidationLayer;
        Light light = go.AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = Color.white;
        light.intensity = intensity;
        light.lightUnit = LightUnit.Candela;
        light.range = 1000.0f;
        light.spotAngle = angle;
        light.innerSpotAngle = 0.0f;
        light.enableSpotReflector = true;
        light.renderMode = LightRenderMode.ForcePixel;
        light.shadows = LightShadows.None;
        light.cullingMask = ValidationMask;
        light.bounceIntensity = 0.0f;
        activeLight = light;
        return light;
    }

    private IEnumerator Measure(Action<Measurement> callback)
    {
        validationCamera.Render();
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(renderTarget, 0, TextureFormat.RGBAFloat);
        while (!request.done) yield return null;

        if (request.hasError)
        {
            callback(new Measurement(false, Vector3.zero, "AsyncGPUReadback error"));
            yield break;
        }

        var pixels = request.GetData<Vector4>();
        int region = Mathf.Clamp(centerRegionSize, 1, Mathf.Min(resolution, resolution));
        int startX = resolution / 2 - region / 2;
        int startY = resolution / 2 - region / 2;
        Vector3 sum = Vector3.zero;
        int count = 0;

        for (int y = 0; y < region; ++y)
        for (int x = 0; x < region; ++x)
        {
            Vector4 p = pixels[(startY + y) * resolution + (startX + x)];
            if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z))
            {
                callback(new Measurement(false, Vector3.zero, $"Non-finite pixel {p}"));
                yield break;
            }
            sum += new Vector3(p.x, p.y, p.z);
            count++;
        }

        callback(new Measurement(true, sum / count, null));
    }

    private void ValidateMeasurement(string category, string name, float expected, Measurement m, float tolerance)
    {
        if (!m.Success) { LogSimple(category, name, false, m.Error); return; }
        Validate(category, name, expected, m.Gray, tolerance);
    }

    private void Validate(string category, string name, float expected, float actual, float tolerance)
    {
        float error = Mathf.Abs(actual - expected) / Mathf.Max(Mathf.Abs(expected), 1e-7f) * 100.0f;
        bool pass = error <= tolerance;
        Result result = new Result(pass, false, category, name, expected, actual, error, tolerance, null);
        results.Add(result);
        string text = $"[{(pass ? "PASS" : "FAIL")}] {category} / {name}\n" +
                      $"  Expected : {expected:G9}\n  Actual   : {actual:G9}\n" +
                      $"  Error    : {error:F4}%\n  Tolerance: {tolerance:F4}%";
        if (pass) Debug.Log(text); else Debug.LogError(text);
    }

    private void LogInfo(string category, string name, float expected, float actual, string note)
    {
        float error = Mathf.Abs(actual - expected) / Mathf.Max(Mathf.Abs(expected), 1e-7f) * 100.0f;
        results.Add(new Result(true, true, category, name, expected, actual, error, 0.0f, note));
        Debug.Log($"[INFO] {category} / {name}\n  Expected : {expected:G9}\n" +
                  $"  Actual   : {actual:G9}\n  Error    : {error:F4}%\n  Note     : {note}");
    }

    private void LogSimple(string category, string name, bool pass, string note)
    {
        results.Add(new Result(pass, false, category, name, 0, 0, 0, 0, note));
        string text = $"[{(pass ? "PASS" : "FAIL")}] {category} / {name}" +
                      (string.IsNullOrEmpty(note) ? "" : $"\n  Note     : {note}");
        if (pass) Debug.Log(text); else Debug.LogError(text);
    }

    private bool HasFailure(string category)
    {
        foreach (Result r in results)
            if (r.Category == category && !r.Pass && !r.Info) return true;
        return false;
    }

    private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    private static void Stage(string title) => Debug.Log($"\n---------------- {title} ----------------");

    private void PrintSummary()
    {
        int pass = 0, fail = 0, info = 0;
        foreach (Result r in results)
        {
            if (r.Info) info++;
            else if (r.Pass) pass++;
            else fail++;
        }
        string text = $"\n============================================================\n" +
                      $" LightPLU Validation Summary\n" +
                      $"============================================================\n" +
                      $" PASS : {pass}\n FAIL : {fail}\n INFO : {info}\n" +
                      $" RESULT: {(fail == 0 ? "PASS" : "FAIL")}\n" +
                      $"============================================================";
        if (fail == 0) Debug.Log(text); else Debug.LogError(text);
    }

    private void DestroyActiveLight()
    {
        if (activeLight == null) return;
        GameObject go = activeLight.gameObject;
        activeLight = null;
        Destroy(go);
    }

    private void Cleanup()
    {
        if (cleanedUp) return;
        cleanedUp = true;
        DestroyActiveLight();

        if (validationCamera != null) validationCamera.targetTexture = null;
        if (renderTarget != null) { renderTarget.Release(); Destroy(renderTarget); }
        if (constantMaterial != null) Destroy(constantMaterial);
        if (referenceMaterial != null) Destroy(referenceMaterial);
        if (exposureMaterial != null) Destroy(exposureMaterial);
        if (urpDiffuseMaterial != null) Destroy(urpDiffuseMaterial);
        if (root != null) Destroy(root);

        if (!environmentCaptured) return;
        RenderSettings.ambientMode = oldAmbientMode;
        RenderSettings.ambientLight = oldAmbientLight;
        RenderSettings.ambientIntensity = oldAmbientIntensity;
        RenderSettings.reflectionIntensity = oldReflectionIntensity;
        RenderSettings.skybox = oldSkybox;
        RenderSettings.fog = oldFog;
        foreach (LightState state in oldLights)
            if (state.Light != null) state.Light.enabled = state.Enabled;
    }
}
