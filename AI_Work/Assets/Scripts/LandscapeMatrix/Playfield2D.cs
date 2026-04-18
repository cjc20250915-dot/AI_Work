using UnityEngine;

namespace LandscapeMatrix
{
    public enum CellType
    {
        Empty,
        Floor,
        Wall,
        Goal,
        Spawn
    }

    public class Playfield2D : MonoBehaviour
    {
        private const float TileSize = 1f;
        private const int Width = 7;
        private const int Height = 7;
        private const int SliceMinX = 2;
        private const int SliceMaxX = 4;

        private CellType[,] _cells = new CellType[Width, Height];
        private GameObject[,] _tiles = new GameObject[Width, Height];
        private MatrixSliceMapper _mapper;
        private Player2DController _player;
        private Vector2Int _spawnCell;
        private Vector2Int _goalCell;
        private bool _initialized;
        private int _lockedColumnX = SliceMinX;
        public bool IsPlayerDead { get; private set; }

        public void Initialize(MatrixSliceMapper mapper)
        {
            if (_initialized)
            {
                return;
            }

            _mapper = mapper;
            BuildStaticBoard();
            ApplyMappedCells(_mapper.BuildCurrentSliceMap(), resetPlayerToSpawn: false);
            SpawnPlayer();
            if (_player != null)
            {
                _lockedColumnX = Mathf.Clamp(_player.GetCurrentCell().x, SliceMinX, SliceMaxX);
            }

            _initialized = true;
        }

        public void RefreshMap(CellType[,] mappedCells)
        {
            ApplyMappedCells(mappedCells, resetPlayerToSpawn: false);
            if (_player != null)
            {
                _player.ResolveAfterTerrainChange();
                _player.CheckGoal();
            }
        }

        public void SetLockedColumn(int columnX)
        {
            _lockedColumnX = Mathf.Clamp(columnX, SliceMinX, SliceMaxX);
        }

        public int GetLockedColumnX()
        {
            return _lockedColumnX;
        }

        public void SetPlayerDead(bool dead)
        {
            IsPlayerDead = dead;
        }

        public bool IsWalkable(Vector2Int cell)
        {
            if (!IsInside(cell) || IsSolid(cell) || !IsInSliceX(cell.x))
            {
                return false;
            }

            Vector2Int below = cell + Vector2Int.down;
            return IsInside(below) && IsSolid(below);
        }

        public Vector3 CellToWorld(Vector2Int cell)
        {
            return new Vector3(cell.x * TileSize, cell.y * TileSize, 0f);
        }

        /// <summary>
        /// 站立格为「空气格」，脚下为实体方块；世界坐标使胶囊底部贴在方块顶面，避免陷入方块。
        /// </summary>
        public Vector3 GetStandWorldPosition(Vector2Int airCell)
        {
            const float blockHalfExtent = 0.475f;
            const float capsuleHalfHeight = 0.45f;
            float feetY = (airCell.y - 1) * TileSize + blockHalfExtent;
            float centerY = feetY + capsuleHalfHeight;
            return new Vector3(airCell.x * TileSize, centerY, 0f);
        }

        public bool IsGoal(Vector2Int cell)
        {
            return cell == _goalCell;
        }

        public Vector2Int GetSpawnCell()
        {
            return GetStandCell(_spawnCell);
        }

        public Vector2Int WorldToCell(Vector3 worldPosition)
        {
            int x = Mathf.RoundToInt(worldPosition.x / TileSize);
            int y = Mathf.RoundToInt(worldPosition.y / TileSize);
            return new Vector2Int(x, y);
        }

