using NUnit.Framework;
using UnityEngine;
using ToolRuntimeGizmos.Core;

namespace ToolRuntimeGizmos.Tests
{
    /// <summary>
    /// ロボット制御側の ToolPosture (ZYX オイラー) との相互変換。
    ///
    /// 対応表:
    ///   Roll  -> Z 軸まわり (最初に適用)
    ///   Pitch -> Y 軸まわり
    ///   Yaw   -> X 軸まわり (最後に適用)
    ///   ツール軸 = 回転行列の col2 (ローカル +Z)
    ///   回転角の基準 = cross(worldZ, ツール軸)  (退化時は (0,1,0))
    ///
    /// このテストはロボット側の実装を写経して、こちらの変換と数値が一致することを固定する。
    /// </summary>
    public class RobotToolPostureInteropTests
    {
        /// <summary>
        /// 両実装は経路が違う (こちらはクォータニオン経由) ので、float の丸めで
        /// 0.03 度程度は差が出る。機械精度から見れば十分小さい。
        /// </summary>
        private const float Tol = 5e-2f;

        /// <summary>
        /// ロボット側 ToolPosture のローカル軸割当。ツール軸 = +Z、回転基準 = +Y。
        /// </summary>
        private static readonly Vector3 ShaftAxis = new Vector3(0f, 0f, 1f);
        private static readonly Vector3 ReferenceAxis = new Vector3(0f, 1f, 0f);

        /// <summary>
        /// ロボット側の回転角基準。cross((0,0,1), toolAxis)、退化時は (0,1,0)。
        /// </summary>
        private static SpinReference RobotSpin =>
            SpinReference.WorldAxisCross(new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f));

        // ------------------------------------------------------------------
        // ロボット側実装の写経 (FromToolAxisAndRotation + FromRotationMatrix)
        // ------------------------------------------------------------------

        private static void RobotFromToolAxisAndRotation(Vector3 toolAxis, float rotationDeg,
                                                         out float rollDeg, out float pitchDeg, out float yawDeg)
        {
            Vector3 Z = toolAxis.normalized;

            Vector3 yRef = Vector3.Cross(new Vector3(0f, 0f, 1f), Z);
            if (yRef.magnitude < 0.0001f) yRef = new Vector3(0f, 1f, 0f);
            yRef = yRef.normalized;

            float s = Mathf.Sin(rotationDeg * Mathf.Deg2Rad);
            float c = Mathf.Cos(rotationDeg * Mathf.Deg2Rad);
            Vector3 Y = (yRef * c + Vector3.Cross(Z, yRef) * s).normalized;

            Vector3 X = Vector3.Cross(Y, Z).normalized;
            Y = Vector3.Cross(Z, X).normalized;

            RobotFromRotationMatrix(X, Y, Z, out rollDeg, out pitchDeg, out yawDeg);
        }

        private static void RobotFromRotationMatrix(Vector3 X, Vector3 Y, Vector3 Z,
                                                    out float rollDeg, out float pitchDeg, out float yawDeg)
        {
            float cp = Mathf.Sqrt(X.x * X.x + X.y * X.y);
            float sp = -X.z;
            float roll, pitch, yaw;

            if (cp < 0.00001f)
            {
                roll = 0f;
                pitch = sp < 0f ? -Mathf.PI / 2f : Mathf.PI / 2f;
                yaw = Mathf.Atan2(-Y.x, Y.y);
            }
            else
            {
                pitch = Mathf.Atan2(sp, cp);
                roll = Mathf.Atan2(X.y / cp, X.x / cp);
                yaw = Mathf.Atan2(Y.z / cp, Z.z / cp);
            }

            rollDeg = roll * Mathf.Rad2Deg;
            pitchDeg = pitch * Mathf.Rad2Deg;
            yawDeg = yaw * Mathf.Rad2Deg;
        }

        // ------------------------------------------------------------------

        private static PathFrame MakeFrame(Vector3 travel, Vector3 normal)
        {
            PathFrame.TryCreate(Vector3.zero, travel, normal, CrossFeedSide.RightOfTravel, out var f);
            return f;
        }

