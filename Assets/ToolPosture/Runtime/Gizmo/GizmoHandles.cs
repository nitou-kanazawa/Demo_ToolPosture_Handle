using UnityEngine;
using ToolRuntimeGizmos.Core;

namespace ToolRuntimeGizmos.Gizmo
{

    #region 共通定義

    /// <summary>
    /// ハンドルの種類。姿勢の保持が球面表現 (theta, phi, spin) なので、
    /// ハンドルもそれに 1 対 1 で対応する。投影角 w / t は導出値であり、
    /// 傾斜角と旋回角で一意に決まるため専用のハンドルは持たない。
    /// </summary>
    public enum GizmoHandleId
    {
        // 姿勢ギズモ
        AxisTip = 0,
        SpinRing = 1,
        AzimuthRing = 2,
        TiltArc = 3,

        // 位置ギズモ (軸方向の平行移動)
        TranslateX = 10,
        TranslateY = 11,
        TranslateZ = 12,
    }

    /// <summary>
    /// ランタイムハンドルの共通インターフェース。
    ///
    /// 当たり判定はハンドル自身では行わない。<see cref="GetShape"/> が返す形状を
    /// もとにコライダー側が判定し、掴まれたハンドルへレイが渡ってくる。
    /// ドラッグ計算はワールドのレイだけで完結するので、Camera にも
    /// スクリーン座標にも依存しない。
    /// </summary>
    internal abstract class GizmoHandleBase
    {
        /// <summary>
        /// 描画とスケールの供給元。姿勢か位置かに依らず共通の部分だけを見る。
        /// </summary>
        protected readonly RuntimeGizmo Gizmo;

        public GizmoHandleId Id { get; }

        protected GizmoHandleBase(RuntimeGizmo owner, GizmoHandleId id)
        {
            Gizmo = owner;
            Id = id;
        }

        public abstract bool Visible { get; }

        /// <summary>
        /// このハンドルの現在の形状。コライダーの追従と描画の両方がこれを読む。
        /// </summary>
        public abstract GizmoHandleShape GetShape();

        /// <summary>
        /// 掴んだ瞬間。掴み位置と現在値を記録して値の飛びを防ぐ。
        /// </summary>
        /// <param name="ray">掴んだ瞬間のレイ。</param>
        /// <param name="grabPoint">掴んだワールド座標 (コライダーのヒット点)。</param>
        public abstract void BeginDrag(Ray ray, Vector3 grabPoint);

        public abstract void Drag(Ray ray, bool snap);

        /// <summary>
        /// ドラッグ終了時の後片付け。必要なハンドルだけが上書きする。
        /// </summary>
        public virtual void EndDrag() { }

        public abstract void Draw(GizmoMeshBuilder b, bool hover, bool active);

        /// <summary>
        /// 掴んだ点が無い場合に、レイから円弧上の掴み角を推定する。
        /// レイ上で中心に最も近い点を平面へ落とすので、視線が平面に寝ていても破綻しない。
        /// </summary>
        protected static float EstimateGrabAngle(GizmoHandleShape shape, Ray ray, float fallbackDeg)
        {
            Vector3 d = ray.direction.normalized;
            float t = Mathf.Max(0f, Vector3.Dot(shape.Center - ray.origin, d));
            Vector3 p = ray.origin + d * t;

            Vector3 rel = p - shape.Center;
            float x = Vector3.Dot(rel, shape.U);
            float y = Vector3.Dot(rel, shape.V);
            if (x * x + y * y < 1e-12f) return fallbackDeg;

            return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 掴み角を決める。コライダーのヒット点があればそれを使い、
        /// 無ければレイから推定する。
        /// </summary>
        protected static float ResolveGrabAngle(GizmoHandleShape shape, Ray ray,
                                                Vector3 grabPoint, float fallbackDeg)
        {
            Vector3 rel = grabPoint - shape.Center;
            float x = Vector3.Dot(rel, shape.U);
            float y = Vector3.Dot(rel, shape.V);

            // ヒット点が中心付近 = 有効な掴み点が渡ってきていない
            if (x * x + y * y < shape.Radius * shape.Radius * 0.04f)
                return EstimateGrabAngle(shape, ray, fallbackDeg);

            return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        }
    }

    /// <summary>
    /// 姿勢ギズモのハンドルの共通部分。
    /// 姿勢や規約を読むので、供給元を ToolPostureGizmo として持ち直す。
    /// </summary>
    internal abstract class PostureHandleBase : GizmoHandleBase
    {
        protected readonly ToolPostureGizmo G;

