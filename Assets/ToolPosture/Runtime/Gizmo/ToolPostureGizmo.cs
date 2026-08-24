using System;
using UnityEngine;
using UnityEngine.InputSystem;
using ToolPosture.Core;

namespace ToolPosture.Gizmo
{
    /// <summary>
    /// ランタイム用の工具姿勢ギズモ。
    ///
    /// このコンポーネントの役割は「与えられた 1 つのフレーム (L, M, N) に対して
    /// 工具軸ベクトル X と、その軸まわりの回転を定める」ことだけ。
    /// フレームをどこから持ってくるか (経路の補間、区間の選択、法線の直交化など) は
    /// 関知せず、<see cref="Frame"/> へ代入されたものをそのまま使う。
    /// 姿勢の保持は球面表現 (theta, phi, spin) 一本で、投影角 w / t は導出値。
    ///
    /// 描画・当たり判定・入力は <see cref="RuntimeGizmo"/> と共通。
    /// </summary>
    [AddComponentMenu("Tool Posture/Tool Posture Gizmo")]
    public class ToolPostureGizmo : RuntimeGizmo
    {
        #region プリセット

        [Tooltip("角度規約と可動範囲")]
        [SerializeField] private ToolPostureProfile profile;

        #endregion

        #region 姿勢

        [SerializeField] private ToolPostureAngles angles = ToolPostureAngles.FromProjected(14f, -10f, 25f);

        #endregion

        #region ハンドル表示

        [Tooltip("傾斜角 alpha の円弧。N と工具軸が張る平面に乗る")]
        public bool showTiltArc = true;

        [Tooltip("旋回角 theta のリング。LM 平面 (母材面) に乗る")]
        public bool showAzimuthRing = true;

        public bool showAxisTip = true;
        public bool showSpinRing = true;

        [Tooltip("LMN フレームの矢印を描く")]
        public bool showFrameAxes = true;

        #endregion

        #region 状態

        private PathFrame _frame;
        private ToolPostureAngles _anglesAtDragStart;

        #endregion

        #region 公開プロパティ

        /// <summary>
        /// 角度規約と可動範囲。未設定なら組み込み既定を返すので null にならない。
        /// </summary>
        public ToolPostureProfile Profile
        {
            get => profile != null ? profile : ToolPostureProfile.Default;
            set => profile = value;
        }

        /// <summary>
        /// 工具姿勢が乗る LMN フレーム。
        ///
        /// このコンポーネントはフレームを計算しない。経路から補間する、カメラの外部パラ
        /// から求める、固定値を使う、いずれの場合も求めた結果をここへ代入する。
        /// 代入が無い間は transform の位置に置いたフォールバックを使う。
        /// </summary>
        public PathFrame Frame
        {
            get
            {
                EnsureState();
                return _frame;
            }
            set => _frame = value;
        }

        /// <summary>
        /// 工具姿勢。保持しているのは球面表現 (theta, phi, spin) そのもので、
        /// 旋回角はこの構造体の中に入っている。ここへ代入して読み戻せば、
        /// 垂直姿勢を経由しても旋回角は失われない。
        /// </summary>
        public ToolPostureAngles Angles
        {
            get => angles;
            set
            {
                angles = value;
                PostureChanged?.Invoke(this);
            }
        }

        /// <summary>
        /// 姿勢が変わったときに呼ばれる。
        /// </summary>
        public event Action<ToolPostureGizmo> PostureChanged;

        public override Vector3 Origin => _frame.Origin;

        #endregion

        #region 球面表現 (theta / phi) での入出力

        /// <summary>
        /// 旋回角 theta。母材面内で L 軸正方向から測る。
        /// </summary>
        public float AzimuthDeg => angles.azimuthDeg;

        /// <summary>
        /// 仰角 phi。90 度で工具軸が N に一致する。
        /// </summary>
        public float ElevationDeg => angles.elevationDeg;

        /// <summary>
        /// N からの傾き量 alpha = 90 - phi。
        /// </summary>
        public float TiltFromNormalDeg => angles.TiltFromNormalDeg;

        /// <summary>
        /// 旋回角が工具軸に影響するか。傾きが 0 付近では false になり、
        /// 旋回角は保持値として扱われる。
        /// </summary>
        public bool AzimuthAffectsToolAxis => angles.TiltIsSignificant();

        /// <summary>
        /// 球面表現で姿勢を与える。
        /// </summary>
        public void SetSpherical(float azimuthDeg, float elevationDeg)
        {
            var a = angles;
            a.azimuthDeg = azimuthDeg;
            a.elevationDeg = elevationDeg;
            Angles = a;
        }

        /// <summary>
        /// 動かせる傾斜角の範囲。内部値 (N からの傾き α)。
        /// tiltConvention の可動範囲そのもので、方位には依存しない。
        /// </summary>
        public void GetTiltRange(out float loDeg, out float hiDeg)
            => Profile.tiltConvention.GetArcRange(Theme.fallbackArcHalfWidthDeg, out loDeg, out hiDeg);

        #endregion

        #region 工具軸の出力

        /// <summary>
        /// 工具軸 X のワールド方向。このギズモの主たる出力。
        /// </summary>
        public Vector3 ToolAxisWorld => angles.GetAxisWorld(_frame);

        /// <summary>
        /// 工具軸 X の LMN 成分。
        /// </summary>
        public Vector3 ToolAxisLmn => angles.GetAxisLmn();