        private void BuildStaticBoard()
        {
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    _cells[x, y] = CellType.Empty;
                    _tiles[x, y] = CreateTileVisual(x, y);
                }
            }
        }

        private GameObject CreateTileVisual(int x, int y)
        {
            Transform existing = transform.Find($"Tile_{x}_{y}");
            if (existing != null)
            {
                return existing.gameObject;
            }

            GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = $"Tile_{x}_{y}";
            tile.transform.SetParent(transform, false);
            tile.transform.position = CellToWorld(new Vector2Int(x, y));
            tile.transform.localScale = new Vector3(0.95f, 0.95f, 0.95f);

            return tile;
        }

        private void ApplyMappedCells(CellType[,] mappedCells, bool resetPlayerToSpawn)
        {
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    _cells[x, y] = mappedCells[x, y];
                    PaintTile(_tiles[x, y], _cells[x, y]);

                    if (_cells[x, y] == CellType.Spawn)
                    {
                        _spawnCell = new Vector2Int(x, y);
                    }
                    else if (_cells[x, y] == CellType.Goal)
                    {
                        _goalCell = new Vector2Int(x, y);
                    }
                }
            }

            if (resetPlayerToSpawn && _player != null)
            {
                _player.SetCell(GetSpawnCell());
            }
        }

        private static void PaintTile(GameObject tile, CellType cellType)
        {
            if (tile == null)
            {
                return;
            }

            Renderer renderer = tile.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            Color color = cellType switch
            {
                CellType.Empty => new Color(0.08f, 0.08f, 0.08f, 1f),
                CellType.Floor => new Color(0.78f, 0.78f, 0.78f, 1f),
                CellType.Wall => new Color(0.28f, 0.2f, 0.2f, 1f),
                CellType.Goal => new Color(0.16f, 0.84f, 0.35f, 1f),
                CellType.Spawn => new Color(0.25f, 0.5f, 1f, 1f),
                _ => Color.magenta
            };
            renderer.material.color = color;
            tile.SetActive(cellType != CellType.Empty);
        }

        private void SpawnPlayer()
        {
            GameObject playerObject = null;
            Transform existing = transform.Find("Player2D");
            if (existing != null)
            {
                playerObject = existing.gameObject;
            }
            else
            {
                playerObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                playerObject.name = "Player2D";
                playerObject.transform.SetParent(transform, false);
                // 角色保持等比缩放，避免拉伸，且明显小于方块。
                playerObject.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
                playerObject.GetComponent<Renderer>().material.color = new Color(1f, 0.9f, 0.25f);
                Destroy(playerObject.GetComponent<Collider>());
            }

            _player = playerObject.GetComponent<Player2DController>();
            if (_player == null)
            {
                _player = playerObject.AddComponent<Player2DController>();
            }
            Vector2Int start = GetSpawnCell();
            _lockedColumnX = Mathf.Clamp(start.x, SliceMinX, SliceMaxX);
            _player.Initialize(this, start);
        }

        private bool IsInside(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < Width && cell.y >= 0 && cell.y < Height;
        }

        public bool IsSolid(Vector2Int cell)
        {
            if (!IsInside(cell))
            {
                return false;
            }

            return _cells[cell.x, cell.y] != CellType.Empty;
        }

        /// <summary>
        /// 地形变化后：不左右移动，仅在锁定列内重新落位；无立足点则死亡。
        /// </summary>
        public void ResolvePlayerAfterTerrainChange()
        {
            if (_player == null || IsPlayerDead)
            {
                return;
            }

            int cx = Mathf.Clamp(_lockedColumnX, SliceMinX, SliceMaxX);
            Vector2Int stand = FindLowestStandInColumn(cx);
            if (stand.x < 0)
            {
                SetPlayerDead(true);
                Debug.Log("Player died: no valid stand in locked column. Click Restart.");
                return;
            }

            _player.SetCell(stand);
        }

        private Vector2Int FindLowestStandInColumn(int cx)
        {
            cx = Mathf.Clamp(cx, SliceMinX, SliceMaxX);
            for (int y = 1; y < Height; y++)
            {
                Vector2Int c = new Vector2Int(cx, y);
                if (!IsSolid(c) && IsSolid(c + Vector2Int.down))
                {
                    return c;
                }
            }

            return new Vector2Int(-1, -1);
        }

        public Vector2Int ResolveStandingCell(Vector2Int desiredCell)
        {
            Vector2Int stand = FindLowestStandInColumn(_lockedColumnX);
            return stand.x < 0 ? GetSpawnCell() : stand;
        }

        public bool IsInitialized()
        {
            return _initialized;
        }

        public bool IsStandingOnGoal(Vector2Int standCell)
        {
            Vector2Int below = standCell + Vector2Int.down;
            return IsInside(below) && _cells[below.x, below.y] == CellType.Goal;
        }

        private Vector2Int GetStandCell(Vector2Int blockCell)
        {
            int cx = Mathf.Clamp(blockCell.x, SliceMinX, SliceMaxX);
            Vector2Int stand = FindLowestStandInColumn(cx);
            if (stand.x >= 0)
            {
                return stand;
            }

            return new Vector2Int(SliceMinX, 1);
        }

        private static bool IsInSliceX(int x)
        {
            return x >= SliceMinX && x <= SliceMaxX;
        }

        public Player2DController GetPlayerController()
        {
            return _player;
        }
    }
}
