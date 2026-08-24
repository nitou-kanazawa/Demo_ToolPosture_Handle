using UnityEngine;
using ToolPosture.Core;

namespace ToolPosture.Gizmo
{

    #region 共通定義

    public enum GizmoHandleId
    {
        AxisTip = 0,
        SpinRing = 1,
        WorkArc = 2,
        TravelArc = 3,
        AzimuthRing = 4,
        TiltArc = 5,
    }

    /// <summary>
    /// 狙い角ハンドルの円弧をどの平面に置くか。
    /// </summary>
    public enum WorkArcPlaneMode
    {
        /// <summary>
        /// LN 平面に固定。円弧上で読める角がそのまま AWS の狙い角 w になる。
        /// </summary>
        FixedCrossFeed = 0,

        /// <summary>
        /// N と現在の工具軸が張る平面 (= 旋回角に追従)。円弧は常に工具軸を含むので
        /// ノブが軸の上に乗る。円弧上で読める角は N からの傾き α になり、
        /// 旋回リング (θ) と組で極座標 (θ, α) を直接操作する形になる。
        /// </summary>
        FollowToolAxis = 1,
    }

    /// <summary>
    /// 投影角の可動範囲から、傾き量の上限を求めるための共通処理。
    /// </summary>
    public static class TiltLimits
    {
        /// <summary>
        /// この方位で w / t の可動範囲に収まる最大の傾き量 (tan)。
        /// </summary>
        public static float MaxTanTilt(ToolPostureGizmo g, float azimuthDeg)
        {
            float a = azimuthDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            float max = Mathf.Tan(ToolPostureAngles.MaxProjectedAngleDeg * Mathf.Deg2Rad);

            max = Mathf.Min(max, LimitFor(c, g.workConvention));
            max = Mathf.Min(max, LimitFor(s, g.travelConvention));
            return Mathf.Max(0f, max);
        }

        private static float LimitFor(float component, AngleConvention conv)
        {
            if (!conv.useLimits || Mathf.Abs(component) < 1e-4f) return float.MaxValue;
            float limitDeg = component > 0f ? conv.maxDeg : conv.minDeg;
            return Mathf.Tan(limitDeg * Mathf.Deg2Rad) / component;
        }
    }

    /// <summary>
    /// ランタイムハンドルの共通インターフェース。
    ///
    /// 当たり判定はハンドル自身では行わない。<see cref="GetShape"/> が返す形状を
    /// もとにコライダー側が判定し、掴まれたハンドルへレイが渡ってくる。
    /// ドラッグ計算はワールドのレイだけで完結するので、Camera にも
    /// スクリーン座標にも依存しない。
    /// </summary>
    public abstract class GizmoHandleBase
    {
        protected readonly ToolPostureGizmo G;

        public GizmoHandleId Id { get; }