        [Test]
        public void ロボット側の_ZYX_オイラーと一致する()
        {
            var frames = new[]
            {
                MakeFrame(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f)),
                MakeFrame(new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f)),
                MakeFrame(new Vector3(0.6f, 0.3f, 0.5f), new Vector3(-0.3f, 0.9f, -0.2f)),
            };

            foreach (var f in frames)
            foreach (float theta in new[] { -150f, -40f, 25f, 110f })
            foreach (float phi in new[] { 30f, 62f, 85f })
            foreach (float spin in new[] { -140f, -35f, 0f, 80f })
            {
                var a = ToolPostureAngles.FromSpherical(theta, phi, spin);
                Vector3 toolAxis = a.GetAxisWorld(f);

                RobotFromToolAxisAndRotation(toolAxis, spin,
                                             out float roll, out float pitch, out float yaw);

                var mine = ZyxEulerAngles.FromRotation(
                    a.GetToolRotation(f, ShaftAxis, ReferenceAxis, RobotSpin));

                string ctx = $"theta={theta} phi={phi} spin={spin} axis={toolAxis:F3}";
                Assert.AreEqual(0f, Mathf.DeltaAngle(roll, mine.RollDeg), Tol, "Roll " + ctx);
                Assert.AreEqual(0f, Mathf.DeltaAngle(pitch, mine.PitchDeg), Tol, "Pitch " + ctx);
                Assert.AreEqual(0f, Mathf.DeltaAngle(yaw, mine.YawDeg), Tol, "Yaw " + ctx);
            }
        }

        [Test]
        public void ロボット側の_ZYX_オイラーから姿勢を復元できる()
        {
            PathFrame f = MakeFrame(new Vector3(0.6f, 0.3f, 0.5f), new Vector3(-0.3f, 0.9f, -0.2f));

            foreach (float theta in new[] { -120f, -15f, 70f })
            foreach (float phi in new[] { 35f, 70f })
            foreach (float spin in new[] { -95f, 40f })
            {
                var src = ToolPostureAngles.FromSpherical(theta, phi, spin);

                RobotFromToolAxisAndRotation(src.GetAxisWorld(f), spin,
                                             out float roll, out float pitch, out float yaw);

                // ロボット側の (Roll, Pitch, Yaw) だけを受け取って姿勢へ戻す
                var euler = ZyxEulerAngles.FromRollPitchYaw(roll, pitch, yaw);
                var back = ToolPostureAngles.FromToolRotation(f, euler.ToRotation(),
                                                              ShaftAxis, ReferenceAxis, RobotSpin);

                string ctx = $"theta={theta} phi={phi} spin={spin} euler={euler}";
                Assert.AreEqual(0f, Mathf.DeltaAngle(theta, back.azimuthDeg), Tol, "theta " + ctx);
                Assert.AreEqual(phi, back.elevationDeg, Tol, "phi " + ctx);
                Assert.AreEqual(0f, Mathf.DeltaAngle(spin, back.spinAngleDeg), Tol, "spin " + ctx);
            }
        }

        [Test]
        public void ロボット側のジンバルロック分岐は_pitch_が正のとき符号が反転している()
        {
            // 写経した実装をそのまま使い、pitch = ±90 で姿勢が再現できるかを見る。
            // pitch = -90 では一致するが、pitch = +90 では yaw の符号が逆になる。
            // (ロボット側 FromRotationMatrix の yaw = Atan2(-Y.x, Y.y) は
            //  pitch = -90 でのみ正しい)
            foreach (float pitch in new[] { 90f, -90f })
            {
                // col0 が Z 軸に沿う = pitch = ±90 になる姿勢を作る
                var src = ZyxEulerAngles.FromRollPitchYaw(-120f, pitch, 33f);
                Matrix4x4 m = Matrix4x4.Rotate(src.ToRotation());
                Vector3 X = new Vector3(m[0, 0], m[1, 0], m[2, 0]);
                Vector3 Y = new Vector3(m[0, 1], m[1, 1], m[2, 1]);
                Vector3 Z = new Vector3(m[0, 2], m[1, 2], m[2, 2]);

                RobotFromRotationMatrix(X, Y, Z, out float roll, out float p, out float yaw);
                float robotError = Quaternion.Angle(
                    src.ToRotation(), ZyxEulerAngles.FromRollPitchYaw(roll, p, yaw).ToRotation());

                // こちらの実装は両方で復元できる
                float mineError = Quaternion.Angle(
                    src.ToRotation(), ZyxEulerAngles.FromRotation(src.ToRotation()).ToRotation());
                Assert.AreEqual(0f, mineError, Tol, $"pitch={pitch}");

                if (pitch < 0f)
                    Assert.AreEqual(0f, robotError, Tol, "pitch = -90 ではロボット側も正しい");
                else
                    Assert.Greater(robotError, 1f, "pitch = +90 ではロボット側は姿勢を再現できない");
            }
        }

        [Test]
        public void 進行方向基準とワールド基準ではスピンの零点が異なる()
        {
            PathFrame f = MakeFrame(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f));
            var a = ToolPostureAngles.FromSpherical(-40f, 60f, 0f);
            Vector3 axis = a.GetAxisWorld(f);

            Vector3 feedBased = SpinReference.FeedProjected.Resolve(f, axis);
            Vector3 worldBased = RobotSpin.Resolve(f, axis);

            // どちらも工具軸に直交するが、向きは一致しない
            Assert.AreEqual(0f, Vector3.Dot(feedBased, axis), 1e-4f);
            Assert.AreEqual(0f, Vector3.Dot(worldBased, axis), 1e-4f);
            Assert.Greater(Vector3.Angle(feedBased, worldBased), 1f,
                           "同じスピン値でも基準が違えば別の姿勢になる");
        }

        [Test]
        public void ワールド基準は工具軸が基準軸と平行なとき退化を回避する()
        {
            PathFrame f = MakeFrame(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f));

            // 工具軸 = ワールド基準軸 (0,0,1) と一致させる
            Vector3 axis = new Vector3(0f, 0f, 1f);
            Vector3 r = RobotSpin.Resolve(f, axis);

            Assert.AreEqual(1f, r.magnitude, 1e-4f);
            Assert.AreEqual(0f, Vector3.Dot(r, axis), 1e-4f);
            Assert.AreEqual(0f, Vector3.Distance(r, new Vector3(0f, 1f, 0f)), 1e-4f,
                            "フォールバックの (0,1,0) が使われる");
        }
    }
}