        protected PostureHandleBase(ToolPostureGizmo owner, GizmoHandleId id) : base(owner, id)
        {
            G = owner;
        }
    }

    #endregion

    #region 傾きハンドル (追従平面)

    /// <summary>
    /// N と現在の工具軸が張る平面に置く円弧ハンドル。傾斜角 alpha を編集する。
    ///
    /// 平面は旋回角 theta に追従するので、円弧は常に工具軸を含む = ノブが軸の上に乗る。
    /// 旋回リング (theta) と組で球面座標 (theta, alpha) を直接操作する形になり、
    /// この 2 つで工具軸は一意に決まる。
    /// </summary>
    internal class TiltArcHandle : PostureHandleBase
    {
        private RayTangentDrag _drag;

        public TiltArcHandle(ToolPostureGizmo owner) : base(owner, GizmoHandleId.TiltArc) { }

        public override bool Visible => G.showTiltArc;

        /// <summary>
        /// 円弧が乗る平面の方位 = 姿勢が持つ旋回角そのもの。
        /// 旋回角は姿勢の中に保持されているので、傾きを 0 にしても平面は動かない。
        /// また仰角は 90 度を超えられるので、垂直をまたいでも旋回角は 180 度飛ばない。
        /// </summary>
        public float PlaneAzimuthDeg => G.Angles.azimuthDeg;

        /// <summary>
        /// 0 度方向 = 面法線 N。
        /// </summary>
        private Vector3 U => G.Frame.Normal;

        /// <summary>
        /// + 方向 = 工具が倒れている向き d(theta)。
        /// </summary>
        private Vector3 V
        {
            get
            {
                float a = PlaneAzimuthDeg * Mathf.Deg2Rad;
                return (G.Frame.CrossFeed * Mathf.Cos(a) + G.Frame.Feed * Mathf.Sin(a)).normalized;
            }
        }

        private float Radius => G.Scale * G.Theme.tiltArcRadiusRatio;

        /// <summary>
        /// この平面内で N から工具軸まで測った符号付き傾き角。
        /// </summary>
        private float Value
        {
            get => G.Angles.TiltFromNormalDeg;
            set
            {
                var a = G.Angles;
                a.TiltFromNormalDeg = G.Profile.tiltConvention.ClampInternal(value);
                G.Angles = a;
            }
        }

        /// <summary>
        /// 傾き角の可動範囲 (内部値)。tiltConvention の範囲そのもの。
        /// </summary>
        public void GetAlphaRange(out float lo, out float hi) => G.GetTiltRange(out lo, out hi);

        public override GizmoHandleShape GetShape()
        {
            GetAlphaRange(out float lo, out float hi);
            return GizmoHandleShape.Arc(G.Frame.Origin, U, V, Radius, lo, hi);
        }

        public override void BeginDrag(Ray ray, Vector3 grabPoint)
        {
            GizmoHandleShape shape = GetShape();
            float value = Value;
            _drag.Begin(shape, ResolveGrabAngle(shape, ray, grabPoint, value), value, ray);
        }

        public override void Drag(Ray ray, bool snap)
        {
            if (!_drag.TryGetValue(ray, out float v)) return;
            if (snap) v = G.Profile.tiltConvention.SnapInternal(v);
            Value = v;
        }

