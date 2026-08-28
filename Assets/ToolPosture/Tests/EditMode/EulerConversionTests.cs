using NUnit.Framework;
using UnityEngine;
using ToolRuntimeGizmos.Core;
using ToolRuntimeGizmos.Gizmo;

namespace ToolRuntimeGizmos.Tests
{
    /// <summary>
    /// ZYX オイラー角と回転の相互変換。
    /// </summary>
    public class ZyxEulerTests
    {
        private const float Tol = 1e-2f;

        [Test]
        public void 合成順は_Rz_Ry_Rx_である()
        {
            var e = new ZyxEulerAngles(30f, 40f, 50f);
            Quaternion expected = Quaternion.AngleAxis(30f, Vector3.forward)
                                * Quaternion.AngleAxis(40f, Vector3.up)
                                * Quaternion.AngleAxis(50f, Vector3.right);

            Assert.AreEqual(0f, Quaternion.Angle(expected, e.ToRotation()), Tol);
        }

        [Test]
        public void Unity_の_eulerAngles_とは別物である()
        {
            // Unity の Quaternion.Euler は ZXY 順。混同すると値がずれることを固定しておく。
            var e = new ZyxEulerAngles(30f, 40f, 50f);
            Quaternion zxy = Quaternion.Euler(50f, 40f, 30f);

            Assert.Greater(Quaternion.Angle(zxy, e.ToRotation()), 1f);
        }

        [Test]
        public void 回転との往復が一致する()
        {
            var rng = new System.Random(12345);
            for (int i = 0; i < 200; i++)
            {
                float z = (float)(rng.NextDouble() * 360.0 - 180.0);
                float y = (float)(rng.NextDouble() * 170.0 - 85.0);   // ジンバルロックは別テスト
                float x = (float)(rng.NextDouble() * 360.0 - 180.0);

                var src = new ZyxEulerAngles(z, y, x);
                var back = ZyxEulerAngles.FromRotation(src.ToRotation());

                Assert.AreEqual(0f, Quaternion.Angle(src.ToRotation(), back.ToRotation()), Tol,
                                $"回転が一致すること src={src} back={back}");
                Assert.AreEqual(y, back.yDeg, Tol, $"pitch src={src}");
            }
        }

        [Test]
        public void 任意の回転から取り出して戻せる()
        {
            var rng = new System.Random(999);
            for (int i = 0; i < 200; i++)
            {
                Quaternion q = new Quaternion(
                    (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)).normalized;

                var e = ZyxEulerAngles.FromRotation(q);
                Assert.AreEqual(0f, Quaternion.Angle(q, e.ToRotation()), Tol, $"i={i} e={e}");
            }
        }

        [Test]
        public void ジンバルロックでも回転自体は再現できる()
        {
            foreach (float pitch in new[] { 90f, -90f })
            foreach (float z in new[] { -120f, 0f, 75f })
            {
                var src = new ZyxEulerAngles(z, pitch, 33f);
                var back = ZyxEulerAngles.FromRotation(src.ToRotation());

                // z と x の分け方は復元できないが、姿勢は一致する
                Assert.AreEqual(0f, Quaternion.Angle(src.ToRotation(), back.ToRotation()), Tol,
                                $"src={src} back={back}");
                Assert.AreEqual(0f, back.zDeg, Tol, "z = 0 に寄せた解を返す");
                Assert.IsTrue(back.IsNearGimbalLock());
            }
        }
    }

    /// <summary>
    /// 工具の回転 (ZYX オイラー等) と LMN フレーム上の姿勢の相互変換。
    /// </summary>
    public class ToolRotationConversionTests
    {
        private const float Tol = 1e-2f;

        private static WorkFrame MakeFrame(Vector3 travel, Vector3 normal)
        {
            WorkFrame.TryCreate(Vector3.zero, travel, normal, CrossFeedSide.RightOfTravel, out var f);
            return f;
        }

        [Test]
        public void 姿勢から回転へ変換して戻せる()
        {
            var frames = new[]
            {
                MakeFrame(Vector3.right, Vector3.up),
                MakeFrame(new Vector3(1f, 0f, 0f), new Vector3(0f, -1f, 0f)),      // 下向き面
                MakeFrame(new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, -1f)),      // 垂直面
                MakeFrame(new Vector3(0.6f, 0.3f, -0.7f), new Vector3(-0.4f, -0.8f, -0.4f)),
            };

            foreach (var f in frames)
            foreach (float theta in new[] { -170f, -40f, 0f, 55f, 130f })
            foreach (float phi in new[] { 35f, 62f, 88f })
            foreach (float spin in new[] { -150f, 0f, 70f })
            {
                var src = ToolPostureAngles.FromSpherical(theta, phi, spin);
                Quaternion r = src.GetToolRotation(f, Vector3.up, Vector3.forward);

                var back = ToolPostureAngles.FromToolRotation(f, r, Vector3.up, Vector3.forward);

                Assert.AreEqual(0f, Mathf.DeltaAngle(theta, back.azimuthDeg), 5e-2f, $"theta phi={phi} spin={spin}");
                Assert.AreEqual(phi, back.elevationDeg, 5e-2f, $"phi theta={theta} spin={spin}");
                Assert.AreEqual(0f, Mathf.DeltaAngle(spin, back.spinAngleDeg), 5e-2f, $"spin theta={theta} phi={phi}");
            }
        }