        protected GizmoHandleBase(ToolPostureGizmo owner, GizmoHandleId id)
        {
            G = owner;
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

    #endregion

    #region 円弧ハンドル

    /// <summary>
    /// 固定平面上の円弧ハンドル。狙い角 (LN 平面) と前進後退角 (MN 平面) で共用する。
    /// 投影角を内部表現にしているため、円弧の乗る平面はもう一方の角度に依存せず固定される。
    /// </summary>
    public class ArcAngleHandle : GizmoHandleBase
    {
        private readonly bool _isWork;
        private RayTangentDrag _drag;

        public ArcAngleHandle(ToolPostureGizmo owner, bool isWork)
            : base(owner, isWork ? GizmoHandleId.WorkArc : GizmoHandleId.TravelArc)
        {
            _isWork = isWork;
        }

        public override bool Visible => _isWork
            ? G.showWorkArc && G.workArcPlane == WorkArcPlaneMode.FixedCrossFeed
            : G.showTravelArc;

        private AngleConvention Conv => _isWork ? G.workConvention : G.travelConvention;

        /// <summary>
        /// 0 度方向 (常に面法線 N)。
        /// </summary>
        private Vector3 U => G.Frame.Normal;

        /// <summary>
        /// 正方向 (狙い角なら L、前進後退角なら M)。
        /// </summary>
        private Vector3 V => _isWork ? G.Frame.CrossFeed : G.Frame.Feed;

        private float Radius => G.Scale * (_isWork ? 0.74f : 1.0f);

        private float Value
        {
            get => _isWork ? G.Angles.WorkAngleDeg : G.Angles.TravelAngleDeg;
            set
            {
                var a = G.Angles;
                if (_isWork) a.WorkAngleDeg = value;
                else a.TravelAngleDeg = value;
                G.Angles = a;
            }
        }

        public override GizmoHandleShape GetShape()
        {
            Conv.GetArcRange(G.fallbackArcHalfWidthDeg, out float lo, out float hi);
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
            if (snap) v = Conv.SnapInternal(v);
            Value = G.ClampProjected(Conv.ClampInternal(v));
        }

        public override void Draw(GizmoMeshBuilder b, bool hover, bool active)
        {
            Camera cam = G.Cam;
            if (cam == null) return;

            Vector3 o = G.Frame.Origin;
            Vector3 eye = G.EyePosition;
            Color c = _isWork ? G.workColor : G.travelColor;
            Color line = (hover || active) ? G.highlightColor : c;
            float r = Radius;
            float halfWidth = G.PixelToWorld(G.arcPixelWidth) * 0.5f;
            float thin = G.PixelToWorld(G.thinPixelWidth);

            Conv.GetArcRange(G.fallbackArcHalfWidthDeg, out float lo, out float hi);
            float value = Value;
            bool atLimit = Conv.useLimits &&
                           (Mathf.Abs(value - Conv.minDeg) < 0.05f || Mathf.Abs(value - Conv.maxDeg) < 0.05f);

            // 可動範囲を示す薄い帯
            b.AddArcBand(o, U, V, r, halfWidth * 0.5f, lo, hi,
                         GizmoMeshBuilder.Fade(atLimit ? G.limitColor : c, 0.30f));

            // 0 度から現在値までの扇形
            b.AddSector(o, U, V, r * 0.60f, 0f, value, GizmoMeshBuilder.Fade(c, 0.22f));

            // 現在値までの太い円弧
            b.AddArcBand(o, U, V, r, halfWidth, 0f, value, line);

            // 平面の基準線 (0 度方向 = N)
            b.AddScreenDashedLine(o, GizmoMeshBuilder.OnCircle(o, U, V, r * 1.14f, 0f),
                                  eye, thin, G.PixelToWorld(9f), GizmoMeshBuilder.Fade(c, 0.55f));

            // 0 度目盛りと可動範囲の端の目盛り
            b.AddRadialTick(o, U, V, r, 0f, G.PixelToWorld(16f), G.PixelToWorld(1.6f), eye, G.zeroTickColor);
            b.AddRadialTick(o, U, V, r, lo, G.PixelToWorld(10f), thin, eye,
                            GizmoMeshBuilder.Fade(G.limitColor, 0.8f));
            b.AddRadialTick(o, U, V, r, hi, G.PixelToWorld(10f), thin, eye,
                            GizmoMeshBuilder.Fade(G.limitColor, 0.8f));

            // 現在値のノブ
            Vector3 knob = GizmoMeshBuilder.OnCircle(o, U, V, r, value);
            b.AddBillboardDisc(knob, cam,
                               G.PixelToWorld(G.knobPixelRadius * ((hover || active) ? 1f : 0.7f)), line);
        }
    }

    #endregion

    #region 傾きハンドル (追従平面)

    /// <summary>
    /// N と現在の工具軸が張る平面に置く円弧ハンドル。狙い角ハンドルの
    /// WorkArcPlaneMode.FollowToolAxis 版。
    ///
    /// 平面は旋回角 theta に追従するので、円弧は常に工具軸を含む = ノブが軸の上に乗る。
    /// 円弧上で読める角は N からの傾き alpha で、旋回リング (theta) と組で
    /// 極座標を直接操作する形になる。内部表現は投影角のままで、
    ///   tan w = tan(alpha) cos(theta),  tan t = tan(alpha) sin(theta)
    /// として書き戻す。
    /// </summary>
    public class TiltArcHandle : GizmoHandleBase
    {
        private RayTangentDrag _drag;

        public TiltArcHandle(ToolPostureGizmo owner) : base(owner, GizmoHandleId.TiltArc) { }

        public override bool Visible
            => G.showWorkArc && G.workArcPlane == WorkArcPlaneMode.FollowToolAxis;

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

        private float Radius => G.Scale * 0.74f;

        /// <summary>
        /// この平面内で N から工具軸まで測った符号付き傾き角。
        /// </summary>
        private float Value
        {
            get => G.Angles.TiltFromNormalDeg;
            set
            {
                var a = G.Angles;
                a.TiltFromNormalDeg = ClampAlpha(value, a.azimuthDeg);
                G.Angles = a;
            }
        }

        /// <summary>
        /// 傾き角の可動範囲。tiltConvention の制限と、w / t 側の可動範囲から
        /// この方位で許される上限の両方を満たす範囲を返す。
        /// </summary>
        public void GetAlphaRange(float azimuthDeg, out float lo, out float hi)
        {
            AngleConvention conv = G.tiltConvention;
            float projectedHi = Mathf.Atan(TiltLimits.MaxTanTilt(G, azimuthDeg)) * Mathf.Rad2Deg;
            float projectedLo = -Mathf.Atan(TiltLimits.MaxTanTilt(G, azimuthDeg + 180f)) * Mathf.Rad2Deg;

            hi = conv.useLimits ? Mathf.Min(conv.maxDeg, projectedHi) : projectedHi;
            lo = conv.useLimits ? Mathf.Max(conv.minDeg, projectedLo) : projectedLo;
        }

        private float ClampAlpha(float value, float azimuthDeg)
        {
            GetAlphaRange(azimuthDeg, out float lo, out float hi);
            return Mathf.Clamp(value, lo, hi);
        }

        public override GizmoHandleShape GetShape()
        {
            GetAlphaRange(PlaneAzimuthDeg, out float lo, out float hi);
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
            if (snap) v = G.tiltConvention.SnapInternal(v);
            Value = v;
        }

        public override void Draw(GizmoMeshBuilder b, bool hover, bool active)
        {
            Camera cam = G.Cam;
            if (cam == null) return;

            Vector3 o = G.Frame.Origin;
            Vector3 eye = G.EyePosition;
            Vector3 u = U, v = V;
            Color c = G.workColor;
            Color line = (hover || active) ? G.highlightColor : c;
            float r = Radius;
            float halfWidth = G.PixelToWorld(G.arcPixelWidth) * 0.5f;
            float thin = G.PixelToWorld(G.thinPixelWidth);

            float azimuth = PlaneAzimuthDeg;
            GetAlphaRange(azimuth, out float lo, out float hi);
            float value = Value;
            bool atLimit = Mathf.Abs(value - lo) < 0.05f || Mathf.Abs(value - hi) < 0.05f;

            // 可動範囲の帯
            b.AddArcBand(o, u, v, r, halfWidth * 0.5f, lo, hi,
                         GizmoMeshBuilder.Fade(atLimit ? G.limitColor : c, 0.30f));

            // N から工具軸までの扇形と円弧
            b.AddSector(o, u, v, r * 0.60f, 0f, value, GizmoMeshBuilder.Fade(c, 0.22f));
            b.AddArcBand(o, u, v, r, halfWidth, 0f, value, line);

            // 0 度方向 = N
            b.AddScreenDashedLine(o, GizmoMeshBuilder.OnCircle(o, u, v, r * 1.14f, 0f),
                                  eye, thin, G.PixelToWorld(9f), GizmoMeshBuilder.Fade(c, 0.55f));

            b.AddRadialTick(o, u, v, r, 0f, G.PixelToWorld(16f), G.PixelToWorld(1.6f), eye, G.zeroTickColor);
            b.AddRadialTick(o, u, v, r, lo, G.PixelToWorld(10f), thin, eye,
                            GizmoMeshBuilder.Fade(G.limitColor, 0.8f));
            b.AddRadialTick(o, u, v, r, hi, G.PixelToWorld(10f), thin, eye,
                            GizmoMeshBuilder.Fade(G.limitColor, 0.8f));

            // 円弧が乗っている平面を示す線 (LM 平面上の倒れ方向)
            b.AddScreenDashedLine(o, o + v * r * 0.9f, eye, thin, G.PixelToWorld(7f),
                                  GizmoMeshBuilder.Fade(G.azimuthColor, 0.45f));

            // 現在値のノブ (常に工具軸の上に乗る)
            Vector3 knob = GizmoMeshBuilder.OnCircle(o, u, v, r, value);
            b.AddBillboardDisc(knob, cam,
                               G.PixelToWorld(G.knobPixelRadius * ((hover || active) ? 1f : 0.7f)), line);
        }
    }

    #endregion

    #region 軸先端ハンドル

    /// <summary>
    /// 工具軸 X の先端をドラッグして狙い角と前進後退角を同時に編集する球面ハンドル。
    /// 掴んだ点をそのまま軸方向にする直接操作。
    /// </summary>
    public class AxisTipHandle : GizmoHandleBase
    {
        public AxisTipHandle(ToolPostureGizmo owner) : base(owner, GizmoHandleId.AxisTip) { }

        public override bool Visible => G.showAxisTip;

        /// <summary>
        /// 工具軸の先端が乗る球の半径。ドラッグはこの球面上で行う。
        /// </summary>
        private float SphereRadius => G.Scale * 1.25f;

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

            ToolPostureAngles.AnglesFromAxisLmn(lmn, out float w, out float t);
            if (snap)
            {
                w = G.workConvention.SnapInternal(w);
                t = G.travelConvention.SnapInternal(t);
            }

            var a = G.Angles;
            a.WorkAngleDeg = G.ClampProjected(G.workConvention.ClampInternal(w));
            a.TravelAngleDeg = G.ClampProjected(G.travelConvention.ClampInternal(t));
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
            if (cam == null) return;

            Vector3 tip = Tip;
            Color col = (hover || active) ? G.highlightColor : G.axisColor;

            b.AddBillboardRing(tip, cam, G.PixelToWorld(G.tipPixelRadius * 1.7f),
                               G.PixelToWorld(G.thinPixelWidth), GizmoMeshBuilder.Fade(col, 0.5f));
            b.AddBillboardDisc(tip, cam,
                               G.PixelToWorld(G.tipPixelRadius * ((hover || active) ? 1f : 0.8f)), col);
        }
    }

