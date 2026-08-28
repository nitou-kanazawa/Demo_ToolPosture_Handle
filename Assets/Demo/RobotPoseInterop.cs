using UnityEngine;
using ToolRuntimeGizmos.Core;
using ToolRuntimeGizmos.Tool;

namespace ToolRuntimeGizmos.Demo
{
    /// <summary>
    /// ロボット側の値とハンドルの間を橋渡しする。3 つの向きの変換をまとめてある。
    ///
    ///   1. ロボットの RPY と経路の点・法線 -> ハンドルへ適用
    ///   2. ハンドル -> LMN とトーチ姿勢 (狙い角 / 前進後退角 / 軸まわりの回転) を取得
    ///   3. LMN とトーチ姿勢 -> ハンドルへ適用
    ///
    /// アプリ側に書くことになるコードの雛形。
    /// </summary>
    /// <remarks>
    /// 座標系をまたぐ値はすべて <see cref="Conversion"/> を通す。位置と方向は軸を
    /// 入れ替えるだけだが、回転はそうではない。詳しくは HandednessConversion を参照。
    /// </remarks>
    [AddComponentMenu("Tool Posture/Robot Pose Interop")]
    public class RobotPoseInterop : MonoBehaviour
    {
        [SerializeField] private ToolPoseHandle handle;

        [Tooltip("進行方向を向いたとき、直交方向 L をどちら側に取るか")]
        public CrossFeedSide crossFeedSide = CrossFeedSide.RightOfTravel;

        /// <summary>Unity とロボットの座標系の対応。</summary>
        public HandednessConversion Conversion = HandednessConversion.SwapYZ;

        private void Awake()
        {
            if (handle == null) handle = FindAnyObjectByType<ToolPoseHandle>();
        }

        #region 1. ロボットの値をハンドルへ

        /// <summary>
        /// ロボット座標系の値でハンドルを設定する。
        ///
        /// フレームは 3 つの入力から組む。原点は工具位置、進行方向は p(i) -> p(i+1)、
        /// 面法線は生の値でよい (進行方向と直交化される)。
        /// そのフレームの上に、RPY から逆算した姿勢を乗せる。
        /// </summary>
        /// <param name="tcpRobot">工具位置 (ロボット座標)。</param>
        /// <param name="rpyRobot">工具姿勢の ZYX オイラー (ロボット座標)。</param>
        /// <param name="pointRobot">区間始点 p(i) (ロボット座標)。</param>
        /// <param name="nextPointRobot">区間終点 p(i+1) (ロボット座標)。</param>
        /// <param name="normalRobot">面法線 (ロボット座標)。</param>
        public bool ApplyFromRobot(Vector3 tcpRobot, ZyxEulerAngles rpyRobot,
                                   Vector3 pointRobot, Vector3 nextPointRobot, Vector3 normalRobot)
        {
            // 位置と方向は軸を入れ替えるだけ
            Vector3 origin = Conversion.ToUnity(tcpRobot);
            Vector3 travel = Conversion.ToUnity(nextPointRobot - pointRobot);
            Vector3 normal = Conversion.ToUnity(normalRobot);

            if (!WorkFrame.TryCreate(origin, travel, normal, crossFeedSide, out WorkFrame frame))
                return false;                       // 区間長ゼロ、または法線が進行方向と平行

            // 回転は成分の入れ替えだけでは合わない。ToUnity に任せる
            Quaternion rotation = Conversion.ToUnity(rpyRobot.ToRotation());

            // フレームを先に確定させてから姿勢を入れる。
            // SetWorldRotation はフレームを基準に分解するので順序が要る
            handle.SetPose(new ToolPose(frame, handle.Pose.Angles));
            handle.SetWorldRotation(rotation);
            return true;
        }

