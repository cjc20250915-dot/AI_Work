using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LandscapeMatrix
{
    /// <summary>
    /// 右侧 3D 角色：水平移动使用世界坐标轴（+X/+Z），不随体素矩阵绕 Y 旋转；重力、跳跃；仅落地可操作矩阵 UI。
    /// 变换挂在矩阵控制器下（非旋转中的 visualRoot），矩阵前进/旋转时不再被吸附，仅由物理与输入移动。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class Player3DController : MonoBehaviour
    {
        private const float OverlapPushStep = 0.03f;
        private const int OverlapPushMaxIterations = 48;
        private const float OverlapEpsilon = 0.02f;

        public MatrixController matrix;

        [Header("Movement")]
        public float moveSpeed = 4.5f;
        public float jumpVelocity = 7.5f;
        public float gravity = -24f;

        private CharacterController _controller;
        private Player2DController _player2D;
        private Playfield2D _playfield;
        private float _verticalVelocity;
        private bool _lastMatrixUiInteractable = true;

        private static readonly Collider[] OverlapScratch = new Collider[32];

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        private void Start()
        {
            if (matrix == null)
            {
                matrix = UnityEngine.Object.FindFirstObjectByType<MatrixController>();
            }

            if (matrix != null)
            {
                matrix.CacheMatrixUiButtons();
            }

            _player2D = UnityEngine.Object.FindFirstObjectByType<Player2DController>();
            _playfield = UnityEngine.Object.FindFirstObjectByType<Playfield2D>();

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            ApplyDimensionsFromMatrix();
            _lastMatrixUiInteractable = CanManipulateMatrix();
        }

        public void ApplyDimensionsFromMatrix()
        {
            if (_controller == null)
            {
                _controller = GetComponent<CharacterController>();
            }

            _controller.height = MatrixController.Player3DHeight;
            _controller.radius = MatrixController.Player3DHeight * 0.24f;
            float half = MatrixController.Player3DHeight * 0.5f;
            _controller.center = new Vector3(0f, half, 0f);
        }

        public void SnapToStandCell(Vector2Int standCell)
        {
            if (matrix == null || matrix.visualRoot == null)
            {
                return;
            }

            // GetLocalPositionForStandCell 为 visualRoot 局部；角色挂在矩阵根节点上，需换算为世界坐标。
            Vector3 local = matrix.GetLocalPositionForStandCell(standCell);
            local.y -= _controller.skinWidth;
            transform.position = matrix.visualRoot.TransformPoint(local);
            _verticalVelocity = 0f;
        }

        /// <summary>可点击矩阵 UI、前进后退旋转、重开关卡（仅贴地且矩阵未在应用状态）。</summary>
        public bool CanManipulateMatrix()
        {
            if (matrix == null)
            {
                return true;
            }

            if (matrix.IsMatrixStateChanging)
            {
                return false;
            }

            return _controller.isGrounded;
        }

        private void Update()
        {
            if (matrix == null || matrix.visualRoot == null)
            {
                return;
            }

            if (_player2D != null && !_player2D.ExternalDrive)
            {
                return;
            }

            bool grounded = _controller.isGrounded;
            bool matrixBusy = matrix.IsMatrixStateChanging;
            bool canMoveHorizontally = !matrixBusy;
            float horizontal = 0f;
            if (canMoveHorizontally)
            {
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
                {
                    horizontal -= 1f;
                }

                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
                {
                    horizontal += 1f;
                }
            }

            // 仅 A/D（或左右方向键）沿世界 X 平移；切面逻辑仍由 LateUpdate 映射到 2D。
            Vector3 planarInput = Vector3.right * horizontal;
            planarInput.y = 0f;
            if (planarInput.sqrMagnitude > 1f)
            {
                planarInput.Normalize();
            }

            Vector3 move = planarInput * moveSpeed;
            if (grounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }

            if (!matrixBusy && grounded && ShouldApplyJump())
            {
                _verticalVelocity = jumpVelocity;
            }

            _verticalVelocity += gravity * Time.deltaTime;
            move.y = _verticalVelocity;
            _controller.Move(move * Time.deltaTime);
            ResolveOverlapPushOutOfVoxels();

            bool uiOk = CanManipulateMatrix();
            if (uiOk != _lastMatrixUiInteractable)
            {
                _lastMatrixUiInteractable = uiOk;
                matrix.RefreshMatrixButtonsInteractable();
            }
        }

        private static bool ShouldApplyJump()
        {
            if (!Input.GetKeyDown(KeyCode.Space))
            {
                return false;
            }

            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            {
                return false;
            }

            return true;
        }

        private void ResolveOverlapPushOutOfVoxels()
        {
            int iter = 0;
            while (iter < OverlapPushMaxIterations && IsOverlappingMatrixVoxelGeometry())
            {
                float push = OverlapPushStep + OverlapEpsilon * (iter / 8);
                transform.position += Vector3.up * push;
                if (_verticalVelocity < 0f)
                {
                    _verticalVelocity = 0f;
                }

                iter++;
            }
        }

        private bool IsOverlappingMatrixVoxelGeometry()
        {
            Bounds b = _controller.bounds;
            Vector3 halfExtents = b.extents * 0.92f;
            int count = Physics.OverlapBoxNonAlloc(
                b.center,
                halfExtents,
                OverlapScratch,
                transform.rotation,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider h = OverlapScratch[i];
                if (h == null || h.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (!IsMatrixVoxelCollider(h))
                {
                    continue;
                }

                if (h.bounds.Intersects(b))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMatrixVoxelCollider(Collider c)
        {
            if (c == null)
            {
                return false;
            }

            if (c.name.StartsWith("Voxel_", StringComparison.Ordinal))
            {
                return true;
            }

            Transform p = c.transform.parent;
            return p != null && p.name == "MatrixVisual";
        }

        private void LateUpdate()
        {
            if (matrix == null || matrix.visualRoot == null || _player2D == null)
            {
                return;
            }

            if (!_player2D.ExternalDrive)
            {
                return;
            }

            Vector2Int stand = matrix.WorldToSliceStandCell(GetFeetWorldPosition());
            if (_playfield != null)
            {
                _playfield.SetLockedColumn(stand.x);
            }

            _player2D.SetCell(stand);
            _player2D.CheckGoal();
        }

        private Vector3 GetFeetWorldPosition()
        {
            Vector3 bottomLocal = _controller.center - Vector3.up * (_controller.height * 0.5f - _controller.skinWidth);
            return transform.TransformPoint(bottomLocal);
        }
    }
}
