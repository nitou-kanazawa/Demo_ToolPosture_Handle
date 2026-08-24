using UnityEngine;
using UnityEngine.UI;
using ToolPosture.Gizmo;

namespace ToolPosture.Demo
{
    /// <summary>
    /// 2D 重畳ビューからギズモを操作するデモ。
    ///
    /// 外部パラから配置した想定の重畳カメラを RenderTexture に描き、
    /// レンズ歪みシェーダを通して UI 上に表示する。その画像上のポインタ座標を
    ///   スクリーン座標 -> RawImage ローカル -> 画像ピクセル
    /// と変換して (ここがアプリ側の独自ロジックに相当)、ギズモの外部ドライブ API
    /// TryPick / BeginDrag / UpdateDrag / EndDrag に流す。
    ///
    /// 画像ピクセルからワールドのレイへの変換は DistortedOverlayViewport が持つ。
    /// ギズモの当たり判定はワールド上のコライダーなので、レイさえ正しく作れれば
    /// 投影がどうであっても掴める。ギズモ側に投影を教える必要は無い。
    /// </summary>
    [AddComponentMenu("Tool Posture/Overlay View Demo")]
    public class OverlayViewDemo : MonoBehaviour
    {

        #region 設定とライフサイクル

        public ToolPostureGizmo gizmo;

        [Tooltip("外部パラから配置した重畳用カメラ")]
        public Camera overlayCamera;

        [Header("画像")]
        public Vector2Int imageSize = new Vector2Int(640, 480);
        public Vector2 panelSize = new Vector2(432f, 324f);

        [Tooltip("半径方向のレンズ歪み係数。負で樽型。画像の隅まで可逆に保てる範囲に制限してある")]
        [Range(-0.11f, 0.11f)] public float distortionK1 = -0.10f;

        [Tooltip("未設定なら Shader.Find で解決する")]
        public Shader distortShader;

        [Header("操作")]
        [Tooltip("true のとき 2D 画面からギズモを操作する (3D ビュー側の操作は止まる)")]
        public bool controlFrom2D;

        private RenderTexture _renderTexture;
        private RawImage _image;
        private RectTransform _imageRect;
        private Material _material;
        private Text _label;
        private DistortedOverlayViewport _viewport;
        private bool _dragging;
        private bool _appliedMode;

        private void Awake()
        {
            if (gizmo == null) gizmo = FindAnyObjectByType<ToolPostureGizmo>();
            BuildOverlay();
            ApplyMode(controlFrom2D, force: true);
        }

        private void OnDestroy()
        {
            if (overlayCamera != null) overlayCamera.targetTexture = null;
            if (_renderTexture != null) _renderTexture.Release();
        }

        private void Update()
        {
            if (gizmo == null || _viewport == null) return;

            _viewport.K1 = distortionK1;
            if (_material != null) _material.SetFloat("_K1", distortionK1);

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.tKey.wasPressedThisFrame) controlFrom2D = !controlFrom2D;

            if (controlFrom2D != _appliedMode) ApplyMode(controlFrom2D, force: false);
            if (controlFrom2D) DriveFrom2D();

            UpdateLabel();
        }

        #endregion

        #region モード切替

        private void ApplyMode(bool from2D, bool force)
        {
            _appliedMode = from2D;
            if (gizmo == null) return;

            gizmo.EndDrag();
            _dragging = false;

            // 当たり判定はワールド上のコライダーなので、投影を差し替える必要は無い。
            // 2D 側で作ったレイをそのまま渡すだけでよい。
            gizmo.inputMode = from2D ? GizmoInputMode.External : GizmoInputMode.BuiltIn;
            if (!from2D) gizmo.SetHover(null);
        }

        #endregion

        #region 2D からの操作

        private void DriveFrom2D()
        {
            if (!GizmoPointer.TryRead(out PointerSample p)) return;

            bool inside = TryScreenToImagePixel(p.position, out Vector2 imagePixel);

            // ここがアプリ側の「2D のタッチ位置 -> 3D のレイ」に相当する部分
            Ray ray = _viewport.ScreenPointToRay(imagePixel);

            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool snap = kb != null && kb.ctrlKey.isPressed;

            if (_dragging)
            {
                if (p.isDown && !p.releasedThisFrame) gizmo.UpdateDrag(ray, snap);
                else { gizmo.EndDrag(); _dragging = false; }
                return;
            }

            if (!inside) { gizmo.SetHover(null); return; }

            if (p.pressedThisFrame &&
                gizmo.TryPick(ray, out GizmoHandleId id, out Vector3 point) &&
                gizmo.BeginDrag(id, ray, point))
            {
                _dragging = true;
                return;
            }

            if (!p.isTouch) gizmo.UpdateHover(ray);
        }

