using UnityEngine;
using static UnityEngine.Rendering.DebugUI;
using UnityEditor.Presets;
using Unity.VisualScripting;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Rendering;
#endif

public class LightPLU : MonoBehaviour
{
	public Light targetLight;

	private void OnEnable()
	{
		if (targetLight == null)
			targetLight = GetComponent<Light>(); 
	}
}

#if UNITY_EDITOR

[CustomEditor(typeof(LightPLU))]
[CanEditMultipleObjects]
public class LightInspector : Editor
{
	LightPLU instance;

	GUIContent iconHighSun;
	GUIContent iconCloudy;
	GUIContent iconLowSun;
	GUIContent iconMoon;
	GUIContent iconExterior;
	GUIContent iconInterior;
	GUIContent iconDecorative;
	GUIContent iconCandle;
	private void OnEnable()
	{
		instance = (LightPLU)target;

		if (iconHighSun == null)
			iconHighSun = new GUIContent(CoreEditorUtils.LoadIcon(@"Packages/com.unity.render-pipelines.core/Editor/Lighting/Icons/LightUnitIcons", "d_VeryBrightSun", ".png"));
		if (iconCloudy == null)
			iconCloudy = new GUIContent(CoreEditorUtils.LoadIcon(@"Packages/com.unity.render-pipelines.core/Editor/Lighting/Icons/LightUnitIcons", "d_Overcast", ".png"));
		if (iconLowSun == null)
			iconLowSun = new GUIContent(CoreEditorUtils.LoadIcon(@"Packages/com.unity.render-pipelines.core/Editor/Lighting/Icons/LightUnitIcons", "d_SunriseSunset", ".png"));
		if (iconMoon == null)
			iconMoon = new GUIContent(CoreEditorUtils.LoadIcon(@"Packages/com.unity.render-pipelines.core/Editor/Lighting/Icons/LightUnitIcons", "d_Moonlight", ".png"));
		if (iconExterior == null)
			iconExterior = new GUIContent(CoreEditorUtils.LoadIcon(@"Packages/com.unity.render-pipelines.core/Editor/Lighting/Icons/LightUnitIcons", "d_ExteriorLight", ".png"));
		if (iconInterior == null)
			iconInterior = new GUIContent(CoreEditorUtils.LoadIcon(@"Packages/com.unity.render-pipelines.core/Editor/Lighting/Icons/LightUnitIcons", "d_InteriorLight", ".png"));
		if (iconDecorative == null)
			iconDecorative = new GUIContent(CoreEditorUtils.LoadIcon(@"Packages/com.unity.render-pipelines.core/Editor/Lighting/Icons/LightUnitIcons", "d_DecorativeLight", ".png"));
		if (iconCandle == null)
			iconCandle = new GUIContent(CoreEditorUtils.LoadIcon(@"Packages/com.unity.render-pipelines.core/Editor/Lighting/Icons/LightUnitIcons", "d_Candlelight", ".png"));
	}

	public override void OnInspectorGUI()
	{
		instance.targetLight = (Light)EditorGUILayout.ObjectField("Light", instance.targetLight, typeof(Light), true);
		if (instance.targetLight == null)
			return;

		switch (instance.targetLight.type) {
			case LightType.Directional: DrawDirectionalLight(); break;
			case LightType.Point: DrawPointLight(); break;
			case LightType.Spot: DrawSpotLight(); break;
		}
	}

	float curIntensity;
	float targetIntensity;
	Color markerColor = new Color32(153, 153, 153, 255);

	const float Lux2Int = 0.7f / 80000f;
	const float Int2Lux = 80000f / 0.7f;
	
