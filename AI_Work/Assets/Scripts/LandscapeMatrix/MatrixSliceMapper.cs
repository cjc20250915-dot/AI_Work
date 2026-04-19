using System.Collections.Generic;
using UnityEngine;

namespace LandscapeMatrix
{
    /// <summary>先于 <see cref="MatrixController"/> 执行 Start，确保切面地图与 2D 棋盘在 BindPlayer/NotifyStateChanged 前已就绪。</summary>
    [DefaultExecutionOrder(-100)]
    public class MatrixSliceMapper : MonoBehaviour
    {
        private const int SliceSize = 3;
        private static int SliceMin => MatrixController.SliceGridMin;
        private static int SliceMax => SliceMin + SliceSize - 1;

        [Header("出生点 / 目标（3D 矩阵体素坐标，与右侧 Voxel_x_y_z 一致：x,y,z ∈ [0,2]）")]
        [Tooltip("3D 出生高亮与左屏出生格优先对应的体素。左屏格仍须落在当前切面且为地板候选；否则左屏按从左到右、从下到上选。3D 体素色始终跟此项（有体素时）。")]
        public Vector3Int preferredSpawnVoxel = new Vector3Int(0, 0, 1);

        [Tooltip("3D 目标物与体素绿色高亮始终使用该体素（有体素且未隐藏时），不随切片前后移动而改变。左屏绿色目标格仍须该体素落在当前切面上，否则左屏按从右到左、从下到上选。")]
        public Vector3Int preferredGoalVoxel = new Vector3Int(2, 0, 1);

        private Playfield2D _playfield;
        private MatrixController _matrix;
        private bool _runtimeReady;

        private void Start()
        {
            EnsureRuntimeReady();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            MatrixController m = _matrix != null ? _matrix : Object.FindFirstObjectByType<MatrixController>();
            if (m != null)
            {
                m.NotifyMatrixStateChanged();
            }
        }

        /// <summary>供左屏 <see cref="Playfield2D"/> 等与 3D 高亮对齐时取矩阵引用。</summary>
        public MatrixController GetMatrixController() => _matrix;

        public void Initialize(Playfield2D playfield, MatrixController matrix)
        {
            _playfield = playfield;
            _matrix = matrix;
            // 必须在 MatrixController.Initialize 之前置位，否则 NotifyStateChanged -> ApplyMatrixState
            // 会再次进入 EnsureRuntimeReady 并形成无限递归。
            _runtimeReady = true;
            // 先初始化左屏棋盘（需已能 BuildCurrentSliceMap：_matrix 已赋值），再通知矩阵刷新。
            if (_playfield != null && !_playfield.IsInitialized())
            {
                _playfield.Initialize(this);
            }

            _matrix.Initialize(this);
        }

        public CellType[,] BuildCurrentSliceMap() => BuildBaseMap();

        public void ApplyMatrixState()
        {
            EnsureRuntimeReady();

            CellType[,] map = BuildCurrentSliceMap();
            if (_playfield != null)
            {
                _playfield.RefreshMap(map);
            }

            if (_matrix != null)
            {
                _matrix.RefreshVoxelColorsFromSliceMap(map);
                _matrix.RefreshGoalItemFromSliceMap(map);
            }
        }

        private void EnsureRuntimeReady()
        {
            if (_runtimeReady)
            {
                return;
            }

            if (_playfield == null)
            {
                _playfield = Object.FindFirstObjectByType<Playfield2D>();
            }
            if (_matrix == null)
            {
                _matrix = Object.FindFirstObjectByType<MatrixController>();
            }

            if (_playfield == null || _matrix == null)
            {
                return;
            }

            if (!_playfield.IsInitialized())
            {
                _playfield.Initialize(this);
            }

            // 必须在 Initialize 之前置位，避免 MatrixController.NotifyStateChanged 再次进入本方法导致栈溢出。
            _runtimeReady = true;
            _matrix.Initialize(this);
        }

