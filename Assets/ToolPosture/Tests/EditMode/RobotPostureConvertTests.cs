using NUnit.Framework;
using UnityEngine;
using ToolRuntimeGizmos.Core;

namespace ToolRuntimeGizmos.Tests
{
    /// <summary>
    /// ロボット座標系の中だけで閉じた 3 つの表し方の相互変換。
    ///
    /// 角度の値そのものではなく、組み上がる回転行列が一致するかを見る。
    /// 分解の仕方は縮退で変わってよいが、姿勢は変わってはいけない。
    /// </summary>
    public class RobotPostureConvertTests
    {
        private const float Tol = 1e-4f;

        private static readonly ToolAxes Tool = ToolAxes.Robot;

        /// <summary>面法線が Z、進行方向が X のごく普通のフレーム。</summary>
        private static LmnFrame FlatFrame
            => new LmnFrame(new Vector3(0f, -1f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f));

        private static LmnFrame TiltedFrame
            => LmnFrame.FromPath(Vector3.zero, new Vector3(1f, 0.4f, 0.2f), new Vector3(0.1f, -0.3f, 1f));

        private static float MaxDiff(Matrix4x4 a, Matrix4x4 b)
        {
            float d = 0f;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    d = Mathf.Max(d, Mathf.Abs(a[r, c] - b[r, c]));
            return d;
        }

        private static void AssertRotationMatrix(Matrix4x4 m, string what)
        {
            Assert.AreEqual(0f, MaxDiff(m * m.transpose, Matrix4x4.identity), Tol, what + ": 直交していない");

            Vector3 c0 = m.GetColumn(0), c1 = m.GetColumn(1), c2 = m.GetColumn(2);
            Assert.AreEqual(1f, Vector3.Dot(Vector3.Cross(c0, c1), c2), Tol, what + ": 行列式が +1 でない");
        }

        #region ZYX

        [TestCase(0f, 0f, 0f)]
        [TestCase(30f, 20f, 10f)]
        [TestCase(-140f, 55f, 170f)]
        [TestCase(179f, -80f, -179f)]
        public void Zyxは往復できる(float rz, float ry, float rx)
        {
            var e = new ZyxEulerAngles(rz, ry, rx);
            Matrix4x4 m = RobotPostureConvert.FromZyx(e);
            AssertRotationMatrix(m, "FromZyx");

            ZyxEulerAngles back = RobotPostureConvert.ToZyx(m);

            Assert.AreEqual(0f, Mathf.DeltaAngle(rz, back.zDeg), 1e-2f, "rz");
            Assert.AreEqual(0f, Mathf.DeltaAngle(ry, back.yDeg), 1e-2f, "ry");
            Assert.AreEqual(0f, Mathf.DeltaAngle(rx, back.xDeg), 1e-2f, "rx");
        }

        [TestCase(90f)]
        [TestCase(-90f)]
        public void ロック帯では角度は分けられないが姿勢は保たれる(float pitch)
        {
            var e = new ZyxEulerAngles(40f, pitch, 25f);
            Matrix4x4 m = RobotPostureConvert.FromZyx(e);

            Assert.IsTrue(RobotPostureConvert.IsNearGimbalLock(m), "ロックが検出されていない");

            // z と x の分け方は変わってよい。行列が戻ればよい
            Matrix4x4 rebuilt = RobotPostureConvert.FromZyx(RobotPostureConvert.ToZyx(m));
            Assert.AreEqual(0f, MaxDiff(m, rebuilt), Tol);
        }

        #endregion

        #region 工具軸 + 軸まわりの回転角

        /// <summary>回転角の零基準。工具軸がこれと平行になると角が決まらない。</summary>
        private static readonly Vector3 SpinZero = new Vector3(0f, 0f, 1f);

        private static Vector3 AxisAt(float aboutZeroDeg, float fromZeroDeg)
        {
            float t = fromZeroDeg * Mathf.Deg2Rad, p = aboutZeroDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(t) * Mathf.Cos(p), Mathf.Sin(t) * Mathf.Sin(p), Mathf.Cos(t));
        }

        [TestCase(0f, 40f, 0f)]
        [TestCase(35f, 40f, 25f)]
        [TestCase(120f, 75f, -80f)]
        [TestCase(-70f, 120f, 170f)]
        public void 工具軸と回転角は往復できる(float about, float from, float spin)
        {
            var posture = new ToolAxisSpin(AxisAt(about, from), spin);
            Matrix4x4 m = RobotPostureConvert.FromToolAxisSpin(posture, Tool, SpinZero);
            AssertRotationMatrix(m, "FromToolAxisSpin");

            Assert.IsTrue(RobotPostureConvert.ToToolAxisSpin(m, Tool, SpinZero, out ToolAxisSpin back));

            Assert.AreEqual(0f, Vector3.Distance(posture.Axis, back.Axis), Tol, "工具軸");
            Assert.AreEqual(0f, Mathf.DeltaAngle(spin, back.SpinDeg), 1e-2f, "回転角");
        }

