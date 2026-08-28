using NUnit.Framework;
using UnityEngine;
using ToolRuntimeGizmos.Core;
using ToolRuntimeGizmos.Tool;

namespace ToolRuntimeGizmos.Tests
{
    /// <summary>
    /// ロボット座標系の ZYX オイラー角から、ハンドルに渡す <see cref="ToolPose"/> を組む手順。
    ///
    /// 姿勢に必要なのは ZYX とフレームの 2 つだけ。工具位置はフレームの原点が持つ。
    /// 経路の点や法線が要るのはフレームを組む段で、姿勢の変換とは別の話。
    ///
    /// 途中でクォータニオンを使わない。ZYX をベクトル 2 本に開いてから座標系を移すので、
    /// 符号の反転を手で入れる場所がひとつも無い。
    /// </summary>
    public class RobotZyxToPoseTests
    {
        private const float Tol = 1e-3f;

        private static readonly HandednessConversion Conv = HandednessConversion.SwapYZ;

        /// <summary>工具モデルの軸割当。Follower の設定をそれぞれの座標系の規約で表したもの。</summary>
        private static ToolAxes ToolUnity => ToolAxes.Unity;
        private static ToolAxes ToolRobot => new ToolAxes(Conv.ToExternal(ToolUnity.Shaft),
                                                          Conv.ToExternal(ToolUnity.Reference));

        #region 手順そのもの

        /// <summary>
        /// ロボット座標系の ZYX と、Unity 側のフレームからハンドルの姿勢を組む。
        /// アプリ側に書くことになるコード。
        /// </summary>
        private static ToolPose BuildPose(ZyxEulerAngles rpyRobot, PathFrame frame)
        {
            // 1. ロボット座標のまま、ZYX を工具軸と基準方向の 2 本に開く
            Matrix4x4 r = RobotPostureConvert.FromZyx(rpyRobot);
            Vector3 axisRobot = r.MultiplyVector(ToolRobot.Shaft);
            Vector3 referenceRobot = r.MultiplyVector(ToolRobot.Reference);

            // 2. ベクトルなので軸の入れ替えだけで Unity へ移る
            Vector3 axis = Conv.ToUnity(axisRobot).normalized;
            Vector3 reference = Conv.ToUnity(referenceRobot).normalized;

            // 3. フレームの上で角度にする。
            //    SetProjected は狙い角と前進後退角を ±85 度で切るので、ここでは使わない
            var angles = new ToolPostureAngles();
            angles.SetAxisWorld(frame, axis);

            Vector3 x = angles.GetAxisWorld(frame);
            angles.spinAngleDeg = Vector3.SignedAngle(
                ToolPostureAngles.SpinZeroReference(frame, x), reference, x);

            return new ToolPose(frame, angles);
        }

        /// <summary>
        /// フレームがロボット座標にある場合の入口。L も含めて分かっているので TryFromBasis を使う。
        /// L の側は与えたベクトルから決まるので、左右手系の反転を自分で考えなくてよい。
        /// </summary>
        private static bool TryUnityFrame(Vector3 originRobot, LmnFrame lmnRobot, out PathFrame frame)
            => PathFrame.TryFromBasis(Conv.ToUnity(originRobot),
                                      Conv.ToUnity(lmnRobot.L),
                                      Conv.ToUnity(lmnRobot.M),
                                      Conv.ToUnity(lmnRobot.N), out frame);

        /// <summary>組んだ姿勢をロボット座標の ZYX へ戻す。<see cref="BuildPose"/> の対。</summary>
        private static ZyxEulerAngles ReadBackRpy(ToolPose pose)
            => ZyxEulerAngles.FromToolAxes(
                   Conv.ToExternal(pose.Angles.GetAxisWorld(pose.Frame)),
                   Conv.ToExternal(pose.Angles.GetToolReferenceWorld(pose.Frame)),
                   ToolRobot.Shaft, ToolRobot.Reference);

        #endregion

        #region 経路の例 (ロボット座標)

        private static readonly Vector3 Tcp = new Vector3(1.2f, -0.4f, 0.8f);
        private static readonly Vector3 P0 = new Vector3(1.0f, -0.4f, 0.8f);
        private static readonly Vector3 P1 = new Vector3(1.6f, -0.1f, 0.9f);
        private static readonly Vector3 RawNormal = new Vector3(0.1f, 0.2f, 1f);

        /// <summary>ロボット座標の LMN。右手系なので L = M x N。</summary>
        private static LmnFrame RobotFrame => LmnFrame.FromPath(P0, P1, RawNormal);

        private static PathFrame Frame
        {
            get
            {
                Assert.IsTrue(TryUnityFrame(Tcp, RobotFrame, out PathFrame f), "フレームが組めない");
                return f;
            }
        }

        #endregion

        #region 姿勢

