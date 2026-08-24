using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using ToolRuntimeGizmos.Core;
using ToolRuntimeGizmos.Gizmo;

namespace ToolRuntimeGizmos.Demo
{
    /// <summary>
    /// 数値表示とハンドル表示切替の UI。アセットに依存しないよう
    /// Canvas ごとランタイムに生成する。
    /// 角度は必ず AngleConvention を通した表示値で出す。
    /// </summary>
    [AddComponentMenu("Tool Posture/Posture Readout UI")]
    public class PostureReadoutUI : MonoBehaviour
    {

        #region 設定とライフサイクル

        public ToolPostureGizmo gizmo;

        [Tooltip("区間の表示に使う経路。未設定ならシーンから探す")]
        public WeldPath path;

        [Header("レイアウト")]
        public Vector2 panelSize = new Vector2(418f, 466f);
        public int fontSize = 14;

        private Text _body;
        private Button[] _toggleButtons;
        private Text[] _toggleLabels;
        private readonly StringBuilder _sb = new StringBuilder(1024);

        private static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.08f, 0.82f);
        private static readonly Color OnColor = new Color(0.20f, 0.42f, 0.62f, 0.95f);
        private static readonly Color OffColor = new Color(0.16f, 0.17f, 0.20f, 0.90f);

        private void Awake()
        {
            if (gizmo == null) gizmo = FindAnyObjectByType<ToolPostureGizmo>();
            EnsureEventSystem();
            BuildUI();
        }

        private void Update()
        {
            if (gizmo == null || _body == null) return;
            _body.text = BuildText();
            UpdateToggleVisuals();
        }

        #endregion

        #region テキスト

        private string BuildText()
        {
            var a = gizmo.Angles;
            Vector3 lmn = a.GetAxisLmn();
            Vector3 world = gizmo.ToolAxisWorld;
            WeldPath src = path != null ? path : FindAnyObjectByType<WeldPath>();
            int segCount = src != null ? src.SegmentCount : 0;
            int segIndex = src != null ? src.segmentIndex : 0;
            float segU = src != null ? src.segmentU : 0f;

            float tiltDeg = a.TiltFromNormalDeg;

            _sb.Clear();
            _sb.AppendLine("<b>工具姿勢  (内部表現 = 球面 θ / φ / spin)</b>");
            _sb.AppendLine("────────────────────────────────");
            _sb.AppendFormat("区間          {0} / {1}      u = {2:F2}\n",
                             segIndex + 1, Mathf.Max(segCount, 1), segU);
            _sb.AppendLine("────────────────────────────────");
            AppendAngle("仰角        φ   ", tiltDeg, gizmo.Profile.tiltConvention, "1");
            _sb.AppendFormat("<color=#6b7480>  (N からの傾き α {0:F1}°)</color>\n", tiltDeg);

            AppendAngle("旋回角      θ   ", gizmo.AzimuthDeg, gizmo.Profile.azimuthConvention, "2");
            if (!gizmo.AzimuthAffectsToolAxis)
                _sb.AppendFormat("<color=#ffb86b>  傾き {0:F2}° -> θ は保持値を使用中</color>\n", tiltDeg);

            AppendAngle("トーチ回転  spin", a.spinAngleDeg, gizmo.Profile.spinConvention, "4");

            _sb.AppendLine("────────────────────────────────");
            _sb.AppendLine("<color=#9aa4b0>投影角 (θ と φ からの導出値。ハンドルは持たない)</color>");
            AppendRaw("狙い角      w   ", a.WorkAngleDeg);
            AppendRaw("進行角      t   ", a.TravelAngleDeg);

            _sb.AppendLine("────────────────────────────────");
            _sb.AppendLine("工具軸 X (LMN)");
            _sb.AppendFormat("  L {0,7:+0.000;-0.000}  M {1,7:+0.000;-0.000}  N {2,7:+0.000;-0.000}\n",
                             lmn.x, lmn.y, lmn.z);
            _sb.AppendLine("工具軸 X (world)");
            _sb.AppendFormat("  x {0,7:+0.000;-0.000}  y {1,7:+0.000;-0.000}  z {2,7:+0.000;-0.000}\n",
                             world.x, world.y, world.z);
            _sb.AppendLine("────────────────────────────────");

            string state = gizmo.IsDragging
                ? "<color=#ffe066>操作中: " + gizmo.ActiveHandle + "  (他ハンドル非表示)</color>"
                : (gizmo.HoveredHandle.HasValue
                    ? "<color=#a8d8ff>ホバー: " + gizmo.HoveredHandle + "</color>"
                    : "<color=#6b7480>左ドラッグでハンドル操作</color>");
            _sb.AppendLine(state);
            _sb.AppendLine("<color=#6b7480>Ctrl スナップ / 右ドラッグ 視点 / ホイール ズーム</color>");
            _sb.Append("<color=#6b7480>←→ 区間  ↑↓ 区間内位置  0 垂直姿勢へ</color>");
            return _sb.ToString();
        }

        /// <summary>
        /// 規約を持たない導出値をそのまま出す。
        /// </summary>
        private void AppendRaw(string label, float deg)
            => _sb.AppendFormat("{0} {1,9:+0.0;-0.0}°      \n", label, deg);

