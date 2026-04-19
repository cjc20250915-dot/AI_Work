using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LandscapeMatrix
{
    public class DemoBootstrap : MonoBehaviour
    {
        private const string SceneName = "Level_01";
        private static readonly bool EnableRuntimeBootstrap = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!EnableRuntimeBootstrap)
            {
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.name != SceneName)
            {
                return;
            }

            if (Object.FindFirstObjectByType<DemoBootstrap>() != null)
            {
                return;
            }

            GameObject root = new GameObject("Level_01_Root");
            root.AddComponent<DemoBootstrap>().BuildScene();
        }

        [ContextMenu("Build Level_01 Skeleton")]
        public void BuildScene()
        {
            EnsureEventSystem();
            RemoveDefaultMainCamera();
            CreateLeftCamera();
            CreateRightCamera();
            CreateGlobalLight();

            GameObject playfieldObject = new GameObject("Left2D_Playfield");
            Playfield2D playfield2D = playfieldObject.AddComponent<Playfield2D>();

            GameObject matrixObject = new GameObject("Right3D_Matrix");
            MatrixController matrixController = matrixObject.AddComponent<MatrixController>();
            matrixController.BuildMatrixVisual();

            GameObject mapperObject = new GameObject("FixedSliceMapper");
            MatrixSliceMapper mapper = mapperObject.AddComponent<MatrixSliceMapper>();
            mapper.Initialize(playfield2D, matrixController);
            matrixController.BindPlayer(playfield2D.GetPlayerController());
            CreateCanvas(matrixController);
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        private static void RemoveDefaultMainCamera()
        {
            Camera main = Camera.main;
            if (main != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(main.gameObject);
                }
                else
                {
                    DestroyImmediate(main.gameObject);
                }
            }
        }

        private static void CreateLeftCamera()
        {
            GameObject cameraObject = new GameObject("Camera_Left2D");
            Camera cameraComponent = cameraObject.AddComponent<Camera>();
            cameraComponent.orthographic = true;
            cameraComponent.orthographicSize = 4.2f;
            cameraComponent.rect = new Rect(0f, 0f, 0.5f, 1f);
            cameraComponent.clearFlags = CameraClearFlags.SolidColor;
            cameraComponent.backgroundColor = new Color(0.08f, 0.08f, 0.08f);
            // 侧视：相机沿 Z 轴观察 X/Y 平面，角色在方块“外侧”移动。
            // 与 SliceBoardWorldOrigin=0 时 3×4 格网中心约 (1, 1.5) 对齐（旧 7×7 时代为 (3,3)）。
            cameraObject.transform.position = new Vector3(1f, 2.2f, -12f);
            cameraObject.transform.LookAt(new Vector3(1f, 1.5f, 0f));
        }

        private static void CreateRightCamera()
        {
            GameObject cameraObject = new GameObject("Camera_Right3D");
            Camera cameraComponent = cameraObject.AddComponent<Camera>();
            cameraComponent.orthographic = false;
            cameraComponent.fieldOfView = 50f;
            cameraComponent.rect = new Rect(0.5f, 0.2f, 0.5f, 0.8f);
            cameraComponent.clearFlags = CameraClearFlags.SolidColor;
            cameraComponent.backgroundColor = new Color(0.12f, 0.14f, 0.2f);
            cameraObject.transform.position = new Vector3(10f, 8f, -8f);
            cameraObject.transform.LookAt(new Vector3(10f, 1f, 0f));
        }

        private static void CreateGlobalLight()
        {
            GameObject lightObject = new GameObject("Directional Light");
            Light lightComponent = lightObject.AddComponent<Light>();
            lightComponent.type = LightType.Directional;
            lightComponent.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        }

        private static void CreateCanvas(MatrixController matrixController)
        {
            GameObject canvasObject = new GameObject("MainCanvas");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject panel = CreatePanel(canvasObject.transform);

            CreateButton(panel.transform, "Forward", new Vector2(-170f, 0f), matrixController.MoveForward);
            CreateButton(panel.transform, "Backward", new Vector2(-60f, 0f), matrixController.MoveBackward);
            CreateButton(panel.transform, "CW", new Vector2(50f, 0f), matrixController.RotateClockwise);
            CreateButton(panel.transform, "CCW", new Vector2(160f, 0f), matrixController.RotateCounterClockwise);
            CreateButton(panel.transform, "Restart", new Vector2(270f, 0f), matrixController.RestartLevel);
        }

        private static GameObject CreatePanel(Transform parent)
        {
            GameObject panelObject = new GameObject("ControlPanel");
            panelObject.transform.SetParent(parent, false);
            Image panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.4f);

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-20f, 20f);
            rect.sizeDelta = new Vector2(700f, 120f);

            return panelObject;
        }

        private static void CreateButton(Transform parent, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction callback)
        {
            GameObject buttonObject = new GameObject($"Btn_{label}");
            buttonObject.transform.SetParent(parent, false);

            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(callback);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(90f, 46f);

            GameObject textObject = new GameObject("Label");
            textObject.transform.SetParent(buttonObject.transform, false);
            Text text = textObject.AddComponent<Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.resizeTextForBestFit = true;
            text.raycastTarget = false;

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }
    }
}
