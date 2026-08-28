using NUnit.Framework;
using UnityEngine;
using ToolRuntimeGizmos.Core;
using ToolRuntimeGizmos.Tool;

namespace ToolRuntimeGizmos.Tests
{
    /// <summary>
    /// ロボット座標系の ZYX オイラー角から、ハンドルに渡す <see cref="ToolPose"/> を組む手順。
    ///
    /// 途中でクォータニオンを使わない。ZYX をベクトル 2 本に開いてから座標系を移し、
    /// Unity 側のフレームの上で角度に分解する。ベクトルは軸の入れ替えだけで移るので、
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
        /// ロボット座標の値からハンドルの姿勢を組む。アプリ側に書くことになるコード。
        /// </summary>
        /// <param name="tcpRobot">工具位置。</param>
        /// <param name="rpyRobot">工具姿勢の ZYX オイラー角。</param>
        /// <param name="pointRobot">区間始点 p(i)。</param>
        /// <param name="nextPointRobot">区間終点 p(i+1)。</param>
        /// <param name="normalRobot">面法線。</param>
        private static bool TryBuildPose(Vector3 tcpRobot, ZyxEulerAngles rpyRobot,
                                        Vector3 pointRobot, Vector3 nextPointRobot, Vector3 normalRobot,
                                        out ToolPose pose)
        {
            pose = default;

            // 1. ロボット座標のまま、ZYX を工具軸と基準方向の 2 本に開く
            Matrix4x4 r = RobotPostureConvert.FromZyx(rpyRobot);
            Vector3 axisRobot = r.MultiplyVector(ToolRobot.Shaft);
            Vector3 referenceRobot = r.MultiplyVector(ToolRobot.Reference);

            // 2. ベクトルなので軸の入れ替えだけで Unity へ移る
            Vector3 axis = Conv.ToUnity(axisRobot).normalized;
            Vector3 reference = Conv.ToUnity(referenceRobot).normalized;

            // 3. Unity 側のフレーム。原点は工具位置、進行方向は p(i) -> p(i+1)
            if (!PathFrame.TryCreate(Conv.ToUnity(tcpRobot),
                                     Conv.ToUnity(nextPointRobot - pointRobot),
                                     Conv.ToUnity(normalRobot),
                                     CrossFeedSide.RightOfTravel, out PathFrame frame))
                return false;

            // 4. フレームの上で角度にする。
            //    工具軸から旋回角と仰角、基準方向からスピン。
            //    SetProjected は ±85 度で頭打ちになるので、ここでは使わない
            var angles = new ToolPostureAngles();
            angles.SetAxisWorld(frame, axis);

            Vector3 x = angles.GetAxisWorld(frame);
            angles.spinAngleDeg = Vector3.SignedAngle(
                ToolPostureAngles.SpinZeroReference(frame, x), reference, x);

            pose = new ToolPose(frame, angles);
            return true;
        }

        /// <summary>組んだ姿勢をロボット座標の ZYX へ戻す。<see cref="TryBuildPose"/> の対。</summary>
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
        private static readonly Vector3 Normal = new Vector3(0.1f, 0.2f, 1f);

        #endregion

        [TestCase(0f, 0f, 0f)]
        [TestCase(30f, 20f, 10f)]
        [TestCase(-140f, 55f, 170f)]
        [TestCase(95f, -70f, -35f)]
        public void ロボットのZyxから組んだ姿勢はそのZyxに戻る(float rz, float ry, float rx)
        {
            var rpy = new ZyxEulerAngles(rz, ry, rx);

            Assert.IsTrue(TryBuildPose(Tcp, rpy, P0, P1, Normal, out ToolPose pose));

            // 姿勢そのものが一致すること。z と x の分け方はロック帯で変わりうるので行列で見る
            Assert.Less(Quaternion.Angle(RotationOf(rpy), RotationOf(ReadBackRpy(pose))), 0.1f);
        }

