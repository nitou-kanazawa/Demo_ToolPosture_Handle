using System;
using UnityEngine;

namespace ToolPosture.Core
{
    /// <summary>
    /// 1 つの角度に対する表示・操作の規約。
    ///
    /// 内部値 (internal) は ToolPostureAngles が保持する正準値で、常に
    ///   0 度 = 工具軸が N (スピンは基準ベクトル) / 正方向 = L または M
    /// という固定の意味を持つ。
    ///
    /// 表示値 (display) は現場やシステムごとの規約に合わせた値で、
    /// 0 度位置のオフセットと回転方向の反転で相互変換する。
    /// ギズモの 0 度目盛りと数値表示は必ずこの規約を通すので、
    /// 規約を差し替えれば表示と操作の両方が同時に追従する。
    /// </summary>
    [Serializable]
    public class AngleConvention
    {
        [Tooltip("表示上の 0 度位置。内部 0 度をこの値として表示する")]
        public float zeroOffsetDeg = 0f;

        [Tooltip("回転の正方向を反転する")]
        public bool invertDirection = false;

        [Tooltip("可動範囲で制限する")]
        public bool useLimits = true;

        [Tooltip("内部値の下限 [deg]")]
        public float minDeg = -60f;

        [Tooltip("内部値の上限 [deg]")]
        public float maxDeg = 60f;

        [Tooltip("スナップ幅 [deg]。0 以下でスナップ無効。表示値の刻みでスナップする")]
        public float snapDeg = 5f;

        public static AngleConvention Ranged(float min, float max, float snap = 5f)
            => new AngleConvention { useLimits = true, minDeg = min, maxDeg = max, snapDeg = snap };

        public static AngleConvention Unlimited(float snap = 5f)
            => new AngleConvention { useLimits = false, minDeg = -180f, maxDeg = 180f, snapDeg = snap };

        /// <summary>
        /// 内部値を「N からの傾き α」、表示値を「LM 平面からの仰角 φ = 90 - α」とする規約。
        /// ハンドルの幾何 (0 度 = N) は変えずに、表示と入力だけを仰角に揃える。
        /// </summary>
        public static AngleConvention Elevation(float minTiltDeg = -60f, float maxTiltDeg = 60f, float snap = 5f)
            => new AngleConvention
            {
                zeroOffsetDeg = 90f,
                invertDirection = true,
                useLimits = true,
                minDeg = minTiltDeg,
                maxDeg = maxTiltDeg,
                snapDeg = snap,
            };

        /// <summary>
        /// 内部値 -> 表示値。
        /// </summary>
        public float ToDisplay(float internalDeg)
            => (invertDirection ? -internalDeg : internalDeg) + zeroOffsetDeg;

        /// <summary>
        /// 表示値 -> 内部値。
        /// </summary>
        public float ToInternal(float displayDeg)
        {
            float v = displayDeg - zeroOffsetDeg;
            return invertDirection ? -v : v;
        }

        /// <summary>
        /// 可動範囲でクランプする (内部値)。
        /// </summary>
        public float ClampInternal(float internalDeg)
            => useLimits ? Mathf.Clamp(internalDeg, minDeg, maxDeg) : internalDeg;

        /// <summary>
        /// 表示値の刻みでスナップする (入出力とも内部値)。
        /// </summary>
        public float SnapInternal(float internalDeg)
        {
            if (snapDeg <= 0f) return internalDeg;
            float d = ToDisplay(internalDeg);
            return ToInternal(Mathf.Round(d / snapDeg) * snapDeg);
        }

        /// <summary>
        /// ギズモが円弧を描く内部値の範囲。制限が無い場合は既定の幅を返す。
        /// </summary>
        public void GetArcRange(float fallbackHalfWidthDeg, out float lo, out float hi)
        {
            if (useLimits)
            {
                lo = Mathf.Min(minDeg, maxDeg);
                hi = Mathf.Max(minDeg, maxDeg);
            }
            else
            {
                lo = -fallbackHalfWidthDeg;
                hi = fallbackHalfWidthDeg;
            }
        }
    }
}
