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
        private Vector2Int _lastLoggedStandCell = new Vector2Int(int.MinValue, int.MinValue);
        private Vector3Int _lastLoggedStorageCoord = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        private bool _lastLoggedGoalDetected2D;
        private bool _lastLoggedGoalDetected3D;

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

        /// <summary>供 Undo 使用的玩家状态快照。</summary>
        public struct UndoSnapshot
        {
            public Vector3 worldPosition;
            public float verticalVelocity;
        }

        public UndoSnapshot CaptureUndoSnapshot()
        {
            return new UndoSnapshot
            {
                worldPosition = transform.position,
                verticalVelocity = _verticalVelocity
            };
        }

        /// <summary>
        /// 由 <see cref="MatrixController"/> 在 Undo 时调用：直接覆盖世界位置与垂直速度；
        /// 用 <see cref="CharacterController.enabled"/> 短暂关闭以绕过其对 transform 赋值的限制。
        /// </summary>
        public void ApplyUndoSnapshot(UndoSnapshot snapshot)
        {
            if (_controller == null)
            {
                _controller = GetComponent<CharacterController>();
            }

            bool wasEnabled = _controller.enabled;
            _controller.enabled = false;
            transform.position = snapshot.worldPosition;
            _controller.enabled = wasEnabled;
            _verticalVelocity = snapshot.verticalVelocity;
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

        /// <summary>
        /// 供 <see cref="MatrixController"/> 在矩阵状态变化后调用：判断角色胶囊是否与任何矩阵体素几何重合。
        /// 原来的"重合即向上推"逻辑已移除，改由 MatrixController 触发融合弹窗 + 撤销流程。
        /// </summary>
        public bool IsOverlappingMatrixVoxels()
        {
            return IsOverlappingMatrixVoxelGeometry();
        }

        private bool IsOverlappingMatrixVoxelGeometry()
        {
            // Unity 默认 Physics.autoSyncTransforms=false：NotifyStateChanged 里刚把 visualRoot 旋转/平移过，
            // 但物理场景里的 Collider 还停留在旧位置；直接查 OverlapBox 会看到"旧体素"布局。强制同步后再查询。
            Physics.SyncTransforms();

            Bounds b = _controller.bounds;

            // SnapToStandCell 刻意让胶囊底嵌入所站方块 skinWidth（供 CharacterController.isGrounded 判定）；
            // 对"正好位于旋转轴上、位置没变"的地板体素（如 3x3x3 中心列），旋转后玩家仍嵌在它里面。
            // 底部至少收缩 skinWidth + epsilon 以避开此接触层；顶/侧只做少量 epsilon 收缩保留灵敏度。
            float skin = Mathf.Max(_controller.skinWidth, 0f);
            float bottomShrink = skin + 0.02f;
            const float topShrink = 0.02f;
            const float sideShrink = 0.02f;

            Vector3 center = b.center;
            Vector3 halfExtents = b.extents;
            halfExtents.x = Mathf.Max(0.005f, halfExtents.x - sideShrink);
            halfExtents.z = Mathf.Max(0.005f, halfExtents.z - sideShrink);
            halfExtents.y = Mathf.Max(0.005f, halfExtents.y - (bottomShrink + topShrink) * 0.5f);
            center.y += (bottomShrink - topShrink) * 0.5f;

            int count = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                OverlapScratch,
                transform.rotation,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            // 第二重 AABB 校验使用与 OverlapBox 同步收缩后的 AABB，避免 Bounds.Intersects 对相切面返回 true。
            Bounds shrunk = new Bounds(center, halfExtents * 2f);

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

                if (h.bounds.Intersects(shrunk))
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

            if (_playfield != null)
            {
                _playfield.SetPlayerSliceVisible(false);
            }

            if (!matrix.TryGetSliceStandCellFromFeetWorldPosition(GetFeetWorldPosition(), out Vector2Int stand))
            {
                return;
            }

            if (_playfield != null)
            {
                _playfield.SetLockedColumn(stand.x);
                _playfield.SetPlayerSliceVisible(true);
            }

            _player2D.SetCell(stand);
            LogPlayerDebugState(stand);
            _player2D.CheckGoal();
        }

        public Vector3 GetFeetWorldPositionForDebug()
        {
            Vector3 bottomLocal = _controller.center - Vector3.up * (_controller.height * 0.5f - _controller.skinWidth);
            return transform.TransformPoint(bottomLocal);
        }

        private Vector3 GetFeetWorldPosition()
        {
            return GetFeetWorldPositionForDebug();
        }

        private void LogPlayerDebugState(Vector2Int standCell)
        {
            if (matrix == null || !matrix.IsDebugLoggingEnabled())
            {
                return;
            }

            if (!matrix.TryGetPlayerStorageDebugInfo(out _, out Vector3Int storageCoord))
            {
                return;
            }

            bool goalDetected2D = _playfield != null && _playfield.IsPlayerOverlappingGoalObject();
            bool goalDetected3D = matrix.IsPlayerOverlappingGoalIn3D();
            if (standCell == _lastLoggedStandCell &&
                storageCoord == _lastLoggedStorageCoord &&
                goalDetected2D == _lastLoggedGoalDetected2D &&
                goalDetected3D == _lastLoggedGoalDetected3D)
            {
                return;
            }

            _lastLoggedStandCell = standCell;
            _lastLoggedStorageCoord = storageCoord;
            _lastLoggedGoalDetected2D = goalDetected2D;
            _lastLoggedGoalDetected3D = goalDetected3D;

            string goalStorageText = matrix.TryGetPreferredGoalStorageCoord(out Vector3Int goalStorage) ? goalStorage.ToString() : "invalid_or_hidden";
            string goalSliceText = matrix.TryGetPreferredGoalSliceBlockCell(out Vector2Int goalSliceBlock) ? goalSliceBlock.ToString() : "out_of_slice";
            Debug.Log(
                $"[LandscapeMatrix Debug][Player] stand={standCell} blockSlice=({standCell.x},{standCell.y - 1}) " +
                $"storage={storageCoord} goalStorage={goalStorageText} goalSliceBlock={goalSliceText} " +
                $"goalDetected2D={goalDetected2D} goalDetected3D={goalDetected3D}");
        }
    }
}
