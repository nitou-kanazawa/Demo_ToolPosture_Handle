using UnityEngine;

namespace ToolRuntimeGizmos.Core
{
    /// <summary>
    /// Unity と外部システム (ロボット等) の座標系の対応。位置と回転を相互に変換する。
    /// </summary>
    /// <remarks>
    /// 位置は軸を入れ替えるだけでよいが、回転はそうではない。軸を 2 つ入れ替える対応は
    /// 行列式が -1 で左右手系が反転するため、同じ物理回転でも向きが逆になる。
    /// クォータニオンの成分を入れ替えただけでは全く別の回転になる (実測で最大誤差 1.97)。
    /// ここではベクトル部に行列式を掛けることでそれを吸収している。
    ///
    /// ZYX オイラーが欲しい場合は、先にこれで外部座標へ変換してから
    /// <see cref="ZyxEulerAngles.FromRotation"/> に渡すこと。順序を逆にすると値が狂う。
    ///
    /// <code>
    /// var conv = HandednessConversion.SwapYZ;
    /// Vector3 tcp = conv.ToExternal(pose.Position);
    /// ZyxEulerAngles rpy = ZyxEulerAngles.FromRotation(conv.ToExternal(handle.WorldRotation));
    /// </code>
    /// </remarks>
    public readonly struct HandednessConversion
    {
        /// <summary>
        /// X -> X, Y -> Z, Z -> Y。Unity (左手 / Y-up) と Z-up の右手系の間でよくある対応。
        /// </summary>
        public static readonly HandednessConversion SwapYZ =
            new HandednessConversion(new Vector3(1f, 0f, 0f),
                                     new Vector3(0f, 0f, 1f),
                                     new Vector3(0f, 1f, 0f));

        /// <summary>変換しない。両者が同じ座標系のときに使う。</summary>
        public static readonly HandednessConversion Identity =
            new HandednessConversion(Vector3.right, Vector3.up, Vector3.forward);

        /// <summary>Unity の各軸が、外部座標系ではどの向きになるか。</summary>
        public readonly Vector3 UnityXTo;
        public readonly Vector3 UnityYTo;
        public readonly Vector3 UnityZTo;

        /// <summary>
        /// 基底の行列式。-1 なら左右手系が反転している。
        /// </summary>
        public readonly float Determinant;

        public HandednessConversion(Vector3 unityXTo, Vector3 unityYTo, Vector3 unityZTo)
        {
            UnityXTo = unityXTo;
            UnityYTo = unityYTo;
            UnityZTo = unityZTo;
            Determinant = Vector3.Dot(unityXTo, Vector3.Cross(unityYTo, unityZTo)) >= 0f ? 1f : -1f;
        }

        /// <summary>左右手系が反転する対応か。</summary>
        public bool FlipsHandedness => Determinant < 0f;

        #region Unity -> 外部

        /// <summary>位置 (または方向ベクトル) を外部座標へ。</summary>
        public Vector3 ToExternal(Vector3 unity)
            => UnityXTo * unity.x + UnityYTo * unity.y + UnityZTo * unity.z;

        /// <summary>
        /// 回転を外部座標へ。左右手系が反転する場合はベクトル部の符号も反転する。
        /// </summary>
        public Quaternion ToExternal(Quaternion unity)
        {
            Vector3 v = ToExternal(new Vector3(unity.x, unity.y, unity.z)) * Determinant;
            return new Quaternion(v.x, v.y, v.z, unity.w);
        }

        #endregion

        #region 外部 -> Unity

        /// <summary>位置 (または方向ベクトル) を Unity 座標へ。</summary>
        public Vector3 ToUnity(Vector3 external)
            => new Vector3(Vector3.Dot(external, UnityXTo),
                           Vector3.Dot(external, UnityYTo),
                           Vector3.Dot(external, UnityZTo));

        /// <summary>回転を Unity 座標へ。</summary>
        public Quaternion ToUnity(Quaternion external)
        {
            Vector3 v = ToUnity(new Vector3(external.x, external.y, external.z)) * Determinant;
            return new Quaternion(v.x, v.y, v.z, external.w);
        }

        #endregion
    }
}
