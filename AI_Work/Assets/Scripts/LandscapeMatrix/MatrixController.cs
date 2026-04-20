using System.Collections;
using System.Collections.Generic;
using System.Text;
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
        /// 保留用于旧序列化；当前逻辑为场景中按关卡尺寸构建方块，空位仅在运行时隐藏并关闭碰撞。
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

        public const int DefaultMatrixSize = 3;
        public const float MatrixMoveStep = 1f;

        /// <summary>地块格坐标原点；与 3×3 切面列/层对应关系为 worldX = 偏移、worldY = 体素层 viewY。</summary>
        public const int SliceGridMin = 0;

        /// <summary>左屏 2D 地图格 (0,0) 地块中心在世界坐标中的原点；与 <see cref="SliceMapWidth"/>×<see cref="SliceMapHeight"/> 一致，不再使用旧 7×7 嵌套偏移。</summary>
        public const float SliceBoardWorldOriginX = 0f;

        public const float SliceBoardWorldOriginY = 0f;

        /// <summary>体素与 BuildMatrixVisual 中静态方块缩放一致。</summary>
        public const float VoxelVisualSize = 0.85f;

        /// <summary>默认体素色；仅出生/目标格由 <see cref="RefreshVoxelColorsFromSliceMap"/> 上色。</summary>
        private static readonly Color VoxelNeutralColor = new Color(0.78f, 0.78f, 0.78f, 1f);

        private static readonly Color VoxelSpawnHighlightColor = new Color(0.25f, 0.5f, 1f, 1f);
        private static readonly Color VoxelGoalHighlightColor = new Color(0.16f, 0.84f, 0.35f, 1f);

        /// <summary>3D 玩家胶囊高度/视觉，略小于 <see cref="VoxelVisualSize"/>，与方块比例协调。</summary>
        public const float Player3DHeight = 0.68f;

        private static float VoxelHalfExtent => VoxelVisualSize * 0.5f;
        private static float Player3DHalfHeight => Player3DHeight * 0.5f;
        private static float Player3DRadius => Player3DHeight * 0.24f;
        private const float FixedSliceEpsilon = 0.02f;

        /// <summary>体素中心在 visualRoot 局部 X/Z 上按关卡尺寸居中排布；缝隙处回退到最近列。</summary>
        private int StorageIndexFromLocalAxis(float localCoord)
        {
            int nearest = CurrentMatrixSize / 2;
            float best = float.MaxValue;
            for (int s = 0; s < CurrentMatrixSize; s++)
            {
                float center = GetCenteredAxisCoordinate(s);
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

        /// <summary>脚点在 visualRoot 局部 Y 上对应的体素高度层。</summary>
        public int GetViewYFromFeetLocalY(float feetLocalY)
        {
            return Mathf.Clamp(Mathf.RoundToInt(feetLocalY - VoxelHalfExtent), 0, CurrentMatrixSize - 1);
        }

        [Header("Matrix State (Editable)")]
        [Min(1)] public int matrixSize = DefaultMatrixSize;
        [Min(0)] public int sliceMapWidthOverride;
        [Min(0)] public int sliceFloorRowsOverride;
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

        [Header("Scene References")]
        [SerializeField] private Button _forwardButton;
        [SerializeField] private Button _backwardButton;
        [SerializeField] private Button _clockwiseButton;
        [SerializeField] private Button _counterClockwiseButton;
        [SerializeField] private Button _restartButton;
        [SerializeField] private Camera _sliceReferenceCamera;

        [Header("Debug")]
        [SerializeField] private bool _enableDebugLogs = true;

        [Header("Presentation Motion")]
        [SerializeField] private bool _enablePresentationMotion = true;
        [SerializeField, Min(0f)] private float _presentationMotionDuration = 0.22f;

        [Tooltip("需要跟随矩阵视觉（MatrixPresentation）一起平滑旋转/平移的额外装饰物。\n" +
                 "把这些 Transform 拖进来，运行时会自动把它们 reparent 到 MatrixPresentation 下，\n" +
                 "编辑期仍可把它们摆在 Matrix 根下调位置。")]
        [SerializeField] private Transform[] _extraPresentationProps;

        private Transform _goalItemVisual;
        private Transform _presentationRoot;
        private Coroutine _presentationMotionRoutine;
        private Vector3 _presentationDisplayedLocalPosition;
        private Quaternion _presentationDisplayedLocalRotation = Quaternion.identity;
        private bool _presentationPoseInitialized;
        private readonly HashSet<Transform> _attachedPresentationProps = new HashSet<Transform>();

        public Vector3Int GridOffset { get; private set; }
        public int RotationStep { get; private set; }

        /// <summary>矩阵状态正在应用（切片/旋转/平移）时玩家不能移动。</summary>
        public bool IsMatrixStateChanging { get; private set; }

        private readonly List<Button> _cachedMatrixButtons = new List<Button>(8);
        private Transform _cachedSlicePlane;
        private Camera _cachedSliceCamera;

        /// <summary>
        /// 体素 Transform 查询缓存，避免反复 <see cref="FindDirectChildByName"/> 扫描 visualRoot 的全部子物体。
        /// 关联的 <see cref="visualRoot"/> 或矩阵尺寸变化时自动失效重建；被销毁的缓存项在下次访问时回退到 Find 再重新记录。
        /// </summary>
        private Transform[,,] _voxelTransformCache;
        private Transform _voxelTransformCacheRoot;
        private bool _levelClearedFrom3DOverlap;
        private Playfield2D _cachedPlayfield;
        private string _pendingDebugReason = "StateChanged";

        private bool _hasPreviousMatrixState;
        private Vector3Int _previousGridOffset;
        private int _previousRotationStep;
        private Vector2Int _previousPlayerCell;
        private Player3DController.UndoSnapshot _previousPlayerSnapshot;
        private bool _hasPreviousPlayerSnapshot;
        private bool _checkFusionAfterNotify;
        private bool _suppressPlayerSnapOnNotify;

#if UNITY_EDITOR
        [System.NonSerialized]
        private bool _inspectorValidationDelayQueued;
#endif

        public int CurrentMatrixSize => Mathf.Max(1, matrixSize);
        public int SliceMapWidth => sliceMapWidthOverride > 0 ? sliceMapWidthOverride : CurrentMatrixSize;
        public int SliceFloorRowCount => sliceFloorRowsOverride > 0 ? sliceFloorRowsOverride : CurrentMatrixSize;
        public int SliceMapHeight => SliceFloorRowCount + 1;
        public int SliceAnchorIndex => Mathf.Clamp((CurrentMatrixSize - 1) / 2, 0, CurrentMatrixSize - 1);
        public int MatrixHalfRange => SliceAnchorIndex;
        public int MinGridOffsetZ => SliceAnchorIndex - (CurrentMatrixSize - 1);
        public int MaxGridOffsetZ => SliceAnchorIndex;

        /// <summary>与 2D 采样一致的切片深度索引（视图 Z）。</summary>
        public int SliceSampledZ => Mathf.Clamp(SliceAnchorIndex - GridOffset.z, 0, CurrentMatrixSize - 1);

        private float CenteredAxisOffset => (CurrentMatrixSize - 1) * 0.5f;
        private float FixedSliceCenterLocalZ => GetCenteredAxisCoordinate(SliceAnchorIndex);

        private float GetCenteredAxisCoordinate(int index)
        {
            return index - CenteredAxisOffset;
        }

        public void ApplyLevelData(
            int configuredMatrixSize,
            int configuredSliceMapWidth,
            int configuredSliceFloorRows,
            Vector3Int configuredInitialGridOffset,
            int configuredInitialRotationStep,
            bool configuredDefaultVoxelVisible,
            VoxelCoord[] configuredHiddenVoxels)
        {
            matrixSize = Mathf.Max(1, configuredMatrixSize);
            sliceMapWidthOverride = Mathf.Max(0, configuredSliceMapWidth);
            sliceFloorRowsOverride = Mathf.Max(0, configuredSliceFloorRows);
            initialGridOffset = configuredInitialGridOffset;
            initialRotationStep = configuredInitialRotationStep % 4;
            defaultVoxelVisible = configuredDefaultVoxelVisible;
            hiddenVoxels = configuredHiddenVoxels ?? System.Array.Empty<VoxelCoord>();

            if (visualRoot != null)
            {
                voxelData = BuildVoxelData();
                GridOffset = ClampOffset(initialGridOffset);
                RotationStep = initialRotationStep % 4;
                EnsureVoxelGridComplete();
                DestroyLegacyVoxelsOutsideDataBounds();
                SyncVoxelTransformsToCurrentLayout();
                ApplyVoxelVisibilityFromData();
                CreateSliceVisualizer();
                EnsureAirWalls();

                if (Application.isPlaying)
                {
                    NotifyStateChanged();
                }
            }
        }

        /// <summary>切片参考面可视化（无碰撞 Quad）。挂在矩阵控制器上，不随体素根的旋转/前后平移变化。</summary>
        private const string SlicePlaneVisualName = "SlicePlaneVisual";

        private const string MatrixVisualChildName = "MatrixVisual";
        private const string MatrixPresentationChildName = "MatrixPresentation";

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
            _presentationRoot = null;
            _presentationPoseInitialized = false;

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

            EnsurePresentationVisuals();
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

            _cachedPlayfield = Object.FindFirstObjectByType<Playfield2D>();

            CacheMatrixUiButtons();
            BindMatrixUiButtons();

            if (boundPlayer != null)
            {
                BindPlayer(boundPlayer);
            }
            else
            {
                NotifyStateChanged();
            }

            EnsureSliceCameraCached();
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
                SyncVoxelTransformsToCurrentLayout();
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
            SyncVoxelTransformsToCurrentLayout();
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
            SyncVoxelTransformsToCurrentLayout();
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

            int n = CurrentMatrixSize;
            if (x < 0 || x >= n || y < 0 || y >= n || z < 0 || z >= n)
            {
                return null;
            }

            if (_voxelTransformCache == null ||
                _voxelTransformCacheRoot != visualRoot ||
                _voxelTransformCache.GetLength(0) != n)
            {
                _voxelTransformCache = new Transform[n, n, n];
                _voxelTransformCacheRoot = visualRoot;
            }

            Transform cached = _voxelTransformCache[x, y, z];
            // Unity 的 UnityEngine.Object 重载了 == null 以检测已销毁对象。
            // 若缓存被重新 parent（极少见，但 DeduplicateMatrixVisualRoots 可能发生），需回退到 Find。
            if (cached != null && cached.parent == visualRoot)
            {
                return cached;
            }

            Transform found = FindDirectChildByName(visualRoot, VoxelObjectName(x, y, z));
            _voxelTransformCache[x, y, z] = found;
            return found;
        }

        /// <summary>
        /// 供对整块体素网格做批量处理的方法复用：在当前矩阵尺寸范围内枚举 (x,y,z)。
        /// 保持 x→y→z 的访问顺序与原版循环一致，避免对依赖遍历顺序的调用方产生影响。
        /// </summary>
        private void ForEachVoxelIndex(System.Action<int, int, int> callback)
        {
            int n = CurrentMatrixSize;
            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++)
                {
                    for (int z = 0; z < n; z++)
                    {
                        callback(x, y, z);
                    }
                }
            }
        }

        private void DestroyLegacyVoxelsOutsideDataBounds()
        {
            if (visualRoot == null)
            {
                return;
            }

            for (int i = visualRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = visualRoot.GetChild(i);
                if (child == null || !child.name.StartsWith("Voxel_", System.StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = child.name.Split('_');
                if (parts.Length != 4 ||
                    !int.TryParse(parts[1], out int x) ||
                    !int.TryParse(parts[2], out int y) ||
                    !int.TryParse(parts[3], out int z))
                {
                    continue;
                }

                if (x >= 0 && x < CurrentMatrixSize && y >= 0 && y < CurrentMatrixSize && z >= 0 && z < CurrentMatrixSize)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Object.Destroy(child.gameObject);
                }
                else
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        /// <summary>
        /// 保证 MatrixVisual 下存在全部体素物体（编辑态写入场景；缺失则创建）。
        /// 不删除已有物体，避免反复清空再生成。
        /// </summary>
        private void EnsureVoxelGridComplete()
        {
            if (visualRoot == null || voxelData == null)
            {
                return;
            }

            ForEachVoxelIndex((x, y, z) =>
            {
                if (FindVoxelTransform(x, y, z) != null)
                {
                    return;
                }

                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = VoxelObjectName(x, y, z);
                cube.transform.SetParent(visualRoot, false);
                cube.transform.localPosition = new Vector3(GetCenteredAxisCoordinate(x), y, GetCenteredAxisCoordinate(z));
                cube.transform.localScale = Vector3.one * VoxelVisualSize;
                LandscapeMatrixRendererColors.SetColor(cube.GetComponent<Renderer>(), VoxelNeutralColor);
                cube.SetActive(true);

                if (_voxelTransformCache != null && _voxelTransformCacheRoot == visualRoot)
                {
                    _voxelTransformCache[x, y, z] = cube.transform;
                }
            });
        }

        private void SyncVoxelTransformsToCurrentLayout()
        {
            if (visualRoot == null)
            {
                return;
            }

            ForEachVoxelIndex((x, y, z) =>
            {
                Transform voxel = FindVoxelTransform(x, y, z);
                if (voxel == null)
                {
                    return;
                }

                voxel.localPosition = new Vector3(GetCenteredAxisCoordinate(x), y, GetCenteredAxisCoordinate(z));
                voxel.localRotation = Quaternion.identity;
                voxel.localScale = Vector3.one * VoxelVisualSize;
            });
        }

        public void RebuildStaticSceneVisualsForEditor()
        {
            voxelData = BuildVoxelData();
            if (visualRoot == null)
            {
                visualRoot = FindDirectChildByName(transform, MatrixVisualChildName);
            }

            if (visualRoot == null)
            {
                BuildMatrixVisual();
                return;
            }

            EnsureVoxelGridComplete();
            DestroyLegacyVoxelsOutsideDataBounds();
            SyncVoxelTransformsToCurrentLayout();
            ApplyVoxelVisibilityFromData();
            CreateSliceVisualizer();
            EnsureAirWalls();
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

            ForEachVoxelIndex((x, y, z) =>
            {
                Transform t = FindVoxelTransform(x, y, z);
                if (t == null)
                {
                    return;
                }

                GameObject go = t.gameObject;
                if (voxelData[x, y, z])
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
            });

            SyncPresentationVisualsFromLogicalRoot();
        }

        /// <summary>视图 (viewX,viewY,viewZ) 对应体素中心在 <see cref="visualRoot"/> 局部空间中的坐标。</summary>
        private Vector3 VoxelCenterLocalFromView(int viewX, int viewY, int viewZ)
        {
            Vector2Int s = ViewXZToStorageXZ(viewX, viewZ, RotationStep);
            return new Vector3(GetCenteredAxisCoordinate(s.x), viewY, GetCenteredAxisCoordinate(s.y));
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
            }

            // 先刷新矩阵/切面地图（含出生点解析与 2D 落位），再根据最终 GetCurrentCell 吸附 3D；
            // 若在 NotifyStateChanged 之前 Snap，会与 RefreshMap 中 ResolveAfterTerrainChange 冲突，导致 2D/3D 错位。
            NotifyStateChanged();
        }

        public void MoveForward() => ExecuteMatrixOperation("MoveForward", () => GridOffset = ClampOffset(GridOffset + new Vector3Int(0, 0, 1)));

        public void MoveBackward() => ExecuteMatrixOperation("MoveBackward", () => GridOffset = ClampOffset(GridOffset + new Vector3Int(0, 0, -1)));

        public void RotateClockwise() => ExecuteMatrixOperation("RotateClockwise", () => RotationStep = (RotationStep + 1) % 4);

        public void RotateCounterClockwise() => ExecuteMatrixOperation("RotateCounterClockwise", () => RotationStep = (RotationStep + 3) % 4);

        /// <summary>
        /// 4 个矩阵操作（前进/后退/顺时针/逆时针）的公共骨架：
        /// 先校验可操作→保存撤销快照→置忙标记→执行操作→通知刷新。try/finally 保证 IsMatrixStateChanging 永远复位。
        /// </summary>
        private void ExecuteMatrixOperation(string debugReason, System.Action mutate)
        {
            if (!CanProceedMatrixOperation())
            {
                return;
            }

            CaptureUndoStateBeforeOperation();
            IsMatrixStateChanging = true;
            try
            {
                _pendingDebugReason = debugReason;
                _checkFusionAfterNotify = true;
                mutate();
                NotifyStateChanged();
            }
            finally
            {
                IsMatrixStateChanging = false;
            }
        }

        /// <summary>
        /// 在 4 个矩阵操作执行前统一调用：保存 <see cref="GridOffset"/>、<see cref="RotationStep"/>、
        /// 2D 玩家格与 3D 玩家位姿，供融合弹窗的 Undo 按钮回滚到上一步。
        /// </summary>
        private void CaptureUndoStateBeforeOperation()
        {
            _previousGridOffset = GridOffset;
            _previousRotationStep = RotationStep;

            if (boundPlayer != null)
            {
                _previousPlayerCell = boundPlayer.GetCurrentCell();
            }
            else
            {
                _previousPlayerCell = Vector2Int.zero;
            }

            if (player3D != null)
            {
                _previousPlayerSnapshot = player3D.CaptureUndoSnapshot();
                _hasPreviousPlayerSnapshot = true;
            }
            else
            {
                _hasPreviousPlayerSnapshot = false;
            }

            _hasPreviousMatrixState = true;
        }

        /// <summary>
        /// 由 <see cref="FusionPresenter"/> 在点击 Undo 时调用：恢复上一次矩阵操作前的姿态与玩家位置。
        /// </summary>
        public void UndoLastMatrixOperation()
        {
            if (!_hasPreviousMatrixState)
            {
                return;
            }

            _hasPreviousMatrixState = false;
            _checkFusionAfterNotify = false;
            _levelClearedFrom3DOverlap = false;

            IsMatrixStateChanging = true;
            try
            {
                _pendingDebugReason = "Undo";
                GridOffset = ClampOffset(_previousGridOffset);
                RotationStep = _previousRotationStep % 4;

                // 1) 抑制 NotifyStateChanged 内部的 SnapToStandCell：我们要用 3D 快照直接还原世界位姿，而不是让它按当前 2D 格反算。
                _suppressPlayerSnapOnNotify = _hasPreviousPlayerSnapshot;
                NotifyStateChanged();

                // 2) 清理 NotifyStateChanged 内部 RefreshMap → ResolvePlayerAfterTerrainChange 可能留下的死亡标记或被覆盖的 2D 玩家格。
                Playfield2D playfield = GetCachedPlayfield();
                if (playfield != null)
                {
                    playfield.SetPlayerDead(false);
                }

                if (boundPlayer != null)
                {
                    boundPlayer.SetCell(_previousPlayerCell);
                }

                // 3) 最后把 3D 玩家放回捕获的世界位姿；下一帧 Player3DController.LateUpdate 会基于此重新解算 _lockedColumnX 与 2D 格。
                if (_hasPreviousPlayerSnapshot && player3D != null)
                {
                    player3D.ApplyUndoSnapshot(_previousPlayerSnapshot);
                }
            }
            finally
            {
                _suppressPlayerSnapOnNotify = false;
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

            _forwardButton = ResolveButtonReference(_forwardButton, "Btn_Forward");
            _backwardButton = ResolveButtonReference(_backwardButton, "Btn_Backward");
            _clockwiseButton = ResolveButtonReference(_clockwiseButton, "Btn_CW");
            _counterClockwiseButton = ResolveButtonReference(_counterClockwiseButton, "Btn_CCW");
            _restartButton = ResolveButtonReference(_restartButton, "Btn_Restart");

            TryCacheButton(_forwardButton);
            TryCacheButton(_backwardButton);
            TryCacheButton(_clockwiseButton);
            TryCacheButton(_counterClockwiseButton);
            TryCacheButton(_restartButton);
        }

        private static Button ResolveButtonReference(Button current, string objectName)
        {
            if (current != null)
            {
                return current;
            }

            GameObject go = GameObject.Find(objectName);
            if (go == null || !go.TryGetComponent(out Button button))
            {
                return null;
            }

            return button;
        }

        private void TryCacheButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (!_cachedMatrixButtons.Contains(button))
            {
                _cachedMatrixButtons.Add(button);
            }
        }

        private void BindMatrixUiButtons()
        {
            BindMatrixButton(_forwardButton, MoveForward);
            BindMatrixButton(_backwardButton, MoveBackward);
            BindMatrixButton(_clockwiseButton, RotateClockwise);
            BindMatrixButton(_counterClockwiseButton, RotateCounterClockwise);
            BindMatrixButton(_restartButton, RestartLevel);
        }

        private static void BindMatrixButton(Button button, UnityEngine.Events.UnityAction callback)
        {
            if (button == null)
            {
                return;
            }

            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(callback);
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
        /// 左侧切面地图上的列偏移 0..2。与世界 +X（两视角水平方向）同向递增。
        /// 90°/270° 时列在水平面上与世界 X 反向，需镜像，否则会与 3D 画面左右相对 180°。
        /// </summary>
        public int InternalViewXToDisplayColumnOffset(int internalViewX)
        {
            internalViewX = Mathf.Clamp(internalViewX, 0, CurrentMatrixSize - 1);
            if (RotationStep % 2 == 0)
            {
                return internalViewX;
            }

            return CurrentMatrixSize - 1 - internalViewX;
        }

        /// <summary>显示列偏移 0..2（网格 x = <see cref="SliceGridMin"/> + 偏移，即 0..2）→ 内部 viewX。</summary>
        public int DisplayColumnOffsetToInternalViewX(int displayColumnOffset)
        {
            displayColumnOffset = Mathf.Clamp(displayColumnOffset, 0, CurrentMatrixSize - 1);
            if (RotationStep % 2 == 0)
            {
                return displayColumnOffset;
            }

            return CurrentMatrixSize - 1 - displayColumnOffset;
        }

        /// <summary>
        /// 切面地图内地块格 (worldX, worldY)（与 <see cref="CellType"/> 地图一致；地块层 y 为 0..2）对应体素在顶面无遮挡：
        /// 该列最上层体素之上不再有填充体素（顶层 y=2 视为满足）。
        /// </summary>
        private Vector3 GetStorageCenterInFixedSliceLocal(int sx, int sy, int sz)
        {
            Vector3 voxelLocal = new Vector3(GetCenteredAxisCoordinate(sx), sy, GetCenteredAxisCoordinate(sz));
            if (visualRoot == null)
            {
                return voxelLocal;
            }

            Vector3 world = visualRoot.TransformPoint(voxelLocal);
            return transform.InverseTransformPoint(world);
        }

        private bool IsInsideFixedSliceSlab(float localZ, float extraHalfThickness = 0f)
        {
            return Mathf.Abs(localZ - FixedSliceCenterLocalZ) <= VoxelHalfExtent + extraHalfThickness + FixedSliceEpsilon;
        }

        private bool TryMapStorageToFixedSliceBlockCell(int sx, int sy, int sz, out Vector2Int blockCell, out float absSliceDepth)
        {
            blockCell = default;
            absSliceDepth = float.MaxValue;
            if (sx < 0 || sx >= CurrentMatrixSize || sy < 0 || sy >= SliceFloorRowCount || sz < 0 || sz >= CurrentMatrixSize)
            {
                return false;
            }

            Vector3 fixedSliceLocal = GetStorageCenterInFixedSliceLocal(sx, sy, sz);
            absSliceDepth = Mathf.Abs(fixedSliceLocal.z - FixedSliceCenterLocalZ);
            if (!IsInsideFixedSliceSlab(fixedSliceLocal.z))
            {
                return false;
            }

            int columnOffset = Mathf.Clamp(StorageIndexFromLocalAxis(fixedSliceLocal.x), 0, CurrentMatrixSize - 1);
            blockCell = new Vector2Int(SliceGridMin + columnOffset, SliceGridMin + sy);
            return true;
        }

        public bool SliceBlockHasNoVoxelAbove(int worldX, int worldY)
        {
            if (voxelData == null)
            {
                return true;
            }

            if (!TryGetStorageForSliceBlockCell(worldX, worldY, out int sx, out int sy, out int sz))
            {
                return false;
            }

            if (sy >= SliceFloorRowCount - 1)
            {
                return true;
            }

            return !voxelData[sx, sy + 1, sz];
        }

        /// <summary>
        /// 将切面内的地块格（与 2D 地图块坐标一致）映射到体素存储索引 (sx,sy,sz)。
        /// </summary>
        public bool TryGetStorageForSliceBlockCell(int worldX, int worldY, out int sx, out int sy, out int sz)
        {
            sx = sy = sz = 0;
            int targetColumnOffset = worldX - SliceGridMin;
            int targetY = worldY - SliceGridMin;
            if (targetColumnOffset < 0 || targetColumnOffset >= SliceMapWidth || targetY < 0 || targetY >= SliceFloorRowCount || voxelData == null)
            {
                return false;
            }

            bool found = false;
            float bestDepth = float.MaxValue;
            for (int x = 0; x < CurrentMatrixSize; x++)
            {
                for (int z = 0; z < CurrentMatrixSize; z++)
                {
                    if (!voxelData[x, targetY, z])
                    {
                        continue;
                    }

                    if (!TryMapStorageToFixedSliceBlockCell(x, targetY, z, out Vector2Int blockCell, out float absSliceDepth))
                    {
                        continue;
                    }

                    if (blockCell.x != worldX || blockCell.y != worldY || absSliceDepth >= bestDepth)
                    {
                        continue;
                    }

                    bestDepth = absSliceDepth;
                    sx = x;
                    sy = targetY;
                    sz = z;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// 当前采样切片上的地块格 (worldX, worldY)（y 为地块层 0..2）是否与 3D 偏好出生/目标体素重合且该体素有实体；
        /// 与 <see cref="RefreshVoxelColorsFromSliceMap"/> 中偏好高亮一致，供左屏贴色。
        /// </summary>
        public bool TryGetSliceBlockTint(int worldX, int worldY, out bool spawnTint, out bool goalTint)
        {
            spawnTint = false;
            goalTint = false;
            if (mapper == null || voxelData == null)
            {
                return false;
            }

            if (worldY < SliceGridMin || worldY > SliceGridMin + SliceFloorRowCount - 1)
            {
                return false;
            }

            if (!TryGetStorageForSliceBlockCell(worldX, worldY, out int sx, out int sy, out int sz))
            {
                return false;
            }

            if (!voxelData[sx, sy, sz])
            {
                return false;
            }

            Vector3Int ps = mapper.preferredSpawnVoxel;
            Vector3Int pg = mapper.preferredGoalVoxel;
            if (sx == pg.x && sy == pg.y && sz == pg.z)
            {
                goalTint = true;
            }
            else if (sx == ps.x && sy == ps.y && sz == ps.z)
            {
                spawnTint = true;
            }

            return spawnTint || goalTint;
        }

        /// <summary>
        /// 3D 矩阵体素存储坐标 (sx,sy,sz)（与场景中 <c>Voxel_x_y_z</c> 一致）→ 当前旋转与切片深度下左屏地块格 (worldX, worldY)。
        /// 仅当该体素处于当前可见切片（viewZ 与 <see cref="SliceSampledZ"/> 一致）时可映射。
        /// </summary>
        public bool TryStorageToSliceBlockCell(int sx, int sy, int sz, out Vector2Int blockCell)
        {
            return TryMapStorageToFixedSliceBlockCell(sx, sy, sz, out blockCell, out _);
        }

        /// <summary>
        /// 体素仅用中性灰；出生/目标高亮优先使用 <see cref="MatrixSliceMapper"/> 的偏好体素（随 visualRoot 旋转、平移），
        /// 与当前切面地图格子解耦；仅当偏好无效时再按切片地图回退。
        /// </summary>
        public void RefreshVoxelColorsFromSliceMap(CellType[,] cells)
        {
            if (visualRoot == null || voxelData == null)
            {
                return;
            }

            ForEachVoxelIndex((x, y, z) =>
            {
                if (!voxelData[x, y, z])
                {
                    return;
                }

                Transform t = FindVoxelTransform(x, y, z);
                if (t == null)
                {
                    return;
                }

                SetVoxelRendererColor(t, VoxelNeutralColor);
            });

            bool spawnFromPreferred = false;
            bool goalFromPreferred = false;
            Vector3Int preferredSpawn = Vector3Int.zero;
            Vector3Int preferredGoal = Vector3Int.zero;
            if (mapper != null)
            {
                preferredSpawn = mapper.preferredSpawnVoxel;
                if (TryHighlightVoxelStorage(preferredSpawn.x, preferredSpawn.y, preferredSpawn.z, VoxelSpawnHighlightColor))
                {
                    spawnFromPreferred = true;
                }

                preferredGoal = mapper.preferredGoalVoxel;
                if (TryHighlightVoxelStorage(preferredGoal.x, preferredGoal.y, preferredGoal.z, VoxelGoalHighlightColor))
                {
                    goalFromPreferred = true;
                }

                if (spawnFromPreferred && goalFromPreferred &&
                    preferredSpawn.x == preferredGoal.x && preferredSpawn.y == preferredGoal.y && preferredSpawn.z == preferredGoal.z)
                {
                    Transform one = FindVoxelTransform(preferredGoal.x, preferredGoal.y, preferredGoal.z);
                    if (one != null && one.gameObject.activeInHierarchy)
                    {
                        SetVoxelRendererColor(one, VoxelGoalHighlightColor);
                    }
                }
            }

            int spawnX = -1;
            int spawnY = -1;
            int goalX = -1;
            int goalY = -1;
            for (int x = 0; x < SliceMapWidth; x++)
            {
                for (int y = 0; y < SliceMapHeight; y++)
                {
                    CellType c = cells[x, y];
                    if (c == CellType.Spawn)
                    {
                        spawnX = x;
                        spawnY = y;
                    }
                    else if (c == CellType.Goal)
                    {
                        goalX = x;
                        goalY = y;
                    }
                }
            }

            if (!spawnFromPreferred && spawnX >= 0 && TryGetStorageForSliceBlockCell(spawnX, spawnY, out int ssx, out int ssy, out int ssz))
            {
                Transform st = FindVoxelTransform(ssx, ssy, ssz);
                if (st != null && st.gameObject.activeInHierarchy)
                {
                    SetVoxelRendererColor(st, VoxelSpawnHighlightColor);
                }
            }

            if (!goalFromPreferred && goalX >= 0 && TryGetStorageForSliceBlockCell(goalX, goalY, out int gsx, out int gsy, out int gsz))
            {
                Transform gt = FindVoxelTransform(gsx, gsy, gsz);
                if (gt != null && gt.gameObject.activeInHierarchy)
                {
                    SetVoxelRendererColor(gt, VoxelGoalHighlightColor);
                }
            }
        }

        private bool TryHighlightVoxelStorage(int sx, int sy, int sz, Color color)
        {
            if (sx < 0 || sx >= CurrentMatrixSize || sy < 0 || sy >= CurrentMatrixSize || sz < 0 || sz >= CurrentMatrixSize)
            {
                return false;
            }

            if (!voxelData[sx, sy, sz])
            {
                return false;
            }

            Transform t = FindVoxelTransform(sx, sy, sz);
            if (t == null || !t.gameObject.activeInHierarchy)
            {
                return false;
            }

            SetVoxelRendererColor(t, color);
            return true;
        }

        private static void SetVoxelRendererColor(Transform voxelTransform, Color color)
        {
            if (voxelTransform == null)
            {
                return;
            }

            Renderer renderer = voxelTransform.GetComponent<Renderer>();
            LandscapeMatrixRendererColors.SetColor(renderer, color);
        }

        /// <summary>
        /// 在代表 <see cref="CellType.Goal"/> 的体素顶面上放置过关物品；挂在体素下以随矩阵旋转、平移。
        /// </summary>
        public void RefreshGoalItemFromSliceMap(CellType[,] cells)
        {
            EnsureGoalItemVisual();
            if (_goalItemVisual == null)
            {
                return;
            }

            int vsx = -1;
            int vsy = -1;
            int vsz = -1;
            if (mapper != null &&
                TryGetGoalStorageFromPreferredVoxel(mapper.preferredGoalVoxel.x, mapper.preferredGoalVoxel.y, mapper.preferredGoalVoxel.z, out vsx, out vsy, out vsz))
            {
                // 已由偏好体素解析
            }
            else
            {
                int goalX = -1;
                int goalY = -1;
                bool foundGoal = false;
                for (int x = 0; x < SliceMapWidth && !foundGoal; x++)
                {
                    for (int y = 0; y < SliceMapHeight; y++)
                    {
                        if (cells[x, y] == CellType.Goal)
                        {
                            goalX = x;
                            goalY = y;
                            foundGoal = true;
                            break;
                        }
                    }
                }

                if (goalX < 0 || !TryGetStorageForSliceBlockCell(goalX, goalY, out vsx, out vsy, out vsz))
                {
                    _goalItemVisual.gameObject.SetActive(false);
                    _goalItemVisual.SetParent(transform, false);
                    return;
                }
            }

            Transform voxel = FindVoxelTransform(vsx, vsy, vsz);
            if (voxel == null || !voxel.gameObject.activeInHierarchy)
            {
                _goalItemVisual.gameObject.SetActive(false);
                _goalItemVisual.SetParent(transform, false);
                return;
            }

            _goalItemVisual.SetParent(voxel, false);
            float sphereHalfExtent = VoxelVisualSize * 0.32f * 0.5f;
            float lift = VoxelHalfExtent + sphereHalfExtent + 0.02f;
            _goalItemVisual.localPosition = new Vector3(0f, lift, 0f);
            _goalItemVisual.localRotation = Quaternion.identity;
            _goalItemVisual.gameObject.SetActive(true);
            SyncPresentationVisualsFromLogicalRoot();
        }

        /// <summary>偏好目标体素在存在且可见时用于 3D 过关物；不要求落在当前采样切层。</summary>
        private bool TryGetGoalStorageFromPreferredVoxel(int sx, int sy, int sz, out int ox, out int oy, out int oz)
        {
            ox = oy = oz = 0;
            if (voxelData == null || sx < 0 || sx >= CurrentMatrixSize || sy < 0 || sy >= CurrentMatrixSize || sz < 0 || sz >= CurrentMatrixSize)
            {
                return false;
            }

            if (!voxelData[sx, sy, sz])
            {
                return false;
            }

            ox = sx;
            oy = sy;
            oz = sz;
            return true;
        }

        private void EnsureGoalItemVisual()
        {
            if (_goalItemVisual != null)
            {
                return;
            }

            const string goalName = "GoalItem3D";
            Transform existing = transform.Find(goalName);
            if (existing != null)
            {
                _goalItemVisual = existing;
                return;
            }

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = goalName;
            Object.Destroy(sphere.GetComponent<Collider>());
            Renderer renderer = sphere.GetComponent<Renderer>();
            LandscapeMatrixRendererColors.SetColor(renderer, new Color(0.2f, 0.92f, 0.55f, 1f));

            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * (VoxelVisualSize * 0.32f);
            _goalItemVisual = sphere.transform;
            sphere.SetActive(false);
        }

        /// <summary>体素存储索引 (sx,sz) 转为视图水平面索引 (vx,vz)，与 ViewXZToStorageXZ 互逆。</summary>
        private Vector2Int RotateStorageToViewXZ(int storageX, int storageZ, int rotationStep)
        {
            int last = CurrentMatrixSize - 1;
            return (rotationStep % 4) switch
            {
                0 => new Vector2Int(storageX, storageZ),
                1 => new Vector2Int(last - storageZ, storageX),
                2 => new Vector2Int(last - storageX, last - storageZ),
                _ => new Vector2Int(storageZ, last - storageX)
            };
        }

        public Vector3 GetLocalPositionForStandCell(Vector2Int standCell)
        {
            int floorGridY = standCell.y - 1;
            if (!TryGetStorageForSliceBlockCell(standCell.x, floorGridY, out int sx, out int sy, out int sz))
            {
                return new Vector3(0f, VoxelHalfExtent, 0f);
            }

            Vector3 voxelCenter = new Vector3(GetCenteredAxisCoordinate(sx), sy, GetCenteredAxisCoordinate(sz));
            float feetLocalY = sy + VoxelHalfExtent;
            return new Vector3(voxelCenter.x, feetLocalY, voxelCenter.z);
        }

        public Vector2Int WorldToSliceStandCell(Vector3 feetWorldPosition)
        {
            if (!TryGetSliceStandCellFromFeetWorldPosition(feetWorldPosition, out Vector2Int standCell))
            {
                return Vector2Int.zero;
            }

            return standCell;
        }

        public bool TryGetSliceStandCellFromFeetWorldPosition(Vector3 feetWorldPosition, out Vector2Int standCell)
        {
            return TryMapFeetWorldPositionToSliceInfo(feetWorldPosition, out standCell, out _, requireInsideFixedSlice: true);
        }

        public bool IsFeetInsideFixedSlice(Vector3 feetWorldPosition)
        {
            Vector3 fixedSliceLocal = transform.InverseTransformPoint(feetWorldPosition);
            return IsInsideFixedSliceSlab(fixedSliceLocal.z, Player3DRadius);
        }

        private bool TryMapFeetWorldPositionToSliceInfo(Vector3 feetWorldPosition, out Vector2Int standCell, out Vector3Int storageCoord)
        {
            return TryMapFeetWorldPositionToSliceInfo(feetWorldPosition, out standCell, out storageCoord, requireInsideFixedSlice: false);
        }

        private bool TryMapFeetWorldPositionToSliceInfo(Vector3 feetWorldPosition, out Vector2Int standCell, out Vector3Int storageCoord, bool requireInsideFixedSlice)
        {
            standCell = default;
            storageCoord = default;
            if (visualRoot == null)
            {
                return false;
            }

            Vector3 fixedSliceLocal = transform.InverseTransformPoint(feetWorldPosition);
            if (requireInsideFixedSlice && !IsInsideFixedSliceSlab(fixedSliceLocal.z, Player3DRadius))
            {
                return false;
            }

            int sy = GetViewYFromFeetLocalY(fixedSliceLocal.y);
            int columnOffset = Mathf.Clamp(StorageIndexFromLocalAxis(fixedSliceLocal.x), 0, CurrentMatrixSize - 1);
            int worldX = SliceGridMin + columnOffset;
            int floorGridY = SliceGridMin + sy;
            standCell = new Vector2Int(worldX, floorGridY + 1);

            if (TryGetStorageForSliceBlockCell(worldX, floorGridY, out int sx, out int mappedSy, out int sz))
            {
                storageCoord = new Vector3Int(sx, mappedSy, sz);
                return true;
            }

            return !requireInsideFixedSlice;
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

            if (storageY < 0 || storageY >= CurrentMatrixSize || viewX < 0 || viewX >= CurrentMatrixSize || viewZ < 0 || viewZ >= CurrentMatrixSize)
            {
                return false;
            }

            Vector2Int baseXZ = ViewXZToStorageXZ(viewX, viewZ, RotationStep);
            if (baseXZ.x < 0 || baseXZ.x >= CurrentMatrixSize || baseXZ.y < 0 || baseXZ.y >= CurrentMatrixSize)
            {
                return false;
            }

            filled = voxelData[baseXZ.x, storageY, baseXZ.y];
            return true;
        }

        /// <summary>视图水平面 (viewX, viewZ) → 存储 (sx, sz)。与 <see cref="RotateStorageToViewXZ"/> 互逆。</summary>
        public Vector2Int ViewXZToStorageXZ(int viewX, int viewZ, int rotationStep)
        {
            return RotateInverse(viewX, viewZ, rotationStep);
        }

        private Vector3Int ClampOffset(Vector3Int offset)
        {
            return new Vector3Int(
                0,
                0,
                Mathf.Clamp(offset.z, MinGridOffsetZ, MaxGridOffsetZ));
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
            if (renderer != null && renderer.sharedMaterial != null)
            {
                Material sliceMat = new Material(renderer.sharedMaterial);
                // 提高切面辨识度，避免“看不出当前切的是哪一层”。
                ConfigureSlicePlaneMaterial(sliceMat, new Color(0.15f, 0.92f, 1f, 0.28f));
                renderer.sharedMaterial = sliceMat;
            }

            UpdateSliceVisualizerTransform(quad.transform);
        }

        /// <summary>
        /// 切片参考面固定在矩阵控制器局部空间，不随矩阵旋转/前后平移移动；
        /// 仅作为“绝对固定切片区域”的可视化提示。
        /// </summary>
        private void UpdateSliceVisualizerTransform(Transform sliceTransform)
        {
            if (sliceTransform == null)
            {
                return;
            }

            _cachedSlicePlane = sliceTransform;
            sliceTransform.SetParent(transform, true);
            float sliceScale = CurrentMatrixSize + 1.4f;
            sliceTransform.localPosition = new Vector3(0f, Mathf.Max(1f, (CurrentMatrixSize - 1) * 0.5f), FixedSliceCenterLocalZ);
            sliceTransform.localScale = new Vector3(sliceScale, sliceScale, 1f);
            EnsureSliceCameraCached();

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

            EnsureSliceCameraCached();

            if (_cachedSlicePlane == null || _cachedSliceCamera == null)
            {
                return;
            }

            Transform camTr = _cachedSliceCamera.transform;
            Transform sliceTr = _cachedSlicePlane;
            if (!camTr.hasChanged && !transform.hasChanged && !sliceTr.hasChanged)
            {
                return;
            }

            camTr.hasChanged = false;
            transform.hasChanged = false;
            sliceTr.hasChanged = false;
            AlignSlicePlaneParallelToCamera(_cachedSlicePlane, _cachedSliceCamera);
        }

        /// <summary>当前固定切片区域是否覆盖了偏好目标体素；覆盖时返回其在左侧 2D 中的地块格。</summary>
        public bool TryGetPreferredGoalSliceBlockCell(out Vector2Int blockCell)
        {
            blockCell = default;
            if (mapper == null || voxelData == null)
            {
                return false;
            }

            Vector3Int preferredGoal = mapper.preferredGoalVoxel;
            if (preferredGoal.x < 0 || preferredGoal.x >= CurrentMatrixSize ||
                preferredGoal.y < 0 || preferredGoal.y >= CurrentMatrixSize ||
                preferredGoal.z < 0 || preferredGoal.z >= CurrentMatrixSize)
            {
                return false;
            }

            if (!voxelData[preferredGoal.x, preferredGoal.y, preferredGoal.z])
            {
                return false;
            }

            return TryStorageToSliceBlockCell(preferredGoal.x, preferredGoal.y, preferredGoal.z, out blockCell);
        }

        public bool TryGetPreferredGoalStorageCoord(out Vector3Int storageCoord)
        {
            storageCoord = default;
            if (mapper == null || voxelData == null)
            {
                return false;
            }

            Vector3Int preferredGoal = mapper.preferredGoalVoxel;
            if (preferredGoal.x < 0 || preferredGoal.x >= CurrentMatrixSize ||
                preferredGoal.y < 0 || preferredGoal.y >= CurrentMatrixSize ||
                preferredGoal.z < 0 || preferredGoal.z >= CurrentMatrixSize)
            {
                return false;
            }

            if (!voxelData[preferredGoal.x, preferredGoal.y, preferredGoal.z])
            {
                return false;
            }

            storageCoord = preferredGoal;
            return true;
        }

        public bool IsDebugLoggingEnabled()
        {
            return _enableDebugLogs;
        }

        public bool TryGetPlayerStorageDebugInfo(out Vector2Int standCell, out Vector3Int storageCoord)
        {
            standCell = default;
            storageCoord = default;
            if (player3D == null)
            {
                return false;
            }

            return TryMapFeetWorldPositionToSliceInfo(player3D.GetFeetWorldPosition(), out standCell, out storageCoord);
        }

        private void EnsureSliceCameraCached()
        {
            if (_sliceReferenceCamera != null)
            {
                _cachedSliceCamera = _sliceReferenceCamera;
                return;
            }

            if (_cachedSliceCamera != null)
            {
                return;
            }

            GameObject go = GameObject.Find("Camera_Right3D");
            _cachedSliceCamera = go != null && go.TryGetComponent(out Camera c) ? c : Camera.main;
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

        /// <summary>矩阵偏移/旋转变化或外部（如 Mapper 偏好体素）修改后，统一刷新切面、体素色与 3D 目标物。</summary>
        public void NotifyMatrixStateChanged()
        {
            NotifyStateChanged();
        }

        private void NotifyStateChanged()
        {
            Vector3 targetLocalPosition = new Vector3(0f, 0f, GridOffset.z * MatrixMoveStep);
            Quaternion targetLocalRotation = Quaternion.Euler(0f, RotationStep * 90f, 0f);

            if (visualRoot != null)
            {
                visualRoot.localPosition = targetLocalPosition;
                visualRoot.localRotation = targetLocalRotation;
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

            // 融合判定必须在 mapper.ApplyMatrixState / SnapToStandCell 之前：
            // 后者会把 2D 玩家重定位到"当前列最低立足点"、再把 3D 玩家搬过去，之后再做 OverlapBox 永远看不到重叠。
            // 这里用"旋转/平移后的新体素位置 + 玩家旧世界位置"做检测：真正有方块挤进玩家体内时才返回 true。
            bool fusionDetected = false;
            if (_checkFusionAfterNotify)
            {
                _checkFusionAfterNotify = false;
                if (!_levelClearedFrom3DOverlap && player3D != null && player3D.IsOverlappingMatrixVoxels())
                {
                    if (_enableDebugLogs)
                    {
                        Debug.Log("[LandscapeMatrix Debug][3DFusion] 角色与方块重合，触发融合弹窗。");
                    }

                    FusionPresenter.NotifyFusionDetected();
                    fusionDetected = true;
                }
            }

            mapper?.ApplyMatrixState();

            // 触发了融合弹窗就不要再 SnapToStandCell：保留玩家被挤入方块的姿态，让玩家看到"融合"并可撤销；
            // 一旦 Snap 到了新的立足格，玩家会瞬移到安全位置，违背弹窗语义。
            if (!fusionDetected
                && boundPlayer != null && player3D != null
                && boundPlayer.ExternalDrive && !_suppressPlayerSnapOnNotify)
            {
                player3D.SnapToStandCell(boundPlayer.GetCurrentCell());
            }

            if (!fusionDetected)
            {
                TryNotifyLevelClearFrom3DGoalOverlap();
            }

            LogDebugSnapshot(_pendingDebugReason);
            _pendingDebugReason = "StateChanged";

            EnsurePresentationVisuals();
            SyncPresentationVisualsFromLogicalRoot();
            ApplyPresentationPose(targetLocalPosition, targetLocalRotation);
        }

        /// <summary>矩阵操作（尤其旋转/平移）后，若 3D 目标物与玩家发生重合则判定通关。</summary>
        private void TryNotifyLevelClearFrom3DGoalOverlap()
        {
            if (_levelClearedFrom3DOverlap || player3D == null || _goalItemVisual == null || !_goalItemVisual.gameObject.activeInHierarchy)
            {
                return;
            }

            Renderer goalRenderer = _goalItemVisual.GetComponent<Renderer>();
            CharacterController playerController = player3D.GetComponent<CharacterController>();
            if (goalRenderer == null || playerController == null)
            {
                return;
            }

            if (!goalRenderer.bounds.Intersects(playerController.bounds))
            {
                return;
            }

            if (_enableDebugLogs)
            {
                Debug.Log($"[LandscapeMatrix Debug][3DGoalClear] playerBounds={playerController.bounds} goalBounds={goalRenderer.bounds}");
            }

            _levelClearedFrom3DOverlap = true;
            LevelClearPresenter.Notify2DLevelCleared();
        }

        public bool IsPlayerOverlappingGoalIn3D()
        {
            if (player3D == null || _goalItemVisual == null || !_goalItemVisual.gameObject.activeInHierarchy)
            {
                return false;
            }

            Renderer goalRenderer = _goalItemVisual.GetComponent<Renderer>();
            CharacterController playerController = player3D.GetComponent<CharacterController>();
            if (goalRenderer == null || playerController == null)
            {
                return false;
            }

            return goalRenderer.bounds.Intersects(playerController.bounds);
        }

        private Playfield2D GetCachedPlayfield()
        {
            if (_cachedPlayfield == null)
            {
                _cachedPlayfield = Object.FindFirstObjectByType<Playfield2D>();
            }

            return _cachedPlayfield;
        }

        private void LogDebugSnapshot(string reason)
        {
            if (!_enableDebugLogs)
            {
                return;
            }

            var sb = new StringBuilder(768);
            sb.Append("[LandscapeMatrix Debug][MatrixState] reason=").Append(reason)
              .Append(" offset=").Append(GridOffset)
              .Append(" rotationStep=").Append(RotationStep)
              .Append(" sliceSampledZ=").Append(SliceSampledZ);

            sb.Append(" | sliceBlocks=");
            bool hasSliceBlocks = false;
            for (int sx = 0; sx < CurrentMatrixSize; sx++)
            {
                for (int sy = 0; sy < CurrentMatrixSize; sy++)
                {
                    for (int sz = 0; sz < CurrentMatrixSize; sz++)
                    {
                        if (voxelData == null || !voxelData[sx, sy, sz])
                        {
                            continue;
                        }

                        if (!TryStorageToSliceBlockCell(sx, sy, sz, out Vector2Int blockCell))
                        {
                            continue;
                        }

                        sb.Append(hasSliceBlocks ? "; " : string.Empty)
                          .Append("slice(").Append(blockCell.x).Append(',').Append(blockCell.y).Append(')')
                          .Append("<-storage(").Append(sx).Append(',').Append(sy).Append(',').Append(sz).Append(')');
                        hasSliceBlocks = true;
                    }
                }
            }
            if (!hasSliceBlocks)
            {
                sb.Append("none");
            }

            if (TryGetPlayerStorageDebugInfo(out Vector2Int playerStandCell, out Vector3Int playerStorage))
            {
                sb.Append(" | playerStand=").Append(playerStandCell)
                  .Append(" playerBlockSlice=(").Append(playerStandCell.x).Append(',').Append(playerStandCell.y - 1).Append(')')
                  .Append(" playerStorage=").Append(playerStorage);
            }
            else
            {
                sb.Append(" | playerStorage=unavailable");
            }

            if (TryGetPreferredGoalStorageCoord(out Vector3Int goalStorage))
            {
                sb.Append(" | goalStorage=").Append(goalStorage);
                if (TryGetPreferredGoalSliceBlockCell(out Vector2Int goalSliceBlock))
                {
                    sb.Append(" goalSliceBlock=").Append(goalSliceBlock);
                }
                else
                {
                    sb.Append(" goalSliceBlock=out_of_slice");
                }
            }
            else
            {
                sb.Append(" | goalStorage=invalid_or_hidden");
            }

            Playfield2D playfield = GetCachedPlayfield();
            bool detected2D = playfield != null && playfield.IsPlayerOverlappingGoalObject();
            bool detected3D = IsPlayerOverlappingGoalIn3D();
            sb.Append(" | goalDetected2D=").Append(detected2D)
              .Append(" goalDetected3D=").Append(detected3D);

            Debug.Log(sb.ToString());
        }

        private bool[,,] BuildVoxelData()
        {
            int n = CurrentMatrixSize;
            bool[,,] data = new bool[n, n, n];
            // bool[,,] 默认全 false；仅当 defaultVoxelVisible 为 true 时才需要显式置 true。
            if (defaultVoxelVisible)
            {
                for (int x = 0; x < n; x++)
                {
                    for (int y = 0; y < n; y++)
                    {
                        for (int z = 0; z < n; z++)
                        {
                            data[x, y, z] = true;
                        }
                    }
                }
            }

            if (hiddenVoxels != null)
            {
                for (int i = 0; i < hiddenVoxels.Length; i++)
                {
                    VoxelCoord coord = hiddenVoxels[i];
                    if (coord.x < 0 || coord.x >= n ||
                        coord.y < 0 || coord.y >= n ||
                        coord.z < 0 || coord.z >= n)
                    {
                        continue;
                    }
                    data[coord.x, coord.y, coord.z] = false;
                }
            }
            return data;
        }

        private Vector2Int RotateInverse(int x, int z, int rotationStep)
        {
            int last = CurrentMatrixSize - 1;
            return (rotationStep % 4) switch
            {
                0 => new Vector2Int(x, z),
                1 => new Vector2Int(z, last - x),
                2 => new Vector2Int(last - x, last - z),
                _ => new Vector2Int(last - z, x)
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
            LandscapeMatrixRendererColors.SetColor(renderer, new Color(1f, 0.9f, 0.25f, 1f));
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
            float gridHalf = (CurrentMatrixSize - 1) * 0.5f + half;
            float t = 0.18f;
            float wallHeight = Mathf.Max(5.5f, CurrentMatrixSize + 2.5f);
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

        private void EnsurePresentationVisuals()
        {
            if (!Application.isPlaying || visualRoot == null)
            {
                return;
            }

            if (_presentationRoot == null)
            {
                _presentationRoot = FindDirectChildByName(transform, MatrixPresentationChildName);
            }

            if (_presentationRoot == null)
            {
                GameObject presentation = new GameObject(MatrixPresentationChildName);
                _presentationRoot = presentation.transform;
                _presentationRoot.SetParent(transform, false);
            }

            ForEachVoxelIndex((x, y, z) =>
            {
                Transform logicalVoxel = FindVoxelTransform(x, y, z);
                if (logicalVoxel == null)
                {
                    return;
                }

                Transform presentationVoxel = FindDirectChildByName(_presentationRoot, VoxelObjectName(x, y, z));
                if (presentationVoxel == null)
                {
                    GameObject visualOnly = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    visualOnly.name = VoxelObjectName(x, y, z);
                    presentationVoxel = visualOnly.transform;
                    presentationVoxel.SetParent(_presentationRoot, false);

                    Collider visualCollider = visualOnly.GetComponent<Collider>();
                    if (visualCollider != null)
                    {
                        visualCollider.enabled = false;
                        Object.Destroy(visualCollider);
                    }
                }

                CopyRendererVisualState(logicalVoxel, presentationVoxel);
            });

            SetLogicalVoxelRenderersVisible(!_enablePresentationMotion);
            _presentationRoot.gameObject.SetActive(_enablePresentationMotion);

            AttachExtraPresentationProps();
        }

        /// <summary>
        /// 把 <see cref="_extraPresentationProps"/> 里登记的装饰物 reparent 到 <see cref="_presentationRoot"/> 下，
        /// 这样它们会随 MatrixPresentation 一起平滑旋转/平移，而不是停留在不旋转的 Matrix 根上产生"反向旋转"错觉。
        /// 保留世界位姿，编辑期摆放的位置不会丢。
        /// </summary>
        private void AttachExtraPresentationProps()
        {
            if (_presentationRoot == null || _extraPresentationProps == null)
            {
                return;
            }

            for (int i = 0; i < _extraPresentationProps.Length; i++)
            {
                Transform prop = _extraPresentationProps[i];
                if (prop == null || prop == _presentationRoot || prop == transform)
                {
                    continue;
                }

                if (prop.parent == _presentationRoot)
                {
                    _attachedPresentationProps.Add(prop);
                    continue;
                }

                if (_attachedPresentationProps.Contains(prop))
                {
                    // 已经被挂过，但运行期被外部再次 reparent 走了，这里尊重外部意愿不强行抢回来。
                    continue;
                }

                prop.SetParent(_presentationRoot, worldPositionStays: true);
                _attachedPresentationProps.Add(prop);
            }
        }

        private void SyncPresentationVisualsFromLogicalRoot()
        {
            if (!Application.isPlaying || !_enablePresentationMotion || visualRoot == null)
            {
                return;
            }

            EnsurePresentationVisuals();
            if (_presentationRoot == null)
            {
                return;
            }

            ForEachVoxelIndex((x, y, z) =>
            {
                Transform logicalVoxel = FindVoxelTransform(x, y, z);
                Transform presentationVoxel = FindDirectChildByName(_presentationRoot, VoxelObjectName(x, y, z));
                if (logicalVoxel == null || presentationVoxel == null)
                {
                    return;
                }

                CopyRendererVisualState(logicalVoxel, presentationVoxel);
            });
        }

        private static void CopyRendererVisualState(Transform source, Transform target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.gameObject.SetActive(source.gameObject.activeSelf);
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;

            Renderer sourceRenderer = source.GetComponent<Renderer>();
            Renderer targetRenderer = target.GetComponent<Renderer>();
            if (sourceRenderer == null || targetRenderer == null)
            {
                return;
            }

            targetRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            targetRenderer.enabled = source.gameObject.activeSelf;

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            sourceRenderer.GetPropertyBlock(block);
            targetRenderer.SetPropertyBlock(block);
        }

        private void SetLogicalVoxelRenderersVisible(bool visible)
        {
            if (visualRoot == null)
            {
                return;
            }

            ForEachVoxelIndex((x, y, z) =>
            {
                Transform logicalVoxel = FindVoxelTransform(x, y, z);
                if (logicalVoxel == null || !logicalVoxel.TryGetComponent(out Renderer renderer))
                {
                    return;
                }

                renderer.enabled = visible && logicalVoxel.gameObject.activeSelf;
            });
        }

        private void ApplyPresentationPose(Vector3 targetLocalPosition, Quaternion targetLocalRotation)
        {
            if (!Application.isPlaying || !_enablePresentationMotion || _presentationRoot == null)
            {
                ApplyPresentationPoseImmediately(targetLocalPosition, targetLocalRotation);
                return;
            }

            if (!_presentationPoseInitialized || _presentationMotionDuration <= 0.001f)
            {
                ApplyPresentationPoseImmediately(targetLocalPosition, targetLocalRotation);
                return;
            }

            Vector3 startLocalPosition = _presentationRoot.localPosition;
            Quaternion startLocalRotation = _presentationRoot.localRotation;
            if (_presentationMotionRoutine != null)
            {
                StopCoroutine(_presentationMotionRoutine);
            }

            _presentationMotionRoutine = StartCoroutine(AnimatePresentationPose(
                startLocalPosition,
                startLocalRotation,
                targetLocalPosition,
                targetLocalRotation));
        }

        private void ApplyPresentationPoseImmediately(Vector3 localPosition, Quaternion localRotation)
        {
            if (_presentationRoot == null)
            {
                return;
            }

            if (_presentationMotionRoutine != null)
            {
                StopCoroutine(_presentationMotionRoutine);
                _presentationMotionRoutine = null;
            }

            _presentationRoot.localPosition = localPosition;
            _presentationRoot.localRotation = localRotation;
            _presentationDisplayedLocalPosition = localPosition;
            _presentationDisplayedLocalRotation = localRotation;
            _presentationPoseInitialized = true;
        }

        private IEnumerator AnimatePresentationPose(
            Vector3 startLocalPosition,
            Quaternion startLocalRotation,
            Vector3 targetLocalPosition,
            Quaternion targetLocalRotation)
        {
            float elapsed = 0f;
            while (elapsed < _presentationMotionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _presentationMotionDuration);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                Vector3 currentLocalPosition = Vector3.LerpUnclamped(startLocalPosition, targetLocalPosition, eased);
                Quaternion currentLocalRotation = Quaternion.Slerp(startLocalRotation, targetLocalRotation, eased);

                _presentationRoot.localPosition = currentLocalPosition;
                _presentationRoot.localRotation = currentLocalRotation;
                _presentationDisplayedLocalPosition = currentLocalPosition;
                _presentationDisplayedLocalRotation = currentLocalRotation;
                yield return null;
            }

            _presentationRoot.localPosition = targetLocalPosition;
            _presentationRoot.localRotation = targetLocalRotation;
            _presentationDisplayedLocalPosition = targetLocalPosition;
            _presentationDisplayedLocalRotation = targetLocalRotation;
            _presentationMotionRoutine = null;
        }
    }
}
