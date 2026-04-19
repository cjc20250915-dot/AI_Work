using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LandscapeMatrix
{
    /// <summary>简单运行时关卡选择页：确保从通关界面返回后可直接切换三关测试。</summary>
    public sealed class LevelSelectSceneBootstrap : MonoBehaviour
    {
        private static readonly string[] SceneNames = { "Level_01", "Level_02", "Level_03" };

        private void Awake()
        {
            Time.timeScale = 1f;
            BuildSimpleMenu();
        }

        private void BuildSimpleMenu()
        {
            if (Object.FindFirstObjectByType<Canvas>() != null)
            {
                return;
            }

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            GameObject canvasObject = new GameObject("LevelSelectCanvas");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject titleObject = CreateText("Title", canvasObject.transform, new Vector2(0f, 180f), new Vector2(700f, 80f), 40, "选择教学关卡");
            titleObject.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;

            for (int i = 0; i < SceneNames.Length; i++)
            {
                string sceneName = SceneNames[i];
                GameObject button = CreateButton(canvasObject.transform, sceneName, new Vector2(0f, 50f - i * 110f));
                button.GetComponent<Button>().onClick.AddListener(() => SceneManager.LoadScene(sceneName));
            }
        }

        private static GameObject CreateButton(Transform parent, string label, Vector2 position)
        {
            GameObject buttonObject = new GameObject($"Btn_{label}");
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.18f, 0.2f, 0.26f, 0.96f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(320f, 72f);

            GameObject textObject = CreateText("Label", buttonObject.transform, Vector2.zero, rect.sizeDelta, 30, label);
            textObject.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
            return buttonObject;
        }

        private static GameObject CreateText(string name, Transform parent, Vector2 position, Vector2 size, int fontSize, string textValue)
        {
            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            Text text = textObject.AddComponent<Text>();
            text.text = textValue;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.raycastTarget = false;

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return textObject;
        }
    }
}