    #endregion

    #region 回転リングハンドル

    /// <summary>
    /// 工具軸まわりの回転 (トーチ回転角) を編集するリングハンドル。
    /// </summary>
    public class SpinRingHandle : GizmoHandleBase
    {
        private RayTangentDrag _drag;

        public SpinRingHandle(ToolPostureGizmo owner) : base(owner, GizmoHandleId.SpinRing) { }

        public override bool Visible => G.showSpinRing;

        private Vector3 Axis => G.Angles.GetAxisWorld(G.Frame);

        /// <summary>
        /// スピン 0 度の基準方向。
        /// </summary>
        private Vector3 U => G.spinReference.Resolve(G.Frame, Axis);

        /// <summary>
        /// +90 度方向。Quaternion.AngleAxis(90, axis) * U と一致する。
        /// </summary>
        private Vector3 V => Vector3.Cross(Axis, U);

        private Vector3 Center => G.Frame.Origin + Axis * (G.Scale * 0.86f);

        private float Radius => G.Scale * 0.50f;

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
            if (snap) v = G.spinConvention.SnapInternal(v);
            Value = G.spinConvention.ClampInternal(v);
        }

        public override void Draw(GizmoMeshBuilder b, bool hover, bool active)
        {
            Camera cam = G.Cam;
            if (cam == null) return;

            Vector3 c = Center;
            Vector3 eye = G.EyePosition;
            Color col = G.spinColor;
            Color line = (hover || active) ? G.highlightColor : col;
            float r = Radius;
            float halfWidth = G.PixelToWorld(G.arcPixelWidth) * 0.5f;
            float thin = G.PixelToWorld(G.thinPixelWidth);

            b.AddArcBand(c, U, V, r, halfWidth * 0.55f, 0f, 360f, GizmoMeshBuilder.Fade(col, 0.45f));

            b.AddSector(c, U, V, r * 0.60f, 0f, Value, GizmoMeshBuilder.Fade(col, 0.20f));
            b.AddArcBand(c, U, V, r, halfWidth, 0f, Value, line);

            b.AddRadialTick(c, U, V, r, 0f, G.PixelToWorld(16f), G.PixelToWorld(1.6f), eye, G.zeroTickColor);
            b.AddScreenDashedLine(c, c + U * r * 1.3f, eye, thin,
                                  G.PixelToWorld(9f), GizmoMeshBuilder.Fade(G.zeroTickColor, 0.6f));

            Vector3 knob = GizmoMeshBuilder.OnCircle(c, U, V, r, Value);
            b.AddBillboardDisc(knob, cam,
                               G.PixelToWorld(G.knobPixelRadius * ((hover || active) ? 1f : 0.7f)), line);
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
    public class AzimuthRingHandle : GizmoHandleBase
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

        private float Radius => G.Scale * 1.42f;

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
            if (snap) azimuth = G.azimuthConvention.SnapInternal(azimuth);

            var angles = G.Angles;
            angles.azimuthDeg = azimuth;

            // 傾きは保つが、w / t の可動範囲を破らない範囲まで縮める。
            // 傾き 0 のときは姿勢に効かないが、旋回角は保持されるので
            // 「次にどちら向きへ倒すか」を先に決められる。
            float maxTilt = Mathf.Atan(TiltLimits.MaxTanTilt(G, azimuth)) * Mathf.Rad2Deg;
            angles.TiltFromNormalDeg = Mathf.Clamp(angles.TiltFromNormalDeg, -maxTilt, maxTilt);

            G.Angles = angles;
        }