        private CellType[,] BuildBaseMap()
        {
            CellType[,] cells = new CellType[MatrixController.SliceMapWidth, MatrixController.SliceMapHeight];

            for (int x = 0; x < MatrixController.SliceMapWidth; x++)
            {
                for (int y = 0; y < MatrixController.SliceMapHeight; y++)
                {
                    cells[x, y] = CellType.Empty;
                }
            }

            if (_matrix == null)
            {
                // 初始化早期兜底，避免因矩阵未绑定导致场景构建中断。
                cells[0, 0] = CellType.Spawn;
                cells[1, 0] = CellType.Floor;
                cells[2, 0] = CellType.Goal;
                return cells;
            }

            // 固定切片规则：扫描全部体素，只把当前世界空间切片实际覆盖到的方块映射到左侧 2D。
            for (int sx = 0; sx < SliceSize; sx++)
            {
                for (int sy = 0; sy < SliceSize; sy++)
                {
                    for (int sz = 0; sz < SliceSize; sz++)
                    {
                        if (_matrix.voxelData == null || !_matrix.voxelData[sx, sy, sz])
                        {
                            continue;
                        }

                        if (!_matrix.TryStorageToSliceBlockCell(sx, sy, sz, out Vector2Int blockCell))
                        {
                            continue;
                        }

                        cells[blockCell.x, blockCell.y] = CellType.Floor;
                    }
                }
            }

            // Spawn / Goal：仅切面内地块；且对应 3D 体素顶面无上层方块（顶格 y=2 视为满足）。必要时回退为切面内任意地板。
            PlaceSpawnAndGoal(cells);

            return cells;
        }

        private void PlaceSpawnAndGoal(CellType[,] cells)
        {
            var floors = new List<Vector2Int>();
            for (int x = SliceMin; x <= SliceMax; x++)
            {
                for (int y = SliceMin; y <= SliceMax; y++)
                {
                    if (cells[x, y] == CellType.Floor)
                    {
                        floors.Add(new Vector2Int(x, y));
                    }
                }
            }

            var openTop = new List<Vector2Int>();
            if (_matrix != null)
            {
                for (int i = 0; i < floors.Count; i++)
                {
                    Vector2Int p = floors[i];
                    if (_matrix.SliceBlockHasNoVoxelAbove(p.x, p.y))
                    {
                        openTop.Add(p);
                    }
                }
            }

            List<Vector2Int> candidates = openTop.Count > 0 ? openTop : floors;
            if (openTop.Count == 0 && floors.Count > 0 && _matrix != null)
            {
                Debug.LogWarning("LandscapeMatrix: 当前切面内无「顶面无上层体素」的地块，出生/目标已回退为切面内任意可见地板。");
            }

            Vector2Int? spawnPreferred = null;
            Vector2Int? goalPreferred = null;
            if (_matrix != null)
            {
                if (_matrix.TryStorageToSliceBlockCell(preferredSpawnVoxel.x, preferredSpawnVoxel.y, preferredSpawnVoxel.z, out Vector2Int sp))
                {
                    spawnPreferred = sp;
                }

                if (_matrix.TryStorageToSliceBlockCell(preferredGoalVoxel.x, preferredGoalVoxel.y, preferredGoalVoxel.z, out Vector2Int gp))
                {
                    goalPreferred = gp;
                }
            }

            Vector2Int spawn = PickSpawnCandidate(candidates, spawnPreferred);
            if (spawn.x >= 0)
            {
                cells[spawn.x, spawn.y] = CellType.Spawn;
            }

            Vector2Int goal = PickGoalCandidate(candidates, spawn, goalPreferred);
            if (goal.x >= 0 && goal != spawn)
            {
                cells[goal.x, goal.y] = CellType.Goal;
            }
        }

        private static Vector2Int PickSpawnCandidate(List<Vector2Int> candidates, Vector2Int? preferred)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return new Vector2Int(-1, -1);
            }

            if (preferred.HasValue && CellListContains(candidates, preferred.Value))
            {
                return preferred.Value;
            }

            for (int x = SliceMin; x <= SliceMax; x++)
            {
                for (int y = SliceMin; y <= SliceMax; y++)
                {
                    Vector2Int c = new Vector2Int(x, y);
                    if (CellListContains(candidates, c))
                    {
                        return c;
                    }
                }
            }

            return candidates[0];
        }

        private static Vector2Int PickGoalCandidate(List<Vector2Int> candidates, Vector2Int spawn, Vector2Int? preferred)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return new Vector2Int(-1, -1);
            }

            if (preferred.HasValue && CellListContains(candidates, preferred.Value) && preferred.Value != spawn)
            {
                return preferred.Value;
            }

            for (int x = SliceMax; x >= SliceMin; x--)
            {
                for (int y = SliceMin; y <= SliceMax; y++)
                {
                    Vector2Int c = new Vector2Int(x, y);
                    if (CellListContains(candidates, c) && c != spawn)
                    {
                        return c;
                    }
                }
            }

            return new Vector2Int(-1, -1);
        }

        private static bool CellListContains(List<Vector2Int> list, Vector2Int cell)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == cell)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
