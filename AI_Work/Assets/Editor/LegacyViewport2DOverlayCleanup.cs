using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LandscapeMatrix.EditorTools
{
    /// <summary>
    /// 一次性清理工具：把旧版 <c>Viewport2DOverlayUI</c> 功能在场景里留下的 GameObject / 相机修改一键抹掉。
    /// <list type="bullet">
    /// <item>删除名为 <c>Viewport2DOverlay</c>, <c>Viewport2DOverlayCanvas</c>, <c>Camera_Viewport2DOverlayUI</c>
    ///       及其子节点（只按名字匹配，防误删别的 Canvas）。</item>
    /// <item>把 <c>Camera_Left2D</c>, <c>Camera_Right3D</c> 的 CullingMask 里被偷摸剔掉的 UI 层（Layer 5）加回去，
    ///       省得下次用新的 UI 部件时它也被忽略。</item>
    /// </list>
    /// 做完记得 Ctrl+S 保存场景。
    /// </summary>
    internal static class LegacyViewport2DOverlayCleanup
    {
        private const string CanvasName = "Viewport2DOverlayCanvas";
        private const string OverlayRootName = "Viewport2DOverlay";
        private const string OverlayCameraName = "Camera_Viewport2DOverlayUI";
        private const string Left2DName = "Camera_Left2D";
        private const string Right3DName = "Camera_Right3D";
        private const int UILayerBit = 1 << 5;

        [MenuItem("Tools/Landscape Matrix/Remove Legacy Viewport2D Overlay In Active Scene")]
        private static void RemoveInActiveScene()
        {
            int removed = 0;
            int cameraMaskFixed = 0;

            // 1) 删 Canvas 整棵子树（因为 UI 节点都在它下面）
            Canvas[] allCanvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Canvas c in allCanvases)
            {
                if (c == null || c.name != CanvasName)
                {
                    continue;
                }

                Debug.Log($"[LegacyOverlayCleanup] Delete {GetScenePath(c.transform)}");
                Undo.DestroyObjectImmediate(c.gameObject);
                removed++;
            }

            // 2) 删挂着旧脚本的空壳 GameObject（Viewport2DOverlay 宿主节点，MissingScript 也会被连带处理掉）
            GameObject[] allGameObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (GameObject go in allGameObjects)
            {
                if (go == null)
                {
                    continue;
                }

                if (go.name == OverlayRootName || go.name == OverlayCameraName)
                {
                    Debug.Log($"[LegacyOverlayCleanup] Delete {GetScenePath(go.transform)}");
                    Undo.DestroyObjectImmediate(go);
                    removed++;
                }
            }

            // 3) 恢复游戏相机的 CullingMask：把 UI 层再加回去。
            Camera[] allCameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Camera cam in allCameras)
            {
                if (cam == null)
                {
                    continue;
                }

                if (cam.name != Left2DName && cam.name != Right3DName)
                {
                    continue;
                }

                if ((cam.cullingMask & UILayerBit) == 0)
                {
                    Undo.RecordObject(cam, "Restore UI Layer To Game Camera");
                    cam.cullingMask |= UILayerBit;
                    cameraMaskFixed++;
                    Debug.Log($"[LegacyOverlayCleanup] Restored UI layer on {cam.name}");
                }
            }

            if (removed > 0 || cameraMaskFixed > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }

            Debug.Log(
                $"[LegacyOverlayCleanup] Done. Removed {removed} GameObject(s), restored UI layer on {cameraMaskFixed} camera(s). " +
                $"别忘了 Ctrl+S 保存场景。");
        }

        private static string GetScenePath(Transform t)
        {
            if (t == null)
            {
                return "<null>";
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(t.name);
            Transform cur = t.parent;
            while (cur != null)
            {
                sb.Insert(0, "/");
                sb.Insert(0, cur.name);
                cur = cur.parent;
            }
            return sb.ToString();
        }
    }
}
