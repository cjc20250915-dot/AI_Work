using LandscapeMatrix;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 在当前打开的关卡场景中一键生成融合弹窗 UI：
/// 创建 FusionCanvas → FusionPanel → [Title 文字 "已融合", Btn_Undo 带子 Label "撤销回上一步"]；
/// 挂上 <see cref="FusionPresenter"/> 并自动引用场景里的 MainCanvas 与 Camera_LevelClearUI。
/// 菜单路径：LandscapeMatrix/Create or Refresh Fusion Canvas (current scene)。
/// </summary>
public static class FusionCanvasBuilder
{
    private const string FusionCanvasObjectName = "FusionCanvas";
    private const string FusionPanelObjectName = "FusionPanel";
    private const string TitleObjectName = "Title";
    private const string UndoButtonObjectName = "Btn_Undo";
    private const string ButtonLabelObjectName = "Label";

    private const string MainCanvasSceneName = "MainCanvas";
    private const string LevelClearUiCameraSceneName = "Camera_LevelClearUI";

    [MenuItem("LandscapeMatrix/Create or Refresh Fusion Canvas (current scene)", false, 40)]
    public static void CreateOrRefreshFusionCanvasInActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Fusion Canvas", "当前没有打开的场景。请先打开一个关卡场景（Level_01/02/03）。", "OK");
            return;
        }

        GameObject mainCanvas = FindRootGameObjectByName(scene, MainCanvasSceneName);
        if (mainCanvas == null)
        {
            Debug.LogWarning($"FusionCanvasBuilder: 场景 '{scene.name}' 中未找到名为 '{MainCanvasSceneName}' 的根物体，FusionPresenter 的 _mainCanvas 将为空，稍后请在 Inspector 手动指定。");
        }

        Camera levelClearUiCamera = FindCameraInScene(scene, LevelClearUiCameraSceneName);
        if (levelClearUiCamera == null)
        {
            Debug.LogWarning($"FusionCanvasBuilder: 场景 '{scene.name}' 中未找到名为 '{LevelClearUiCameraSceneName}' 的相机，FusionPresenter 的 _levelClearUiCamera 将为空，稍后请在 Inspector 手动指定。");
        }

        GameObject existing = FindRootGameObjectByName(scene, FusionCanvasObjectName);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }

        GameObject canvasGo = BuildCanvasHierarchy(levelClearUiCamera, out FusionPresenter presenter, out Button undoButton, out GameObject panel);
        SceneManager.MoveGameObjectToScene(canvasGo, scene);
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create FusionCanvas");

        WirePresenterReferences(presenter, canvasGo, undoButton, mainCanvas, levelClearUiCamera);

        canvasGo.SetActive(true);
        panel.SetActive(true);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = canvasGo;
        EditorGUIUtility.PingObject(canvasGo);

        Debug.Log($"[FusionCanvasBuilder] Fusion Canvas 已在场景 '{scene.name}' 创建完成。MainCanvas={(mainCanvas != null)}, Camera_LevelClearUI={(levelClearUiCamera != null)}。");
    }

    private static GameObject BuildCanvasHierarchy(Camera renderCamera, out FusionPresenter presenter, out Button undoButton, out GameObject panelGo)
    {
        GameObject root = new GameObject(FusionCanvasObjectName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(FusionPresenter));
        root.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = renderCamera;
        canvas.planeDistance = 100f;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32767;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.zero;
        rootRect.pivot = Vector2.zero;
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.sizeDelta = Vector2.zero;

        presenter = root.GetComponent<FusionPresenter>();

        panelGo = new GameObject(FusionPanelObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelGo.layer = root.layer;
        panelGo.transform.SetParent(root.transform, false);
        RectTransform panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = Vector2.zero;
        Image overlay = panelGo.GetComponent<Image>();
        overlay.color = new Color(0f, 0f, 0f, 0.72f);
        overlay.raycastTarget = true;

        GameObject titleGo = new GameObject(TitleObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        titleGo.layer = root.layer;
        titleGo.transform.SetParent(panelGo.transform, false);
        RectTransform titleRect = titleGo.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.58f);
        titleRect.anchorMax = new Vector2(0.5f, 0.58f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = Vector2.zero;
        titleRect.sizeDelta = new Vector2(820f, 160f);
        Text titleText = titleGo.GetComponent<Text>();
        titleText.text = "已融合";
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.raycastTarget = false;
        titleText.color = new Color(1f, 0.96f, 0.62f, 1f);
        titleText.resizeTextForBestFit = true;
        titleText.resizeTextMinSize = 24;
        titleText.resizeTextMaxSize = 96;
        titleText.fontSize = 72;
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject buttonGo = new GameObject(UndoButtonObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonGo.layer = root.layer;
        buttonGo.transform.SetParent(panelGo.transform, false);
        RectTransform buttonRect = buttonGo.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.38f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.38f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = Vector2.zero;
        buttonRect.sizeDelta = new Vector2(360f, 64f);
        Image buttonBg = buttonGo.GetComponent<Image>();
        buttonBg.color = new Color(0.22f, 0.55f, 0.95f, 0.95f);
        Button button = buttonGo.GetComponent<Button>();
        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
        button.targetGraphic = buttonBg;
        undoButton = button;

        GameObject labelGo = new GameObject(ButtonLabelObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelGo.layer = root.layer;
        labelGo.transform.SetParent(buttonGo.transform, false);
        RectTransform labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.sizeDelta = Vector2.zero;
        Text labelText = labelGo.GetComponent<Text>();
        labelText.text = "撤销回上一步";
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.raycastTarget = false;
        labelText.color = Color.white;
        labelText.resizeTextForBestFit = true;
        labelText.resizeTextMinSize = 14;
        labelText.resizeTextMaxSize = 40;
        labelText.fontSize = 28;
        labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        return root;
    }

    private static void WirePresenterReferences(FusionPresenter presenter, GameObject canvasRoot, Button undoButton, GameObject mainCanvas, Camera levelClearUiCamera)
    {
        SerializedObject so = new SerializedObject(presenter);
        AssignIfExists(so, "_root", canvasRoot);
        AssignIfExists(so, "_undoButton", undoButton);
        AssignIfExists(so, "_mainCanvas", mainCanvas);
        AssignIfExists(so, "_levelClearUiCamera", levelClearUiCamera);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AssignIfExists(SerializedObject so, string propertyName, Object value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogWarning($"FusionCanvasBuilder: 在 FusionPresenter 上未找到字段 '{propertyName}'。");
            return;
        }

        prop.objectReferenceValue = value;
    }

    private static GameObject FindRootGameObjectByName(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root != null && root.name == name)
            {
                return root;
            }
        }

        return null;
    }

    private static Camera FindCameraInScene(Scene scene, string cameraName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == null)
            {
                continue;
            }

            foreach (Camera c in root.GetComponentsInChildren<Camera>(true))
            {
                if (c != null && c.gameObject.name == cameraName)
                {
                    return c;
                }
            }
        }

        return null;
    }
}
