using LandscapeMatrix;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LandscapeMatrixSceneMigrationTool
{
    private const string LevelScenePath = "Assets/Scenes/Level_01.unity";

    [MenuItem("LandscapeMatrix/Migrate Level_01 To Static Scene")]
    public static void MigrateLevelScene()
    {
        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(LevelScenePath);
        if (sceneAsset == null)
        {
            Debug.LogError($"Scene not found at path: {LevelScenePath}");
            return;
        }

        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        var scene = EditorSceneManager.OpenScene(LevelScenePath, OpenSceneMode.Single);

        DemoBootstrap bootstrap = Object.FindFirstObjectByType<DemoBootstrap>();
        if (bootstrap == null)
        {
            GameObject root = GameObject.Find("Level_01_Root");
            if (root == null)
            {
                root = new GameObject("Level_01_Root");
            }
            bootstrap = root.GetComponent<DemoBootstrap>();
            if (bootstrap == null)
            {
                bootstrap = root.AddComponent<DemoBootstrap>();
            }
        }

        // 清理上一轮生成的骨架，避免重复层级。
        TryDestroy("Left2D_Playfield");
        TryDestroy("Right3D_Matrix");
        TryDestroy("FixedSliceMapper");
        TryDestroy("MainCanvas");
        TryDestroy("Camera_Left2D");
        TryDestroy("Camera_Right3D");
        TryDestroy("Directional Light");
        TryDestroy("EventSystem");

        bootstrap.BuildScene();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Level_01 static scene migration completed.");
    }

    private static void TryDestroy(string objectName)
    {
        GameObject target = GameObject.Find(objectName);
        if (target != null)
        {
            Object.DestroyImmediate(target);
        }
    }
}
