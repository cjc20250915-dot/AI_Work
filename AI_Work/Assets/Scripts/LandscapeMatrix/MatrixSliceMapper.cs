namespace LandscapeMatrix
{
    public class MatrixSliceMapper : UnityEngine.MonoBehaviour
    {
        private const int Width = 7;
        private const int Height = 7;
        private const int SliceSize = 3;
        private static int SliceMin => MatrixController.SliceGridMin;
        private static int SliceMax => SliceMin + SliceSize - 1;

        private Playfield2D _playfield;
        private MatrixController _matrix;
        private bool _runtimeReady;

        private void Start()
        {
            EnsureRuntimeReady();
        }

        public void Initialize(Playfield2D playfield, MatrixController matrix)
        {
            _playfield = playfield;
            _matrix = matrix;
            // 必须在 MatrixController.Initialize 之前置位，否则 NotifyStateChanged -> ApplyMatrixState
            // 会再次进入 EnsureRuntimeReady 并形成无限递归。
            _runtimeReady = true;
            _matrix.Initialize(this);
        }

        public CellType[,] BuildCurrentSliceMap()
        {
            CellType[,] cells = BuildBaseMap();
            ApplyMatrixOpenPath(cells);
            return cells;
        }

        public void ApplyMatrixState()
        {
            EnsureRuntimeReady();

            if (_playfield == null)
            {
                return;
            }

            _playfield.RefreshMap(BuildCurrentSliceMap());
        }

        private void EnsureRuntimeReady()
        {
            if (_runtimeReady)
            {
                return;
            }

            if (_playfield == null)
            {
                _playfield = UnityEngine.Object.FindFirstObjectByType<Playfield2D>();
            }
            if (_matrix == null)
            {
                _matrix = UnityEngine.Object.FindFirstObjectByType<MatrixController>();
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
            CellType[,] cells = new CellType[Width, Height];

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    cells[x, y] = CellType.Empty;
                }
            }

            if (_matrix == null)
            {
                // 初始化早期兜底，避免因矩阵未绑定导致场景构建中断。
                cells[SliceMin, SliceMin] = CellType.Spawn;
                cells[SliceMin + 1, SliceMin] = CellType.Floor;
                cells[SliceMax, SliceMin] = CellType.Goal;
                return cells;
            }

            // 仅由当前切面采样决定地形：与 TrySampleFilledCell(viewX, viewY, SliceSampledZ) 一致。
            // viewX 为内部列索引；映射到 2D 时用 InternalViewXToDisplayColumnOffset，使中心 3×3 左→右与世界 +X 一致（90°/270° 需镜像）。
            int viewZ = _matrix.SliceSampledZ;
            for (int viewX = 0; viewX < SliceSize; viewX++)
            {
                for (int viewY = 0; viewY < SliceSize; viewY++)
                {
                    if (!_matrix.TrySampleFilledCell(viewX, viewY, viewZ, out bool filled) || !filled)
                    {
                        continue;
                    }

                    int displayColumnOffset = _matrix.InternalViewXToDisplayColumnOffset(viewX);
                    int worldX = SliceMin + displayColumnOffset;
                    int worldY = SliceMin + viewY;
                    cells[worldX, worldY] = CellType.Floor;
                }
            }

            // Spawn / Goal 仍在矩阵范围内，若目标点为空则降级到可见地形。
            PlaceSpawnAndGoal(cells);

            return cells;
        }

        private void ApplyMatrixOpenPath(CellType[,] cells)
        {
            // 改为纯切片驱动，不再额外“造通道”。
        }

        private static void PlaceSpawnAndGoal(CellType[,] cells)
        {
            UnityEngine.Vector2Int spawn = new UnityEngine.Vector2Int(SliceMin, SliceMin);
            UnityEngine.Vector2Int goal = new UnityEngine.Vector2Int(SliceMax, SliceMin);

            if (cells[spawn.x, spawn.y] == CellType.Empty)
            {
                spawn = FindAnyFloor(cells, true);
            }

            if (cells[goal.x, goal.y] == CellType.Empty)
            {
                goal = FindAnyFloor(cells, false);
            }

            if (spawn.x >= 0)
            {
                cells[spawn.x, spawn.y] = CellType.Spawn;
            }

            if (goal.x >= 0 && goal != spawn)
            {
                cells[goal.x, goal.y] = CellType.Goal;
            }
        }

        private static UnityEngine.Vector2Int FindAnyFloor(CellType[,] cells, bool fromLeft)
        {
            if (fromLeft)
            {
                for (int x = SliceMin; x <= SliceMax; x++)
                {
                    for (int y = SliceMin; y <= SliceMax; y++)
                    {
                        if (cells[x, y] == CellType.Floor)
                        {
                            return new UnityEngine.Vector2Int(x, y);
                        }
                    }
                }
            }
            else
            {
                for (int x = SliceMax; x >= SliceMin; x--)
                {
                    for (int y = SliceMin; y <= SliceMax; y++)
                    {
                        if (cells[x, y] == CellType.Floor)
                        {
                            return new UnityEngine.Vector2Int(x, y);
                        }
                    }
                }
            }

            return new UnityEngine.Vector2Int(-1, -1);
        }
    }
}