	void DrawDirectionalLight()
	{
		var minValue = 0f;
		var maxValue = 130000;
		if (instance.targetLight.intensity < 0.001f)
			instance.targetLight.intensity = 0.001f;	// URP light cannot express less than 0.001
		curIntensity = instance.targetLight.intensity * Int2Lux;
		if (curIntensity > maxValue)
			curIntensity = maxValue;

		EditorGUILayout.BeginHorizontal();
		EditorGUILayout.LabelField("Intensity", GUILayout.Width(120));

		EditorGUILayout.BeginVertical();
		EditorGUILayout.BeginHorizontal();
		
		targetIntensity = GUILayout.HorizontalSlider(curIntensity, minValue, maxValue);
		if (curIntensity != targetIntensity) {
			instance.targetLight.intensity = targetIntensity * Lux2Int;
		}

		Rect sliderRect = GUILayoutUtility.GetLastRect();
		
		// markers
		Rect marker = new Rect(sliderRect.x, sliderRect.y + 8f, 1f, 2f);
		marker.x = sliderRect.x + 500f / 130000f * sliderRect.width + 4f;
		EditorGUI.DrawRect(marker, markerColor);
		marker.x = sliderRect.x + 10000f / 130000f * sliderRect.width + 3f;
		EditorGUI.DrawRect(marker, markerColor);
		marker.x = sliderRect.x + 80000f / 130000f * sliderRect.width - 2f;
		EditorGUI.DrawRect(marker, markerColor);

		var lightType = curIntensity >= 80000f ? 0 : curIntensity >= 10000f ? 1 : curIntensity >= 500f ? 2 : 3;
		var menu = new GenericMenu();
		menu.AddItem(new GUIContent("High Sun"), lightType == 0, () => instance.targetLight.intensity = 100000f * Lux2Int);
		menu.AddItem(new GUIContent("Cloudy"), lightType == 1, () => instance.targetLight.intensity = 40000f * Lux2Int);
		menu.AddItem(new GUIContent("Low Sun"), lightType == 2, () => instance.targetLight.intensity = 5000f * Lux2Int);
		menu.AddItem(new GUIContent("Moon"), lightType == 3, () => instance.targetLight.intensity = 300f * Lux2Int);

		GUIContent lightIcon = null;
		int minExposure = 0;
		int maxExposure = 15;
		switch (lightType) {
			case 0:
				lightIcon = iconHighSun;
				minExposure = 12;
				maxExposure = 15;
				break;
			case 1:
				lightIcon = iconCloudy;
				minExposure = 8;
				maxExposure = 12;
				break;
			case 2:
				lightIcon = iconLowSun;
				minExposure = 6;
				maxExposure = 8;
				break;
			case 3:
				lightIcon = iconMoon;
				minExposure = 0;
				maxExposure = 6;
				break;
		}
		if (EditorGUILayout.DropdownButton(lightIcon, FocusType.Passive, GUILayout.Width(40))) {
			menu.ShowAsContext();
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.BeginHorizontal();
		// Lux
		targetIntensity = EditorGUILayout.FloatField(targetIntensity);
		if (curIntensity != targetIntensity) {
			instance.targetLight.intensity = targetIntensity * Lux2Int;
		}
		EditorGUILayout.LabelField("Lux", GUILayout.Width(40));

		EditorGUILayout.EndHorizontal();
		EditorGUILayout.EndVertical();

		EditorGUILayout.EndHorizontal();

		EditorGUI.BeginDisabledGroup(true);
		EditorGUILayout.TextField("Exposure range", minExposure + " ~ " + maxExposure);
		EditorGUILayout.TextField("Post Exposure range", (15f - maxExposure) + " ~ " + (15f - minExposure));
		EditorGUI.EndDisabledGroup();
	}

	const float pLumen2Int = 0.01f / 40000f;
	const float Int2pLumen = 40000f / 0.01f;
	void DrawPointLight()
	{
		var minValue = 0f;
		var maxValue = 40000;
		if (instance.targetLight.intensity < 0.0001f)
			instance.targetLight.intensity = 0.0001f;    // URP light cannot express less than 0.001
		curIntensity = instance.targetLight.intensity * Int2pLumen;
		if (curIntensity > maxValue)
			curIntensity = maxValue;

		EditorGUILayout.BeginHorizontal();
		EditorGUILayout.LabelField("Intensity", GUILayout.Width(120));

		EditorGUILayout.BeginVertical();
		EditorGUILayout.BeginHorizontal();

		targetIntensity = GUILayout.HorizontalSlider(curIntensity, minValue, maxValue);
		if (curIntensity != targetIntensity) {
			instance.targetLight.intensity = targetIntensity * pLumen2Int;
		}

		Rect sliderRect = GUILayoutUtility.GetLastRect();

		// markers
		Rect marker = new Rect(sliderRect.x, sliderRect.y + 8f, 1f, 2f);
		marker.x = sliderRect.x + 1600f / 40000f * sliderRect.width + 4f;
		EditorGUI.DrawRect(marker, Color.black);
		marker.x = sliderRect.x + 4000f / 40000f * sliderRect.width + 3f;
		EditorGUI.DrawRect(marker, Color.red);
		marker.x = sliderRect.x + 10000f / 40000f * sliderRect.width - 2f;
		EditorGUI.DrawRect(marker, markerColor);

		var lightType = curIntensity >= 10000f ? 0 : curIntensity >= 3000f ? 1 : curIntensity >= 1600f ? 2 : 3;
		var menu = new GenericMenu();
		menu.AddItem(new GUIContent("Exterior"), lightType == 0, () => instance.targetLight.intensity = 20000f * pLumen2Int);
		menu.AddItem(new GUIContent("Interior"), lightType == 1, () => instance.targetLight.intensity = 5000f * pLumen2Int);
		menu.AddItem(new GUIContent("Decorative"), lightType == 2, () => instance.targetLight.intensity = 2000f * pLumen2Int);
		menu.AddItem(new GUIContent("Candle"), lightType == 3, () => instance.targetLight.intensity = 1200f * pLumen2Int);

		GUIContent lightIcon = null;
		int minExposure = 0;
		int maxExposure = 15;
		switch (lightType) {
			case 0:
				lightIcon = iconExterior;
				minExposure = 6;
				maxExposure = 8;
				break;
			case 1:
				lightIcon = iconInterior;
				minExposure = 3;
				maxExposure = 6;
				break;
			case 2:
				lightIcon = iconDecorative;
				minExposure = 0;
				maxExposure = 3;
				break;
			case 3:
				lightIcon = iconCandle;
				minExposure = 0;
				maxExposure = 3;
				break;
		}
		if (EditorGUILayout.DropdownButton(lightIcon, FocusType.Passive, GUILayout.Width(40))) {
			menu.ShowAsContext();
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.BeginHorizontal();
		// Lux
		targetIntensity = EditorGUILayout.FloatField(targetIntensity);
		if (curIntensity != targetIntensity) {
			instance.targetLight.intensity = targetIntensity * pLumen2Int;
		}
		EditorGUILayout.LabelField("Lux", GUILayout.Width(40));

		EditorGUILayout.EndHorizontal();
		EditorGUILayout.EndVertical();

		EditorGUILayout.EndHorizontal();

		if (instance.targetLight.intensity < 0.001f)
			EditorGUILayout.LabelField("URP light cannot express less than 0.001");

		EditorGUI.BeginDisabledGroup(true);
		EditorGUILayout.TextField("Exposure range", minExposure + " ~ " + maxExposure);
		EditorGUILayout.TextField("Post Exposure range", (15f - maxExposure) + " ~ " + (15f - minExposure));
		EditorGUI.EndDisabledGroup();
	}

	const float sLumen2Int = 0.38f / 10000f;
	const float Int2sLumen = 10000f / 0.38f;
	void DrawSpotLight()
	{
		var minValue = 0f;
		var maxValue = 40000;
		if (instance.targetLight.intensity < 0.001f)
			instance.targetLight.intensity = 0.001f;    // URP light cannot express less than 0.001
		curIntensity = instance.targetLight.intensity * Int2sLumen;
		if (curIntensity > maxValue)
			curIntensity = maxValue;

		EditorGUILayout.BeginHorizontal();
		EditorGUILayout.LabelField("Intensity", GUILayout.Width(120));

		EditorGUILayout.BeginVertical();
		EditorGUILayout.BeginHorizontal();

		targetIntensity = GUILayout.HorizontalSlider(curIntensity, minValue, maxValue);
		if (curIntensity != targetIntensity) {
			instance.targetLight.intensity = targetIntensity * sLumen2Int;
		}

		Rect sliderRect = GUILayoutUtility.GetLastRect();

		// markers
		Rect marker = new Rect(sliderRect.x, sliderRect.y + 8f, 1f, 2f);
		marker.x = sliderRect.x + 1600f / 40000f * sliderRect.width + 4f;
		EditorGUI.DrawRect(marker, markerColor);
		marker.x = sliderRect.x + 3000f / 40000f * sliderRect.width + 3f;
		EditorGUI.DrawRect(marker, markerColor);
		marker.x = sliderRect.x + 10000f / 40000f * sliderRect.width - 2f;
		EditorGUI.DrawRect(marker, markerColor);

		var lightType = curIntensity >= 10000f ? 0 : curIntensity >= 3000f ? 1 : curIntensity >= 300f ? 2 : 3;
		var menu = new GenericMenu();
		menu.AddItem(new GUIContent("Exterior"), lightType == 0, () => instance.targetLight.intensity = 20000f * sLumen2Int);
		menu.AddItem(new GUIContent("Interior"), lightType == 1, () => instance.targetLight.intensity = 5000f * sLumen2Int);
		menu.AddItem(new GUIContent("Decorative"), lightType == 2, () => instance.targetLight.intensity = 2000f * sLumen2Int);
		menu.AddItem(new GUIContent("Candle"), lightType == 3, () => instance.targetLight.intensity = 250f * sLumen2Int);

		GUIContent lightIcon = null;
		int minExposure = 0;
		int maxExposure = 15;
		switch (lightType) {
			case 0:
				lightIcon = iconExterior;
				minExposure = 6;
				maxExposure = 8;
				break;
			case 1:
				lightIcon = iconInterior;
				minExposure = 3;
				maxExposure = 6;
				break;
			case 2:
				lightIcon = iconDecorative;
				minExposure = 0;
				maxExposure = 3;
				break;
			case 3:
				lightIcon = iconCandle;
				minExposure = 0;
				maxExposure = 3;
				break;
		}
		if (EditorGUILayout.DropdownButton(lightIcon, FocusType.Passive, GUILayout.Width(40))) {
			menu.ShowAsContext();
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.BeginHorizontal();
		// Lux
		targetIntensity = EditorGUILayout.FloatField(targetIntensity);
		if (curIntensity != targetIntensity) {
			instance.targetLight.intensity = targetIntensity * sLumen2Int;
		}
		EditorGUILayout.LabelField("Lux", GUILayout.Width(40));

		EditorGUILayout.EndHorizontal();
		EditorGUILayout.EndVertical();

		EditorGUILayout.EndHorizontal();

		EditorGUI.BeginDisabledGroup(true);
		EditorGUILayout.TextField("Exposure range", minExposure + " ~ " + maxExposure);
		EditorGUILayout.TextField("Post Exposure range", (15f - maxExposure) + " ~ " + (15f - minExposure));
		EditorGUI.EndDisabledGroup();
	}
}

#endif