using UnityEngine;

namespace LandscapeMatrix
{
    /// <summary>
    /// 暂停菜单装载器：关卡场景加载时把 Resources/PauseMenu 预制体实例化一次。
    /// 仅负责挂载，UI 本身由 PauseMenu.prefab 直接搭建，不做程序化生成。
    /// </summary>
    public sealed class LevelPauseMenuInstaller : MonoBehaviour
    {
        [SerializeField] private string _pauseMenuResourcePath = "PauseMenu";

        private void Awake()
        {
            GameObject prefab = Resources.Load<GameObject>(_pauseMenuResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"LevelPauseMenuInstaller: cannot find prefab Resources/{_pauseMenuResourcePath}.prefab");
                return;
            }

            Instantiate(prefab);
        }
    }
}
