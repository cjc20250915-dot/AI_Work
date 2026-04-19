using LandscapeMatrix;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Level02StaticSceneRebuilder
{
    private const string Level02ScenePath = "Assets/Scenes/Level_02.unity";
    private const string Level02SceneName = "Level_02";

    [InitializeOnLoadMethod]
    private static void RebuildWhenLevel02Open()
    {
        EditorApplication.delayCall += TryAutoRebuildOpenLevel02;
    }

    [MenuItem("LandscapeMatrix/Rebuild Level_02 Static 4x4x4", false, 2)]
    public static void RebuildLevel02FromMenu()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.path != Level02ScenePath)
        {
            scene = EditorSceneManager.OpenScene(Level02ScenePath, OpenSceneMode.Single);
        }

        RebuildScene(scene);
    }

    private static void TryAutoRebuildOpenLevel02()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        Scene activeScene = EditorSceneManager.GetActiveScene();
        if (activeScene.path != Level02ScenePath && activeScene.name != Level02SceneName)
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
            Debug.LogWarning("Level_02 static rebuild skipped: required scene objects are missing.");
            return;
        }

        SerializedObject bootstrapObject = new SerializedObject(bootstrap);
        LevelDefinition definition = bootstrapObject.FindProperty("_levelDefinition").objectReferenceValue as LevelDefinition;
        if (definition == null)
        {
            Debug.LogWarning("Level_02 static rebuild skipped: LevelDefinition is missing.");
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
        Debug.Log("Level_02 static 4x4x4 matrix rebuild completed.");
    }
}
