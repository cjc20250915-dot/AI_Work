using UnityEngine;

namespace LandscapeMatrix
{
    [CreateAssetMenu(fileName = "LevelDefinition", menuName = "LandscapeMatrix/Level Definition")]
    public sealed class LevelDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string levelId = "Level_01";
        public string displayName = "Level 01";
        [TextArea] public string teachingGoal = "理解角色移动和简单矩阵操作";

        [Header("Matrix")]
        [Min(1)] public int matrixSize = 3;
        [Min(0)] public int sliceMapWidth;
        [Min(0)] public int sliceFloorRows;
        public Vector3Int initialGridOffset = Vector3Int.zero;
        [Range(0, 3)] public int initialRotationStep;
        public bool defaultVoxelVisible = true;

        /// <summary>
        /// 隐藏方格的初始模板，仅供参考 / 首次搭建关卡时复制到场景 MatrixController。
        /// 运行时与编辑器自动重刷都不再拿这里的数据去覆盖场景——实际布局以场景中 MatrixController.hiddenVoxels 为准。
        /// </summary>
        public MatrixController.VoxelCoord[] hiddenVoxels;

        [Header("Slice Mapping")]
        public Vector3Int preferredSpawnVoxel = new Vector3Int(0, 2, 1);
        public Vector3Int preferredGoalVoxel = new Vector3Int(2, 2, 2);
    }
}
