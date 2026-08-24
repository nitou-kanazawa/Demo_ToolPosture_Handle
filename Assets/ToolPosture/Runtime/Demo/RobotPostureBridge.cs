using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using ToolPosture.Core;
using ToolPosture.Gizmo;

namespace ToolPosture.Demo
{
    /// <summary>
    /// ロボット制御側の ZYX オイラー姿勢と、LMN フレーム上の姿勢
    /// (旋回角 / 仰角 / トーチ回転角) を双方向に変換するデモ。
    ///
    /// ハンドルを操作すると ZYX の値が追従し (姿勢 -> ZYX)、
    /// パネルのボタンで ZYX を動かすとギズモが追従する (ZYX -> 姿勢)。
    /// 往復誤差も常時表示するので、変換が可逆であることがその場で確認できる。
    ///
    /// 注意: ここではロボット座標系と Unity 座標系を同じものとして扱っている。
    /// 実機と繋ぐ場合は Z-up 右手系 (ロボット) と Y-up 左手系 (Unity) の
    /// 基底変換が別途必要になる。
    /// </summary>
    [AddComponentMenu("Tool Posture/Robot Posture Bridge")]
    public class RobotPostureBridge : MonoBehaviour
    {
        #region 設定とライフサイクル

        public ToolPostureGizmo gizmo;

        [Header("ロボット側のツール座標系の割当")]
        [Tooltip("回転行列の col2。ロボット側 ToolPosture ではツール軸")]
        public Vector3 robotShaftAxis = new Vector3(0f, 0f, 1f);

        [Tooltip("回転行列の col1。ロボット側 ToolPosture では回転基準")]
        public Vector3 robotReferenceAxis = new Vector3(0f, 1f, 0f);

        [Tooltip("yRef = cross(この軸, ツール軸) の基準軸")]
        public Vector3 robotSpinWorldAxis = new Vector3(0f, 0f, 1f);

        [Tooltip("基準軸とツール軸が平行になったときのフォールバック")]
        public Vector3 robotSpinFallback = new Vector3(0f, 1f, 0f);

        [Header("操作")]
        [Tooltip("ボタン 1 回あたりの変化量 [deg]")]
        public float nudgeDeg = 5f;

        public Vector2 panelSize = new Vector2(300f, 214f);

        private Text _body;

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
        }

        #endregion

        #region 変換

        /// <summary>
        /// ロボット側の回転角の基準 (cross(ワールド軸, ツール軸))。
        /// </summary>
        public SpinReference RobotSpin
            => SpinReference.WorldAxisCross(robotSpinWorldAxis, robotSpinFallback);

        /// <summary>
        /// 現在の姿勢をロボット側の ZYX オイラー角で取り出す。
        /// </summary>
        public ZyxEulerAngles CurrentEuler
            => ZyxEulerAngles.FromRotation(
                   gizmo.Angles.GetToolRotation(gizmo.Frame, robotShaftAxis, robotReferenceAxis, RobotSpin));

        /// <summary>
        /// ロボット側の ZYX オイラー角で姿勢を設定する。
        /// </summary>
        public void Apply(ZyxEulerAngles euler)
        {
            gizmo.RefreshFrame();

            var a = gizmo.Angles;
            a.SetToolRotation(gizmo.Frame, euler.ToRotation(), robotShaftAxis, robotReferenceAxis, RobotSpin);
            gizmo.Angles = a;
        }

        /// <summary>
        /// 姿勢 -&gt; ZYX -&gt; 姿勢 と往復させたときの、工具姿勢のずれ [deg]。
        /// </summary>
        public float RoundTripResidualDeg
        {
            get
            {
                PathFrame f = gizmo.Frame;
                Quaternion before = gizmo.Angles.GetToolRotation(f, robotShaftAxis, robotReferenceAxis, RobotSpin);

                var back = gizmo.Angles;
                back.SetToolRotation(f, ZyxEulerAngles.FromRotation(before).ToRotation(),
                                     robotShaftAxis, robotReferenceAxis, RobotSpin);

                Quaternion after = back.GetToolRotation(f, robotShaftAxis, robotReferenceAxis, RobotSpin);
                return Quaternion.Angle(before, after);
            }
        }

