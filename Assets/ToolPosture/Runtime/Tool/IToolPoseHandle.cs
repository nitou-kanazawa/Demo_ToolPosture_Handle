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
        ///
        /// 座標系をまたぐ場合は、これを変換するより
        /// <see cref="ToolAxisWorld"/> と <see cref="ToolReferenceWorld"/> を渡す方が安全。
        /// クォータニオンは成分を入れ替えるだけでは別の回転になるが、
        /// ベクトルは軸の対応で入れ替えるだけで済む。
        /// </summary>
        Quaternion WorldRotation { get; }

        /// <summary>
        /// 工具軸のワールド方向。<see cref="ToolPostureFollower.shaftAxis"/> が向く先。
        /// </summary>
        Vector3 ToolAxisWorld { get; }

        /// <summary>
        /// スピンを適用したあとの工具基準方向。工具軸に直交する。
        /// <see cref="ToolPostureFollower.referenceAxis"/> が向く先。
        /// </summary>
        Vector3 ToolReferenceWorld { get; }

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
        /// ハンドル全体の倍率。大きさだけでなく線の太さや当たり判定にも一様に効く。
        ///
        /// 2D 重畳ビューの拡大率に合わせるなど、見せ方に応じて動かす。見た目のプリセットは
        /// 共有アセットなので実行中に書き換えず、こちらを使うこと。
        /// </summary>
        float SizeScale { get; set; }

        /// <summary>
        /// レイがハンドルに当たるか。当たった点までの距離も返す。
        ///
        /// ハンドルのコライダーは Ignore Raycast レイヤーに居て
        /// <see cref="Physics.Raycast"/> には出てこない。アプリが自前の raycast で
        /// 掴む対象を決めているなら、その前にこれを見て、当たっていて手前なら
        /// 自分の処理を飛ばすこと。そうしないとハンドルが候補にすら入らない。
        /// </summary>
        bool Raycast(Ray ray, out float distance);

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