        [Test]
        public void ZYX_オイラーとの往復が一致する()
        {
            WorkFrame f = MakeFrame(new Vector3(0.7f, 0.1f, 0.4f), new Vector3(-0.2f, 0.9f, 0.3f));

            foreach (float theta in new[] { -95f, -20f, 48f })
            foreach (float phi in new[] { 40f, 75f })
            foreach (float spin in new[] { -60f, 25f })
            {
                var src = ToolPostureAngles.FromSpherical(theta, phi, spin);

                // 姿勢 -> 回転 -> ZYX オイラー -> 回転 -> 姿勢
                var euler = ZyxEulerAngles.FromRotation(src.GetToolRotation(f, Vector3.up, Vector3.forward));
                var back = ToolPostureAngles.FromToolRotation(f, euler.ToRotation(), Vector3.up, Vector3.forward);

                Assert.AreEqual(theta, back.azimuthDeg, Tol, $"theta euler={euler}");
                Assert.AreEqual(phi, back.elevationDeg, Tol, $"phi euler={euler}");
                Assert.AreEqual(spin, back.spinAngleDeg, Tol, $"spin euler={euler}");
            }
        }

        [Test]
        public void 工具モデルの軸割当が変わっても往復する()
        {
            WorkFrame f = MakeFrame(Vector3.right, Vector3.up);
            var src = ToolPostureAngles.FromSpherical(-33f, 58f, 44f);

            // シャフト = -Z、基準 = +X という別の割当
            Vector3 shaft = Vector3.back;
            Vector3 reference = Vector3.right;

            Quaternion r = src.GetToolRotation(f, shaft, reference);
            var back = ToolPostureAngles.FromToolRotation(f, r, shaft, reference);

            Assert.AreEqual(-33f, back.azimuthDeg, Tol);
            Assert.AreEqual(58f, back.elevationDeg, Tol);
            Assert.AreEqual(44f, back.spinAngleDeg, Tol);
        }

        [Test]
        public void 極では回転から旋回角が決まらないので現在値が残る()
        {
            WorkFrame f = MakeFrame(Vector3.right, Vector3.up);

            var a = ToolPostureAngles.FromSpherical(-77f, 55f, 10f);
            Quaternion vertical = ToolPostureAngles.FromSpherical(0f, 90f, 10f)
                                  .GetToolRotation(f, Vector3.up, Vector3.forward);

            a.SetToolRotation(f, vertical, Vector3.up, Vector3.forward);

            Assert.AreEqual(90f, a.elevationDeg, Tol);
            Assert.AreEqual(-77f, a.azimuthDeg, Tol, "旋回角は保持される");
        }
    }

    /// <summary>
    /// フレームの生成経路。
    /// </summary>
    public class FrameConstructionTests
    {
        private const float Tol = 1e-4f;

        [Test]
        public void 基底から作ると正規直交化される()
        {
            // わざと直交していない / 長さも 1 でない三つ組
            Assert.IsTrue(WorkFrame.TryFromBasis(Vector3.one,
                new Vector3(0f, 0f, -2f), new Vector3(3f, 0.2f, 0f), new Vector3(0.1f, 5f, 0f), out var f));

            Assert.AreEqual(1f, f.CrossFeed.magnitude, Tol);
            Assert.AreEqual(1f, f.Feed.magnitude, Tol);
            Assert.AreEqual(1f, f.Normal.magnitude, Tol);
            Assert.AreEqual(0f, Vector3.Dot(f.CrossFeed, f.Feed), Tol);
            Assert.AreEqual(0f, Vector3.Dot(f.Feed, f.Normal), Tol);
            Assert.AreEqual(0f, Vector3.Dot(f.Normal, f.CrossFeed), Tol);
        }

        [Test]
        public void 基底から作ると_L_の側が保たれる()
        {
            WorkFrame.TryFromBasis(Vector3.zero, Vector3.back, Vector3.right, Vector3.up, out var right);
            WorkFrame.TryFromBasis(Vector3.zero, Vector3.forward, Vector3.right, Vector3.up, out var left);

            Assert.AreEqual(0f, Vector3.Distance(right.CrossFeed, Vector3.back), Tol);
            Assert.AreEqual(0f, Vector3.Distance(left.CrossFeed, Vector3.forward), Tol);
        }

        [Test]
        public void 退化した基底は拒否される()
        {
            Assert.IsFalse(WorkFrame.TryFromBasis(Vector3.zero, Vector3.up, Vector3.right, Vector3.right, out _));
            Assert.IsFalse(WorkFrame.TryFromBasis(Vector3.zero, Vector3.up, Vector3.zero, Vector3.up, out _));
        }


        [Test]
        public void ギズモにフレームを直接与えられる()
        {
            var go = new GameObject("TestGizmo");
            try
            {
                var gizmo = go.AddComponent<ToolPostureGizmo>();
                WorkFrame.TryCreate(new Vector3(1f, 2f, 3f), Vector3.forward, Vector3.right,
                                    CrossFeedSide.RightOfTravel, out var f);

                gizmo.Frame = f;

                Assert.AreEqual(0f, Vector3.Distance(f.Origin, gizmo.Frame.Origin), Tol);
                Assert.AreEqual(0f, Vector3.Distance(f.Normal, gizmo.Frame.Normal), Tol);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