        private void AppendAngle(string label, float internalDeg, AngleConvention conv, string key)
        {
            float display = conv.ToDisplay(internalDeg);
            bool limited = conv.useLimits &&
                           (Mathf.Abs(internalDeg - conv.MinInternal) < 0.05f ||
                            Mathf.Abs(internalDeg - conv.MaxInternal) < 0.05f);
            string value = string.Format("{0,9:+0.0;-0.0}°", display);
            if (limited) value = "<color=#ff6b5e>" + value + "</color>";
            _sb.AppendFormat("{0} {1}   {2}\n", label, value,
                             string.IsNullOrEmpty(key) ? "   " : "[" + key + "]");
        }

        #endregion

        #region 構築

        /// <summary>
        /// EventSystem を用意する。既存のものが旧 StandaloneInputModule を持っている場合、
        /// Input System 専用設定 (Active Input Handling = Input System Package) では
        /// 一切入力を拾えないので InputSystemUIInputModule に差し替える。
        /// </summary>
        private static void EnsureEventSystem()
        {
            EventSystem es = EventSystem.current;
            if (es == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem));
                es = go.GetComponent<EventSystem>();
            }

            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module == null)
            {
                var legacy = es.GetComponent<BaseInputModule>();
                if (legacy != null) Destroy(legacy);
                module = es.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            // 既定アクションには <Touchscreen>/touch*/position と press のバインドが含まれる
            if (module.point == null || module.leftClick == null)
                module.AssignDefaultActions();
        }

        private static Font ResolveFont()
        {
            Font f = null;
            try { f = Font.CreateDynamicFontFromOSFont("Consolas", 14); }
            catch { /* OS フォントが取れない環境では組み込みにフォールバック */ }
            return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void BuildUI()
        {
            Font font = ResolveFont();

            var canvasGo = new GameObject("PostureReadoutCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            RectTransform panel = CreateRect("Panel", canvasGo.transform, PanelColor);
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(16f, -16f);
            panel.sizeDelta = panelSize;

            var bodyGo = new GameObject("Body", typeof(RectTransform), typeof(Text));
            var bodyRect = bodyGo.GetComponent<RectTransform>();
            bodyRect.SetParent(panel, false);
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(14f, 58f);
            bodyRect.offsetMax = new Vector2(-14f, -12f);

            _body = bodyGo.GetComponent<Text>();
            _body.font = font;
            _body.fontSize = fontSize;
            _body.color = new Color(0.90f, 0.93f, 0.96f);
            _body.alignment = TextAnchor.UpperLeft;
            _body.supportRichText = true;
            _body.horizontalOverflow = HorizontalWrapMode.Overflow;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _body.lineSpacing = 1.05f;

            string[] labels = { "1 傾斜角弧", "2 旋回リング", "3 軸先端", "4 トーチ回転" };
            _toggleButtons = new Button[labels.Length];
            _toggleLabels = new Text[labels.Length];

            float bw = (panelSize.x - 28f - (labels.Length - 1) * 5f) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                RectTransform br = CreateRect("Toggle" + i, panel, OffColor);
                br.anchorMin = br.anchorMax = new Vector2(0f, 0f);
                br.pivot = new Vector2(0f, 0f);
                br.anchoredPosition = new Vector2(14f + i * (bw + 5f), 12f);
                br.sizeDelta = new Vector2(bw, 36f);

                var button = br.gameObject.AddComponent<Button>();
                button.targetGraphic = br.GetComponent<Image>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => ToggleHandle(index));
                _toggleButtons[i] = button;

                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                var lr = labelGo.GetComponent<RectTransform>();
                lr.SetParent(br, false);
                lr.anchorMin = Vector2.zero;
                lr.anchorMax = Vector2.one;
                lr.offsetMin = Vector2.zero;
                lr.offsetMax = Vector2.zero;

                var t = labelGo.GetComponent<Text>();
                t.font = font;
                t.fontSize = 11;
                t.alignment = TextAnchor.MiddleCenter;
                t.color = Color.white;
                t.text = labels[i];
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                _toggleLabels[i] = t;
            }
        }

        private static RectTransform CreateRect(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return rect;
        }

        private void ToggleHandle(int index)
        {
            if (gizmo == null) return;
            switch (index)
            {
                case 0: gizmo.showTiltArc = !gizmo.showTiltArc; break;
                case 1: gizmo.showAzimuthRing = !gizmo.showAzimuthRing; break;
                case 2: gizmo.showAxisTip = !gizmo.showAxisTip; break;
                case 3: gizmo.showSpinRing = !gizmo.showSpinRing; break;

            }
        }

        private void UpdateToggleVisuals()
        {
            if (_toggleButtons == null || gizmo == null) return;
            bool[] states =
            {
                gizmo.showTiltArc, gizmo.showAzimuthRing, gizmo.showAxisTip, gizmo.showSpinRing

            };
            for (int i = 0; i < _toggleButtons.Length && i < states.Length; i++)
            {
                var image = _toggleButtons[i].targetGraphic as Image;
                if (image != null) image.color = states[i] ? OnColor : OffColor;
                if (_toggleLabels[i] != null)
                    _toggleLabels[i].color = states[i] ? Color.white : new Color(0.55f, 0.58f, 0.62f);
            }
        }

        #endregion
    }
}
