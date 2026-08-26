using System;
using UnityEngine;

namespace ToolRuntimeGizmos.Core
{
    /// <summary>
    /// ZYX オイラー角 (yaw-pitch-roll)。ロボットのツール姿勢表現でよく使われる形式。
    ///
    /// 回転は内因的に Z -> Y' -> X'' の順で適用する。行列では
    ///   R = Rz(zDeg) * Ry(yDeg) * Rx(xDeg)
    /// で、これは外因的な X -> Y -> Z と同じもの。
    ///
    /// 注意: Unity の Quaternion.eulerAngles は ZXY 順であって ZYX ではない。
    /// 混同すると値が静かにずれるので、この構造体を経由すること。
    ///
    /// 注意: これは「与えられた回転を、その回転が乗っている座標系の軸で分解する」だけで、
    /// ロボットの座標系を知らない。Unity の回転をそのまま渡してもロボットの RPY にはならない。
    /// ロボット側の値が欲しければ、先に <see cref="HandednessConversion"/> で外部座標へ
    /// 変換してから渡すこと。順序を逆にすると全く別の値になる
    /// (実測: 真値 z=-19.3 に対し、変換せず分解すると z=-60.0)。
    ///
    /// 極 (yDeg = ±90 度) ではジンバルロックが起き、zDeg と xDeg が分離できなくなる。
    /// FromRotation はその場合 xDeg = 0 に寄せた解を返す。姿勢そのものは正しいが、
    /// 元の (z, x) の分け方は復元できない。
    /// </summary>
    [Serializable]
    public struct ZyxEulerAngles
    {
        /// <summary>
        /// ジンバルロックとみなす cos(yDeg) の閾値。
        /// </summary>
        private const float GimbalLockEpsilon = 1e-5f;

        [Tooltip("Z 軸まわり (最初に適用)。ロボット側の Roll に相当")]
        public float zDeg;

        [Tooltip("Y 軸まわり (2 番目に適用)。ロボット側の Pitch に相当")]
        public float yDeg;

        [Tooltip("X 軸まわり (最後に適用)。ロボット側の Yaw に相当")]
        public float xDeg;

        /// <summary>
        /// Z 軸まわりの角。呼び出し側が Roll と呼んでいる場合の別名。
        /// </summary>
        public float RollDeg { get => zDeg; set => zDeg = value; }

        /// <summary>
        /// Y 軸まわりの角。呼び出し側が Pitch と呼んでいる場合の別名。
        /// </summary>
        public float PitchDeg { get => yDeg; set => yDeg = value; }

        /// <summary>
        /// X 軸まわりの角。呼び出し側が Yaw と呼んでいる場合の別名。
        /// </summary>
        public float YawDeg { get => xDeg; set => xDeg = value; }

        /// <summary>
        /// Roll (Z) / Pitch (Y) / Yaw (X) の呼び方で構築する。
        /// </summary>
        public static ZyxEulerAngles FromRollPitchYaw(float rollDeg, float pitchDeg, float yawDeg)
            => new ZyxEulerAngles(rollDeg, pitchDeg, yawDeg);

        public ZyxEulerAngles(float zDeg, float yDeg, float xDeg)
        {
            this.zDeg = zDeg;
            this.yDeg = yDeg;
            this.xDeg = xDeg;
        }

        /// <summary>
        /// 回転へ変換する。R = Rz * Ry * Rx。
        /// </summary>
        public Quaternion ToRotation()
            => Quaternion.AngleAxis(zDeg, Vector3.forward)
             * Quaternion.AngleAxis(yDeg, Vector3.up)
             * Quaternion.AngleAxis(xDeg, Vector3.right);

        /// <summary>
        /// 回転から ZYX オイラー角を取り出す。
        ///
        /// 回転行列の列ベクトル (col0, col1, col2) から取り出す形で、
        ///   cp = |(m00, m10)| ,  sp = -m20
        ///   pitch = atan2(sp, cp) ,  z = atan2(m10, m00) ,  x = atan2(m21, m22)
        /// ジンバルロック時は z = 0 に寄せ、x = atan2(-m01, m11) で解く。
        /// </summary>
        public static ZyxEulerAngles FromRotation(Quaternion rotation)
        {
            Matrix4x4 m = Matrix4x4.Rotate(rotation);

            // cp は 1 - sin^2 からではなく行列要素から直接求める。
            // yDeg = ±90 度ちょうどのとき前者は float で 1e-4 程度の誤差が残り、
            // 通常分岐に落ちて atan2(≈0, ≈0) を評価してしまう。
            float cp = Mathf.Sqrt(m[0, 0] * m[0, 0] + m[1, 0] * m[1, 0]);
            float sp = -m[2, 0];

            if (cp < GimbalLockEpsilon)
            {
                // ロック時は z と x が (yDeg = +90 なら差、-90 なら和) の形でしか決まらない。
                // z = 0 に寄せて x を解く。符号は pitch の向きで変わる。
                //   yDeg = +90 : m01 = sin(x - z), m11 = cos(x - z)  -> x = atan2( m01, m11)
                //   yDeg = -90 : m01 = -sin(x + z), m11 = cos(x + z) -> x = atan2(-m01, m11)
                bool positivePitch = sp >= 0f;
                float x = positivePitch
                    ? Mathf.Atan2(m[0, 1], m[1, 1])
                    : Mathf.Atan2(-m[0, 1], m[1, 1]);

                return new ZyxEulerAngles(0f, positivePitch ? 90f : -90f, x * Mathf.Rad2Deg);
            }

            return new ZyxEulerAngles(
                Mathf.Atan2(m[1, 0], m[0, 0]) * Mathf.Rad2Deg,
                Mathf.Atan2(sp, cp) * Mathf.Rad2Deg,
                Mathf.Atan2(m[2, 1], m[2, 2]) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// ジンバルロック付近か (yDeg が ±90 度に近いか)。
        /// </summary>
        public bool IsNearGimbalLock(float thresholdDeg = 1f)
            => Mathf.Abs(Mathf.Abs(Mathf.DeltaAngle(yDeg, 0f)) - 90f) < thresholdDeg;

        public override string ToString()
            => string.Format("z={0:F2} y={1:F2} x={2:F2}", zDeg, yDeg, xDeg);
    }
}