        [Test]
        public void 位置はそのまま座標変換されている()
        {
            Assert.IsTrue(TryBuildPose(Tcp, new ZyxEulerAngles(30f, 20f, 10f), P0, P1, Normal,
                                       out ToolPose pose));

            Assert.AreEqual(0f, Vector3.Distance(Conv.ToUnity(Tcp), pose.Position), Tol);
        }

        [Test]
        public void 進行方向と法線がフレームに入っている()
        {
            Assert.IsTrue(TryBuildPose(Tcp, new ZyxEulerAngles(30f, 20f, 10f), P0, P1, Normal,
                                       out ToolPose pose));

            Vector3 travel = Conv.ToUnity(P1 - P0).normalized;
            Assert.AreEqual(0f, Vector3.Distance(travel, pose.Frame.Feed), Tol, "進行方向");

            // 法線は進行方向と直交化されるので、平面の中にいることだけ見る
            Assert.AreEqual(0f, Vector3.Dot(pose.Frame.Normal, pose.Frame.Feed), Tol, "直交していない");
            Assert.Greater(Vector3.Dot(pose.Frame.Normal, Conv.ToUnity(Normal).normalized), 0.9f, "法線の向き");
        }

        [Test]
        public void 工具軸が倒れていても往復する()
        {
            // 投影角なら ±85 度で頭打ちになる領域。SetAxisWorld を使っているので通る
            var rpy = new ZyxEulerAngles(20f, 88f, 0f);

            Assert.IsTrue(TryBuildPose(Tcp, rpy, P0, P1, Normal, out ToolPose pose));
            Assert.Greater(Mathf.Abs(pose.Angles.WorkAngleDeg) + Mathf.Abs(pose.Angles.TravelAngleDeg), 85f,
                           "前提が崩れている (投影角が頭打ちの領域に入っていない)");

            Assert.Less(Quaternion.Angle(RotationOf(rpy), RotationOf(ReadBackRpy(pose))), 0.1f);
        }

        [Test]
        public void 工具のローカル軸割当を変換し忘れると百八十度ずれる()
        {
            var rpy = new ZyxEulerAngles(35f, -20f, 70f);
            Assert.IsTrue(TryBuildPose(Tcp, rpy, P0, P1, Normal, out ToolPose pose));

            // 読み戻すときだけ Unity の規約のまま渡してしまった場合
            ZyxEulerAngles wrong = ZyxEulerAngles.FromToolAxes(
                Conv.ToExternal(pose.Angles.GetAxisWorld(pose.Frame)),
                Conv.ToExternal(pose.Angles.GetToolReferenceWorld(pose.Frame)),
                ToolUnity.Shaft, ToolUnity.Reference);

            // 静かにずれるのではなく、きっちり 180 度ずれる。間違いとして固定しておく
            Assert.AreEqual(180f, Quaternion.Angle(RotationOf(rpy), RotationOf(wrong)), 0.5f);
        }

        [Test]
        public void スピンの零基準は進行方向なので投影した進行方向と一致する()
        {
            // スピン 0 で組んだ姿勢では、工具の基準方向が M の射影に重なる
            Assert.IsTrue(TryBuildPose(Tcp, new ZyxEulerAngles(0f, 0f, 0f), P0, P1, Normal,
                                       out ToolPose pose));

            var zeroSpin = new ToolPose(pose.Frame,
                new ToolPostureAngles(pose.Angles.azimuthDeg, pose.Angles.elevationDeg, 0f));

            Vector3 x = zeroSpin.Angles.GetAxisWorld(zeroSpin.Frame);
            Assert.AreEqual(0f, Vector3.Distance(ToolPostureAngles.SpinZeroReference(zeroSpin.Frame, x),
                                                 zeroSpin.Angles.GetToolReferenceWorld(zeroSpin.Frame)), Tol);
        }

        private static Quaternion RotationOf(ZyxEulerAngles e) => e.ToRotation();
    }
}
