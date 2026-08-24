using System;
using UnityEngine;

namespace ToolPosture.Core
{
    /// <summary>
    /// 工具軸まわりの回転 (スピン) をどこから測るか。
    /// </summary>
    public enum SpinReferenceMode
    {
        /// <summary>
        /// 進行方向 M を工具軸直交面へ投影した向きを 0 度とする。
        /// 溶接線に対する相対的な向きになるので、経路が曲がるとワールド上では回る。
        /// </summary>
        FeedProjected = 0,

        /// <summary>
        /// cross(基準軸, 工具軸) を 0 度とする。基準軸はワールド固定 (ロボットの上方向など)。
        /// 経路に依存しないので、ロボット側が「ツール軸まわりの回転角」を
        /// ワールド基準で持っている場合はこちら。
        /// </summary>
        WorldAxisCross = 1,
    }

    /// <summary>
    /// スピン 0 度の基準ベクトルを決める設定。
    ///
    /// 既定値 (default) は FeedProjected。ロボット制御側が
    ///   yRef = cross(worldUp, toolAxis)  (退化時は固定ベクトルへフォールバック)
    /// のようなワールド基準で回転角を定義している場合は WorldAxisCross にして
    /// worldAxis / degenerateFallback をその定義に合わせる。
    /// </summary>
    [Serializable]
    public struct SpinReference
    {
        /// <summary>
        /// cross(基準軸, 工具軸) が退化しているとみなす長さの閾値。
        /// </summary>
        public const float DegenerateThreshold = 1e-4f;

        public SpinReferenceMode mode;

        [Tooltip("WorldAxisCross のときの基準軸。未設定 (ゼロ) なら Vector3.up")]
        public Vector3 worldAxis;

        [Tooltip("基準軸と工具軸が平行になったときに使う向き。未設定 (ゼロ) なら Vector3.forward")]
        public Vector3 degenerateFallback;

        /// <summary>
        /// 進行方向 M を基準にする既定の設定。
        /// </summary>
        public static SpinReference FeedProjected => default;

        /// <summary>
        /// ワールド固定の基準軸との外積を 0 度にする設定。
        /// </summary>
        public static SpinReference WorldAxisCross(Vector3 worldAxis, Vector3 degenerateFallback)
            => new SpinReference
            {
                mode = SpinReferenceMode.WorldAxisCross,
                worldAxis = worldAxis,
                degenerateFallback = degenerateFallback,
            };

        /// <summary>
        /// この設定でのスピン 0 度の基準ベクトル (工具軸に直交する単位ベクトル)。
        /// </summary>
        public Vector3 Resolve(in PathFrame frame, Vector3 toolAxisWorld)
        {
            if (mode != SpinReferenceMode.WorldAxisCross)
                return ToolPostureAngles.SpinZeroReference(frame, toolAxisWorld);

            Vector3 axis = worldAxis.sqrMagnitude > 1e-12f ? worldAxis : Vector3.up;
            Vector3 r = Vector3.Cross(axis, toolAxisWorld);

            if (r.magnitude < DegenerateThreshold)
            {
                Vector3 fallback = degenerateFallback.sqrMagnitude > 1e-12f
                    ? degenerateFallback
                    : Vector3.forward;
                r = fallback - Vector3.Dot(fallback, toolAxisWorld) * toolAxisWorld;
            }

            return r.normalized;
        }
    }
}
