using UnityEngine;

namespace ToolPosture.Gizmo
{
    /// <summary>
    /// ハンドルの形状の種別。
    /// </summary>
    public enum GizmoShapeKind
    {
        /// <summary>
        /// 平面上の円弧。当たり判定は円弧に沿ったチューブ (トーラス) で取る。
        /// </summary>
        Arc = 0,

        /// <summary>
        /// 球。当たり判定は SphereCollider。
        /// </summary>
        Sphere = 1,

        /// <summary>
        /// 線分。当たり判定は線分に沿ったカプセル。
        /// 円弧のチューブと同じく断面が円なので、視線角度によらず掴み幅が一定になる。
        /// </summary>
        Segment = 2,
    }

    /// <summary>
    /// ハンドルが自分の形をコライダー側・描画側へ伝えるための記述。
    ///
    /// ハンドルは「自分がどこにどう置かれているか」を返すだけで、コライダーの生成も
    /// 描画もこの記述を読む側が行う。これによりハンドルは Physics にも Camera にも
    /// 依存しない。
    /// </summary>
    public struct GizmoHandleShape
    {
        public GizmoShapeKind Kind;

        /// <summary>
        /// 円弧の中心、または球の中心。
        /// </summary>
        public Vector3 Center;

        /// <summary>
        /// 円弧の 0 度方向 (単位ベクトル)。球では未使用。
        /// </summary>
        public Vector3 U;

        /// <summary>
        /// 円弧の +90 度方向 (単位ベクトル)。球では未使用。
        /// </summary>
        public Vector3 V;

        /// <summary>
        /// 円弧の半径、または球の半径。
        /// </summary>
        public float Radius;

        /// <summary>
        /// 掴める角度範囲の下限 [deg]。
        /// </summary>
        public float FromDeg;

        /// <summary>
        /// 掴める角度範囲の上限 [deg]。
        /// </summary>
        public float ToDeg;

        public static GizmoHandleShape Arc(Vector3 center, Vector3 u, Vector3 v, float radius,
                                           float fromDeg, float toDeg)
            => new GizmoHandleShape
            {
                Kind = GizmoShapeKind.Arc,
                Center = center,
                U = u,
                V = v,
                Radius = radius,
                FromDeg = fromDeg,
                ToDeg = toDeg,
            };

        /// <summary>
        /// 線分の長さ。Segment でのみ使う。
        /// </summary>
        public float Length;

        /// <summary>
        /// 線分ハンドル。Center は中点、U が軸方向、Radius がカプセルの半径。
        /// </summary>
        public static GizmoHandleShape Line(Vector3 start, Vector3 end, float radius)
        {
            Vector3 d = end - start;
            float length = d.magnitude;

            return new GizmoHandleShape
            {
                Kind = GizmoShapeKind.Segment,
                Center = (start + end) * 0.5f,
                U = length > 1e-6f ? d / length : Vector3.forward,
                V = Vector3.up,
                Radius = radius,
                Length = length,
                FromDeg = 0f,
                ToDeg = 360f,
            };
        }

        public static GizmoHandleShape Ball(Vector3 center, float radius)
            => new GizmoHandleShape
            {
                Kind = GizmoShapeKind.Sphere,
                Center = center,
                U = Vector3.right,
                V = Vector3.up,
                Radius = radius,
                FromDeg = 0f,
                ToDeg = 360f,
            };

        /// <summary>
        /// 全周かどうか。全周なら角度範囲の判定を省ける。
        /// </summary>
        public bool IsFullCircle => Mathf.Abs(ToDeg - FromDeg) >= 359f;

        /// <summary>
        /// 平面の法線。U から V へ右ねじの向き。
        /// </summary>
        public Vector3 Normal => Vector3.Cross(U, V);

        /// <summary>
        /// 角度 [deg] に対応する円弧上の点。
        /// </summary>
        public Vector3 PointAt(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return Center + (U * Mathf.Cos(r) + V * Mathf.Sin(r)) * Radius;
        }

        /// <summary>
        /// 角度 [deg] における進行方向 (単位接線)。
        /// </summary>
        public Vector3 TangentAt(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return (V * Mathf.Cos(r) - U * Mathf.Sin(r)).normalized;
        }

        /// <summary>
        /// ワールド上の点を平面へ落として測った角度 [deg]。
        /// </summary>
        public float AngleOf(Vector3 worldPoint)
        {
            Vector3 d = worldPoint - Center;
            return Mathf.Atan2(Vector3.Dot(d, V), Vector3.Dot(d, U)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// ワールド上の点が、この円弧の角度範囲に入っているか。
        ///
        /// コライダーは全周のチューブとして作り、範囲の絞り込みはここで行う。
        /// こうしておくと可動範囲が動的に変わってもメッシュを作り直さずに済む。
        /// </summary>
        public bool ContainsAngleOf(Vector3 worldPoint, float marginDeg = 1f)
        {
            if (Kind != GizmoShapeKind.Arc || IsFullCircle) return true;

            float a = AngleOf(worldPoint);
            float lo = Mathf.Min(FromDeg, ToDeg) - marginDeg;
            float hi = Mathf.Max(FromDeg, ToDeg) + marginDeg;

            while (a < lo) a += 360f;
            while (a > hi) a -= 360f;
            return a >= lo;
        }
    }
}
