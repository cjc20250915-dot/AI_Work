using UnityEngine;
using UnityEngine.Rendering;

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

            EnsurePipelineCompatibleMaterial(renderer);

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// 修复 <see cref="GameObject.CreatePrimitive"/> 在 URP 项目中带来的“粉色/洋红”问题：
        /// Unity 内置 Default-Material 使用 Built-in 的 Standard shader，在 URP 构建中该 shader
        /// 未被打包进 build，运行时即以 error shader（粉色）呈现。
        /// 检测到不兼容的 shader 时，将 sharedMaterial 换成当前渲染管线的默认材质
        /// （URP 下为 URP/Lit 的 Lit.mat，随 URP 资产被引用，一定会在 build 中可用）。
        /// </summary>
        public static void EnsurePipelineCompatibleMaterial(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            Material current = renderer.sharedMaterial;
            if (!IsIncompatibleMaterial(current))
            {
                return;
            }

            Material fallback = GetPipelineDefaultMaterial();
            if (fallback != null && fallback != current)
            {
                renderer.sharedMaterial = fallback;
            }
        }

        /// <summary>递归修复某个根节点（含未激活子物体）下所有渲染器的材质兼容性。</summary>
        public static void EnsurePipelineCompatibleMaterialsInHierarchy(Transform root)
        {
            if (root == null)
            {
                return;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                EnsurePipelineCompatibleMaterial(renderers[i]);
            }
        }

        private static bool IsIncompatibleMaterial(Material material)
        {
            if (material == null)
            {
                return true;
            }

            Shader shader = material.shader;
            if (shader == null)
            {
                return true;
            }

            string shaderName = shader.name;
            if (string.IsNullOrEmpty(shaderName))
            {
                return true;
            }

            // Built-in RP 着色器在 URP 构建里无法渲染，一律替换。
            if (shaderName == "Standard" ||
                shaderName == "Standard (Specular setup)" ||
                shaderName.StartsWith("Legacy Shaders/", System.StringComparison.Ordinal))
            {
                return true;
            }

            // Unity 在找不到 shader 时会把材质临时挂上 Hidden/InternalErrorShader。
            if (shaderName.StartsWith("Hidden/InternalError", System.StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private static Material GetPipelineDefaultMaterial()
        {
            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            if (rp != null && rp.defaultMaterial != null)
            {
                return rp.defaultMaterial;
            }

            return null;
        }
    }
}
