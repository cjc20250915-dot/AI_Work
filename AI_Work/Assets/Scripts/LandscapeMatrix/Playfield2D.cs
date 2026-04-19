using System;
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
        private const int SliceMinX = 0;

        private CellType[,] _cells = new CellType[MatrixController.DefaultMatrixSize, MatrixController.DefaultMatrixSize + 1];
        private GameObject[,] _tiles = new GameObject[MatrixController.DefaultMatrixSize, MatrixController.DefaultMatrixSize + 1];
        private MatrixSliceMapper _mapper;
        private Player2DController _player;
        private Vector2Int _spawnCell = new Vector2Int(-1, -1);
        private Vector2Int _goalCell = new Vector2Int(-1, -1);
        private Transform _goalItem2D;
        private bool _initialized;
        private int _lockedColumnX = SliceMinX;
        private bool _playerVisible = true;
        public bool IsPlayerDead { get; private set; }

        private int Width => _cells.GetLength(0);
        private int Height => _cells.GetLength(1);
        private int SliceMaxX => Width - 1;

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
            return new Vector3(
                MatrixController.SliceBoardWorldOriginX + cell.x * TileSize,
                MatrixController.SliceBoardWorldOriginY + cell.y * TileSize,
                0f);
        }

        /// <summary>
        /// 站立格为「空气格」，脚下为实体方块；世界坐标使胶囊底部贴在方块顶面，避免陷入方块。
        /// </summary>
        public Vector3 GetStandWorldPosition(Vector2Int airCell)
        {
            const float blockHalfExtent = 0.475f;
            const float capsuleHalfHeight = 0.45f;
            float feetY = MatrixController.SliceBoardWorldOriginY + (airCell.y - 1) * TileSize + blockHalfExtent;
            float centerY = feetY + capsuleHalfHeight;
            return new Vector3(
                MatrixController.SliceBoardWorldOriginX + airCell.x * TileSize,
                centerY,
                0f);
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
            int x = Mathf.RoundToInt((worldPosition.x - MatrixController.SliceBoardWorldOriginX) / TileSize);
            int y = Mathf.RoundToInt((worldPosition.y - MatrixController.SliceBoardWorldOriginY) / TileSize);
            return new Vector2Int(x, y);
        }

        private void BuildStaticBoard()
        {
            int width = GetConfiguredWidth();
            int height = GetConfiguredHeight();
            _cells = new CellType[width, height];
            _tiles = new GameObject[width, height];
            DestroyLegacyTilesOutsideMap();
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    _cells[x, y] = CellType.Empty;
                    _tiles[x, y] = CreateTileVisual(x, y);
                }
            }
        }

        public void RebuildStaticBoardForEditor(MatrixSliceMapper mapper)
        {
            _mapper = mapper;
            BuildStaticBoard();
            CellType[,] map = _mapper != null ? _mapper.BuildCurrentSliceMap() : new CellType[Width, Height];
            ApplyMappedCells(map, resetPlayerToSpawn: false);
        }

        /// <summary>移除超出当前 <see cref="MatrixController.SliceMapWidth"/>×<see cref="MatrixController.SliceMapHeight"/> 的 Tile_*（含旧 7×7 场景残留），避免残留物体。</summary>
        private void DestroyLegacyTilesOutsideMap()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform c = transform.GetChild(i);
                if (c == null)
                {
                    continue;
                }

                string n = c.name;
                if (!n.StartsWith("Tile_", StringComparison.Ordinal))
                {
                    continue;
                }

                string rest = n.Substring("Tile_".Length);
                int u = rest.LastIndexOf('_');
                if (u <= 0)
                {
                    continue;
                }

                if (!int.TryParse(rest.Substring(0, u), out int tx))
                {
                    continue;
                }

                if (!int.TryParse(rest.Substring(u + 1), out int ty))
                {
                    continue;
                }

                if (tx >= 0 && tx < Width && ty >= 0 && ty < Height)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(c.gameObject);
                }
                else
                {
                    DestroyImmediate(c.gameObject);
                }
            }
        }

        private GameObject CreateTileVisual(int x, int y)
        {
            Transform existing = transform.Find($"Tile_{x}_{y}");
            if (existing != null)
            {
                // 场景中旧版 Tile 可能仍带 7×7 时代的世界坐标，原点或 SliceBoard 变更后须对齐。
                existing.position = CellToWorld(new Vector2Int(x, y));
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
            EnsureBoardMatchesMap(mappedCells);
            _spawnCell = new Vector2Int(-1, -1);
            _goalCell = new Vector2Int(-1, -1);

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    _cells[x, y] = mappedCells[x, y];
                    PaintTile(_tiles[x, y], x, y, _cells[x, y]);

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

            UpdateGoalItem2DVisual();

            if (resetPlayerToSpawn && _player != null)
            {
                _player.SetCell(GetSpawnCell());
            }
        }

        private static readonly Color TileNeutralColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        private static readonly Color TileEmptyBackgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f);
        private static readonly Color TileSpawnTintColor = new Color(0.25f, 0.5f, 1f, 1f);
        private static readonly Color TileGoalTintColor = new Color(0.16f, 0.84f, 0.35f, 1f);

        /// <summary>
        /// 左屏地块默认显示当前固定切片覆盖到的实体方块；
        /// 仅当出生/目标偏好体素当前确实落入切片范围时，才将对应方块染为蓝/绿。
        /// </summary>
        private void PaintTile(GameObject tile, int gridX, int gridY, CellType cellType)
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

            if (cellType == CellType.Empty)
            {
                LandscapeMatrixRendererColors.SetColor(renderer, TileEmptyBackgroundColor);
                tile.SetActive(false);
                return;
            }

            tile.SetActive(true);

            Color color = TileNeutralColor;
            MatrixController matrix = _mapper != null ? _mapper.GetMatrixController() : null;
            if (matrix != null && matrix.TryGetSliceBlockTint(gridX, gridY, out bool spawnTint, out bool goalTint))
            {
                color = goalTint ? TileGoalTintColor : TileSpawnTintColor;
            }

            LandscapeMatrixRendererColors.SetColor(renderer, color);
        }

        private void EnsureGoalItem2D()
        {
            if (_goalItem2D != null)
            {
                return;
            }

            const string itemName = "GoalItem2D";
            Transform existing = transform.Find(itemName);
            if (existing != null)
            {
                _goalItem2D = existing;
                if (existing.TryGetComponent(out Collider c))
                {
                    Destroy(c);
                }

                return;
            }

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = itemName;
            Destroy(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(transform, false);
            if (sphere.TryGetComponent(out Renderer r))
            {
                LandscapeMatrixRendererColors.SetColor(r, new Color(0.2f, 0.92f, 0.55f, 1f));
            }

            sphere.transform.localScale = Vector3.one * 0.28f;
            _goalItem2D = sphere.transform;
            sphere.SetActive(false);
        }

        private void UpdateGoalItem2DVisual()
        {
            EnsureGoalItem2D();
            if (_goalItem2D == null)
            {
                return;
            }

            MatrixController matrix = _mapper != null ? _mapper.GetMatrixController() : null;
            if (matrix == null || !matrix.TryGetPreferredGoalSliceBlockCell(out Vector2Int goalBlockCell))
            {
                _goalItem2D.gameObject.SetActive(false);
                return;
            }

            Vector3 p = CellToWorld(goalBlockCell);
            _goalItem2D.position = new Vector3(p.x, p.y + TileSize * 0.58f, p.z - 0.18f);
            _goalItem2D.gameObject.SetActive(true);
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
                LandscapeMatrixRendererColors.SetColor(playerObject.GetComponent<Renderer>(), new Color(1f, 0.9f, 0.25f));
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

        private int GetConfiguredWidth()
        {
            MatrixController matrix = _mapper != null ? _mapper.GetMatrixController() : null;
            return matrix != null ? matrix.SliceMapWidth : MatrixController.DefaultMatrixSize;
        }

        private int GetConfiguredHeight()
        {
            MatrixController matrix = _mapper != null ? _mapper.GetMatrixController() : null;
            return matrix != null ? matrix.SliceMapHeight : MatrixController.DefaultMatrixSize + 1;
        }

        private void EnsureBoardMatchesMap(CellType[,] mappedCells)
        {
            if (mappedCells == null)
            {
                return;
            }

            if (_cells.GetLength(0) == mappedCells.GetLength(0) && _cells.GetLength(1) == mappedCells.GetLength(1))
            {
                return;
            }

            _cells = new CellType[mappedCells.GetLength(0), mappedCells.GetLength(1)];
            _tiles = new GameObject[mappedCells.GetLength(0), mappedCells.GetLength(1)];
            BuildStaticBoard();
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

        /// <summary>角色与 2D 目标物（GoalItem2D）在世界空间中是否相交（包围盒），不依赖脚下是否为 Goal 格。</summary>
        public bool IsPlayerOverlappingGoalObject()
        {
            if (!_playerVisible || _player == null || _goalItem2D == null || !_goalItem2D.gameObject.activeInHierarchy)
            {
                return false;
            }

            Renderer goalRenderer = _goalItem2D.GetComponent<Renderer>();
            Renderer playerRenderer = _player.GetComponent<Renderer>();
            if (goalRenderer == null || playerRenderer == null)
            {
                return false;
            }

            return goalRenderer.bounds.Intersects(playerRenderer.bounds);
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

        private bool IsInSliceX(int x)
        {
            return x >= SliceMinX && x <= SliceMaxX;
        }

        public Player2DController GetPlayerController()
        {
            return _player;
        }

        public void SetPlayerSliceVisible(bool visible)
        {
            _playerVisible = visible;
            if (_player == null)
            {
                return;
            }

            Renderer renderer = _player.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = visible;
            }
        }
    }
}
