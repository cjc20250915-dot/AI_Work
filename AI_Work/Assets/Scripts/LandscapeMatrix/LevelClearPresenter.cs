using UnityEngine;

using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LandscapeMatrix
{
    /// <summary>
    /// 2D 达成目标后暂停游戏并显示通关层；提供返回关卡选择场景（空场景占位）的入口。
    /// 界面由场景中的 LevelClearCanvas 搭建，本组件挂在该 Canvas 上并引用根物体与返回按钮。
    /// </summary>
    public sealed class LevelClearPresenter : MonoBehaviour
    {
        public const string LevelSelectSceneName = "LevelSelect";

        [SerializeField] private GameObject _root;
        [SerializeField] private Button _backButton;
        [SerializeField] private GameObject _mainCanvas;
        [SerializeField] private Camera _levelClearUiCamera;

        private bool _shown;

        private void Awake()
        {
            if (_root == null)
            {
                _root = gameObject;
            }

            if (_backButton != null)
            {
                _backButton.onClick.RemoveAllListeners();
                _backButton.onClick.AddListener(OnBackToLevelSelectClicked);
            }
        }

        /// <summary>由 2D 玩家达成目标时调用（仅首次生效）。</summary>
        public static void Notify2DLevelCleared()
        {
            LevelClearPresenter presenter = FindPresenterInScene();
            if (presenter == null)
            {
                Debug.LogError("LevelClearPresenter: 场景中未找到通关界面（需存在挂有 LevelClearPresenter 的 LevelClearCanvas）。");
                return;
            }

            presenter.ShowOverlay();
        }

        private static LevelClearPresenter FindPresenterInScene()
        {
            return Object.FindFirstObjectByType<LevelClearPresenter>(FindObjectsInactive.Include);
        }

        private void ShowOverlay()
        {
            if (_shown)
            {
                return;
            }

            _shown = true;
            Time.timeScale = 0f;

            // Screen Space Overlay（如 MainCanvas）会在所有摄像机之后绘制；通关层用全屏摄像机渲染时，需临时隐藏主 UI，否则会压在最上层。
            if (_mainCanvas != null)
            {
                _mainCanvas.SetActive(false);
            }

            // 仅在通关时启用：平时保持关闭，否则 URP 下全屏 UI 相机会每帧参与合成导致整屏发黑。
            SetLevelClearUiCameraEnabled(true);

            if (_root != null)
            {
                _root.SetActive(true);
            }
        }

        private void OnBackToLevelSelectClicked()
        {
            Time.timeScale = 1f;
            _shown = false;

            SetLevelClearUiCameraEnabled(false);

            if (_root != null)
            {
                _root.SetActive(false);
            }

            SceneManager.LoadScene(LevelSelectSceneName, LoadSceneMode.Single);
        }

        private void SetLevelClearUiCameraEnabled(bool enabled)
        {
            if (_levelClearUiCamera != null)
            {
                _levelClearUiCamera.enabled = enabled;
            }
        }
    }
}

