using UnityEngine;
using UnityEngine.SceneManagement;

namespace LandscapeMatrix
{
    /// <summary>
    /// 关卡内暂停菜单控制器：按下 Esc 弹出确认退出对话框，点击"是"回关卡选择，点击"否"关闭弹窗。
    /// UI 层级由 Assets/Resources/PauseMenu.prefab 直接搭建，脚本不生成任何界面元素。
    /// </summary>
    public sealed class LevelPauseMenu : MonoBehaviour
    {
        [SerializeField] private GameObject _dialog;
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
            _dialog.SetActive(true);
            _isOpen = true;
        }

        private void Close()
        {
            if (_dialog != null)
            {
                _dialog.SetActive(false);
            }

            Time.timeScale = _previousTimeScale <= 0f ? 1f : _previousTimeScale;
            _isOpen = false;
        }

        public void OnYesClicked()
        {
            Time.timeScale = 1f;
            _isOpen = false;
            SceneManager.LoadScene(_levelSelectSceneName, LoadSceneMode.Single);
        }

        public void OnNoClicked()
        {
            Close();
        }
    }
}
