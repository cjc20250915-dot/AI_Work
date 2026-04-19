using UnityEngine;

namespace LandscapeMatrix
{
    /// <summary>
    /// 使用 MaterialPropertyBlock 设置颜色，避免访问 <see cref="Renderer.material"/> 产生编辑器内带 DontSave 的材质实例，从而触发 Unity 关于持久化的断言。
    /// 同时写入 URP Lit（_BaseColor）与 Built-in（_Color）常用属性名。
    /// </summary>
    public static class LandscapeMatrixRendererColors
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }
    }
}
