using UnityEngine;
using UnityEngine.SceneManagement;

namespace LandscapeMatrix
{
    /// <summary>
    /// 关卡内暂停菜单控制器：按下 Esc 弹出确认退出对话框，点击"是"回关卡选择，点击"否"关闭弹窗。
    /// 复用关卡已有的 Camera_LevelClearUI 做 Screen Space Camera 渲染，平时相机保持关闭，打开弹窗时启用；
    /// 同时隐藏 Screen Space Overlay 的 MainCanvas，避免其覆盖在弹窗之上。思路与 LevelClearPresenter 一致。
    /// </summary>
    public sealed class LevelPauseMenu : MonoBehaviour
    {
        [SerializeField] private GameObject _dialog;
        [SerializeField] private Camera _levelClearUiCamera;
        [SerializeField] private GameObject _mainCanvas;
        [SerializeField] private string _levelSelectSceneName = "LevelSelect";

        private float _previousTimeScale = 1f;
        private bool _isOpen;

        private void Awake()
        {
            if (_dialog != null)
            {
                _dialog.SetActive(false);
            }
            _isOpen = false;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // 融合弹窗打开时：Esc 不应切换暂停菜单，否则会叠加两个弹窗且 MainCanvas/UI 相机状态互相冲突。
                if (!_isOpen && FusionPresenter.IsAnyOpen)
                {
                    return;
                }

                if (_isOpen)
                {
                    Close();
                }
                else
                {
                    Open();
                }
            }
        }

        private void Open()
        {
            if (_dialog == null)
            {
                return;
            }

            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            if (_mainCanvas != null)
            {
                _mainCanvas.SetActive(false);
            }

            SetUiCameraEnabled(true);

            _dialog.SetActive(true);
            _isOpen = true;
        }

        private void Close()
        {
            if (_dialog != null)
            {
                _dialog.SetActive(false);
            }

            SetUiCameraEnabled(false);

            if (_mainCanvas != null)
            {
                _mainCanvas.SetActive(true);
            }

            Time.timeScale = _previousTimeScale <= 0f ? 1f : _previousTimeScale;
            _isOpen = false;
        }

        public void OnYesClicked()
        {
            Time.timeScale = 1f;
            _isOpen = false;

            SetUiCameraEnabled(false);

            SceneManager.LoadScene(_levelSelectSceneName, LoadSceneMode.Single);
        }

        public void OnNoClicked()
        {
            Close();
        }

        private void SetUiCameraEnabled(bool enabled)
        {
            if (_levelClearUiCamera != null)
            {
                _levelClearUiCamera.enabled = enabled;
            }
        }
    }
}
