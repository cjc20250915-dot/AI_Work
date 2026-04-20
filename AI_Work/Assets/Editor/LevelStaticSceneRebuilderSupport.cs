using LandscapeMatrix;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Level_02 / Level_03 静态场景重建共用流程：
/// 按 <see cref="LevelDefinition"/> 把 <see cref="MatrixController"/>、<see cref="MatrixSliceMapper"/>、
/// <see cref="Playfield2D"/> 重新初始化到保存好的静态布局。避免两个关卡脚本各自重复 80+ 行相同代码。
/// </summary>
internal static class LevelStaticSceneRebuilderSupport
{
    /// <summary>
    /// 若当前打开的场景正是指定路径/名称，则在下一编辑器帧触发静态重建（非播放态）。
    /// </summary>
    public static void TryAutoRebuildIfSceneOpen(string scenePath, string sceneName, string logLabel)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        Scene activeScene = EditorSceneManager.GetActiveScene();
        if (activeScene.path != scenePath && activeScene.name != sceneName)
        {
            return;
        }

        RebuildSceneFromLevelDefinition(activeScene, logLabel);
    }

    /// <summary>确保指定路径的场景已打开后再执行静态重建。</summary>
    public static void OpenAndRebuild(string scenePath, string logLabel)
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.path != scenePath)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        RebuildSceneFromLevelDefinition(scene, logLabel);
    }

    private static void RebuildSceneFromLevelDefinition(Scene scene, string logLabel)
    {
        MatrixController matrix = Object.FindFirstObjectByType<MatrixController>(FindObjectsInactive.Include);
        MatrixSliceMapper mapper = Object.FindFirstObjectByType<MatrixSliceMapper>(FindObjectsInactive.Include);
        Playfield2D playfield = Object.FindFirstObjectByType<Playfield2D>(FindObjectsInactive.Include);
        LevelSceneBootstrap bootstrap = Object.FindFirstObjectByType<LevelSceneBootstrap>(FindObjectsInactive.Include);

        if (matrix == null || mapper == null || playfield == null || bootstrap == null)
        {
            Debug.LogWarning($"{logLabel} static rebuild skipped: required scene objects are missing.");
            return;
        }

        SerializedObject bootstrapObject = new SerializedObject(bootstrap);
        LevelDefinition definition = bootstrapObject.FindProperty("_levelDefinition").objectReferenceValue as LevelDefinition;
        if (definition == null)
        {
            Debug.LogWarning($"{logLabel} static rebuild skipped: LevelDefinition is missing.");
            return;
        }

        // hiddenVoxels 以场景中 MatrixController 当前配置为准，
        // 避免每次打开场景把用户手动调好的隐藏方格列表冲回 LevelDefinition。
        matrix.ApplyLevelData(
            definition.matrixSize,
            definition.sliceMapWidth,
            definition.sliceFloorRows,
            definition.initialGridOffset,
            definition.initialRotationStep,
            definition.defaultVoxelVisible,
            matrix.hiddenVoxels);
        mapper.ApplyLevelData(definition.preferredSpawnVoxel, definition.preferredGoalVoxel);
        matrix.RebuildStaticSceneVisualsForEditor();
        playfield.RebuildStaticBoardForEditor(mapper);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"{logLabel} static matrix rebuild completed.");
    }
}