        // Quaternion での出力はここには置かない。工具軸から回転を組むには
        // 「向けたい対象のどのローカル軸を工具軸に合わせるか」という対象側の都合が要り、
        // その値はロボットのフランジと工具モデルで別物になる。
        // 対象を持っている側が Core の関数を直接呼ぶこと:
        //
        //   gizmo.Angles.GetToolRotation(gizmo.Frame, shaftAxis, referenceAxis,
        //                                gizmo.Profile.spinReference)
        //
        // 工具モデルを追従させるだけなら ToolPostureFollower が使える。

        #endregion

        #region ライフサイクル

        protected override void BuildHandles()
        {
            Handles.Clear();
            Handles.Add(new AxisTipHandle(this));
            Handles.Add(new SpinRingHandle(this));
            Handles.Add(new TiltArcHandle(this));      // 傾斜角 alpha
            Handles.Add(new AzimuthRingHandle(this));  // 旋回角 theta
        }

        /// <summary>
        /// フレームが未設定 (または無効) なら transform の位置に置いたフォールバックを使う。
        ///
        /// PathFrame は readonly フィールドを持つ構造体でシリアライズされないので、
        /// ドメインリロードや再コンパイルの直後は既定値 = 無効に戻る。
        /// 有効性で判定しておけば、フラグを別に持つより取りこぼしが無い。
        /// </summary>
        protected override void EnsureState()
        {
            if (!_frame.IsValid) _frame = PathFrame.Fallback(transform.position);
        }

        protected override void HandleKeyboard()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.digit1Key.wasPressedThisFrame) showTiltArc = !showTiltArc;
            if (kb.digit2Key.wasPressedThisFrame) showAzimuthRing = !showAzimuthRing;
            if (kb.digit3Key.wasPressedThisFrame) showAxisTip = !showAxisTip;
            if (kb.digit4Key.wasPressedThisFrame) showSpinRing = !showSpinRing;
            if (kb.digit0Key.wasPressedThisFrame) SetSpherical(angles.azimuthDeg, 90f);
        }

        protected override void OnDragBegan(GizmoHandleBase handle) => _anglesAtDragStart = angles;

        protected override void OnDragCancelled(GizmoHandleBase handle) => Angles = _anglesAtDragStart;

        #endregion

        #region 姿勢を直接与える

        /// <summary>
        /// 角度を直接与える。値は AngleConvention を通した表示値。
        /// アプリ側が独自に角度を算出する場合の入口。
        /// </summary>
        public void SetAngleDisplay(GizmoHandleId id, float displayDeg)
        {
            var a = angles;
            switch (id)
            {
                case GizmoHandleId.SpinRing:
                    a.spinAngleDeg = Profile.spinConvention.ClampInternal(
                        Profile.spinConvention.ToInternal(displayDeg));
                    break;
                case GizmoHandleId.AzimuthRing:
                    // 旋回角だけを差し替える。傾きは変わらない。
                    a.azimuthDeg = Profile.azimuthConvention.ToInternal(displayDeg);
                    break;
                case GizmoHandleId.TiltArc:
                    // 傾きだけを差し替える。旋回角は変わらない。
                    a.TiltFromNormalDeg = Profile.tiltConvention.ClampInternal(
                        Profile.tiltConvention.ToInternal(displayDeg));
                    break;
                default:
                    return;   // AxisTip は角度 1 つでは決まらない
            }
            Angles = a;
        }

        /// <summary>
        /// 工具軸をワールド方向で直接与える。
        /// </summary>
        public void SetToolAxisWorld(Vector3 worldDirection)
        {
            Vector3 lmn = _frame.WorldDirectionToLmn(worldDirection.normalized);
            if (lmn.z < 0.03f) lmn.z = 0.03f;

            var a = angles;
            a.SetAxisLmn(lmn);      // 極付近では旋回角がそのまま保たれる
            a.TiltFromNormalDeg = Profile.tiltConvention.ClampInternal(a.TiltFromNormalDeg);
            Angles = a;
        }

        #endregion

        #region 描画

        protected override void BuildBaseGeometry(GizmoMeshBuilder b)
        {
            Camera cam = Cam;
            if (cam == null || !_frame.IsValid) return;

            GizmoTheme th = Theme;
            Vector3 o = _frame.Origin;
            Vector3 camPos = EyePosition;
            float s = Scale;

            float lineHalf = PixelToWorld(th.frameAxisPixelWidth) * 0.5f;
            float headR = PixelToWorld(th.arrowHeadPixelRadius);
            float headL = PixelToWorld(th.arrowHeadPixelLength);

            if (showFrameAxes)
            {
                b.AddArrow(o, _frame.CrossFeed, s * th.crossFeedAxisLengthRatio, camPos,
                           lineHalf, headR, headL, th.frameColorL);
                b.AddArrow(o, _frame.Feed, s * th.frameAxisLengthRatio, camPos,
                           lineHalf, headR, headL, th.frameColorM);
                b.AddArrow(o, _frame.Normal, s * th.frameAxisLengthRatio, camPos,
                           lineHalf, headR, headL, th.frameColorN);
            }

            // 工具軸 X は常に描く (軸先端ハンドルの表示に依存しない)
            Vector3 axis = angles.GetAxisWorld(_frame);
            b.AddArrow(o, axis, s * th.toolAxisLengthRatio, camPos,
                       PixelToWorld(th.toolAxisPixelWidth) * 0.5f,
                       PixelToWorld(th.toolArrowHeadPixelRadius), headL, th.axisColor);

            b.AddBillboardDisc(o, cam, PixelToWorld(th.originDotPixelRadius), th.zeroTickColor);
        }

        #endregion
    }
}