        [Test]
        public void 組み上げた姿勢は本当にその工具軸を向く()
        {
            var posture = new ToolAxisSpin(AxisAt(35f, 40f), 25f);
            Matrix4x4 m = RobotPostureConvert.FromToolAxisSpin(posture, Tool, SpinZero);

            Assert.AreEqual(0f, Vector3.Distance(posture.Axis.normalized,
                                                 m.MultiplyVector(Tool.Shaft).normalized), Tol);
        }

        [Test]
        public void 工具軸が零基準と平行だと回転角が決まらない()
        {
            // 軸は零基準そのもの。姿勢は有効だが角の基準が消える
            var posture = new ToolAxisSpin(SpinZero, 0f);
            Matrix4x4 m = RobotPostureConvert.FromToolAxisSpin(posture, Tool, SpinZero);

            Assert.IsFalse(RobotPostureConvert.ToToolAxisSpin(m, Tool, SpinZero, out ToolAxisSpin back),
                           "決まらないのに true が返っている");

            // 角が不定でも工具軸は取れること
            Assert.AreEqual(0f, Vector3.Distance(SpinZero, back.Axis), Tol);
        }

        [Test]
        public void ジンバルロックでも工具軸と回転角は取り出せる()
        {
            // ZYX が壊れる姿勢 (ピッチ +90)
            var locked = new ZyxEulerAngles(0f, 90f, 30f);
            Assert.IsTrue(locked.IsNearGimbalLock());

            Assert.IsTrue(RobotPostureConvert.ZyxToToolAxisSpin(locked, Tool, SpinZero,
                                                                out ToolAxisSpin posture));
            Assert.AreEqual(1f, posture.Axis.magnitude, Tol);

            // 特異点の位置が違うので、こちらの表現では普通に往復する
            Matrix4x4 rebuilt = RobotPostureConvert.FromToolAxisSpin(posture, Tool, SpinZero);
            Assert.AreEqual(0f, MaxDiff(RobotPostureConvert.FromZyx(locked), rebuilt), 1e-3f);
        }

        [Test]
        public void 零基準に進行方向を渡すとトーチ姿勢の回転角と一致する()
        {
            LmnFrame f = TiltedFrame;

            for (int i = 0; i < 100; i++)
            {
                var t = new TorchAngles(Random.Range(-70f, 70f), Random.Range(-70f, 70f),
                                        Random.Range(-180f, 180f));

                Assert.IsTrue(RobotPostureConvert.TorchToToolAxisSpin(f, t, Tool, f.M,
                                                                     out ToolAxisSpin posture));
                Assert.AreEqual(0f, Mathf.DeltaAngle(t.SpinDeg, posture.SpinDeg), 1e-2f);
            }
        }

