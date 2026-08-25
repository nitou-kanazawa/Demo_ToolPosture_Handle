using UnityEngine;
using ToolRuntimeGizmos.Core;

namespace ToolRuntimeGizmos.Tool
{
    /// <summary>
    /// 工具姿勢の正準表現。
    ///
    ///   位置          … <see cref="Frame"/> の原点。フレームと別に持たない
    ///   ローカルフレーム … L / M / N。溶接角の基準
    ///   フレーム上の姿勢 … 傾斜角 / 旋回角 / 軸まわり回転
    ///
    /// 世界回転 (Quaternion) はここに入れない。組むには「モデルのどのローカル軸を
    /// 工具軸に向けるか」という対象側の都合が要り、それは
    /// <see cref="ToolPostureFollower"/> が持つ。世界回転が要る場面
    /// (ロボットとの受け渡しなど) では <see cref="IToolPoseHandle.WorldRotation"/> を使う。
    ///
    /// 角度は内部値であって表示値ではない。<see cref="ToolPostureProfile"/> の
    /// AngleConvention を通した値とは、ゼロ点と向きが異なることがある。
    /// </summary>
    public readonly struct ToolPose
    {
        public readonly PathFrame Frame;
        public readonly ToolPostureAngles Angles;

        public ToolPose(PathFrame frame, ToolPostureAngles angles)
        {
            Frame = frame;
            Angles = angles;
        }

        /// <summary>工具位置。フレームの原点そのもの。</summary>
        public Vector3 Position => Frame.Origin;

        public bool IsValid => Frame.IsValid;

        /// <summary>
        /// 位置だけ差し替える。向きと姿勢はそのまま。
        /// </summary>
        public ToolPose WithPosition(Vector3 position) => new ToolPose(Frame.WithOrigin(position), Angles);

        /// <summary>
        /// フレームだけ差し替える。姿勢はフレーム相対なので、向きが変われば
        /// 工具の世界姿勢も一緒に回る (溶接角を継目基準に保つということ)。
        /// </summary>
        public ToolPose WithFrame(PathFrame frame) => new ToolPose(frame, Angles);

        /// <summary>
        /// 姿勢だけ差し替える。位置と向きはそのまま。
        /// </summary>
        public ToolPose WithAngles(ToolPostureAngles angles) => new ToolPose(Frame, angles);

        public override string ToString()
            => $"pos {Position:F3}  {Angles}";
    }

    /// <summary>
    /// どのハンドルが動かしたか。
    /// </summary>
    public enum ToolHandleKind
    {
        /// <summary>どちらでもない (外から与えられた場合など)。</summary>
        None = 0,

        /// <summary>座標軸。軸方向の平行移動。</summary>
        Position = 1,

        /// <summary>傾斜角 / 旋回角 / スピン。</summary>
        Posture = 2,
    }

    /// <summary>
    /// ハンドルのイベントで渡る内容。
    /// </summary>
    public readonly struct ToolPoseEvent
    {
        /// <summary>そのときの姿勢。</summary>
        public readonly ToolPose Pose;

        /// <summary>動かしたハンドルの種類。</summary>
        public readonly ToolHandleKind Kind;

        /// <summary>
        /// 取り消して終わったか。DragEnded でだけ意味を持つ。
        /// true のとき Pose はドラッグ開始前の値に戻っている。
        /// </summary>
        public readonly bool Cancelled;

        public ToolPoseEvent(ToolPose pose, ToolHandleKind kind, bool cancelled = false)
        {
            Pose = pose;
            Kind = kind;
            Cancelled = cancelled;
        }
    }
}
