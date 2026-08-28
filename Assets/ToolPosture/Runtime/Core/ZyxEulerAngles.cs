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
    /// FromRotation はその場合 zDeg = 0 に寄せた解を返す。姿勢そのものは正しいが、
    /// 元の (z, x) の分け方は復元できない。
    /// </summary>
    [Serializable]
    public struct ZyxEulerAngles
    {
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
            => FromMatrix(Matrix4x4.Rotate(rotation));

        /// <summary>
        /// 回転行列から ZYX オイラー角を取り出す。行列の要素しか見ないので、
        /// 成分がどの座標系のものであっても同じように使える。
        /// </summary>
        public static ZyxEulerAngles FromMatrix(Matrix4x4 m) => RobotPostureConvert.ToZyx(m);

        /// <summary>
        /// 回転行列へ変換する。<see cref="ToRotation"/> と同じ姿勢を、
        /// クォータニオンを経由せずに返す。
        /// </summary>
        public Matrix4x4 ToMatrix() => RobotPostureConvert.FromZyx(this);

        #region 工具軸 + スピン との相互変換

        /// <summary>
        /// 工具軸ベクトルと、その軸まわりの回転量から組み立てる。
        /// </summary>
        /// <remarks>
        /// 座標系をまたぐときはこちらの形の方が安全。工具軸は軸の入れ替えだけで移り、
        /// スピンは左右手系が反転するとき符号が反転するだけで済む (実測で一致)。
        /// クォータニオンの成分を入れ替えるだけだと全く別の回転になる。
        ///
        /// この表現は ZYX のジンバルロック (ピッチ ±90 度) を持たない代わりに、
        /// 工具軸が spinZeroReference と平行になるとスピンの基準が決まらなくなる。
        /// 特異点の位置が違うので、両方を持っておくと片方が不定でももう片方は連続。
        ///
        /// 注意: 引数はすべて同じ座標系の規約で与えること。工具軸とスピンだけでなく、
        /// toolShaftAxis / toolReferenceAxis も同じである。工具のローカル軸割当を
        /// Unity の規約のまま渡すと、他が正しくてもちょうど 180 度ずれる (実測)。
        /// </remarks>
        /// <param name="axis">この座標系での工具軸 (単位ベクトル)。</param>
        /// <param name="spinDeg">工具軸まわりの回転量。右ねじの向きが正。</param>
        /// <param name="toolShaftAxis">工具モデルのどのローカル軸を工具軸に向けるか。</param>
        /// <param name="toolReferenceAxis">スピン基準に向けるローカル軸。shaft と直交していること。</param>
        /// <param name="spinZeroReference">スピン 0 のときに基準が向く方向。軸に平行な成分は落として使う。</param>
        public static ZyxEulerAngles FromToolAxis(Vector3 axis, float spinDeg,
                                                  Vector3 toolShaftAxis, Vector3 toolReferenceAxis,
                                                  Vector3 spinZeroReference)
            => RobotPostureConvert.ToolAxisSpinToZyx(
                   new ToolAxisSpin(axis, spinDeg),
                   new ToolAxes(toolShaftAxis, toolReferenceAxis),
                   spinZeroReference);

        /// <summary>
        /// 工具軸と、スピンを適用したあとの基準方向から組み立てる。
        /// </summary>
        /// <remarks>
        /// 2 本のベクトルで姿勢が決まるので、スピンをスカラーで渡す版と違い
        /// spinZeroReference の規約が要らない。座標系をまたぐときはこちらが一番安全で、
        /// ベクトルを軸の対応で入れ替えるだけで済む (符号の反転も要らない)。
        ///
        /// toolShaftAxis / toolReferenceAxis はこの座標系の規約で与えること。
        /// Unity の規約のまま渡すとちょうど 180 度ずれる。
        /// </remarks>
        /// <param name="axis">工具軸。<paramref name="toolShaftAxis"/> が向く方向。</param>
        /// <param name="reference"><paramref name="toolReferenceAxis"/> が向く方向。軸に直交していること。</param>
        public static ZyxEulerAngles FromToolAxes(Vector3 axis, Vector3 reference,
                                                  Vector3 toolShaftAxis, Vector3 toolReferenceAxis)
            => RobotPostureConvert.ToZyx(RobotPostureConvert.FromToolAxes(
                   axis, reference, new ToolAxes(toolShaftAxis, toolReferenceAxis)));

        /// <summary>
        /// 工具軸ベクトルと、その軸まわりの回転量へ分解する。<see cref="FromToolAxis"/> の逆。
        /// 引数の意味はそちらと同じ。
        /// </summary>
        public void ToToolAxis(Vector3 toolShaftAxis, Vector3 toolReferenceAxis, Vector3 spinZeroReference,
                               out Vector3 axis, out float spinDeg)
        {
            RobotPostureConvert.ToToolAxisSpin(ToMatrix(),
                                               new ToolAxes(toolShaftAxis, toolReferenceAxis),
                                               spinZeroReference, out ToolAxisSpin posture);
            axis = posture.Axis;
            spinDeg = posture.SpinDeg;
        }

        #endregion

        /// <summary>
        /// ジンバルロック付近か (yDeg が ±90 度に近いか)。
        /// </summary>
        public bool IsNearGimbalLock(float thresholdDeg = 1f)
            => Mathf.Abs(Mathf.Abs(Mathf.DeltaAngle(yDeg, 0f)) - 90f) < thresholdDeg;

        public override string ToString()
            => string.Format("z={0:F2} y={1:F2} x={2:F2}", zDeg, yDeg, xDeg);
    }
}