        [Test]
        public void 零基準が違うと姿勢は同じでも回転角がずれる()
        {
            var posture = new ToolAxisSpin(AxisAt(35f, 40f), 25f);
            Matrix4x4 m = RobotPostureConvert.FromToolAxisSpin(posture, Tool, SpinZero);

            // 同じ姿勢を別の零基準で読み直す。規約の食い違いはこう出る
            Assert.IsTrue(RobotPostureConvert.ToToolAxisSpin(m, Tool, Vector3.right, out ToolAxisSpin other));

            Assert.AreEqual(0f, Vector3.Distance(posture.Axis, other.Axis), Tol, "工具軸は同じ");
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(posture.SpinDeg, other.SpinDeg)), 1f, "角はずれる");
        }

        #endregion

        #region 姿勢を 1 回の回転として渡す (3 表現とは別)

        [Test]
        public void ロドリゲスの回転軸は工具軸とは別物()
        {
            var posture = new ToolAxisSpin(AxisAt(35f, 40f), 25f);
            Matrix4x4 m = RobotPostureConvert.FromToolAxisSpin(posture, Tool, SpinZero);

            AxisRotation whole = RobotPostureConvert.ToAxisRotation(m);

            // 取り違えると静かにずれる。別物であることを固定しておく
            Assert.Greater(Vector3.Angle(whole.Axis, posture.Axis), 5f);
        }

        [TestCase(1f, 0f, 0f, 30f)]
        [TestCase(0f, 0f, 1f, -120f)]
        [TestCase(0.3f, -0.5f, 0.8f, 95f)]
        [TestCase(0.3f, -0.5f, 0.8f, 180f)]
        public void 軸と回転量は往復できる(float x, float y, float z, float angle)
        {
            var a = new AxisRotation(new Vector3(x, y, z), angle);
            Matrix4x4 m = RobotPostureConvert.FromAxisRotation(a);
            AssertRotationMatrix(m, "FromAxisRotation");

            AxisRotation back = RobotPostureConvert.ToAxisRotation(m);

            // 軸の符号と角度の符号は同時に反転しうる。行列で確かめる
            Assert.AreEqual(0f, MaxDiff(m, RobotPostureConvert.FromAxisRotation(back)), 1e-3f);
        }

        [Test]
        public void 無回転では軸が定まらないので角度だけ零になる()
        {
            AxisRotation a = RobotPostureConvert.ToAxisRotation(Matrix4x4.identity);
            Assert.AreEqual(0f, a.AngleDeg, Tol);
            Assert.AreEqual(1f, a.Axis.magnitude, Tol, "軸が正規化されていない");
        }

        [Test]
        public void Zyxと軸回転は同じ姿勢を指す()
        {
            for (int i = 0; i < 200; i++)
            {
                var e = new ZyxEulerAngles(Random.Range(-180f, 180f), Random.Range(-89f, 89f),
                                     Random.Range(-180f, 180f));

                AxisRotation a = RobotPostureConvert.ToAxisRotation(RobotPostureConvert.FromZyx(e));
                ZyxEulerAngles back = RobotPostureConvert.ToZyx(RobotPostureConvert.FromAxisRotation(a));

                Assert.AreEqual(0f, MaxDiff(RobotPostureConvert.FromZyx(e),
                                            RobotPostureConvert.FromZyx(back)), 1e-3f);
            }
        }

        #endregion

        #region LMN + トーチ姿勢

        [TestCase(0f, 0f, 0f)]
        [TestCase(25f, -15f, 40f)]
        [TestCase(-60f, 45f, -170f)]
        [TestCase(80f, 5f, 90f)]
        public void トーチ姿勢は往復できる(float work, float travel, float spin)
        {
            foreach (LmnFrame f in new[] { FlatFrame, TiltedFrame })
            {
                var t = new TorchAngles(work, travel, spin);
                Matrix4x4 m = RobotPostureConvert.FromTorch(f, t, Tool);
                AssertRotationMatrix(m, "FromTorch");

                TorchAngleIssues issues = RobotPostureConvert.ToTorch(m, f, Tool, out TorchAngles back);

                Assert.AreEqual(TorchAngleIssues.None, issues);
                Assert.AreEqual(0f, Mathf.DeltaAngle(work, back.WorkDeg), 1e-2f, "狙い角");
                Assert.AreEqual(0f, Mathf.DeltaAngle(travel, back.TravelDeg), 1e-2f, "前進後退角");
                Assert.AreEqual(0f, Mathf.DeltaAngle(spin, back.SpinDeg), 1e-2f, "スピン");
            }
        }

        [Test]
        public void 角度零のとき工具軸は法線を向く()
        {
            LmnFrame f = TiltedFrame;
            Matrix4x4 m = RobotPostureConvert.FromTorch(f, new TorchAngles(0f, 0f, 0f), Tool);

            Assert.AreEqual(0f, Vector3.Distance(f.N, m.MultiplyVector(Tool.Shaft).normalized), Tol);
        }

        [Test]
        public void 狙い角と前進後退角は工具軸の射影として定義どおり()
        {
            LmnFrame f = FlatFrame;
            var t = new TorchAngles(20f, -35f, 0f);
            Vector3 axis = RobotPostureConvert.FromTorch(f, t, Tool).MultiplyVector(Tool.Shaft).normalized;

            Assert.AreEqual(Mathf.Tan(20f * Mathf.Deg2Rad),
                            Vector3.Dot(axis, f.L) / Vector3.Dot(axis, f.N), 1e-3f, "狙い角");
            Assert.AreEqual(Mathf.Tan(-35f * Mathf.Deg2Rad),
                            Vector3.Dot(axis, f.M) / Vector3.Dot(axis, f.N), 1e-3f, "前進後退角");
        }

        [Test]
        public void 工具軸が進行方向と平行だとスピンが決まらない()
        {
            LmnFrame f = FlatFrame;

            // 工具軸を M に向ける
            Matrix4x4 m = RobotPostureConvert.FromTorch(f, new TorchAngles(0f, 90f, 0f), Tool);
            Vector3 axis = m.MultiplyVector(Tool.Shaft).normalized;
            Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(axis, f.M)), 1e-3f, "前提が崩れている");

            TorchAngleIssues issues = RobotPostureConvert.ToTorch(m, f, Tool, out _);
            Assert.IsTrue((issues & TorchAngleIssues.SpinUndefined) != 0);
        }

        [Test]
        public void 工具軸が面の裏を向くと射影角では表せない()
        {
            LmnFrame f = FlatFrame;
            Matrix4x4 m = RobotPostureConvert.FromAxisRotation(new AxisRotation(f.L, 150f));

            TorchAngleIssues issues = RobotPostureConvert.ToTorch(m, f, Tool, out _);
            Assert.IsTrue((issues & TorchAngleIssues.AxisNotAboveSurface) != 0);
        }

        #endregion

        #region ZyxEulerAngles との一本化

        [Test]
        public void 行列版とクォータニオン版は同じ姿勢を指す()
        {
            for (int i = 0; i < 200; i++)
            {
                var e = new ZyxEulerAngles(Random.Range(-180f, 180f), Random.Range(-89f, 89f),
                                           Random.Range(-180f, 180f));

                // ToMatrix はクォータニオンを経由しない。ToRotation と一致すること
                Assert.AreEqual(0f, MaxDiff(e.ToMatrix(), Matrix4x4.Rotate(e.ToRotation())), 1e-3f);
            }
        }

        [Test]
        public void ZyxEulerAnglesの分解はRobotPostureConvertと同じ結果になる()
        {
            for (int i = 0; i < 200; i++)
            {
                Matrix4x4 m = RobotPostureConvert.FromZyx(
                    new ZyxEulerAngles(Random.Range(-180f, 180f), Random.Range(-90f, 90f),
                                       Random.Range(-180f, 180f)));

                ZyxEulerAngles viaStruct = ZyxEulerAngles.FromMatrix(m);
                ZyxEulerAngles viaConvert = RobotPostureConvert.ToZyx(m);

                Assert.AreEqual(viaConvert.zDeg, viaStruct.zDeg, 1e-4f);
                Assert.AreEqual(viaConvert.yDeg, viaStruct.yDeg, 1e-4f);
                Assert.AreEqual(viaConvert.xDeg, viaStruct.xDeg, 1e-4f);
            }
        }

        #endregion

        #region 3 つを跨ぐ

        [Test]
        public void 三つの表し方は同じ姿勢を指す()
        {
            LmnFrame f = TiltedFrame;

            for (int i = 0; i < 200; i++)
            {
                var t = new TorchAngles(Random.Range(-70f, 70f), Random.Range(-70f, 70f),
                                        Random.Range(-180f, 180f));
                Matrix4x4 truth = RobotPostureConvert.FromTorch(f, t, Tool);

                // トーチ -> ZYX -> 行列
                ZyxEulerAngles e = RobotPostureConvert.TorchToZyx(f, t, Tool);
                Assert.AreEqual(0f, MaxDiff(truth, RobotPostureConvert.FromZyx(e)), 1e-3f, "ZYX 経由");

                // トーチ -> 工具軸 + 回転角 -> 行列
                Assert.IsTrue(RobotPostureConvert.TorchToToolAxisSpin(f, t, Tool, SpinZero,
                                                                      out ToolAxisSpin p));
                Assert.AreEqual(0f, MaxDiff(truth, RobotPostureConvert.FromToolAxisSpin(p, Tool, SpinZero)),
                                1e-3f, "工具軸経由");

                // ZYX -> トーチ、工具軸 + 回転角 -> トーチ が元に戻る
                Assert.AreEqual(TorchAngleIssues.None,
                                RobotPostureConvert.ZyxToTorch(e, f, Tool, out TorchAngles viaZyx));
                Assert.AreEqual(TorchAngleIssues.None,
                                RobotPostureConvert.ToolAxisSpinToTorch(p, Tool, SpinZero, f,
                                                                        out TorchAngles viaAxis));

                Assert.AreEqual(0f, Mathf.DeltaAngle(t.SpinDeg, viaZyx.SpinDeg), 1e-2f, "ZYX 経由のスピン");
                Assert.AreEqual(0f, Mathf.DeltaAngle(t.WorkDeg, viaAxis.WorkDeg), 1e-2f, "工具軸経由の狙い角");
                Assert.AreEqual(0f, Mathf.DeltaAngle(t.SpinDeg, viaAxis.SpinDeg), 1e-2f, "工具軸経由のスピン");
            }
        }

        #endregion
    }
}
