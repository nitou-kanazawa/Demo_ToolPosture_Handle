using UnityEngine;

namespace ToolPosture.Gizmo
{
    /// <summary>
    /// 軸方向の平行移動ドラッグ。
    ///
    /// 掴んだ瞬間に「掴んだ点」と「軸の向き」を固定し、以降はレイと軸直線の
    /// 最近接点だけを見る。円弧の <see cref="RayTangentDrag"/> と同じ考え方で、
    /// 弧長ではなくそのまま移動量として使う版。
    /// カメラにもスクリーン座標にも依存しない。
    /// </summary>
    public struct RayAxisDrag
    {
        /// <summary>
        /// レイと軸が平行に近いとみなす閾値。sin^2(なす角) がこれを下回ると
        /// 最近接点が発散するので値の更新を止める。0.02 は約 8 度に相当する。
        /// </summary>
        public const float MinDenominator = RayTangentDrag.MinDenominator;

        private Vector3 _anchor;
        private Vector3 _axis;
        private Vector3 _startPosition;
        private float _startAlong;
        private float _offset;
        private bool _hasStart;

        /// <summary>
        /// 掴んだ瞬間の状態を記録する。
        /// </summary>
        /// <param name="grabPoint">掴んだワールド座標。</param>
        /// <param name="axis">移動を許す軸 (単位ベクトル)。</param>
        /// <param name="startPosition">掴んだ時点の位置。</param>
        /// <param name="ray">掴んだ瞬間のレイ。</param>
        public void Begin(Vector3 grabPoint, Vector3 axis, Vector3 startPosition, Ray ray)
        {
            _anchor = grabPoint;
            _axis = axis.normalized;
            _startPosition = startPosition;
            _offset = 0f;
            _hasStart = TryAlong(ray, out _startAlong);
        }

        /// <summary>
        /// 掴んだ時点からの移動量 [world]。
        /// </summary>
        public float Offset => _offset;

        /// <summary>
        /// 現在のレイから位置を求める。
        /// レイが軸と平行に近い間は false を返し、直前の値を保つ。
        /// </summary>
        public bool TryGetPosition(Ray ray, out Vector3 position)
        {
            position = _startPosition + _axis * _offset;
            if (!TryAlong(ray, out float along)) return false;

            // 掴んだ瞬間が退化していた場合は、最初に取れたところを起点にし直す。
            if (!_hasStart)
            {
                _startAlong = along;
                _hasStart = true;
                return true;
            }

            _offset = along - _startAlong;
            position = _startPosition + _axis * _offset;
            return true;
        }

        /// <summary>
        /// 移動量をスナップ幅で丸めた位置。
        /// </summary>
        public Vector3 Snap(float step)
            => step > 0f
                ? _startPosition + _axis * (Mathf.Round(_offset / step) * step)
                : _startPosition + _axis * _offset;

        /// <summary>
        /// レイに最も近い軸直線上の点を、軸方向のパラメータ [world] で返す。
        /// </summary>
        private bool TryAlong(Ray ray, out float along)
        {
            along = 0f;

            Vector3 d = ray.direction.normalized;
            Vector3 w0 = ray.origin - _anchor;

            float b = Vector3.Dot(d, _axis);
            float denom = 1f - b * b;
            if (denom < MinDenominator) return false;

            float dd = Vector3.Dot(d, w0);
            float e = Vector3.Dot(_axis, w0);
            along = (e - b * dd) / denom;
            return true;
        }
    }
}
