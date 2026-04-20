using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LandscapeMatrix
{
    /// <summary>
    /// 3D 角色与矩阵方块重合时暂停游戏并弹出"已融合"弹窗；弹窗提供"重新开始"按钮以直接重载当前关卡。
    /// UI 由场景中的 FusionCanvas 搭建（文字 + Restart 按钮），本组件挂在该 Canvas 上并引用根物体/按钮。
    /// 复用 Camera_LevelClearUI（Screen Space Camera）做渲染，显示时临时隐藏 MainCanvas（Screen Space Overlay），
    /// 思路与 <see cref="LevelClearPresenter"/>、<see cref="LevelPauseMenu"/> 一致。
    /// </summary>
    public sealed class FusionPresenter : MonoBehaviour
    {
        [SerializeField] private GameObject _root;

        // 字段从旧的 _undoButton 迁移：场景里原先挂到 Undo 按钮的引用会自动继承到 _restartButton，
        // 只需在 Inspector/场景里把按钮文字改成"重新开始"。
        [FormerlySerializedAs("_undoButton")]
        [SerializeField] private Button _restartButton;
        [SerializeField] private GameObject _mainCanvas;
        [SerializeField] private Camera _levelClearUiCamera;

        private float _previousTimeScale = 1f;
        private bool _shown;

        /// <summary>供 <see cref="LevelPauseMenu"/> 判断：融合弹窗打开时需忽略 Esc 的暂停切换。</summary>
        public static bool IsAnyOpen { get; private set; }

        private void Awake()
        {
            if (_root == null)
            {
                _root = gameObject;
            }

            _root.SetActive(false);

            if (_restartButton != null)
            {
                _restartButton.onClick.RemoveAllListeners();
                _restartButton.onClick.AddListener(OnRestartClicked);
            }
        }

        private void OnDisable()
        {
            // 场景切换/组件被禁用时：若此前打开过弹窗，需把全局状态（IsAnyOpen、Time.timeScale、MainCanvas、UI 相机）
            // 恢复到常态，避免切关卡后残留灰屏或时间冻结。
            if (_shown)
            {
                _shown = false;
                IsAnyOpen = false;
                Time.timeScale = _previousTimeScale <= 0f ? 1f : _previousTimeScale;
                if (_mainCanvas != null)
                {
                    _mainCanvas.SetActive(true);
                }
                SetLevelClearUiCameraEnabled(false);
            }
        }

        /// <summary>由 <see cref="MatrixController"/> 检测到融合时调用（仅当前场景首个 FusionPresenter 生效）。</summary>
        public static void NotifyFusionDetected()
        {
            FusionPresenter presenter = FindPresenterInScene();
            if (presenter == null)
            {
                Debug.LogError("FusionPresenter: 场景中未找到融合弹窗（需存在挂有 FusionPresenter 的 FusionCanvas）。");
                return;
            }

            presenter.Show();
        }

        private static FusionPresenter FindPresenterInScene()
        {
            return Object.FindFirstObjectByType<FusionPresenter>(FindObjectsInactive.Include);
        }

        private void Show()
        {
            if (_shown)
            {
                return;
            }

            _shown = true;
            IsAnyOpen = true;
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            // Screen Space Overlay 的 MainCanvas 会盖在 UI 相机上，需临时隐藏；和 LevelClearPresenter 思路一致。
            if (_mainCanvas != null)
            {
                _mainCanvas.SetActive(false);
            }

            SetLevelClearUiCameraEnabled(true);

            if (_root != null)
            {
                _root.SetActive(true);
            }
        }

        private void OnRestartClicked()
        {
            if (!_shown)
            {
                return;
            }

            // 重新载入当前场景前必须先复位 Time.timeScale：Show() 里已经把它改为 0，
            // 否则新场景会以冻结状态启动；同步复位 IsAnyOpen、MainCanvas、UI 相机与 _shown，
            // 防止场景加载途中残留的全局状态影响下一次弹窗行为。
            Time.timeScale = _previousTimeScale <= 0f ? 1f : _previousTimeScale;
            IsAnyOpen = false;
            _shown = false;

            if (_root != null)
            {
                _root.SetActive(false);
            }

            SetLevelClearUiCameraEnabled(false);

            if (_mainCanvas != null)
            {
                _mainCanvas.SetActive(true);
            }

            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
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
