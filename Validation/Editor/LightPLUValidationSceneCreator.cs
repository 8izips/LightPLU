#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LightPLUValidationSceneCreator
{
    private const string ScenePath = "Assets/LightPLUValidation.unity";

    [MenuItem("Tools/LightPLU Validation/Create or Reset Validation Scene")]
    public static void CreateValidationScene()
    {
        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);

        GameObject runnerObject = new GameObject("LightPLU Validation Runner");
        runnerObject.AddComponent<LightPLUValidationRunner>();

        EditorSceneManager.MarkSceneDirty(scene);

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            Debug.LogError($"[LightPLU Validation] Could not save {ScenePath}");
            return;
        }

        Selection.activeGameObject = runnerObject;
        Debug.Log(
            $"[LightPLU Validation] Created {ScenePath}. " +
            "Press Play and read the Console output.");
    }

    [MenuItem("Tools/LightPLU Validation/Open Validation Scene")]
    public static void OpenValidationScene()
    {
        SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);

        if (scene == null)
        {
            CreateValidationScene();
            return;
        }

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}
#endif
