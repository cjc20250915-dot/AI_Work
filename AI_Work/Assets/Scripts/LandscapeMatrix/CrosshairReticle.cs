using UnityEngine;
using UnityEngine.UI;

namespace LandscapeMatrix
{
    /// <summary>
    /// 通用的"十字准星 + 圆环"UI 部件，挂在任何 <see cref="Canvas"/> 下都能用。
    /// <para>
    /// 部件自带 4 个 <see cref="Image"/> 子节点（外圆环、十字准星、中心点、命中闪烁占位），
    /// 所有贴图都按 Inspector 参数程序化生成，不依赖外部 Sprite 资源。
    /// </para>
    /// <para>用法：把 <c>CrosshairReticle.prefab</c> 拖到任意 Canvas 子节点下即可；编辑期会实时预览，
    /// 运行时在 <see cref="Awake"/> 里重建一次保证参数最新。</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class CrosshairReticle : MonoBehaviour
    {
        private const string RingChildName = "Ring";
        private const string CrosshairChildName = "Crosshair";
        private const string CenterDotChildName = "CenterDot";
        private const string BarUpName = "Bar_Up";
        private const string BarDownName = "Bar_Down";
        private const string BarLeftName = "Bar_Left";
        private const string BarRightName = "Bar_Right";

        [Header("Ring")]
        [Tooltip("是否绘制外圆环。")]
        [SerializeField] private bool _drawRing = true;

        [Tooltip("圆环颜色。")]
        [SerializeField] private Color _ringColor = new Color(0f, 0f, 0f, 1f);

        [Tooltip("圆环内缘半径相对短边的比例（越大圆越贴近视口边缘）。")]
        [SerializeField, Range(0.05f, 0.98f)] private float _ringRadiusRatio = 0.45f;

        [Tooltip("圆环线宽（像素，按贴图 512 基准缩放）。")]
        [SerializeField, Range(0.5f, 32f)] private float _ringThickness = 4f;

        [Tooltip("圆环内外缘羽化（0 为硬边）。")]
        [SerializeField, Range(0f, 0.05f)] private float _ringFeather = 0.003f;

        [Header("Crosshair")]
        [Tooltip("是否绘制十字准星（4 根线）。")]
        [SerializeField] private bool _drawCrosshair = true;

        [Tooltip("十字准星颜色。")]
        [SerializeField] private Color _crosshairColor = new Color(1f, 1f, 1f, 0.9f);

        [Tooltip("十字单臂长度相对部件短边的比例。")]
        [SerializeField, Range(0.02f, 0.5f)] private float _armLengthRatio = 0.12f;

        [Tooltip("十字单臂到正中心的空缺相对部件短边的比例。")]
        [SerializeField, Range(0f, 0.2f)] private float _armGapRatio = 0.03f;

        [Tooltip("十字线条像素厚度。")]
        [SerializeField, Range(0.5f, 8f)] private float _crosshairThickness = 2f;

        [Header("Center Dot")]
        [Tooltip("是否绘制中心小圆点。")]
        [SerializeField] private bool _drawCenterDot = false;

        [Tooltip("中心小圆点颜色。")]
        [SerializeField] private Color _centerDotColor = new Color(1f, 1f, 1f, 0.9f);

        [Tooltip("中心小圆点直径（像素）。")]
        [SerializeField, Range(1f, 40f)] private float _centerDotDiameter = 4f;

        [Header("Advanced")]
        [Tooltip("程序化贴图分辨率（圆环用，正方形贴图）。")]
        [SerializeField, Range(128, 2048)] private int _ringTextureSize = 512;

        // 程序化生成的运行时资产，需要手动释放。
        private Texture2D _ringTexture;
        private Sprite _ringSprite;
        private Texture2D _dotTexture;
        private Sprite _dotSprite;
        private Texture2D _whiteTexture;
        private Sprite _whiteSprite;

        private RectTransform _ringRect;
        private Image _ringImage;
        private RectTransform _crosshairRoot;
        private Image _barUp;
        private Image _barDown;
        private Image _barLeft;
        private Image _barRight;
        private RectTransform _centerDotRect;
        private Image _centerDotImage;

        private RectTransform _selfRect;

        private void Awake()
        {
            EnsureChildren();
            Rebuild();
        }

        private void OnEnable()
        {
            EnsureChildren();
            Rebuild();
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            // 编辑期随改随刷。
            EnsureChildren();
            Rebuild();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled || _crosshairRoot == null)
            {
                return;
            }

            // 只重排不重贴图，避免每次 Resize 都重新 SetPixels。
            LayoutCrosshair();
            LayoutCenterDot();
        }

