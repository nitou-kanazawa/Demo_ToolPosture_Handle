using System;
using UnityEngine;

namespace ToolRuntimeGizmos.Tool
{
    /// <summary>
    /// 利用側が握る口。ハンドルが何本あるか、どちらが有効か、どう掴むかは知らなくてよい。
    ///
    /// 値の流れは基本的にハンドル → 利用側の一方向。ドラッグ中に値を書き戻すと、
    /// 次のフレームの再計算と殴り合う (ドラッグは掴んだ瞬間の基準から毎回引き直すため)。
    /// <see cref="SetPose"/> はドラッグ外専用で、イベントを発火しない。
    ///
    /// 想定している使い方:
    /// <code>
    /// handle.PoseChanged += e => { if (ik.TrySolve(handle.WorldRotation, e.Pose.Position)) robot.Apply(); };
    /// handle.DragEnded   += e => {
    ///     if (e.Cancelled) return;
    ///     var p = e.Pose.WithPosition(robot.CurrentPosition);   // 最後に IK が通った位置
    ///     handle.SetPose(p.WithFrame(workpiece.FrameAt(robot.CurrentPosition)));
    /// };
    /// </code>
    /// </summary>
    public interface IToolPoseHandle
    {
        /// <summary>
        /// 今の姿勢。位置・ローカルフレーム・フレーム上の姿勢をまとめたもの。
        /// </summary>
        ToolPose Pose { get; }

        /// <summary>
        /// 姿勢を与える。フレームと角度を同時に差し替えるので、
        /// 「原点は新しいが向きは古い」ような中間状態を作らない。
        ///
        /// イベントは発火しない。書き戻しがそのまま次の計算を呼ぶループを防ぐため。
        /// ドラッグ中に呼んでも次のフレームで上書きされるので、DragEnded の後に呼ぶこと。
        /// </summary>
        void SetPose(ToolPose pose);

        /// <summary>
        /// 工具の世界回転。ロボットとの受け渡しに使う。
        /// 軸の割当は <see cref="ToolPostureFollower"/> が持つ。
        /// </summary>
        Quaternion WorldRotation { get; }

        /// <summary>
        /// 世界回転を与えて姿勢を逆算する。
        /// 垂直姿勢では旋回角が幾何的に決まらないが、現在値を残す経路で書くので失われない。
        /// </summary>
        void SetWorldRotation(Quaternion rotation);

        /// <summary>
        /// ハンドルを出すかどうか。false でどれも消え、掴めなくなる。
        /// </summary>
        bool Visible { get; set; }

        /// <summary>
        /// 掴んだとき。1 ドラッグにつき一度だけやる仕事に使う。
        ///
        /// カメラの抑止には使わないこと。購読側でフラグを持つと、ドラッグ中に
        /// 無効化された場合に終了が届かず止まったままになる。
        /// <see cref="Gizmo.RuntimeGizmo.AnyDragging"/> を見ればズレようがない。
        /// </summary>
        event Action<ToolPoseEvent> DragBegan;

        /// <summary>
        /// 値が変わったとき。ドラッグ中は毎フレーム飛ぶ。ここで IK を回す。
        /// 変わったのはハンドルの値であって、対象はまだ更新されていない。
        /// </summary>
        event Action<ToolPoseEvent> PoseChanged;

        /// <summary>
        /// 離したとき。操作中のハンドルを解除した後に発火するので、
        /// このハンドラの中から <see cref="SetPose"/> を呼んでよい。
        /// </summary>
        event Action<ToolPoseEvent> DragEnded;
    }
}