        /// <summary>
        /// 今のハンドルの姿勢をロボット座標系で取り出す。<see cref="ApplyFromRobot"/> の対。
        /// </summary>
        /// <remarks>
        /// クォータニオンを座標変換せず、工具軸と基準方向の 2 本のベクトルで渡す。
        /// ベクトルは軸の対応で入れ替えるだけで移るので符号の取り違えが起きない
        /// (クォータニオンは成分を入れ替えるだけだと全く別の回転になる)。
        ///
        /// 工具のローカル軸割当も同じ座標系の規約に揃えること。忘れると 180 度ずれる。
        ///
        /// ZYX はピッチ ±90 度で z と x が分離できず値が飛ぶ。姿勢そのものは正しいが、
        /// 数値の連続性が要る用途では <see cref="GetLocal"/> の方を併用すること。
        /// </remarks>
        /// <param name="tcpRobot">工具位置 (ロボット座標)。</param>
        /// <param name="rpyRobot">工具姿勢の ZYX オイラー (ロボット座標)。</param>
        public void GetRobotPose(out Vector3 tcpRobot, out ZyxEulerAngles rpyRobot)
        {
            tcpRobot = Conversion.ToExternal(handle.Pose.Position);

            ToolPostureFollower f = handle.Follower;
            Vector3 shaft = f != null ? f.shaftAxis : Vector3.up;
            Vector3 reference = f != null ? f.referenceAxis : Vector3.forward;

            rpyRobot = ZyxEulerAngles.FromToolAxes(
                Conversion.ToExternal(handle.ToolAxisWorld),
                Conversion.ToExternal(handle.ToolReferenceWorld),
                Conversion.ToExternal(shaft),
                Conversion.ToExternal(reference));
        }

        #endregion

        #region 2. ハンドルから LMN とトーチ姿勢を取得

        /// <summary>
        /// 今の LMN フレームとトーチ姿勢を取り出す。
        /// 角度は AngleConvention を通さない内部値。
        /// </summary>
        /// <param name="frame">LMN フレーム。原点は工具位置。</param>
        /// <param name="workDeg">狙い角。LN 平面上で N から L 方向へ測る。</param>
        /// <param name="travelDeg">前進後退角。MN 平面上で N から M 方向へ測る。</param>
        /// <param name="spinDeg">工具軸まわりの回転。</param>
        public void GetLocal(out WorkFrame frame,
                             out float workDeg, out float travelDeg, out float spinDeg)
        {
            ToolPose pose = handle.Pose;

            frame = pose.Frame;
            workDeg = pose.Angles.WorkAngleDeg;
            travelDeg = pose.Angles.TravelAngleDeg;
            spinDeg = pose.Angles.spinAngleDeg;
        }

        #endregion

        #region 3. LMN とトーチ姿勢をハンドルへ

        /// <summary>
        /// LMN フレームとトーチ姿勢を与える。フレームと角度を 1 回で渡すので、
        /// 原点だけ新しく向きが古い、といった中間状態を作らない。
        /// </summary>
        public void SetLocal(WorkFrame frame, float workDeg, float travelDeg, float spinDeg)
        {
            ToolPostureAngles angles = handle.Pose.Angles;

            // 投影角の組から工具軸を決める。X = normalize(tan(w) L + tan(t) M + N)
            angles.SetProjected(workDeg, travelDeg);
            angles.spinAngleDeg = spinDeg;

            handle.SetPose(new ToolPose(frame, angles));
        }

        /// <summary>
        /// 経路の点と法線からフレームを組んで、そのままトーチ姿勢を乗せる。
        /// 引数は Unity 座標。ロボット座標なら Conversion.ToUnity を通してから渡すこと。
        /// </summary>
        public bool SetLocal(Vector3 origin, Vector3 point, Vector3 nextPoint, Vector3 normal,
                             float workDeg, float travelDeg, float spinDeg)
        {
            if (!WorkFrame.TryCreate(origin, nextPoint - point, normal, crossFeedSide, out WorkFrame frame))
                return false;

            SetLocal(frame, workDeg, travelDeg, spinDeg);
            return true;
        }

        #endregion
    }
}