        public override void Draw(GizmoMeshBuilder b, bool hover, bool active)
        {
            Camera cam = G.Cam;
            if (cam == null) return;

            Vector3 o = G.Frame.Origin;
            Vector3 eye = G.EyePosition;
            float r = Radius;
            float halfWidth = G.PixelToWorld(G.arcPixelWidth) * 0.5f;

            bool defined = G.AzimuthAffectsToolAxis;
            float azimuth = G.AzimuthDeg;

            Color col = G.azimuthColor;
            Color line = (hover || active) ? G.highlightColor : col;

            // 姿勢から決まらない (保持値を使っている) ときは薄く描いて区別する
            float held = defined ? 1f : 0.45f;

            b.AddArcBand(o, U, V, r, halfWidth * 0.55f, 0f, 360f,
                         GizmoMeshBuilder.Fade(col, 0.50f * held));

            b.AddRadialTick(o, U, V, r, 0f, G.PixelToWorld(18f), G.PixelToWorld(1.6f), eye,
                            GizmoMeshBuilder.Fade(G.zeroTickColor, held));

            b.AddSector(o, U, V, r * 0.55f, 0f, azimuth, GizmoMeshBuilder.Fade(col, 0.18f * held));
            b.AddArcBand(o, U, V, r, halfWidth, 0f, azimuth, GizmoMeshBuilder.Fade(line, held));

            // 工具軸を LM 平面へ落とした向き = 倒れている方向 (保持中は破線を細く)
            Vector3 dir = GizmoMeshBuilder.OnCircle(o, U, V, r, azimuth);
            b.AddScreenDashedLine(o, dir, eye, G.PixelToWorld(defined ? 1.6f : 1.0f), G.PixelToWorld(10f),
                                  GizmoMeshBuilder.Fade(col, 0.75f * held));

            b.AddBillboardDisc(dir, cam,
                               G.PixelToWorld(G.knobPixelRadius * ((hover || active) ? 1f : 0.7f)),
                               GizmoMeshBuilder.Fade(line, held));
        }
    }

    #endregion
}
