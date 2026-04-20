using UnityEditor;

public static class Level03StaticSceneRebuilder
{
    private const string Level03ScenePath = "Assets/Scenes/Level_03.unity";
    private const string Level03SceneName = "Level_03";
    private const string LogLabel = "Level_03 (5x5x5)";

    [InitializeOnLoadMethod]
    private static void RebuildWhenLevel03Open()
    {
        EditorApplication.delayCall += TryAutoRebuildOpenLevel03;
    }

    [MenuItem("LandscapeMatrix/Rebuild Level_03 Static 5x5x5", false, 3)]
    public static void RebuildLevel03FromMenu()
    {
        LevelStaticSceneRebuilderSupport.OpenAndRebuild(Level03ScenePath, LogLabel);
    }

    private static void TryAutoRebuildOpenLevel03()
    {
        LevelStaticSceneRebuilderSupport.TryAutoRebuildIfSceneOpen(Level03ScenePath, Level03SceneName, LogLabel);
    }
}
