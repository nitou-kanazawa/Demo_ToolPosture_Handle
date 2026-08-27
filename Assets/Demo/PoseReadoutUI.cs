using UnityEngine;
using UnityEngine.UI;
using ToolRuntimeGizmos.Core;
using ToolRuntimeGizmos.Gizmo;
using ToolRuntimeGizmos.Tool;

namespace ToolRuntimeGizmos.Demo
{
    /// <summary>
    /// 同じ姿勢を 2 つの表現で並べて出すデモ。
    ///
    ///   グローバル … ロボットの TCP。位置 xyz と ZYX オイラー
    ///   ローカル   … LMN フレームと、その上でのトーチ姿勢 (傾斜 / 旋回 / スピン)
    ///
    /// 特異点の位置が違うので並べる意味がある。ZYX はピッチ ±90 度で z と x が
    /// 分離できず数値が飛ぶが、そのとき LMN + 角度は連続なまま。逆に傾斜 0 度では
    /// 旋回角が幾何的に決まらないが、そちらは保持値として残る。
    /// どちらの表示も姿勢そのものは常に正しい。
    /// </summary>
    [AddComponentMenu("Tool Posture/Pose Readout UI")]
    public class PoseReadoutUI : MonoBehaviour
    {
        // 参照
        [SerializeField] private ToolPoseHandle handle;
        [Tooltip("世界回転の軸割当。未設定ならシーンから探す")]
        [SerializeField] private ToolPostureFollower follower;

        // 表示
        [Tooltip("パネルの大きさ [px]")]
        public Vector2 panelSize = new Vector2(340f, 250f);
        [Tooltip("ハンドルの切り替えボタンを出す")]
        public bool showModeButtons = true;

        private Text _text;
        private Text _modeLabel;

        /// <summary>
        /// Unity とロボットの座標系の対応。アプリに合わせて差し替える。
        /// </summary>
        public HandednessConversion Conversion = HandednessConversion.SwapYZ;

        #region Lifecycle

        private void Awake()
        {
            if (handle == null) handle = FindAnyObjectByType<ToolPoseHandle>();
            if (follower == null) follower = FindAnyObjectByType<ToolPostureFollower>();
            Build();
        }

        private void Update()
        {
            if (_text == null || handle == null) return;

            _text.text = Compose();
            if (_modeLabel != null)
                _modeLabel.text = handle.Mode == ToolPoseHandle.HandleMode.Position ? "併進" : "回転";
        }

        #endregion

        #region 表示内容

        private string Compose()
        {
            ToolPose pose = handle.Pose;
            if (!pose.IsValid) return "フレーム未設定";

            // --- グローバル (ロボット系) ---
            Vector3 tcp = Conversion.ToExternal(pose.Position);
            var rpy = ZyxEulerAngles.FromRotation(Conversion.ToExternal(handle.WorldRotation));

            string lockNote = rpy.IsNearGimbalLock()
                ? "  <color=#ff9b6b>ジンバルロック付近 (z と x が分離不能)</color>"
                : "";

            // --- ローカル (LMN + トーチ姿勢) ---
            PathFrame f = pose.Frame;
            ToolPostureAngles a = pose.Angles;

            return
                "<b>グローバル  ロボット TCP</b>\n" +
                $"  位置   X {tcp.x,7:F3}   Y {tcp.y,7:F3}   Z {tcp.z,7:F3}\n" +
                $"  ZYX    Z {rpy.zDeg,7:F2}   Y {rpy.yDeg,7:F2}   X {rpy.xDeg,7:F2}{lockNote}\n" +
                "\n" +
                "<b>ローカル  LMN フレーム</b>\n" +
                $"  L 直交 {Fmt(f.CrossFeed)}\n" +
                $"  M 進行 {Fmt(f.Feed)}\n" +
                $"  N 法線 {Fmt(f.Normal)}\n" +
                "\n" +
                "<b>ローカル  トーチ姿勢</b>\n" +
                $"  傾斜角 {a.TiltFromNormalDeg,7:F2}   旋回角 {a.azimuthDeg,7:F2}\n" +
                $"  スピン {a.spinAngleDeg,7:F2}" +
                (a.TiltIsSignificant() ? "" : "   <color=#ff9b6b>傾斜 0 付近 (旋回は保持値)</color>");
        }

        private static string Fmt(Vector3 v) => $"({v.x,6:F3},{v.y,6:F3},{v.z,6:F3})";

        #endregion

        #region 構築

        private void Build()
        {
            var canvasGo = new GameObject("PoseReadoutCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGo.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            RectTransform panel = MakeRect("Panel", canvasGo.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(16f, -16f);
            panel.sizeDelta = panelSize;
            panel.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 0.86f);

            RectTransform textRect = MakeRect("Text", panel);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 12f);
            textRect.offsetMax = new Vector2(-12f, -12f);

            _text = textRect.gameObject.AddComponent<Text>();
            _text.font = ResolveFont();
            _text.fontSize = 12;
            _text.color = new Color(0.86f, 0.89f, 0.93f);
            _text.supportRichText = true;
            _text.alignment = TextAnchor.UpperLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;

            if (showModeButtons) BuildModeButtons(canvasGo.transform);
        }

        private void BuildModeButtons(Transform parent)
        {
            RectTransform row = MakeRect("ModeRow", parent);
            row.anchorMin = row.anchorMax = new Vector2(0f, 1f);
            row.pivot = new Vector2(0f, 1f);
            row.anchoredPosition = new Vector2(16f, -16f - panelSize.y - 8f);
            row.sizeDelta = new Vector2(panelSize.x, 30f);

            MakeButton(row, "併進", new Vector2(0f, 0f), new Vector2(0.32f, 1f),
                       () => handle.Mode = ToolPoseHandle.HandleMode.Position);
            MakeButton(row, "回転", new Vector2(0.34f, 0f), new Vector2(0.66f, 1f),
                       () => handle.Mode = ToolPoseHandle.HandleMode.Posture);

            RectTransform labelRect = MakeRect("ModeLabel", row);
            labelRect.anchorMin = new Vector2(0.7f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            _modeLabel = labelRect.gameObject.AddComponent<Text>();
            _modeLabel.font = ResolveFont();
            _modeLabel.fontSize = 13;
            _modeLabel.color = new Color(1f, 0.88f, 0.45f);
            _modeLabel.alignment = TextAnchor.MiddleCenter;
        }

        private void MakeButton(Transform parent, string label,
                                Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
        {
            RectTransform rect = MakeRect(label, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.16f, 0.19f, 0.24f, 0.95f);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            RectTransform textRect = MakeRect("Label", rect);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textRect.gameObject.AddComponent<Text>();
            text.font = ResolveFont();
            text.fontSize = 13;
            text.color = new Color(0.88f, 0.91f, 0.95f);
            text.alignment = TextAnchor.MiddleCenter;
            text.text = label;
            text.raycastTarget = false;
        }

        private static RectTransform MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static Font ResolveFont()
        {
            Font f = null;
            try { f = Font.CreateDynamicFontFromOSFont("Consolas", 12); }
            catch { }
            return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        #endregion
    }
}
