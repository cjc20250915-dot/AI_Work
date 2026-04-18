using UnityEngine;
using UnityEngine.SceneManagement;
using System;

namespace LandscapeMatrix
{
    public class Player2DController : MonoBehaviour
    {
        public event Action<Vector2Int> OnCellChanged;

        /// <summary>为 true 时由 3D 物理位姿驱动，不再读取横向输入。</summary>
        public bool ExternalDrive { get; set; }

        private Playfield2D _playfield;
        private Vector2Int _cell;
        private float _moveCooldown;

        public void Initialize(Playfield2D playfield, Vector2Int spawnCell)
        {
            _playfield = playfield;
            SetCell(spawnCell);
        }

        public void SetCell(Vector2Int cell)
        {
            _cell = cell;
            transform.position = _playfield.GetStandWorldPosition(_cell);
            OnCellChanged?.Invoke(_cell);
        }

        public Vector2Int GetCurrentCell()
        {
            return _cell;
        }

        public void ClampInsideBoard()
        {
            SetCell(_playfield.ResolveStandingCell(_cell));
        }

        public void ResolveAfterTerrainChange()
        {
            _playfield.ResolvePlayerAfterTerrainChange();
        }

        public void CheckGoal()
        {
            if (_playfield.IsStandingOnGoal(_cell))
            {
                Debug.Log("Level clear: reached Goal.");
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                return;
            }

            if (_playfield.IsPlayerDead)
            {
                return;
            }

            if (ExternalDrive)
            {
                return;
            }

            _moveCooldown -= Time.deltaTime;
            if (_moveCooldown > 0f)
            {
                return;
            }

            Vector2Int direction = ReadDirectionInput();
            if (direction == Vector2Int.zero)
            {
                return;
            }

            Vector2Int nextCell = _cell + direction;
            if (_playfield.IsWalkable(nextCell))
            {
                if (direction.x != 0)
                {
                    _playfield.SetLockedColumn(nextCell.x);
                }

                SetCell(_playfield.ResolveStandingCell(nextCell));
                CheckGoal();
            }

            _moveCooldown = 0.08f;
        }

        private static Vector2Int ReadDirectionInput()
        {
            if (Input.GetKey(KeyCode.A))
            {
                return Vector2Int.left;
            }

            if (Input.GetKey(KeyCode.D))
            {
                return Vector2Int.right;
            }

            if (Input.GetKey(KeyCode.LeftArrow))
            {
                return Vector2Int.left;
            }

            if (Input.GetKey(KeyCode.RightArrow))
            {
                return Vector2Int.right;
            }

            return Vector2Int.zero;
        }
    }
}
