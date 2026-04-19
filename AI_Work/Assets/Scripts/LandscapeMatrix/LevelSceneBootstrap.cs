using UnityEngine;

namespace LandscapeMatrix
{
    [DefaultExecutionOrder(-1000)]
    public sealed class LevelSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private LevelDefinition _levelDefinition;
        [SerializeField] private MatrixController _matrixController;
        [SerializeField] private MatrixSliceMapper _sliceMapper;
        [SerializeField] private bool _overrideCameraLayout;
        [SerializeField] private Camera _leftCamera;
        [SerializeField] private Camera _rightCamera;

        private void Awake()
        {
            ApplyLevelDefinition();
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                ApplyLevelDefinition();
            }
        }

        private void ApplyLevelDefinition()
        {
            if (_levelDefinition == null)
            {
                return;
            }

            if (_matrixController == null)
            {
                _matrixController = Object.FindFirstObjectByType<MatrixController>(FindObjectsInactive.Include);
            }

            if (_sliceMapper == null)
            {
                _sliceMapper = Object.FindFirstObjectByType<MatrixSliceMapper>(FindObjectsInactive.Include);
            }

            if (_matrixController != null)
            {
                _matrixController.ApplyLevelData(
                    _levelDefinition.matrixSize,
                    _levelDefinition.sliceMapWidth,
                    _levelDefinition.sliceFloorRows,
                    _levelDefinition.initialGridOffset,
                    _levelDefinition.initialRotationStep,
                    _levelDefinition.defaultVoxelVisible,
                    _levelDefinition.hiddenVoxels);
            }

            if (_sliceMapper != null)
            {
                _sliceMapper.ApplyLevelData(
                    _levelDefinition.preferredSpawnVoxel,
                    _levelDefinition.preferredGoalVoxel);
            }

            if (_overrideCameraLayout)
            {
                ApplyCameraLayout();
            }
        }

        private void ApplyCameraLayout()
        {
            if (_levelDefinition == null)
            {
                return;
            }

            int sliceWidth = _levelDefinition.sliceMapWidth > 0 ? _levelDefinition.sliceMapWidth : _levelDefinition.matrixSize;
            int sliceFloorRows = _levelDefinition.sliceFloorRows > 0 ? _levelDefinition.sliceFloorRows : _levelDefinition.matrixSize;
            float boardCenterX = MatrixController.SliceBoardWorldOriginX + (sliceWidth - 1) * 0.5f;
            float boardCenterY = MatrixController.SliceBoardWorldOriginY + sliceFloorRows * 0.5f;
            float matrixCenterY = (_levelDefinition.matrixSize - 1) * 0.5f;

            if (_leftCamera != null)
            {
                _leftCamera.orthographic = true;
                _leftCamera.orthographicSize = Mathf.Max(4.2f, Mathf.Max(sliceWidth, sliceFloorRows + 1) * 0.72f);
                _leftCamera.transform.position = new Vector3(boardCenterX, boardCenterY + 0.2f, -12f);
                _leftCamera.transform.LookAt(new Vector3(boardCenterX, boardCenterY - 0.3f, 0f));
            }

            if (_rightCamera != null)
            {
                float distance = Mathf.Max(8f, _levelDefinition.matrixSize * 2.4f);
                _rightCamera.transform.position = new Vector3(10f + distance, matrixCenterY + distance * 0.75f, -distance);
                _rightCamera.transform.LookAt(new Vector3(10f, matrixCenterY, 0f));
            }
        }
    }
}
