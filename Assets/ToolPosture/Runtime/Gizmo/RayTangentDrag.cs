using UnityEngine;

namespace ToolPosture.Gizmo
{
    /// <summary>
    /// 円弧ハンドルの回転ドラッグ。
    ///
    /// 掴んだ瞬間に「掴んだ点」と「その点での接線」を固定し、以降はレイと接線直線の
    /// 最近接点だけを見る。UnityEditor.Handles や Runtime Transform Gizmos と同じ
    /// 考え方を、スクリーン座標ではなくワールドのレイで行う版。
    ///
    /// 光線と円弧平面の交点から極角を取る方式は、視線が平面に寝ると交点が遠方へ
    /// 飛んで発散する。接線へ投影する方式にはその破綻が無い。
    /// カメラにもビューポートにも依存しないので、アプリが独自の 2D -&gt; 3D 変換で
    /// 作ったレイをそのまま渡せる。
    /// </summary>
    public struct RayTangentDrag
    {
        /// <summary>
        /// レイと接線が平行に近いとみなす閾値。sin^2(なす角) がこれを下回ると
        /// 最近接点が発散するので値の更新を止める。0.02 は約 8 度に相当する。
        /// </summary>
        public const float MinDenominator = 0.02f;

        private Vector3 _anchor;
        private Vector3 _tangent;
        private float _startValueDeg;
        private float _startAlong;
        private float _degPerUnit;
        private float _lastValueDeg;
        private bool _hasStart;

        /// <summary>
        /// 掴んだ瞬間の状態を記録する。
        /// </summary>
        /// <param name="shape">掴んだ円弧。</param>
        /// <param name="grabAngleDeg">円弧上のどこを掴んだか [deg]。</param>
        /// <param name="startValueDeg">掴んだ時点のハンドルの値 [deg]。</param>
        /// <param name="ray">掴んだ瞬間のレイ。</param>
        public void Begin(GizmoHandleShape shape, float grabAngleDeg, float startValueDeg, Ray ray)
        {
            _anchor = shape.PointAt(grabAngleDeg);
            _tangent = shape.TangentAt(grabAngleDeg);
            _startValueDeg = startValueDeg;
            _lastValueDeg = startValueDeg;

            // 弧長 -> 角度。半径が小さいほど同じ移動量で大きく回る。
            _degPerUnit = shape.Radius > 1e-6f ? Mathf.Rad2Deg / shape.Radius : 0f;

            _hasStart = TryAlong(ray, out _startAlong);
        }

        /// <summary>
        /// 現在のレイからハンドルの値を求める。
        /// レイが接線と平行に近い間は false を返し、直前の値を保つ。
        /// </summary>
        public bool TryGetValue(Ray ray, out float valueDeg)
        {
            valueDeg = _lastValueDeg;
            if (!TryAlong(ray, out float along)) return false;

            // 掴んだ瞬間が退化していた場合は、最初に取れたところを起点にし直す。
            if (!_hasStart)
            {
                _startAlong = along;
                _hasStart = true;
                return true;
            }

            valueDeg = _startValueDeg + (along - _startAlong) * _degPerUnit;
            _lastValueDeg = valueDeg;
            return true;
        }

        /// <summary>
        /// レイに最も近い接線直線上の点を、接線方向のパラメータ [world] で返す。
        ///
        /// 直線 P(s) = ray.origin + s * d と Q(u) = anchor + u * tangent の最近接で、
        /// d も tangent も単位ベクトルなので分母は 1 - (d . tangent)^2 になる。
        /// </summary>
        private bool TryAlong(Ray ray, out float along)
        {
            along = 0f;

            Vector3 d = ray.direction.normalized;
            Vector3 w0 = ray.origin - _anchor;

            float b = Vector3.Dot(d, _tangent);
            float denom = 1f - b * b;
            if (denom < MinDenominator) return false;

            float dd = Vector3.Dot(d, w0);
            float e = Vector3.Dot(_tangent, w0);
            along = (e - b * dd) / denom;
            return true;
        }
    }
}
