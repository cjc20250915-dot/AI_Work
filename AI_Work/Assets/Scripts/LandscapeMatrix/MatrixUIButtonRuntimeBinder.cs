using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LandscapeMatrix
{
    public static class MatrixUIButtonRuntimeBinder
    {
        private const string SceneName = "Level_01";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BindButtonsAfterSceneLoad()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.name != SceneName)
            {
                return;
            }

            MatrixController matrixController = Object.FindFirstObjectByType<MatrixController>();
            if (matrixController == null)
            {
                Debug.LogWarning("MatrixUIButtonRuntimeBinder: MatrixController not found.");
                return;
            }

            Bind("Btn_Forward", matrixController.MoveForward);
            Bind("Btn_Backward", matrixController.MoveBackward);
            Bind("Btn_CW", matrixController.RotateClockwise);
            Bind("Btn_CCW", matrixController.RotateCounterClockwise);
            Bind("Btn_Restart", matrixController.RestartLevel);

            matrixController.CacheMatrixUiButtons();
            matrixController.RefreshMatrixButtonsInteractable();

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private static void Bind(string buttonName, UnityEngine.Events.UnityAction callback)
        {
            GameObject buttonObject = GameObject.Find(buttonName);
            if (buttonObject == null)
            {
                return;
            }

            Button button = buttonObject.GetComponent<Button>();
            if (button == null)
            {
                return;
            }

            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(callback);
        }
    }
}