        [TestCase(0f, 0f, 0f)]
        [TestCase(30f, 20f, 10f)]
        [TestCase(-140f, 55f, 170f)]
        [TestCase(95f, -70f, -35f)]
        public void ロボットのZyxから組んだ姿勢はそのZyxに戻る(float rz, float ry, float rx)
        {
            var rpy = new ZyxEulerAngles(rz, ry, rx);
            ToolPose pose = BuildPose(rpy, Frame);

            // z と x の分け方はロック帯で変わりうるので、姿勢そのもので見る
            Assert.Less(Quaternion.Angle(rpy.ToRotation(), ReadBackRpy(pose).ToRotation()), 0.1f);
        }

        [Test]
        public void 工具軸が倒れていても往復する()
        {
            // 投影角なら ±85 度で頭打ちになる領域。SetAxisWorld を使っているので通る
            var rpy = new ZyxEulerAngles(20f, 88f, 0f);
            ToolPose pose = BuildPose(rpy, Frame);

            Assert.Greater(Mathf.Abs(pose.Angles.WorkAngleDeg) + Mathf.Abs(pose.Angles.TravelAngleDeg), 85f,
                           "前提が崩れている (投影角が頭打ちの領域に入っていない)");
            Assert.Less(Quaternion.Angle(rpy.ToRotation(), ReadBackRpy(pose).ToRotation()), 0.1f);
        }

        [Test]
        public void 工具のローカル軸割当を変換し忘れると百八十度ずれる()
        {
            var rpy = new ZyxEulerAngles(35f, -20f, 70f);
            ToolPose pose = BuildPose(rpy, Frame);

            // 読み戻すときだけ Unity の規約のまま渡してしまった場合
            ZyxEulerAngles wrong = ZyxEulerAngles.FromToolAxes(
                Conv.ToExternal(pose.Angles.GetAxisWorld(pose.Frame)),
                Conv.ToExternal(pose.Angles.GetToolReferenceWorld(pose.Frame)),
                ToolUnity.Shaft, ToolUnity.Reference);

            // 静かにずれるのではなく、きっちり 180 度ずれる。間違いとして固定しておく
            Assert.AreEqual(180f, Quaternion.Angle(rpy.ToRotation(), wrong.ToRotation()), 0.5f);
        }

        [Test]
        public void スピンの零基準は進行方向の射影()
        {
            // スピン 0 で組んだ姿勢では、工具の基準方向が M の射影に重なる
            ToolPose pose = BuildPose(new ZyxEulerAngles(0f, 0f, 0f), Frame);
            var zeroSpin = new ToolPose(pose.Frame,
                new ToolPostureAngles(pose.Angles.azimuthDeg, pose.Angles.elevationDeg, 0f));

            Vector3 x = zeroSpin.Angles.GetAxisWorld(zeroSpin.Frame);
            Assert.AreEqual(0f, Vector3.Distance(ToolPostureAngles.SpinZeroReference(zeroSpin.Frame, x),
                                                 zeroSpin.Angles.GetToolReferenceWorld(zeroSpin.Frame)), Tol);
        }

        #endregion

        #region フレームの持ち込み

        [Test]
        public void フレームの原点がそのまま工具位置になる()
        {
            Assert.AreEqual(0f, Vector3.Distance(Conv.ToUnity(Tcp), Frame.Origin), Tol);
            Assert.AreEqual(0f, Vector3.Distance(Frame.Origin,
                                                 BuildPose(new ZyxEulerAngles(30f, 20f, 10f), Frame).Position),
                            Tol);
        }

        [Test]
        public void ロボットのLMNは軸の入れ替えだけで移る()
        {
            LmnFrame robot = RobotFrame;
            PathFrame unity = Frame;

            Assert.AreEqual(0f, Vector3.Distance(Conv.ToUnity(robot.M), unity.Feed), Tol, "M");
            Assert.AreEqual(0f, Vector3.Distance(Conv.ToUnity(robot.N), unity.Normal), Tol, "N");
            Assert.AreEqual(0f, Vector3.Distance(Conv.ToUnity(robot.L), unity.CrossFeed), Tol, "L");
        }

        [Test]
        public void 右手系のLはUnityでも進行方向の右側になる()
        {
            // 反射なので外積の向きは裏返るが、L そのものを渡しているので側は保たれる。
            // TryFromBasis は L から側を決めるため、ここを自分で考えなくてよい
            PathFrame unity = Frame;

            Assert.Greater(Vector3.Dot(unity.CrossFeed, Vector3.Cross(unity.Normal, unity.Feed)), 0.9f,
                           "RightOfTravel になっていない");
        }

        [Test]
        public void 側を取り違えると狙い角の符号が反転する()
        {
            var rpy = new ZyxEulerAngles(35f, -20f, 70f);
            PathFrame right = Frame;

            Assert.IsTrue(PathFrame.TryCreate(right.Origin, right.Feed, right.Normal,
                                              CrossFeedSide.LeftOfTravel, out PathFrame left));

            float w = BuildPose(rpy, right).Angles.WorkAngleDeg;
            float wFlipped = BuildPose(rpy, left).Angles.WorkAngleDeg;

            Assert.AreEqual(0f, w + wFlipped, 1e-2f, "符号反転になっていない");
            Assert.Greater(Mathf.Abs(w), 1f, "前提が崩れている (狙い角がほぼ 0)");
        }

        #endregion
    }
}