        public override void Draw(GizmoMeshBuilder b, bool hover, bool active)
        {
            Camera cam = G.Cam;
            GizmoTheme th = G.Theme;
            if (cam == null) return;

            Vector3 o = G.Frame.Origin;
            Vector3 eye = G.EyePosition;
            Vector3 u = U, v = V;
            Color c = th.tiltColor;
            Color line = (hover || active) ? th.highlightColor : c;
            float r = Radius;
            float halfWidth = G.PixelToWorld(th.arcPixelWidth) * 0.5f;
            float thin = G.PixelToWorld(th.thinPixelWidth);

            GetAlphaRange(out float lo, out float hi);
            float value = Value;
            bool atLimit = Mathf.Abs(value - lo) < 0.05f || Mathf.Abs(value - hi) < 0.05f;

            // 可動範囲の帯
            b.AddArcBand(o, u, v, r, halfWidth * 0.5f, lo, hi,
                         GizmoMeshBuilder.Fade(atLimit ? th.limitColor : c, 0.30f));

            // N から工具軸までの扇形と円弧
            b.AddSector(o, u, v, r * 0.60f, 0f, value, GizmoMeshBuilder.Fade(c, 0.22f));
            b.AddArcBand(o, u, v, r, halfWidth, 0f, value, line);

            // 0 度方向 = N
            b.AddScreenDashedLine(o, GizmoMeshBuilder.OnCircle(o, u, v, r * 1.14f, 0f),
                                  eye, thin, G.PixelToWorld(th.dashPixelLength), GizmoMeshBuilder.Fade(c, 0.55f));

            b.AddRadialTick(o, u, v, r, 0f, G.PixelToWorld(th.tickPixelLength), G.PixelToWorld(th.tickPixelWidth), eye, th.zeroTickColor);
            b.AddRadialTick(o, u, v, r, lo, G.PixelToWorld(th.limitTickPixelLength), thin, eye,
                            GizmoMeshBuilder.Fade(th.limitColor, 0.8f));
            b.AddRadialTick(o, u, v, r, hi, G.PixelToWorld(th.limitTickPixelLength), thin, eye,
                            GizmoMeshBuilder.Fade(th.limitColor, 0.8f));

            // 円弧が乗っている平面を示す線 (LM 平面上の倒れ方向)
            b.AddScreenDashedLine(o, o + v * r * 0.9f, eye, thin, G.PixelToWorld(th.dashPixelLength),
                                  GizmoMeshBuilder.Fade(th.azimuthColor, 0.45f));

            // 現在値のノブ (常に工具軸の上に乗る)
            Vector3 knob = GizmoMeshBuilder.OnCircle(o, u, v, r, value);
            b.AddBillboardDisc(knob, cam,
                               G.PixelToWorld(th.knobPixelRadius * ((hover || active) ? 1f : 0.7f)), line);
        }
    }

    #endregion

    #region 軸先端ハンドル

    /// <summary>
    /// 工具軸 X の先端をドラッグして旋回角と傾斜角を同時に編集する球面ハンドル。
    /// 掴んだ点をそのまま軸方向にする直接操作。
    /// </summary>
    internal class AxisTipHandle : PostureHandleBase
    {
        public AxisTipHandle(ToolPostureGizmo owner) : base(owner, GizmoHandleId.AxisTip) { }

        public override bool Visible => G.showAxisTip;

        /// <summary>
        /// 工具軸の先端が乗る球の半径。ドラッグはこの球面上で行う。
        /// </summary>
        private float SphereRadius => G.Scale * G.Theme.toolAxisLengthRatio;

        private Vector3 Tip => G.Frame.Origin + G.Angles.GetAxisWorld(G.Frame) * SphereRadius;

        public override GizmoHandleShape GetShape()
            => GizmoHandleShape.Ball(Tip, G.PixelToWorld(G.TipHitPixelRadius));

        public override void BeginDrag(Ray ray, Vector3 grabPoint) { }

        public override void Drag(Ray ray, bool snap)
        {
            Vector3 dir = ClosestDirectionOnSphere(ray, G.Frame.Origin, SphereRadius);
            Vector3 lmn = G.Frame.WorldDirectionToLmn(dir);

            // 母材の裏側 (N 成分が負) には行かせない
            if (lmn.z < 0.03f) lmn.z = 0.03f;

            // 保持している角 (theta / alpha) の側で丸めて縛る。
            // 投影角を経由すると、掴んだ向きと結果がずれる。
            var a = G.Angles;
            a.SetAxisLmn(lmn);

            if (snap)
            {
                a.azimuthDeg = G.Profile.azimuthConvention.SnapInternal(a.azimuthDeg);
                a.TiltFromNormalDeg = G.Profile.tiltConvention.SnapInternal(a.TiltFromNormalDeg);
            }
            a.TiltFromNormalDeg = G.Profile.tiltConvention.ClampInternal(a.TiltFromNormalDeg);

            G.Angles = a;
        }

        /// <summary>
        /// 中心 center・半径 radius の球面上で、レイに最も近い点の方向を返す。
        /// 球から外れていても最近点を球面へ射影するので、ドラッグが途切れない。
        /// </summary>
        public static Vector3 ClosestDirectionOnSphere(Ray ray, Vector3 center, float radius)
        {
            Vector3 d = ray.direction.normalized;
            Vector3 oc = ray.origin - center;

            float b = Vector3.Dot(oc, d);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float disc = b * b - c;

            if (disc >= 0f)
            {
                float s = Mathf.Sqrt(disc);
                float t0 = -b - s;
                float t1 = -b + s;
                float t = t0 >= 0f ? t0 : t1;
                if (t >= 0f)
                {
                    Vector3 hit = ray.origin + d * t - center;
                    if (hit.sqrMagnitude > 1e-10f) return hit.normalized;
                }
            }

            float tc = Mathf.Max(0f, Vector3.Dot(center - ray.origin, d));
            Vector3 p = ray.origin + d * tc - center;
            return p.sqrMagnitude < 1e-10f ? d : p.normalized;
        }

