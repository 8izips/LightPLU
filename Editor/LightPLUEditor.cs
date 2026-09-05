using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[CustomEditor(typeof(LightPLU))]
public sealed class LightPLUEditor : Editor
{
    private readonly struct PresetRange
    {
        public readonly string Name;
        public readonly float Min;
        public readonly float Max;
        public readonly float Preset;

        public PresetRange(string name, float min, float max, float preset)
        {
            Name = name;
            Min = min;
            Max = max;
            Preset = preset;
        }
    }

    // These ranges follow Unity 6 Core's physical-light authoring categories,
    // while this inspector remains independent from Unity's internal editor API.
    private static readonly PresetRange[] LuxRanges =
    {
        new PresetRange("Moon", 0.0f, 1.0f, 0.5f),
        new PresetRange("Low Sun", 1.0f, 10000.0f, 5000.0f),
        new PresetRange("Cloudy", 10000.0f, 80000.0f, 20000.0f),
        new PresetRange("High Sun", 80000.0f, 130000.0f, 100000.0f),
    };

    private static readonly float[] LuxDistribution =
    {
        0.0f, 0.05f, 0.5f, 0.9f, 1.0f
    };

    private static readonly PresetRange[] LumenRanges =
    {
        new PresetRange("Candle", 0.0f, 15.0f, 12.5f),
        new PresetRange("Decorative", 15.0f, 300.0f, 100.0f),
        new PresetRange("Interior", 300.0f, 3000.0f, 1000.0f),
        new PresetRange("Exterior", 3000.0f, 40000.0f, 10000.0f),
    };

    private static readonly float[] LumenDistribution =
    {
        0.0f, 0.25f, 0.5f, 0.75f, 1.0f
    };

    private SerializedProperty _targetLight;
    private SerializedProperty _physicalUnit;
    private SerializedProperty _physicalIntensity;
    private SerializedProperty _referenceEV100;
    private SerializedProperty _applyAutomatically;

