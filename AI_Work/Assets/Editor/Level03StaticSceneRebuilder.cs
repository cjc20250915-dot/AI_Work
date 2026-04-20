using LandscapeMatrix;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Level03StaticSceneRebuilder
{
    private const string Level03ScenePath = "Assets/Scenes/Level_03.unity";
    private const string Level03SceneName = "Level_03";

    [InitializeOnLoadMethod]
    private static void RebuildWhenLevel03Open()
    {
        EditorApplication.delayCall += TryAutoRebuildOpenLevel03;
    }

    [MenuItem("LandscapeMatrix/Rebuild Level_03 Static 5x5x5", false, 3)]
    public static void RebuildLevel03FromMenu()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.path != Level03ScenePath)
        {
            scene = EditorSceneManager.OpenScene(Level03ScenePath, OpenSceneMode.Single);
        }

        RebuildScene(scene);
    }

    private static void TryAutoRebuildOpenLevel03()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        Scene activeScene = EditorSceneManager.GetActiveScene();
        if (activeScene.path != Level03ScenePath && activeScene.name != Level03SceneName)
        {
            return;
        }

        RebuildScene(activeScene);
    }

    private static void RebuildScene(Scene scene)
    {
        MatrixController matrix = Object.FindFirstObjectByType<MatrixController>(FindObjectsInactive.Include);
        MatrixSliceMapper mapper = Object.FindFirstObjectByType<MatrixSliceMapper>(FindObjectsInactive.Include);
        Playfield2D playfield = Object.FindFirstObjectByType<Playfield2D>(FindObjectsInactive.Include);
        LevelSceneBootstrap bootstrap = Object.FindFirstObjectByType<LevelSceneBootstrap>(FindObjectsInactive.Include);

        if (matrix == null || mapper == null || playfield == null || bootstrap == null)
        {
            Debug.LogWarning("Level_03 static rebuild skipped: required scene objects are missing.");
            return;
        }

        SerializedObject bootstrapObject = new SerializedObject(bootstrap);
        LevelDefinition definition = bootstrapObject.FindProperty("_levelDefinition").objectReferenceValue as LevelDefinition;
        if (definition == null)
        {
            Debug.LogWarning("Level_03 static rebuild skipped: LevelDefinition is missing.");
            return;
        }

        matrix.ApplyLevelData(
            definition.matrixSize,
            definition.sliceMapWidth,
            definition.sliceFloorRows,
            definition.initialGridOffset,
            definition.initialRotationStep,
            definition.defaultVoxelVisible,
            definition.hiddenVoxels);
        mapper.ApplyLevelData(definition.preferredSpawnVoxel, definition.preferredGoalVoxel);
        matrix.RebuildStaticSceneVisualsForEditor();
        playfield.RebuildStaticBoardForEditor(mapper);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Level_03 static 5x5x5 matrix rebuild completed.");
    }
}
