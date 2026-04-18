using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LandscapeMatrix
{
    public class MatrixController : MonoBehaviour
    {
        /// <summary>
        /// 保留用于旧序列化；当前逻辑为场景中始终有 3×3×3 共 27 个方块，空位仅在运行时隐藏并关闭碰撞。
        /// </summary>
        public enum HiddenVoxelStrategy
        {
            DoNotGenerate,
            GenerateDisableVisualAndCollision
        }

        [System.Serializable]
        public struct VoxelCoord
        {
            public int x;
            public int y;
            public int z;
        }

        public const int MatrixHalfRange = 1;
        public const float MatrixMoveStep = 1f;
        public const int MatrixSize = 3;
        /// <summary>左侧 7x7 中与 3x3 切片对齐的起始格索引（与 MatrixSliceMapper 一致）。</summary>
        public const int SliceGridMin = 2;

        /// <summary>体素与 BuildMatrixVisual 中静态方块缩放一致。</summary>
        public const float VoxelVisualSize = 0.85f;

        /// <summary>3D 玩家胶囊高度/视觉，略小于 <see cref="VoxelVisualSize"/>，与方块比例协调。</summary>
        public const float Player3DHeight = 0.68f;

        private static float VoxelHalfExtent => VoxelVisualSize * 0.5f;
        private static float Player3DHalfHeight => Player3DHeight * 0.5f;

        /// <summary>体素中心在 visualRoot 局部 X/Z 上为 -1,0,1；缩放下体素在轴上的占用区间用于命中检测（缝隙处回退到最近列）。</summary>
        private static int StorageIndexFromLocalAxis(float localCoord)
        {
            int nearest = 1;
            float best = float.MaxValue;
            for (int s = 0; s < MatrixSize; s++)
            {
                float center = s - 1f;
                if (localCoord >= center - VoxelHalfExtent && localCoord <= center + VoxelHalfExtent)
                {
                    return s;
                }

                float d = Mathf.Abs(localCoord - center);
                if (d < best)
                {
                    best = d;
                    nearest = s;
                }
            }

            return nearest;
        }

        /// <summary>脚点在 visualRoot 局部 Y 上对应的体素高度层 0..2（与 BuildBaseMap 中 viewY 一致）。</summary>
        public static int GetViewYFromFeetLocalY(float feetLocalY)
        {
            return Mathf.Clamp(Mathf.RoundToInt(feetLocalY - VoxelHalfExtent), 0, MatrixSize - 1);
        }

        [Header("Matrix State (Editable)")]
        public Vector3Int initialGridOffset = Vector3Int.zero;
        [Range(0, 3)] public int initialRotationStep;

        [Header("Voxel Layout (Editable)")]
        public bool defaultVoxelVisible = true;
        [Tooltip("已废弃分支差异：空位均由场景中对应 Voxel_* 隐藏处理。")]
        public HiddenVoxelStrategy hiddenVoxelStrategy = HiddenVoxelStrategy.DoNotGenerate;
        public VoxelCoord[] hiddenVoxels =
        {
            new VoxelCoord { x = 0, y = 2, z = 0 },
            new VoxelCoord { x = 2, y = 2, z = 0 },
            new VoxelCoord { x = 2, y = 0, z = 2 },
            new VoxelCoord { x = 1, y = 2, z = 1 },
            new VoxelCoord { x = 0, y = 1, z = 2 },
            new VoxelCoord { x = 2, y = 1, z = 0 }
        };

        [Header("Runtime References (Public)")]
        public MatrixSliceMapper mapper;
        public Transform visualRoot;
        public bool[,,] voxelData;
        public Player2DController boundPlayer;
        public Player3DController player3D;

        public Vector3Int GridOffset { get; private set; }
        public int RotationStep { get; private set; }

        /// <summary>矩阵状态正在应用（切片/旋转/平移）时玩家不能移动。</summary>
        public bool IsMatrixStateChanging { get; private set; }

        private readonly List<Button> _cachedMatrixButtons = new List<Button>(8);
        private Transform _cachedSlicePlane;
        private Camera _cachedSliceCamera;

#if UNITY_EDITOR
        [System.NonSerialized]
        private bool _inspectorValidationDelayQueued;
#endif

        /// <summary>与 2D 采样一致的切片深度索引（视图 Z，0..2）。</summary>
        public int SliceSampledZ => Mathf.Clamp(1 - GridOffset.z, 0, 2);

        /// <summary>切片参考面可视化（无碰撞 Quad）。挂在矩阵控制器上，不随体素根的旋转/前后平移变化。</summary>
        private const string SlicePlaneVisualName = "SlicePlaneVisual";

        private const string MatrixVisualChildName = "MatrixVisual";

        /// <summary>
        /// 与 <see cref="Transform.Find"/> 不同：包含未激活的子物体；仅搜索直接子级。
        /// 避免 MatrixVisual 被关掉时 Find 失败，又在 <see cref="BuildMatrixVisual"/> 里再建一套导致每运行一次多 27 方块。
        /// </summary>
        private static Transform FindDirectChildByName(Transform parent, string childName)
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform c = parent.GetChild(i);
                if (c != null && c.name == childName)
                {
                    return c;
                }
            }

            return null;
        }

        /// <summary>若历史上叠了多个 MatrixVisual，只保留一个（优先保留已赋值的 <see cref="visualRoot"/>）。</summary>
        private void DeduplicateMatrixVisualRoots()
        {
            var found = new List<Transform>(4);
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform c = transform.GetChild(i);
                if (c != null && c.name == MatrixVisualChildName)
                {
                    found.Add(c);
                }
            }

            if (found.Count == 0)
            {
                return;
            }

            Transform keep = visualRoot != null && found.Contains(visualRoot) ? visualRoot : found[0];
            visualRoot = keep;

            for (int i = 0; i < found.Count; i++)
            {
                Transform t = found[i];
                if (t != null && t != keep)
                {
                    Object.DestroyImmediate(t.gameObject);
                }
            }
        }

        private void Awake()
        {
            if (visualRoot == null)
            {
                visualRoot = FindDirectChildByName(transform, MatrixVisualChildName);
            }

            DeduplicateMatrixVisualRoots();
            _cachedSlicePlane = null;
            _cachedSliceCamera = null;

            if (voxelData == null)
            {
                voxelData = BuildVoxelData();
            }

            GridOffset = ClampOffset(initialGridOffset);
            RotationStep = initialRotationStep % 4;

            if (visualRoot == null)
            {
                BuildMatrixVisual();
            }
            else
            {
                EnsureVoxelGridComplete();
                ApplyVoxelVisibilityFromData();
                if (FindSlicePlaneVisualTransform() == null)
                {
                    CreateSliceVisualizer();
                }

                EnsureAirWalls();
            }
        }

        private void Start()
        {
            if (mapper == null)
            {
                mapper = Object.FindFirstObjectByType<MatrixSliceMapper>();
            }

            if (boundPlayer == null)
            {
                boundPlayer = Object.FindFirstObjectByType<Player2DController>();
            }

            CacheMatrixUiButtons();

            if (boundPlayer != null)
            {
                BindPlayer(boundPlayer);
            }
            else
            {
                NotifyStateChanged();
            }
        }

        public void BuildMatrixVisual()
        {
            transform.position = new Vector3(10f, 0f, 0f);

            Transform existing = FindDirectChildByName(transform, MatrixVisualChildName);
            if (existing != null)
            {
                visualRoot = existing;
                voxelData = BuildVoxelData();
                GridOffset = ClampOffset(initialGridOffset);
                RotationStep = initialRotationStep % 4;
                EnsureVoxelGridComplete();
                ApplyVoxelVisibilityFromData();
                CreateSliceVisualizer();
                EnsureAirWalls();
                return;
            }

            visualRoot = new GameObject(MatrixVisualChildName).transform;
            visualRoot.SetParent(transform, false);
            voxelData = BuildVoxelData();
            GridOffset = ClampOffset(initialGridOffset);
            RotationStep = initialRotationStep % 4;

            EnsureVoxelGridComplete();
            ApplyVoxelVisibilityFromData();

            CreateSliceVisualizer();
            EnsureAirWalls();
        }

        [ContextMenu("Landscape Matrix/Ensure 27 voxels in scene (editor)")]
        private void ContextMenuEnsureVoxelsInScene()
        {
            if (visualRoot == null)
            {
                visualRoot = FindDirectChildByName(transform, MatrixVisualChildName);
            }

            DeduplicateMatrixVisualRoots();
            if (visualRoot == null)
            {
                Debug.LogWarning("MatrixController: assign or create MatrixVisual first.");
                return;
            }

            voxelData = BuildVoxelData();
            EnsureVoxelGridComplete();
            ApplyVoxelVisibilityFromData();
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            // OnValidate 内禁止 DestroyImmediate；推迟到下一编辑器帧再跑，与 Unity 要求一致。
            if (_inspectorValidationDelayQueued)
            {
                return;
            }

            _inspectorValidationDelayQueued = true;
            EditorApplication.delayCall += ApplyInspectorValidationDeferred;
#else
            ApplyInspectorValidationDeferred();
#endif
        }

        private void ApplyInspectorValidationDeferred()
        {
#if UNITY_EDITOR
            _inspectorValidationDelayQueued = false;
            if (this == null)
            {
                return;
            }
#endif

            voxelData = BuildVoxelData();
            if (visualRoot == null)
            {
                visualRoot = FindDirectChildByName(transform, MatrixVisualChildName);
            }

            DeduplicateMatrixVisualRoots();

            if (visualRoot == null)
            {
                return;
            }

            EnsureVoxelGridComplete();
            ApplyVoxelVisibilityFromData();

            Transform slice = FindSlicePlaneVisualTransform();
            if (slice != null)
            {
                UpdateSliceVisualizerTransform(slice);
            }
            else
            {
                CreateSliceVisualizer();
            }

            EnsureAirWalls();

            if (Application.isPlaying)
            {
                NotifyStateChanged();
            }
        }

        private static string VoxelObjectName(int x, int y, int z) => $"Voxel_{x}_{y}_{z}";

        private Transform FindVoxelTransform(int x, int y, int z)
        {
            if (visualRoot == null)
            {
                return null;
            }

            return FindDirectChildByName(visualRoot, VoxelObjectName(x, y, z));
        }

        /// <summary>
        /// 保证 MatrixVisual 下存在全部 27 个体素物体（编辑态写入场景；缺失则创建）。
        /// 不删除已有物体，避免反复清空再生成。
        /// </summary>
        private void EnsureVoxelGridComplete()
        {
            if (visualRoot == null || voxelData == null)
            {
                return;
            }

            for (int x = 0; x < MatrixSize; x++)
            {
                for (int y = 0; y < MatrixSize; y++)
                {
                    for (int z = 0; z < MatrixSize; z++)
                    {
                        if (FindVoxelTransform(x, y, z) != null)
                        {
                            continue;
                        }

                        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        cube.name = VoxelObjectName(x, y, z);
                        cube.transform.SetParent(visualRoot, false);
                        cube.transform.localPosition = new Vector3(x - 1f, y, z - 1f);
                        cube.transform.localScale = Vector3.one * VoxelVisualSize;
                        Color color = y switch
                        {
                            0 => new Color(0.48f, 0.72f, 0.95f, 0.9f),
                            1 => new Color(0.9f, 0.62f, 0.26f, 0.9f),
                            _ => new Color(0.62f, 0.86f, 0.56f, 0.9f)
                        };
                        cube.GetComponent<Renderer>().material.color = color;
                        cube.SetActive(true);
                    }
                }
            }
        }

        /// <summary>
        /// 按 <see cref="voxelData"/> 切换体素：有体素则显示并开启碰撞，空位则隐藏物体（无渲染、无碰撞）。
        /// </summary>
        private void ApplyVoxelVisibilityFromData()
        {
            if (visualRoot == null || voxelData == null)
            {
                return;
            }

            for (int x = 0; x < MatrixSize; x++)
            {
                for (int y = 0; y < MatrixSize; y++)
                {
                    for (int z = 0; z < MatrixSize; z++)
                    {
                        Transform t = FindVoxelTransform(x, y, z);
                        if (t == null)
                        {
                            continue;
                        }

                        bool filled = voxelData[x, y, z];
                        GameObject go = t.gameObject;
                        if (filled)
                        {
                            go.SetActive(true);
                            if (go.TryGetComponent(out Renderer r))
                            {
                                r.enabled = true;
                            }

                            if (go.TryGetComponent(out Collider col))
                            {
                                col.enabled = true;
                            }
                        }
                        else
                        {
                            go.SetActive(false);
                        }
                    }
                }
            }
        }

        /// <summary>视图 (viewX,viewY,viewZ) 对应体素中心在 <see cref="visualRoot"/> 局部空间中的坐标。</summary>
        private Vector3 VoxelCenterLocalFromView(int viewX, int viewY, int viewZ)
        {
            Vector2Int s = ViewXZToStorageXZ(viewX, viewZ, RotationStep);
            return new Vector3(s.x - 1f, viewY, s.y - 1f);
        }

        public void Initialize(MatrixSliceMapper mapper)
        {
            this.mapper = mapper;
            NotifyStateChanged();
        }

        public void BindPlayer(Player2DController player)
        {
            boundPlayer = player;
            if (boundPlayer == null)
            {
                return;
            }

            boundPlayer.ExternalDrive = true;
            EnsurePlayer3D();
            if (player3D != null)
            {
                player3D.ApplyDimensionsFromMatrix();
                player3D.SnapToStandCell(boundPlayer.GetCurrentCell());
            }

            NotifyStateChanged();
        }

        public void MoveForward()
        {
            if (!CanProceedMatrixOperation())
            {
                return;
            }

            IsMatrixStateChanging = true;
            try
            {
                GridOffset += new Vector3Int(0, 0, 1);
                GridOffset = ClampOffset(GridOffset);
                NotifyStateChanged();
            }
            finally
            {
                IsMatrixStateChanging = false;
            }
        }

        public void MoveBackward()
        {
            if (!CanProceedMatrixOperation())
            {
                return;
            }

            IsMatrixStateChanging = true;
            try
            {
                GridOffset += new Vector3Int(0, 0, -1);
                GridOffset = ClampOffset(GridOffset);
                NotifyStateChanged();
            }
            finally
            {
                IsMatrixStateChanging = false;
            }
        }

        public void RotateClockwise()
        {
            if (!CanProceedMatrixOperation())
            {
                return;
            }

            IsMatrixStateChanging = true;
            try
            {
                RotationStep = (RotationStep + 1) % 4;
                NotifyStateChanged();
            }
            finally
            {
                IsMatrixStateChanging = false;
            }
        }

        public void RotateCounterClockwise()
        {
            if (!CanProceedMatrixOperation())
            {
                return;
            }

            IsMatrixStateChanging = true;
            try
            {
                RotationStep = (RotationStep + 3) % 4;
                NotifyStateChanged();
            }
            finally
            {
                IsMatrixStateChanging = false;
            }
        }

        public void RestartLevel()
        {
            if (!CanProceedMatrixOperation())
            {
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.name);
        }

        private bool CanProceedMatrixOperation()
        {
            if (player3D == null)
            {
                return true;
            }

            return player3D.CanManipulateMatrix();
        }

        public void CacheMatrixUiButtons()
        {
            _cachedMatrixButtons.Clear();
            string[] ids = { "Btn_Forward", "Btn_Backward", "Btn_CW", "Btn_CCW", "Btn_Restart" };
            for (int i = 0; i < ids.Length; i++)
            {
                GameObject go = GameObject.Find(ids[i]);
                if (go != null && go.TryGetComponent(out Button b))
                {
                    b.navigation = new Navigation { mode = Navigation.Mode.None };
                    _cachedMatrixButtons.Add(b);
                }
            }
        }

        public void RefreshMatrixButtonsInteractable()
        {
            if (_cachedMatrixButtons.Count == 0)
            {
                CacheMatrixUiButtons();
            }

            bool ok = player3D == null || player3D.CanManipulateMatrix();
            for (int i = 0; i < _cachedMatrixButtons.Count; i++)
            {
                Button b = _cachedMatrixButtons[i];
                if (b != null)
                {
                    b.interactable = ok;
                }
            }
        }

        /// <summary>
        /// 内部 viewX（与 <see cref="TrySampleFilledCell"/> / <see cref="VoxelCenterLocalFromView"/> 一致，0..2）→
        /// 左侧 7×7 中心 3×3 上的列偏移 0..2。与世界 +X（两视角水平方向）同向递增。
        /// 90°/270° 时列在水平面上与世界 X 反向，需镜像，否则会与 3D 画面左右相对 180°。
        /// </summary>
        public int InternalViewXToDisplayColumnOffset(int internalViewX)
        {
            internalViewX = Mathf.Clamp(internalViewX, 0, MatrixSize - 1);
            if (RotationStep % 2 == 0)
            {
                return internalViewX;
            }

            return MatrixSize - 1 - internalViewX;
        }

        /// <summary>显示列偏移 0..2（网格 x = <see cref="SliceGridMin"/> + 偏移）→ 内部 viewX。</summary>
        public int DisplayColumnOffsetToInternalViewX(int displayColumnOffset)
        {
            displayColumnOffset = Mathf.Clamp(displayColumnOffset, 0, MatrixSize - 1);
            if (RotationStep % 2 == 0)
            {
                return displayColumnOffset;
            }

            return MatrixSize - 1 - displayColumnOffset;
        }

        /// <summary>体素存储索引 (sx,sz) 转为视图水平面索引 (vx,vz)，与 ViewXZToStorageXZ 互逆。</summary>
        public static Vector2Int RotateStorageToViewXZ(int storageX, int storageZ, int rotationStep)
        {
            return (rotationStep % 4) switch
            {
                0 => new Vector2Int(storageX, storageZ),
                1 => new Vector2Int(2 - storageZ, storageX),
                2 => new Vector2Int(2 - storageX, 2 - storageZ),
                _ => new Vector2Int(storageZ, 2 - storageX)
            };
        }

        public Vector3 GetLocalPositionForStandCell(Vector2Int standCell)
        {
            int floorGridY = standCell.y - 1;
            int displayColumnOffset = standCell.x - SliceGridMin;
            int sy = floorGridY - SliceGridMin;
            int vz = SliceSampledZ;
            if (displayColumnOffset < 0 || displayColumnOffset > 2 || sy < 0 || sy > 2)
            {
                return new Vector3(0f, VoxelHalfExtent, 0f);
            }

            int internalVx = DisplayColumnOffsetToInternalViewX(displayColumnOffset);

            // 脚底 XZ 取当前采样深度 vz 上体素中心，与切片 viewZ 一致（对齐 SliceSampledZ）。
            Vector3 voxelCenter = VoxelCenterLocalFromView(internalVx, sy, vz);
            float feetLocalY = sy + VoxelHalfExtent;
            return new Vector3(voxelCenter.x, feetLocalY, voxelCenter.z);
        }

        public Vector2Int WorldToSliceStandCell(Vector3 feetWorldPosition)
        {
            if (visualRoot == null)
            {
                return Vector2Int.zero;
            }

            Vector3 local = visualRoot.InverseTransformPoint(feetWorldPosition);
            int sy = GetViewYFromFeetLocalY(local.y);
            int vz = SliceSampledZ;

            // 与 MatrixSliceMapper / TrySampleFilledCell 使用同一套 viewX（0..2）：取当前切片深度上距脚底最近的列心，
            // 避免仅用 storage→view 公式在边界处与「切面实际采样的列」不一致导致 2D 左右反转。
            int bestVx = 0;
            float bestDist = float.MaxValue;
            for (int vx = 0; vx < MatrixSize; vx++)
            {
                Vector3 c = VoxelCenterLocalFromView(vx, sy, vz);
                float dx = local.x - c.x;
                float dz = local.z - c.z;
                float d = dx * dx + dz * dz;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestVx = vx;
                }
            }

            int floorGridY = SliceGridMin + sy;
            int standY = floorGridY + 1;
            int displayColumnOffset = InternalViewXToDisplayColumnOffset(bestVx);
            return new Vector2Int(SliceGridMin + displayColumnOffset, standY);
        }

        /// <param name="viewX">切片平面上的列 0..2（内部索引；与 2D 显示列在 90°/270° 时经 <see cref="InternalViewXToDisplayColumnOffset"/> 对应）。</param>
        /// <param name="storageY">体素高度层 0..2，与数据第二维一致。</param>
        /// <param name="viewZ">切片深度 0..2，与 <see cref="SliceSampledZ"/> 一致时与 2D 地形对齐。</param>
        public bool TrySampleFilledCell(int viewX, int storageY, int viewZ, out bool filled)
        {
            filled = false;
            if (voxelData == null)
            {
                return false;
            }

            if (storageY < 0 || storageY >= MatrixSize || viewX < 0 || viewX >= MatrixSize || viewZ < 0 || viewZ >= MatrixSize)
            {
                return false;
            }

            Vector2Int baseXZ = ViewXZToStorageXZ(viewX, viewZ, RotationStep);
            if (baseXZ.x < 0 || baseXZ.x >= MatrixSize || baseXZ.y < 0 || baseXZ.y >= MatrixSize)
            {
                return false;
            }

            filled = voxelData[baseXZ.x, storageY, baseXZ.y];
            return true;
        }

        /// <summary>视图水平面 (viewX, viewZ) → 存储 (sx, sz)。与 <see cref="RotateStorageToViewXZ"/> 互逆。</summary>
        public static Vector2Int ViewXZToStorageXZ(int viewX, int viewZ, int rotationStep)
        {
            return RotateInverse(viewX, viewZ, rotationStep);
        }

        private static Vector3Int ClampOffset(Vector3Int offset)
        {
            return new Vector3Int(
                0,
                0,
                Mathf.Clamp(offset.z, -MatrixHalfRange, MatrixHalfRange));
        }

        private Transform FindSlicePlaneVisualTransform()
        {
            Transform t = transform.Find(SlicePlaneVisualName);
            if (t != null)
            {
                return t;
            }

            if (visualRoot != null)
            {
                t = visualRoot.Find(SlicePlaneVisualName);
                if (t != null)
                {
                    return t;
                }

                return visualRoot.Find("FixedSliceVolume");
            }

            return null;
        }

        private void DestroySlicePlaneChildrenUnder(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            _cachedSlicePlane = null;

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform c = parent.GetChild(i);
                if (c == null)
                {
                    continue;
                }

                string n = c.name;
                if (n == SlicePlaneVisualName || n == "FixedSliceVolume")
                {
                    if (Application.isPlaying)
                    {
                        Object.Destroy(c.gameObject);
                    }
                    else
                    {
                        Object.DestroyImmediate(c.gameObject);
                    }
                }
            }
        }

        private void CreateSliceVisualizer()
        {
            DestroySlicePlaneChildrenUnder(transform);
            if (visualRoot != null)
            {
                DestroySlicePlaneChildrenUnder(visualRoot);
            }

            Transform legacyOnMatrix = transform.Find("FixedSliceVolume");
            if (legacyOnMatrix != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(legacyOnMatrix.gameObject);
                }
                else
                {
                    Object.DestroyImmediate(legacyOnMatrix.gameObject);
                }
            }

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = SlicePlaneVisualName;
            quad.transform.SetParent(transform, false);
            EnsureSlicePlaneHasNoColliders(quad);

            Renderer renderer = quad.GetComponent<Renderer>();
            if (renderer != null)
            {
                ConfigureSlicePlaneMaterial(renderer.material, new Color(0.95f, 0.2f, 0.2f, 0.16f));
            }

            UpdateSliceVisualizerTransform(quad.transform);
        }

        /// <summary>
        /// 切片参考面：固定在矩阵控制器局部位置，不随 RotationStep / GridOffset 变化；朝向与右侧参考相机像平面平行（面向相机）。
        /// </summary>
        private void UpdateSliceVisualizerTransform(Transform sliceTransform)
        {
            if (sliceTransform == null)
            {
                return;
            }

            _cachedSlicePlane = sliceTransform;

            sliceTransform.SetParent(transform, true);
            sliceTransform.localPosition = new Vector3(0f, 1f, 0f);
            sliceTransform.localScale = new Vector3(4.4f, 4.4f, 1f);
            if (_cachedSliceCamera == null)
            {
                GameObject go = GameObject.Find("Camera_Right3D");
                _cachedSliceCamera = go != null && go.TryGetComponent(out Camera c) ? c : Camera.main;
            }

            AlignSlicePlaneParallelToCamera(sliceTransform, _cachedSliceCamera);
            EnsureSlicePlaneHasNoColliders(sliceTransform.gameObject);
        }

        /// <summary>切面仅渲染：移除自身及子物体上所有 Collider（含运行时误加）。</summary>
        private static void EnsureSlicePlaneHasNoColliders(GameObject sliceRoot)
        {
            if (sliceRoot == null)
            {
                return;
            }

            Collider[] cols = sliceRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider c = cols[i];
                if (c == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Object.Destroy(c);
                }
                else
                {
                    Object.DestroyImmediate(c);
                }
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (_cachedSlicePlane == null)
            {
                _cachedSlicePlane = FindSlicePlaneVisualTransform();
            }

            if (_cachedSliceCamera == null)
            {
                GameObject go = GameObject.Find("Camera_Right3D");
                _cachedSliceCamera = go != null && go.TryGetComponent(out Camera c) ? c : Camera.main;
            }

            if (_cachedSlicePlane != null)
            {
                AlignSlicePlaneParallelToCamera(_cachedSlicePlane, _cachedSliceCamera);
            }
        }

        /// <summary>
        /// 使 Quad 在水平面内朝向相机（仅绕世界 Y），世界欧拉 X=0，避免随相机俯仰倾斜。
        /// </summary>
        private static void AlignSlicePlaneParallelToCamera(Transform sliceTransform, Camera cam)
        {
            if (sliceTransform == null || cam == null)
            {
                return;
            }

            Vector3 worldPos = sliceTransform.position;
            Vector3 toCamera = cam.transform.position - worldPos;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude < 1e-10f)
            {
                return;
            }

            sliceTransform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
        }

        private static void ConfigureSlicePlaneMaterial(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            ConfigureTransparentMaterial(material, color);

            // URP Lit：双面渲染，避免单面 Quad 因视角被完全剔除。
            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", 0f);
            }
        }

        private void NotifyStateChanged()
        {
            if (visualRoot != null)
            {
                visualRoot.localPosition = new Vector3(0f, 0f, GridOffset.z * MatrixMoveStep);
                visualRoot.localRotation = Quaternion.Euler(0f, RotationStep * 90f, 0f);
            }

            Transform slice = FindSlicePlaneVisualTransform();
            if (slice != null)
            {
                UpdateSliceVisualizerTransform(slice);
            }
            else
            {
                _cachedSlicePlane = null;
            }

            mapper?.ApplyMatrixState();
        }

        private bool[,,] BuildVoxelData()
        {
            bool[,,] data = new bool[MatrixSize, MatrixSize, MatrixSize];
            for (int x = 0; x < MatrixSize; x++)
            {
                for (int y = 0; y < MatrixSize; y++)
                {
                    for (int z = 0; z < MatrixSize; z++)
                    {
                        data[x, y, z] = defaultVoxelVisible;
                    }
                }
            }

            if (hiddenVoxels != null)
            {
                for (int i = 0; i < hiddenVoxels.Length; i++)
                {
                    VoxelCoord coord = hiddenVoxels[i];
                    if (coord.x < 0 || coord.x >= MatrixSize ||
                        coord.y < 0 || coord.y >= MatrixSize ||
                        coord.z < 0 || coord.z >= MatrixSize)
                    {
                        continue;
                    }
                    data[coord.x, coord.y, coord.z] = false;
                }
            }
            return data;
        }

        private static Vector2Int RotateInverse(int x, int z, int rotationStep)
        {
            return (rotationStep % 4) switch
            {
                0 => new Vector2Int(x, z),
                1 => new Vector2Int(z, 2 - x),
                2 => new Vector2Int(2 - x, 2 - z),
                _ => new Vector2Int(2 - z, x)
            };
        }

        private void EnsurePlayer3D()
        {
            DestroyLegacy3DMarkersAndMeshes();

            if (player3D != null)
            {
                player3D.matrix = this;
                if (player3D.transform.parent != transform)
                {
                    player3D.transform.SetParent(transform, true);
                }

                player3D.ApplyDimensionsFromMatrix();
                DestroyLegacyMeshChild(player3D.transform);
                EnsurePlayerVisualCapsule(player3D.transform);
                return;
            }

            Transform existing = transform.Find("Player3D");
            if (existing == null && visualRoot != null)
            {
                existing = visualRoot.Find("Player3D");
            }

            if (existing != null)
            {
                player3D = existing.GetComponent<Player3DController>();
                if (player3D != null)
                {
                    player3D.matrix = this;
                    player3D.transform.SetParent(transform, true);
                    player3D.ApplyDimensionsFromMatrix();
                    DestroyLegacyMeshChild(existing);
                    EnsurePlayerVisualCapsule(existing);
                    return;
                }
            }

            if (visualRoot == null)
            {
                return;
            }

            GameObject body = new GameObject("Player3D");
            body.transform.SetParent(transform, false);
            CharacterController cc = body.AddComponent<CharacterController>();
            cc.skinWidth = 0.05f;
            ApplyPlayerCharacterDimensions(cc);

            EnsurePlayerVisualCapsule(body.transform);

            player3D = body.AddComponent<Player3DController>();
            player3D.matrix = this;
        }

        private static void DestroyLegacyMeshChild(Transform playerRoot)
        {
            if (playerRoot == null)
            {
                return;
            }

            Transform mesh = playerRoot.Find("Mesh");
            if (mesh != null)
            {
                Object.Destroy(mesh.gameObject);
            }
        }

        private void DestroyLegacy3DMarkersAndMeshes()
        {
            void TryDestroyMarker(Transform root)
            {
                if (root == null)
                {
                    return;
                }

                Transform legacy = root.Find("Player3D_Marker");
                if (legacy != null)
                {
                    Object.Destroy(legacy.gameObject);
                }
            }

            TryDestroyMarker(transform);
            TryDestroyMarker(visualRoot);
        }

        private static void ApplyPlayerCharacterDimensions(CharacterController cc)
        {
            cc.height = Player3DHeight;
            cc.radius = Player3DHeight * 0.24f;
            cc.center = new Vector3(0f, Player3DHalfHeight, 0f);
        }

        private static void EnsurePlayerVisualCapsule(Transform playerRoot)
        {
            Transform existingVisual = playerRoot.Find("Visual");
            if (existingVisual != null)
            {
                Object.Destroy(existingVisual.gameObject);
            }

            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            cap.name = "Visual";
            cap.transform.SetParent(playerRoot, false);
            cap.transform.localPosition = new Vector3(0f, Player3DHalfHeight, 0f);
            float H = Player3DHeight;
            float R = Player3DHeight * 0.24f;
            cap.transform.localScale = new Vector3(R / 0.5f, H * 0.5f, R / 0.5f);
            Object.Destroy(cap.GetComponent<Collider>());
            Renderer renderer = cap.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = new Color(1f, 0.9f, 0.25f, 1f);
            }
        }

        /// <summary>MatrixVisual 下空气墙根节点：精确名或 Unity 自动重命名 AirWalls (1) 等。</summary>
        private static bool IsAirWallsRootName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return false;
            }

            if (objectName == "AirWalls")
            {
                return true;
            }

            return objectName.StartsWith("AirWalls (", System.StringComparison.Ordinal);
        }

        /// <summary>
        /// 在体素区域四周放置无渲染盒体碰撞，防止角色从侧面掉落（随 visualRoot 旋转）。
        /// 已存在则不销毁重建：避免 OnValidate、进出 Play 时 Destroy 未生效又 new 导致多套 AirWalls。
        /// </summary>
        private void EnsureAirWalls()
        {
            if (visualRoot == null)
            {
                return;
            }

            var roots = new List<Transform>(4);
            for (int i = 0; i < visualRoot.childCount; i++)
            {
                Transform c = visualRoot.GetChild(i);
                if (c != null && IsAirWallsRootName(c.name))
                {
                    roots.Add(c);
                }
            }

            Transform keep = null;
            for (int i = 0; i < roots.Count; i++)
            {
                if (roots[i] != null && roots[i].name == "AirWalls")
                {
                    keep = roots[i];
                    break;
                }
            }

            if (keep == null && roots.Count > 0)
            {
                keep = roots[0];
            }

            for (int i = 0; i < roots.Count; i++)
            {
                Transform r = roots[i];
                if (r != null && r != keep)
                {
                    Object.DestroyImmediate(r.gameObject);
                }
            }

            if (keep != null)
            {
                if (keep.name != "AirWalls")
                {
                    keep.gameObject.name = "AirWalls";
                }

                return;
            }

            GameObject root = new GameObject("AirWalls");
            root.transform.SetParent(visualRoot, false);

            float half = VoxelHalfExtent;
            float gridHalf = (MatrixSize - 1) * 0.5f + half;
            float t = 0.18f;
            float wallHeight = 5.5f;
            float span = gridHalf * 2f + t * 2f;

            void AddWall(string wallName, Vector3 localCenter, Vector3 size)
            {
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = wallName;
                wall.transform.SetParent(root.transform, false);
                wall.transform.localPosition = localCenter;
                wall.transform.localScale = size;
                Renderer rend = wall.GetComponent<Renderer>();
                if (rend != null)
                {
                    rend.enabled = false;
                }
            }

            float yCenter = wallHeight * 0.5f;
            AddWall("AirWall_XMin", new Vector3(-gridHalf - t * 0.5f, yCenter, 0f), new Vector3(t, wallHeight, span));
            AddWall("AirWall_XMax", new Vector3(gridHalf + t * 0.5f, yCenter, 0f), new Vector3(t, wallHeight, span));
            AddWall("AirWall_ZMin", new Vector3(0f, yCenter, -gridHalf - t * 0.5f), new Vector3(span, wallHeight, t));
            AddWall("AirWall_ZMax", new Vector3(0f, yCenter, gridHalf + t * 0.5f), new Vector3(span, wallHeight, t));
        }

        private static void ConfigureTransparentMaterial(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            material.color = color;

            // URP/Lit
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_ZWrite", 0f);
                material.renderQueue = 3000;
            }

            // Built-in Standard
            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = 3000;
            }
        }
    }
}
