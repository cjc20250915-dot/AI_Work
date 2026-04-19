using UnityEngine;

namespace LandscapeMatrix
{
    /// <summary>空关卡选择场景占位：确保从暂停通关界面返回后时间缩放恢复正常，便于后续在此搭建 UI。</summary>
    public sealed class LevelSelectSceneBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            Time.timeScale = 1f;
        }
    }
}