        /// <summary>
        /// アプリ側が持つ「2D 画面座標 -> 画像ピクセル」変換に相当する部分。
        /// ここでは RawImage の矩形へのマッピングだが、実アプリでは
        /// パン / ズーム / 表示倍率などが入る。
        /// </summary>
        public bool TryScreenToImagePixel(Vector2 screenPos, out Vector2 imagePixel)
        {
            imagePixel = default;
            if (_imageRect == null) return false;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_imageRect, screenPos, null, out Vector2 local))
                return false;

            Rect r = _imageRect.rect;
            float u = (local.x - r.xMin) / r.width;
            float v = (local.y - r.yMin) / r.height;

            imagePixel = new Vector2(u * imageSize.x, v * imageSize.y);
            return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
        }

        #endregion

        #region 構築

        private void BuildOverlay()
        {
            if (overlayCamera == null)
            {
                Debug.LogWarning("OverlayViewDemo: 重畳カメラが未設定です", this);
                return;
            }

            _renderTexture = new RenderTexture(imageSize.x, imageSize.y, 24)
            {
                name = "OverlayRT",
                antiAliasing = 2,
            };
            overlayCamera.targetTexture = _renderTexture;

            _viewport = new DistortedOverlayViewport(overlayCamera, new Vector2(imageSize.x, imageSize.y), distortionK1);

            Shader sh = distortShader != null ? distortShader : Shader.Find("ToolPosture/OverlayDistort");
            if (sh != null)
            {
                _material = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                _material.SetFloat("_K1", distortionK1);
                _material.SetFloat("_Aspect", imageSize.x / (float)imageSize.y);
            }

            var canvasGo = new GameObject("OverlayViewCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            canvasGo.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            var frameRect = frameGo.GetComponent<RectTransform>();
            frameRect.SetParent(canvasGo.transform, false);
            frameRect.anchorMin = frameRect.anchorMax = new Vector2(1f, 0f);
            frameRect.pivot = new Vector2(1f, 0f);
            frameRect.anchoredPosition = new Vector2(-16f, 16f);
            frameRect.sizeDelta = panelSize + new Vector2(8f, 34f);
            frameGo.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 0.9f);

            var imageGo = new GameObject("OverlayImage", typeof(RectTransform), typeof(RawImage));
            _imageRect = imageGo.GetComponent<RectTransform>();
            _imageRect.SetParent(frameRect, false);
            _imageRect.anchorMin = _imageRect.anchorMax = new Vector2(0.5f, 0f);
            _imageRect.pivot = new Vector2(0.5f, 0f);
            _imageRect.anchoredPosition = new Vector2(0f, 4f);
            _imageRect.sizeDelta = panelSize;

            _image = imageGo.GetComponent<RawImage>();
            _image.texture = _renderTexture;
            if (_material != null) _image.material = _material;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.SetParent(frameRect, false);
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -4f);
            labelRect.sizeDelta = new Vector2(-16f, 26f);

            _label = labelGo.GetComponent<Text>();
            _label.font = ResolveFont();
            _label.fontSize = 12;
            _label.alignment = TextAnchor.UpperLeft;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static Font ResolveFont()
        {
            Font f = null;
            try { f = Font.CreateDynamicFontFromOSFont("Consolas", 12); }
            catch { }
            return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void UpdateLabel()
        {
            if (_label == null) return;

            string mode = controlFrom2D
                ? "<color=#ffe066>2D 操作中</color>"
                : "<color=#6b7480>3D 操作中</color>";
            _label.text = string.Format("2D 重畳ビュー {0}x{1}  k1={2:F2}   {3}   [T] 切替",
                                        imageSize.x, imageSize.y, distortionK1, mode);
            _label.color = new Color(0.85f, 0.88f, 0.92f);
            _label.supportRichText = true;
        }

        #endregion
    }
}
