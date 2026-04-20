using UnityEditor;

public static class Level02StaticSceneRebuilder
{
    private const string Level02ScenePath = "Assets/Scenes/Level_02.unity";
    private const string Level02SceneName = "Level_02";
    private const string LogLabel = "Level_02 (4x4x4)";

    [InitializeOnLoadMethod]
    private static void RebuildWhenLevel02Open()
    {
        EditorApplication.delayCall += TryAutoRebuildOpenLevel02;
    }

    [MenuItem("LandscapeMatrix/Rebuild Level_02 Static 4x4x4", false, 2)]
    public static void RebuildLevel02FromMenu()
    {
        LevelStaticSceneRebuilderSupport.OpenAndRebuild(Level02ScenePath, LogLabel);
    }

    private static void TryAutoRebuildOpenLevel02()
    {
        LevelStaticSceneRebuilderSupport.TryAutoRebuildIfSceneOpen(Level02ScenePath, Level02SceneName, LogLabel);
    }
}