        /// <summary>
        /// ZYX のいずれかを増減させる。0 = Roll(Z) / 1 = Pitch(Y) / 2 = Yaw(X)。
        /// </summary>
        public void Nudge(int index, float deltaDeg)
        {
            ZyxEulerAngles e = CurrentEuler;
            if (index == 0) e.zDeg += deltaDeg;
            else if (index == 1) e.yDeg += deltaDeg;
            else e.xDeg += deltaDeg;
            Apply(e);
        }

        #endregion

        #region テキスト

        private string BuildText()
        {
            ZyxEulerAngles e = CurrentEuler;
            var a = gizmo.Angles;

            return
                "<b>ロボット姿勢 (ZYX オイラー)</b>\n" +
                "──────────────────────\n" +
                string.Format("Roll  (Z) {0,9:+0.0;-0.0}°\n", e.RollDeg) +
                string.Format("Pitch (Y) {0,9:+0.0;-0.0}°\n", e.PitchDeg) +
                string.Format("Yaw   (X) {0,9:+0.0;-0.0}°\n", e.YawDeg) +
                "──────────────────────\n" +
                string.Format("<color=#9aa4b0>θ {0,7:+0.0;-0.0}°  φ {1,6:F1}°  spin {2,7:+0.0;-0.0}°</color>\n",
                              a.azimuthDeg, a.elevationDeg, a.spinAngleDeg) +
                string.Format("<color=#8fe08f>往復誤差 {0:F4}°</color>\n", RoundTripResidualDeg) +
                "<color=#6b7480>ボタンで ZYX を動かすとギズモが追従</color>";
        }

        #endregion

        #region 構築

        private static void EnsureEventSystem()
        {
            EventSystem es = EventSystem.current;
            if (es == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem));
                es = go.GetComponent<EventSystem>();
            }

            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module == null) module = es.gameObject.AddComponent<InputSystemUIInputModule>();
            if (module.point == null || module.leftClick == null) module.AssignDefaultActions();
        }

        private static Font ResolveFont()
        {
            Font f = null;
            try { f = Font.CreateDynamicFontFromOSFont("Consolas", 13); }
            catch { }
            return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void BuildUI()
        {
            Font font = ResolveFont();

            var canvasGo = new GameObject("RobotPostureCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGo.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            RectTransform panel = CreateRect("Panel", canvasGo.transform, new Color(0.05f, 0.06f, 0.08f, 0.82f));
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = new Vector2(-16f, -16f);
            panel.sizeDelta = panelSize;

            var bodyGo = new GameObject("Body", typeof(RectTransform), typeof(Text));
            var bodyRect = bodyGo.GetComponent<RectTransform>();
            bodyRect.SetParent(panel, false);
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(12f, 68f);
            bodyRect.offsetMax = new Vector2(-12f, -10f);

            _body = bodyGo.GetComponent<Text>();
            _body.font = font;
            _body.fontSize = 13;
            _body.color = new Color(0.90f, 0.93f, 0.96f);
            _body.alignment = TextAnchor.UpperLeft;
            _body.supportRichText = true;
            _body.horizontalOverflow = HorizontalWrapMode.Overflow;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _body.lineSpacing = 1.05f;

            string[] labels = { "Roll −", "Roll +", "Pitch −", "Pitch +", "Yaw −", "Yaw +" };
            float bw = (panelSize.x - 24f - 2f * 5f) / 3f;

            for (int i = 0; i < labels.Length; i++)
            {
                int index = i / 2;
                float sign = (i % 2 == 0) ? -1f : 1f;
                int column = i / 2;
                int row = i % 2;

                RectTransform br = CreateRect("Nudge" + i, panel, new Color(0.18f, 0.22f, 0.28f, 0.95f));
                br.anchorMin = br.anchorMax = new Vector2(0f, 0f);
                br.pivot = new Vector2(0f, 0f);
                br.anchoredPosition = new Vector2(12f + column * (bw + 5f), 10f + (1 - row) * 25f);
                br.sizeDelta = new Vector2(bw, 22f);

                var button = br.gameObject.AddComponent<Button>();
                button.targetGraphic = br.GetComponent<Image>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => Nudge(index, sign * nudgeDeg));

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

        #endregion
    }
}
