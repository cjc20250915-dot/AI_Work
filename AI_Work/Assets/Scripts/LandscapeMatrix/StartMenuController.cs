using UnityEngine;
using UnityEngine.SceneManagement;

namespace LandscapeMatrix
{
    /// <summary>
    /// 开始菜单控制器：仅提供按钮要绑定的回调，UI 层级由 StartMenu.unity 场景直接搭建。
    /// 帮助弹窗的显示/隐藏通过按钮 OnClick 直接调用 GameObject.SetActive(bool)，不需要在此处处理。
    /// </summary>
    public sealed class StartMenuController : MonoBehaviour
    {
        [SerializeField] private string _levelSelectSceneName = "LevelSelect";

        private void Awake()
        {
            Time.timeScale = 1f;
        }

        public void OnStartClicked()
        {
            SceneManager.LoadScene(_levelSelectSceneName, LoadSceneMode.Single);
        }

        public void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
