using UnityEngine;
using UnityEngine.SceneManagement;

namespace LandscapeMatrix
{
    /// <summary>
    /// 关卡选择控制器：仅负责按钮回调，UI 由 LevelSelect.unity 场景直接搭建。
    /// 关卡按钮在场景里通过 LoadLevel(string) 的字符串参数指定要加载的关卡场景名。
    /// </summary>
    public sealed class LevelSelectController : MonoBehaviour
    {
        [SerializeField] private string _startMenuSceneName = "StartMenu";

        private void Awake()
        {
            Time.timeScale = 1f;
        }

        public void LoadLevel(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("LevelSelectController.LoadLevel: scene name is empty.");
                return;
            }

            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        public void OnBackToStartMenuClicked()
        {
            SceneManager.LoadScene(_startMenuSceneName, LoadSceneMode.Single);
        }
    }
}