    private void OnEnable()
    {
        _targetLight = serializedObject.FindProperty("targetLight");
        _physicalUnit = serializedObject.FindProperty("physicalUnit");
        _physicalIntensity = serializedObject.FindProperty("physicalIntensity");
        _referenceEV100 = serializedObject.FindProperty("referenceEV100");
        _applyAutomatically = serializedObject.FindProperty("applyAutomatically");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        LightPLU lightPLU = (LightPLU)target;
        Light light = _targetLight.objectReferenceValue as Light;
        if (light == null)
            light = lightPLU.GetComponent<Light>();

        EditorGUILayout.LabelField("Physical Light", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_targetLight);

        if (light == null)
        {
            EditorGUILayout.HelpBox("LightPLU requires a target Light.", MessageType.Error);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        DrawPhysicalUnit(light);
        DrawIntensityField(light);
        DrawPresetSlider(light);

        EditorGUILayout.Space(4.0f);
        EditorGUILayout.PropertyField(
            _referenceEV100,
            new GUIContent(
                "Reference EV100",
                "Reference exposure used to pre-expose this light. Keep this synchronized with the Physical Exposure Volume."));

        EditorGUILayout.PropertyField(
            _applyAutomatically,
            new GUIContent(
                "Apply Automatically",
                "Recalculate the pre-exposed Unity Light intensity whenever this component changes."));

        bool changed = serializedObject.ApplyModifiedProperties();

        if (changed && _applyAutomatically.boolValue)
            ApplyToLight(lightPLU, light, "Adjust LightPLU");

        EditorGUILayout.Space(6.0f);
        DrawDiagnostics(lightPLU, light);

        using (new EditorGUI.DisabledScope(_applyAutomatically.boolValue))
        {
            if (GUILayout.Button("Apply Physical Light"))
                ApplyToLight(lightPLU, light, "Apply LightPLU");
        }
    }

    private void DrawPhysicalUnit(Light light)
    {
        LightUnit current = (LightUnit)_physicalUnit.intValue;
        List<LightUnit> supported = GetSupportedUnits(light.type);

        if (supported.Count == 0)
        {
            EditorGUILayout.HelpBox(
                $"No supported physical units were found for {light.type}.",
                MessageType.Warning);
            return;
        }

        int selectedIndex = supported.IndexOf(current);
        if (selectedIndex < 0)
            selectedIndex = 0;

        string[] labels = new string[supported.Count];
        for (int i = 0; i < supported.Count; ++i)
            labels[i] = supported[i].ToString();

        EditorGUI.BeginChangeCheck();
        int nextIndex = EditorGUILayout.Popup("Unit", selectedIndex, labels);
        if (!EditorGUI.EndChangeCheck())
            return;

        LightUnit oldUnit = current;
        LightUnit newUnit = supported[nextIndex];
        float oldIntensity = Mathf.Max(0.0f, _physicalIntensity.floatValue);

        float converted = oldIntensity;
        try
        {
            if (LightUnitUtils.IsLightUnitSupported(light.type, oldUnit))
            {
                converted = LightUnitUtils.ConvertIntensity(
                    light,
                    oldIntensity,
                    oldUnit,
                    newUnit);
            }
        }
        catch (ArgumentException)
        {
            converted = oldIntensity;
        }

        _physicalUnit.intValue = (int)newUnit;
        if (IsFinite(converted) && converted >= 0.0f)
            _physicalIntensity.floatValue = converted;
    }

    private void DrawIntensityField(Light light)
    {
        LightUnit unit = (LightUnit)_physicalUnit.intValue;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(
            new GUIContent(
                "Intensity",
                "Authored physical intensity. The preset slider below changes this same value."));

        float value = EditorGUILayout.FloatField(
            Mathf.Max(0.0f, _physicalIntensity.floatValue));

        GUILayout.Label(unit.ToString(), GUILayout.Width(64.0f));
        EditorGUILayout.EndHorizontal();

        _physicalIntensity.floatValue = Mathf.Max(0.0f, value);
    }

    private void DrawPresetSlider(Light light)
    {
        LightUnit authoredUnit = (LightUnit)_physicalUnit.intValue;
        LightUnit sliderUnit = light.type == LightType.Directional
            ? LightUnit.Lux
            : LightUnit.Lumen;

        if (!LightUnitUtils.IsLightUnitSupported(light.type, sliderUnit))
            return;

        float sliderIntensity;
        try
        {
            sliderIntensity = LightUnitUtils.ConvertIntensity(
                light,
                Mathf.Max(0.0f, _physicalIntensity.floatValue),
                authoredUnit,
                sliderUnit);
        }
        catch (ArgumentException)
        {
            return;
        }

        if (!IsFinite(sliderIntensity))
            return;

        PresetRange[] ranges = sliderUnit == LightUnit.Lux
            ? LuxRanges
            : LumenRanges;
        float[] distribution = sliderUnit == LightUnit.Lux
            ? LuxDistribution
            : LumenDistribution;

        int activeRange = FindRange(ranges, sliderIntensity);

        EditorGUILayout.Space(3.0f);
        EditorGUILayout.LabelField("Standard Range", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < ranges.Length; ++i)
        {
            bool active = i == activeRange;
            GUIStyle style = GetPresetButtonStyle(i, ranges.Length);

            bool pressed = GUILayout.Toggle(
                active,
                new GUIContent(
                    ranges[i].Name,
                    $"{ranges[i].Name}: {FormatIntensity(ranges[i].Preset)} {sliderUnit}"),
                style);

            if (pressed && !active)
            {
                SetFromSliderUnit(
                    light,
                    sliderUnit,
                    authoredUnit,
                    ranges[i].Preset);
                sliderIntensity = ranges[i].Preset;
                activeRange = i;
            }
        }
        EditorGUILayout.EndHorizontal();

        float normalized = IntensityToSlider(
            sliderIntensity,
            ranges,
            distribution);

        EditorGUI.BeginChangeCheck();
        float newNormalized = GUILayout.HorizontalSlider(normalized, 0.0f, 1.0f);
        if (EditorGUI.EndChangeCheck())
        {
            float newSliderIntensity = SliderToIntensity(
                newNormalized,
                ranges,
                distribution);

            SetFromSliderUnit(
                light,
                sliderUnit,
                authoredUnit,
                newSliderIntensity);

            sliderIntensity = newSliderIntensity;
            activeRange = FindRange(ranges, sliderIntensity);
        }

        string rangeName = activeRange >= 0
            ? ranges[activeRange].Name
            : sliderIntensity < ranges[0].Min
                ? "Below standard range"
                : "Above standard range";

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(rangeName, EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        GUILayout.Label(
            $"{FormatIntensity(sliderIntensity)} {sliderUnit}",
            EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void SetFromSliderUnit(
        Light light,
        LightUnit sliderUnit,
        LightUnit authoredUnit,
        float sliderIntensity)
    {
        try
        {
            float converted = LightUnitUtils.ConvertIntensity(
                light,
                Mathf.Max(0.0f, sliderIntensity),
                sliderUnit,
                authoredUnit);

            if (IsFinite(converted) && converted >= 0.0f)
                _physicalIntensity.floatValue = converted;
        }
        catch (ArgumentException)
        {
            // Keep the current authored value if Unity rejects the conversion.
        }
    }

    private static float IntensityToSlider(
        float intensity,
        PresetRange[] ranges,
        float[] distribution)
    {
        if (intensity <= ranges[0].Min)
            return 0.0f;

        if (intensity >= ranges[ranges.Length - 1].Max)
            return 1.0f;

        int rangeIndex = FindRange(ranges, intensity);
        if (rangeIndex < 0)
            return intensity < ranges[0].Min ? 0.0f : 1.0f;

        PresetRange range = ranges[rangeIndex];
        float t = Mathf.InverseLerp(range.Min, range.Max, intensity);
        return Mathf.Lerp(
            distribution[rangeIndex],
            distribution[rangeIndex + 1],
            t);
    }

    private static float SliderToIntensity(
        float slider,
        PresetRange[] ranges,
        float[] distribution)
    {
        slider = Mathf.Clamp01(slider);

        for (int i = 0; i < ranges.Length; ++i)
        {
            if (slider <= distribution[i + 1] || i == ranges.Length - 1)
            {
                float t = Mathf.InverseLerp(
                    distribution[i],
                    distribution[i + 1],
                    slider);

                return Mathf.Lerp(ranges[i].Min, ranges[i].Max, t);
            }
        }

        return ranges[ranges.Length - 1].Max;
    }

    private static int FindRange(PresetRange[] ranges, float intensity)
    {
        for (int i = 0; i < ranges.Length; ++i)
        {
            if (intensity >= ranges[i].Min && intensity <= ranges[i].Max)
                return i;
        }

        return -1;
    }

    private static GUIStyle GetPresetButtonStyle(int index, int count)
    {
        if (count <= 1)
            return EditorStyles.miniButton;
        if (index == 0)
            return EditorStyles.miniButtonLeft;
        if (index == count - 1)
            return EditorStyles.miniButtonRight;
        return EditorStyles.miniButtonMid;
    }

    private static List<LightUnit> GetSupportedUnits(LightType lightType)
    {
        var result = new List<LightUnit>();

        foreach (LightUnit unit in Enum.GetValues(typeof(LightUnit)))
        {
            if (LightUnitUtils.IsLightUnitSupported(lightType, unit))
                result.Add(unit);
        }

        return result;
    }

    private static void DrawDiagnostics(LightPLU lightPLU, Light light)
    {
        EditorGUILayout.LabelField("Calculated", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.EnumPopup(
                "Native Unit",
                LightUnitUtils.GetNativeLightUnit(light.type));

            EditorGUILayout.FloatField(
                "Physical Native",
                lightPLU.NativePhysicalIntensity);

            EditorGUILayout.FloatField(
                "URP Intensity",
                lightPLU.PreExposedNativeIntensity);
        }
    }

    private static void ApplyToLight(
        LightPLU lightPLU,
        Light light,
        string undoName)
    {
        Undo.RecordObject(light, undoName);
        lightPLU.ApplyPhysicalLight();
        EditorUtility.SetDirty(light);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string FormatIntensity(float value)
    {
        if (value >= 1000.0f)
            return value.ToString("N0");
        if (value >= 100.0f)
            return value.ToString("N1");
        if (value >= 1.0f)
            return value.ToString("N2");
        return value.ToString("N3");
    }
}