        public override void Draw(GizmoMeshBuilder b, bool hover, bool active)
        {
            Camera cam = G.Cam;
            GizmoTheme th = G.Theme;
            if (cam == null) return;

            Vector3 tip = Tip;
            Color col = (hover || active) ? th.highlightColor : th.axisColor;

            b.AddBillboardRing(tip, cam, G.PixelToWorld(th.tipPixelRadius * 1.7f),
                               G.PixelToWorld(th.thinPixelWidth), GizmoMeshBuilder.Fade(col, 0.5f));
            b.AddBillboardDisc(tip, cam,
                               G.PixelToWorld(th.tipPixelRadius * ((hover || active) ? 1f : 0.8f)), col);
        }
    }

    #endregion

    #region 回転リングハンドル

    /// <summary>
    /// 工具軸まわりの回転 (トーチ回転角) を編集するリングハンドル。
    /// </summary>
    internal class SpinRingHandle : PostureHandleBase
    {
        private RayTangentDrag _drag;

        public SpinRingHandle(ToolPostureGizmo owner) : base(owner, GizmoHandleId.SpinRing) { }

        public override bool Visible => G.showSpinRing;

        private Vector3 Axis => G.Angles.GetAxisWorld(G.Frame);

        /// <summary>
        /// スピン 0 度の基準方向。
        /// </summary>
        private Vector3 U => G.Profile.spinReference.Resolve(G.Frame, Axis);

        /// <summary>
        /// +90 度方向。Quaternion.AngleAxis(90, axis) * U と一致する。
        /// </summary>
        private Vector3 V => Vector3.Cross(Axis, U);

        private Vector3 Center => G.Frame.Origin + Axis * (G.Scale * G.Theme.spinRingOffsetRatio);

        private float Radius => G.Scale * G.Theme.spinRingRadiusRatio;

        private float Value
        {
            get => G.Angles.spinAngleDeg;
            set
            {
                var a = G.Angles;
                a.spinAngleDeg = value;
                G.Angles = a;
            }
        }

        public override GizmoHandleShape GetShape()
            => GizmoHandleShape.Arc(Center, U, V, Radius, 0f, 360f);

        public override void BeginDrag(Ray ray, Vector3 grabPoint)
        {
            GizmoHandleShape shape = GetShape();
            float value = Value;
            _drag.Begin(shape, ResolveGrabAngle(shape, ray, grabPoint, value), value, ray);
        }

        public override void Drag(Ray ray, bool snap)
        {
            if (!_drag.TryGetValue(ray, out float v)) return;
            if (snap) v = G.Profile.spinConvention.SnapInternal(v);
            Value = G.Profile.spinConvention.ClampInternal(v);
        }

        public override void Draw(GizmoMeshBuilder b, bool hover, bool active)
        {
            Camera cam = G.Cam;
            GizmoTheme th = G.Theme;
            if (cam == null) return;

            Vector3 c = Center;
            Vector3 eye = G.EyePosition;
            Color col = th.spinColor;
            Color line = (hover || active) ? th.highlightColor : col;
            float r = Radius;
            float halfWidth = G.PixelToWorld(th.arcPixelWidth) * 0.5f;
            float thin = G.PixelToWorld(th.thinPixelWidth);

            b.AddArcBand(c, U, V, r, halfWidth * 0.55f, 0f, 360f, GizmoMeshBuilder.Fade(col, 0.45f));

            b.AddSector(c, U, V, r * 0.60f, 0f, Value, GizmoMeshBuilder.Fade(col, 0.20f));
            b.AddArcBand(c, U, V, r, halfWidth, 0f, Value, line);

            b.AddRadialTick(c, U, V, r, 0f, G.PixelToWorld(th.tickPixelLength), G.PixelToWorld(th.tickPixelWidth), eye, th.zeroTickColor);
            b.AddScreenDashedLine(c, c + U * r * 1.3f, eye, thin,
                                  G.PixelToWorld(th.dashPixelLength), GizmoMeshBuilder.Fade(th.zeroTickColor, 0.6f));

            Vector3 knob = GizmoMeshBuilder.OnCircle(c, U, V, r, Value);
            b.AddBillboardDisc(knob, cam,
                               G.PixelToWorld(th.knobPixelRadius * ((hover || active) ? 1f : 0.7f)), line);
        }
    }

    #endregion

    #region 旋回リングハンドル