        private void OnDestroy()
        {
            ReleaseRuntimeAsset(ref _ringSprite);
            ReleaseRuntimeAsset(ref _ringTexture);
            ReleaseRuntimeAsset(ref _dotSprite);
            ReleaseRuntimeAsset(ref _dotTexture);
            ReleaseRuntimeAsset(ref _whiteSprite);
            ReleaseRuntimeAsset(ref _whiteTexture);
        }

        // ------------------------------------------------------------------
        // 构建子节点（幂等）
        // ------------------------------------------------------------------

        private void EnsureChildren()
        {
            if (_selfRect == null)
            {
                _selfRect = (RectTransform)transform;
            }

            // Ring
            Transform ringTf = transform.Find(RingChildName);
            if (ringTf == null)
            {
                GameObject go = new GameObject(RingChildName, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                go.layer = gameObject.layer;
                ringTf = go.transform;
            }
            _ringRect = (RectTransform)ringTf;
            _ringImage = ringTf.GetComponent<Image>();
            if (_ringImage == null)
            {
                _ringImage = ringTf.gameObject.AddComponent<Image>();
            }
            _ringImage.raycastTarget = false;
            StretchToParent(_ringRect);

            // Crosshair
            Transform crosshairTf = transform.Find(CrosshairChildName);
            if (crosshairTf == null)
            {
                GameObject go = new GameObject(CrosshairChildName, typeof(RectTransform));
                go.transform.SetParent(transform, false);
                go.layer = gameObject.layer;
                crosshairTf = go.transform;
            }
            _crosshairRoot = (RectTransform)crosshairTf;
            StretchToParent(_crosshairRoot);

            _barUp = EnsureBar(_crosshairRoot, BarUpName);
            _barDown = EnsureBar(_crosshairRoot, BarDownName);
            _barLeft = EnsureBar(_crosshairRoot, BarLeftName);
            _barRight = EnsureBar(_crosshairRoot, BarRightName);

            // Center dot
            Transform dotTf = transform.Find(CenterDotChildName);
            if (dotTf == null)
            {
                GameObject go = new GameObject(CenterDotChildName, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                go.layer = gameObject.layer;
                dotTf = go.transform;
            }
            _centerDotRect = (RectTransform)dotTf;
            _centerDotImage = dotTf.GetComponent<Image>();
            if (_centerDotImage == null)
            {
                _centerDotImage = dotTf.gameObject.AddComponent<Image>();
            }
            _centerDotImage.raycastTarget = false;
            _centerDotRect.anchorMin = new Vector2(0.5f, 0.5f);
            _centerDotRect.anchorMax = new Vector2(0.5f, 0.5f);
            _centerDotRect.pivot = new Vector2(0.5f, 0.5f);
        }

        private Image EnsureBar(RectTransform parent, string barName)
        {
            Transform tf = parent.Find(barName);
            if (tf == null)
            {
                GameObject go = new GameObject(barName, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                go.layer = gameObject.layer;
                tf = go.transform;
            }

            Image image = tf.GetComponent<Image>();
            if (image == null)
            {
                image = tf.gameObject.AddComponent<Image>();
            }
            image.raycastTarget = false;
            return image;
        }

        // ------------------------------------------------------------------
        // 刷新视觉
        // ------------------------------------------------------------------

        private void Rebuild()
        {
            ApplyRing();
            ApplyCrosshair();
            ApplyCenterDot();
        }

        private void ApplyRing()
        {
            if (_ringImage == null)
            {
                return;
            }

            _ringImage.enabled = _drawRing;
            if (!_drawRing)
            {
                return;
            }

            ReleaseRuntimeAsset(ref _ringSprite);
            ReleaseRuntimeAsset(ref _ringTexture);

            _ringTexture = CreateRingTexture(
                Mathf.Max(32, _ringTextureSize),
                Mathf.Clamp(_ringRadiusRatio, 0.01f, 0.999f),
                Mathf.Max(0.5f, _ringThickness),
                Mathf.Max(0f, _ringFeather),
                _ringColor);

            _ringSprite = Sprite.Create(
                _ringTexture,
                new Rect(0f, 0f, _ringTexture.width, _ringTexture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            _ringSprite.name = "CrosshairReticle_Ring";

            _ringImage.sprite = _ringSprite;
            _ringImage.color = Color.white;
            _ringImage.preserveAspect = false;
            _ringImage.type = Image.Type.Simple;
        }

        private void ApplyCrosshair()
        {
            SetBarEnabled(_barUp, _drawCrosshair);
            SetBarEnabled(_barDown, _drawCrosshair);
            SetBarEnabled(_barLeft, _drawCrosshair);
            SetBarEnabled(_barRight, _drawCrosshair);

            if (!_drawCrosshair)
            {
                return;
            }

            LayoutCrosshair();
        }

        private void LayoutCrosshair()
        {
            if (_crosshairRoot == null)
            {
                return;
            }

            float shortSide = GetShortSidePixels();
            float armLength = shortSide * _armLengthRatio;
            float armGap = shortSide * _armGapRatio;

            LayoutBar(_barUp, new Vector2(0f, armGap + armLength * 0.5f),
                new Vector2(_crosshairThickness, armLength), _crosshairColor);
            LayoutBar(_barDown, new Vector2(0f, -(armGap + armLength * 0.5f)),
                new Vector2(_crosshairThickness, armLength), _crosshairColor);
            LayoutBar(_barLeft, new Vector2(-(armGap + armLength * 0.5f), 0f),
                new Vector2(armLength, _crosshairThickness), _crosshairColor);
            LayoutBar(_barRight, new Vector2(armGap + armLength * 0.5f, 0f),
                new Vector2(armLength, _crosshairThickness), _crosshairColor);
        }

        private void ApplyCenterDot()
        {
            if (_centerDotImage == null)
            {
                return;
            }

            _centerDotImage.enabled = _drawCenterDot && _centerDotDiameter > 0.5f;
            if (!_centerDotImage.enabled)
            {
                return;
            }

            ReleaseRuntimeAsset(ref _dotSprite);
            ReleaseRuntimeAsset(ref _dotTexture);

            _dotTexture = CreateDiscTexture(64);
            _dotSprite = Sprite.Create(
                _dotTexture,
                new Rect(0f, 0f, _dotTexture.width, _dotTexture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            _dotSprite.name = "CrosshairReticle_Dot";

            _centerDotImage.sprite = _dotSprite;
            _centerDotImage.color = _centerDotColor;
            _centerDotImage.preserveAspect = false;
            _centerDotImage.type = Image.Type.Simple;

            LayoutCenterDot();
        }

        private void LayoutCenterDot()
        {
            if (_centerDotRect == null)
            {
                return;
            }

            _centerDotRect.anchoredPosition = Vector2.zero;
            _centerDotRect.sizeDelta = new Vector2(_centerDotDiameter, _centerDotDiameter);
        }

        private float GetShortSidePixels()
        {
            if (_selfRect == null)
            {
                _selfRect = (RectTransform)transform;
            }

            float w = _selfRect.rect.width;
            float h = _selfRect.rect.height;
            float shortSide = Mathf.Min(Mathf.Abs(w), Mathf.Abs(h));
            if (shortSide < 1f)
            {
                shortSide = 256f;
            }
            return shortSide;
        }

        private static void SetBarEnabled(Image image, bool enabled)
        {
            if (image != null)
            {
                image.enabled = enabled;
            }
        }

        private static void LayoutBar(Image image, Vector2 anchoredPosition, Vector2 sizeDelta, Color color)
        {
            if (image == null)
            {
                return;
            }

            image.color = color;

            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void StretchToParent(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        // ------------------------------------------------------------------
        // 程序化贴图
        // ------------------------------------------------------------------

        /// <summary>
        /// 生成「只有一圈彩色描边、内外都透明」的圆环贴图。
        /// </summary>
        private static Texture2D CreateRingTexture(int size, float innerRadiusRatio, float ringPixelThickness, float feather, Color color)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "CrosshairReticle_RingTex",
                hideFlags = HideFlags.HideAndDontSave
            };

            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float innerRadius = size * 0.5f * innerRadiusRatio;
            float ringPx = Mathf.Max(0.5f, ringPixelThickness * (size / 512f));
            float outerRadius = innerRadius + ringPx;
            float featherPx = Mathf.Max(0.0001f, size * feather);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    float inner = Mathf.Clamp01((distance - innerRadius) / featherPx + 0.5f);
                    float outer = Mathf.Clamp01((outerRadius - distance) / featherPx + 0.5f);
                    float ringAlpha = Mathf.Min(inner, outer);

                    Color c = color;
                    c.a *= ringAlpha;
                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D CreateDiscTexture(int size)
        {
            size = Mathf.Max(8, size);
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "CrosshairReticle_DotTex",
                hideFlags = HideFlags.HideAndDontSave
            };

            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((radius - distance) + 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static void ReleaseRuntimeAsset<T>(ref T obj) where T : Object
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }

            obj = null;
        }
    }
}
