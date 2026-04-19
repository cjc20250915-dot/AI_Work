using LandscapeMatrix;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 用 DemoBootstrap 在编辑器中重建关卡对象（破坏性），并套用 <see cref="Level01SceneOrganizer"/> 层级。
/// </summary>
public static class LandscapeMatrixSceneMigrationTool
{
    [MenuItem("LandscapeMatrix/Rebuild Level_01 From Bootstrap (destructive)", false, 0)]
    public static void MigrateLevelScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(Level01SceneOrganizer.LevelScenePath);
        if (sceneAsset == null)
        {
            Debug.LogError($"Scene not found at path: {Level01SceneOrganizer.LevelScenePath}");
            return;
        }

        var scene = EditorSceneManager.OpenScene(Level01SceneOrganizer.LevelScenePath, OpenSceneMode.Single);

        GameObject tempBootstrapHost = null;
        DemoBootstrap bootstrap = Object.FindFirstObjectByType<DemoBootstrap>();
        if (bootstrap == null)
        {
            tempBootstrapHost = new GameObject("Temp_DemoBootstrap");
            Undo.RegisterCreatedObjectUndo(tempBootstrapHost, "Temp DemoBootstrap");
            bootstrap = tempBootstrapHost.AddComponent<DemoBootstrap>();
        }

        TryDestroy("Left2D_Playfield");
        TryDestroy("Right3D_Matrix");
        TryDestroy("FixedSliceMapper");
        TryDestroy("MainCanvas");
        TryDestroy("Camera_Left2D");
        TryDestroy("Camera_Right3D");
        TryDestroy("Directional Light");
        TryDestroy("EventSystem");

        bootstrap.BuildScene();

        if (tempBootstrapHost != null)
        {
            Undo.DestroyObjectImmediate(tempBootstrapHost);
        }

        GameObject leftoverRoot = GameObject.Find("Level_01_Root");
        if (leftoverRoot != null && leftoverRoot.transform.childCount == 0)
        {
            Undo.DestroyObjectImmediate(leftoverRoot);
        }

        Level01SceneOrganizer.OrganizeSceneContents();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Level_01 已从 Bootstrap 重建并完成层级整理。");
    }

    [MenuItem("LandscapeMatrix/Migrate Level_01 To Static Scene", false, 1)]
    public static void MigrateLevelSceneLegacyMenu()
    {
        MigrateLevelScene();
    }

    private static void TryDestroy(string objectName)
    {
        GameObject target = GameObject.Find(objectName);
        if (target != null)
        {
            Undo.DestroyObjectImmediate(target);
        }
    }
}

/// <summary>
/// 将 Level_01 场景内对象归入清晰层级（非破坏性），与当前 3×4 切面 + 右侧 3×3×3 矩阵玩法一致。
/// </summary>
public static class Level01SceneOrganizer
{
    public const string LevelScenePath = "Assets/Scenes/Level_01.unity";

    private const string RootName = "Landscape_Level01";

    [MenuItem("LandscapeMatrix/Organize Level_01 Hierarchy", false, 10)]
    public static void OrganizeFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(LevelScenePath, OpenSceneMode.Single);
        OrganizeSceneContents();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[{RootName}] 层级整理完成并已保存。");
    }

    public static void OrganizeSceneContents()
    {
        Transform root = GetOrCreateOrganizerRoot();
        Transform environment = GetOrCreateChild(root, "Environment");
        Transform cameras = GetOrCreateChild(root, "Cameras");
        Transform gameplay = GetOrCreateChild(root, "Gameplay");
        Transform ui = GetOrCreateChild(root, "UI");

        ReparentIfExists("Directional Light", environment);
        ReparentIfExists("Global Volume", environment);
        ReparentIfExists("Lit", environment);

        ReparentIfExists("Camera_Left2D", cameras);
        ReparentIfExists("Camera_Right3D", cameras);

        ReparentIfExists("Left2D_Playfield", gameplay);
        ReparentIfExists("Right3D_Matrix", gameplay);
        ReparentIfExists("FixedSliceMapper", gameplay);

        ReparentIfExists("EventSystem", ui);
        ReparentIfExists("MainCanvas", ui);

        RemoveEmptyLegacyBootstrapRoot();

        Undo.RecordObject(root, "Organize Level_01");
        root.SetAsFirstSibling();
    }

    private static Transform GetOrCreateOrganizerRoot()
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            return existing.transform;
        }

        GameObject go = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(go, "Create " + RootName);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        return go.transform;
    }

    private static Transform GetOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
        {
            return child;
        }

        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static void ReparentIfExists(string objectName, Transform newParent)
    {
        GameObject go = GameObject.Find(objectName);
        if (go == null)
        {
            return;
        }

        if (go.transform.parent == newParent)
        {
            return;
        }

        if (go.name == RootName)
        {
            return;
        }

        Undo.SetTransformParent(go.transform, newParent, "Organize Level_01");
    }

    private static void RemoveEmptyLegacyBootstrapRoot()
    {
        GameObject legacy = GameObject.Find("Level_01_Root");
        if (legacy == null)
        {
            return;
        }

        if (legacy.transform.childCount > 0)
        {
            return;
        }

        Undo.DestroyObjectImmediate(legacy);
    }
}