    /// <summary>
    /// LM 平面 (母材面) 上に寝かせた回転ハンドル。工具軸を「どちら向きに倒すか」を
    /// N まわりに回す。0 度は L 軸正方向。
    ///
    /// N からの傾き量は掴んだ時点の値を保ち、方位だけを変える。内部表現 (投影角) は
    ///   tan w = r cos(theta),  tan t = r sin(theta)
    /// で再構成するので、保持している値は投影角のまま変わらない。
    /// </summary>
    internal class AzimuthRingHandle : PostureHandleBase
    {
        /// <summary>
        /// 旋回角が工具軸に影響するとみなす最小の傾き [deg]。
        /// </summary>
        public const float MinTiltDeg = 0.5f;

        private RayTangentDrag _drag;

        public AzimuthRingHandle(ToolPostureGizmo owner) : base(owner, GizmoHandleId.AzimuthRing) { }

        public override bool Visible => G.showAzimuthRing;

        /// <summary>
        /// 0 度方向 = L (直交方向)。
        /// </summary>
        private Vector3 U => G.Frame.CrossFeed;

        /// <summary>
        /// +90 度方向 = M (進行方向)。
        /// </summary>
        private Vector3 V => G.Frame.Feed;

        private float Radius => G.Scale * G.Theme.azimuthRingRadiusRatio;

        /// <summary>
        /// 旋回角が姿勢そのものから決まるか。false なら保持値を表示・編集している。
        /// </summary>
        public bool IsDefined => G.AzimuthAffectsToolAxis;

        // 傾き 0 でも掴める。姿勢は変わらないが、次に起こす方向を先に決められる。
        public override GizmoHandleShape GetShape()
            => GizmoHandleShape.Arc(G.Frame.Origin, U, V, Radius, 0f, 360f);

        public override void BeginDrag(Ray ray, Vector3 grabPoint)
        {
            GizmoHandleShape shape = GetShape();
            float start = G.Angles.azimuthDeg;
            _drag.Begin(shape, ResolveGrabAngle(shape, ray, grabPoint, start), start, ray);
        }

        public override void Drag(Ray ray, bool snap)
        {
            if (!_drag.TryGetValue(ray, out float azimuth)) return;
            if (snap) azimuth = G.Profile.azimuthConvention.SnapInternal(azimuth);

            // 方位だけを変え、傾きはそのまま残す。
            // 傾き 0 のときは姿勢に効かないが、旋回角は保持されるので
            // 「次にどちら向きへ倒すか」を先に決められる。
            var angles = G.Angles;
            angles.azimuthDeg = azimuth;
            G.Angles = angles;
        }

        public override void Draw(GizmoMeshBuilder b, bool hover, bool active)
        {
            Camera cam = G.Cam;
            GizmoTheme th = G.Theme;
            if (cam == null) return;

            Vector3 o = G.Frame.Origin;
            Vector3 eye = G.EyePosition;
            float r = Radius;
            float halfWidth = G.PixelToWorld(th.arcPixelWidth) * 0.5f;

            bool defined = G.AzimuthAffectsToolAxis;
            float azimuth = G.AzimuthDeg;

            Color col = th.azimuthColor;
            Color line = (hover || active) ? th.highlightColor : col;

            // 姿勢から決まらない (保持値を使っている) ときは薄く描いて区別する
            float held = defined ? 1f : 0.45f;

            b.AddArcBand(o, U, V, r, halfWidth * 0.55f, 0f, 360f,
                         GizmoMeshBuilder.Fade(col, 0.50f * held));

            b.AddRadialTick(o, U, V, r, 0f, G.PixelToWorld(th.tickPixelLength), G.PixelToWorld(th.tickPixelWidth), eye,
                            GizmoMeshBuilder.Fade(th.zeroTickColor, held));

            b.AddSector(o, U, V, r * 0.55f, 0f, azimuth, GizmoMeshBuilder.Fade(col, 0.18f * held));
            b.AddArcBand(o, U, V, r, halfWidth, 0f, azimuth, GizmoMeshBuilder.Fade(line, held));

            // 工具軸を LM 平面へ落とした向き = 倒れている方向 (保持中は破線を細く)
            Vector3 dir = GizmoMeshBuilder.OnCircle(o, U, V, r, azimuth);
            b.AddScreenDashedLine(o, dir, eye, G.PixelToWorld(defined ? 1.6f : 1.0f), G.PixelToWorld(th.dashPixelLength),
                                  GizmoMeshBuilder.Fade(col, 0.75f * held));

            b.AddBillboardDisc(dir, cam,
                               G.PixelToWorld(th.knobPixelRadius * ((hover || active) ? 1f : 0.7f)),
                               GizmoMeshBuilder.Fade(line, held));
        }
    }

    #endregion
}
