using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LandscapeMatrix
{
    public class MatrixController : MonoBehaviour
    {
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
            for (int s = 0; s < MatrixSize; s++)
            {
                float center = s - 1f;
                if (localCoord >= center - VoxelHalfExtent && localCoord <= center + VoxelHalfExtent)
                {
                    return s;
                }
            }

            int nearest = 1;
            float best = float.MaxValue;
            for (int s = 0; s < MatrixSize; s++)
            {
                float d = Mathf.Abs(localCoord - (s - 1f));
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

        /// <summary>与 2D 采样一致的切片深度索引（视图 Z，0..2）。</summary>
        public int SliceSampledZ => Mathf.Clamp(1 - GridOffset.z, 0, 2);

        /// <summary>切片参考面可视化（无碰撞 Quad）。挂在矩阵控制器上，不随体素根的旋转/前后平移变化。</summary>
        private const string SlicePlaneVisualName = "SlicePlaneVisual";

        private void Awake()
        {
            if (visualRoot == null)
            {
                Transform existingRoot = transform.Find("MatrixVisual");
                if (existingRoot != null)
                {
                    visualRoot = existingRoot;
                }
            }

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
                DestroyVoxelCubeChildren();
                CreateAllVoxelCubes();
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

            EnsureAirWalls();
        }

        public void BuildMatrixVisual()
        {
            transform.position = new Vector3(10f, 0f, 0f);
            visualRoot = new GameObject("MatrixVisual").transform;
            visualRoot.SetParent(transform, false);
            voxelData = BuildVoxelData();
            GridOffset = ClampOffset(initialGridOffset);
            RotationStep = initialRotationStep % 4;

            CreateAllVoxelCubes();

            CreateSliceVisualizer();
            EnsureAirWalls();
        }

        private void OnValidate()
        {
            voxelData = BuildVoxelData();
            if (visualRoot == null)
            {
                return;
            }

            DestroyVoxelCubeChildren();
            CreateAllVoxelCubes();

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

        private void DestroyVoxelCubeChildren()
        {
            if (visualRoot == null)
            {
                return;
            }

            for (int i = visualRoot.childCount - 1; i >= 0; i--)
            {
                Transform c = visualRoot.GetChild(i);
                if (c == null || !c.name.StartsWith("Voxel_", System.StringComparison.Ordinal))
                {
                    continue;
                }

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

        private void CreateAllVoxelCubes()
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
                        bool isFilled = voxelData[x, y, z];
                        if (!isFilled && hiddenVoxelStrategy == HiddenVoxelStrategy.DoNotGenerate)
                        {
                            continue;
                        }

                        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        cube.name = $"Voxel_{x}_{y}_{z}";
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

                        if (!isFilled && hiddenVoxelStrategy == HiddenVoxelStrategy.GenerateDisableVisualAndCollision)
                        {
                            Renderer renderer = cube.GetComponent<Renderer>();
                            Collider collider = cube.GetComponent<Collider>();
                            if (renderer != null)
                            {
                                renderer.enabled = false;
                            }

                            if (collider != null)
                            {
                                collider.enabled = false;
                            }
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

        /// <summary>
        /// 切片平面内沿 viewX 增大方向、投影到世界水平面后的单位向量；随 <see cref="RotationStep"/> 一起旋转。
        /// 使用 0→1→2 三列差分回退，避免单段退化；不再使用 storage 的 +X 作为回退（旋转后会与切片切向不一致）。
        /// </summary>
        public Vector3 GetWorldSliceTangentViewX(int viewY, int viewZ)
        {
            if (visualRoot == null)
            {
                return Vector3.right;
            }

            viewY = Mathf.Clamp(viewY, 0, MatrixSize - 1);
            viewZ = Mathf.Clamp(viewZ, 0, MatrixSize - 1);

            Vector3 p0 = visualRoot.TransformPoint(VoxelCenterLocalFromView(0, viewY, viewZ));
            Vector3 p1 = visualRoot.TransformPoint(VoxelCenterLocalFromView(1, viewY, viewZ));
            Vector3 p2 = visualRoot.TransformPoint(VoxelCenterLocalFromView(2, viewY, viewZ));

            Vector3 d01 = Vector3.ProjectOnPlane(p1 - p0, Vector3.up);
            if (d01.sqrMagnitude > 1e-10f)
            {
                return d01.normalized;
            }

            Vector3 d12 = Vector3.ProjectOnPlane(p2 - p1, Vector3.up);
            if (d12.sqrMagnitude > 1e-10f)
            {
                return d12.normalized;
            }

            Vector3 d02 = Vector3.ProjectOnPlane(p2 - p0, Vector3.up);
            return d02.sqrMagnitude > 1e-10f ? d02.normalized : Vector3.right;
        }

        /// <summary>
        /// 切片平面内沿 viewZ 增大方向、投影到世界水平面后的单位向量（与 viewX 切向共同张成切片上的行走平面投影）。
        /// </summary>
        public Vector3 GetWorldSliceTangentViewZ(int viewX, int viewY)
        {
            if (visualRoot == null)
            {
                return Vector3.forward;
            }

            viewX = Mathf.Clamp(viewX, 0, MatrixSize - 1);
            viewY = Mathf.Clamp(viewY, 0, MatrixSize - 1);

            Vector3 p0 = visualRoot.TransformPoint(VoxelCenterLocalFromView(viewX, viewY, 0));
            Vector3 p1 = visualRoot.TransformPoint(VoxelCenterLocalFromView(viewX, viewY, 1));
            Vector3 p2 = visualRoot.TransformPoint(VoxelCenterLocalFromView(viewX, viewY, 2));

            Vector3 d01 = Vector3.ProjectOnPlane(p1 - p0, Vector3.up);
            if (d01.sqrMagnitude > 1e-10f)
            {
                return d01.normalized;
            }

            Vector3 d12 = Vector3.ProjectOnPlane(p2 - p1, Vector3.up);
            if (d12.sqrMagnitude > 1e-10f)
            {
                return d12.normalized;
            }

            Vector3 d02 = Vector3.ProjectOnPlane(p2 - p0, Vector3.up);
            return d02.sqrMagnitude > 1e-10f ? d02.normalized : Vector3.forward;
        }

        /// <summary>同 <see cref="GetWorldSliceTangentViewX"/>。</summary>
        public Vector3 GetWorldHorizontalAlongViewX(int viewY, int viewZ)
        {
            return GetWorldSliceTangentViewX(viewY, viewZ);
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
            int vx = standCell.x - SliceGridMin;
            int sy = floorGridY - SliceGridMin;
            int vz = SliceSampledZ;
            if (vx < 0 || vx > 2 || sy < 0 || sy > 2)
            {
                return new Vector3(0f, VoxelHalfExtent, 0f);
            }

            // 脚底 XZ 取当前采样深度 vz 上体素中心，与切片 viewZ 一致（对齐 SliceSampledZ）。
            Vector3 voxelCenter = VoxelCenterLocalFromView(vx, sy, vz);
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
            return new Vector2Int(SliceGridMin + bestVx, standY);
        }

        /// <param name="viewX">切片平面上的列 0..2（与左侧 7×7 中心块列对应）。</param>
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

            sliceTransform.SetParent(transform, true);
            sliceTransform.localPosition = new Vector3(0f, 1f, 0f);
            sliceTransform.localScale = new Vector3(4.4f, 4.4f, 1f);
            AlignSlicePlaneParallelToCamera(sliceTransform);
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

            Transform slice = FindSlicePlaneVisualTransform();
            if (slice != null)
            {
                AlignSlicePlaneParallelToCamera(slice);
            }
        }

        /// <summary>
        /// 使 Quad 在水平面内朝向相机（仅绕世界 Y），世界欧拉 X=0，避免随相机俯仰倾斜。
        /// </summary>
        private static void AlignSlicePlaneParallelToCamera(Transform sliceTransform)
        {
            if (sliceTransform == null)
            {
                return;
            }

            Camera cam = ResolveSliceReferenceCamera();
            if (cam == null)
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

        private static Camera ResolveSliceReferenceCamera()
        {
            GameObject go = GameObject.Find("Camera_Right3D");
            if (go != null && go.TryGetComponent(out Camera c))
            {
                return c;
            }

            return Camera.main;
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

        /// <summary>
        /// 在体素区域四周放置无渲染盒体碰撞，防止角色从侧面掉落（随 visualRoot 旋转）。
        /// </summary>
        private void EnsureAirWalls()
        {
            if (visualRoot == null)
            {
                return;
            }

            Transform container = visualRoot.Find("AirWalls");
            if (container != null)
            {
                Object.Destroy(container.gameObject);
            }

            GameObject root = new GameObject("AirWalls");
            root.transform.SetParent(visualRoot, false);

            float half = VoxelHalfExtent;
            float gridHalf = (MatrixSize - 1) * 0.5f + half;
            float t = 0.18f;
            float wallHeight = 5.5f;
            float span = gridHalf * 2f + t * 2f;

            void AddWall(string name, Vector3 localCenter, Vector3 size)
            {
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = name;
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
