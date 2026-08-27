using NUnit.Framework;
using UnityEngine;
using ToolRuntimeGizmos.Core;

namespace ToolRuntimeGizmos.Tests
{
    /// <summary>
    /// ZYX と「工具軸 + スピン」の相互変換。
    ///
    /// 一番怖いのは、この経路と既存のクォータニオン経路が食い違うこと。
    /// 両者が同じ姿勢を指すことを固定する。
    /// </summary>
    public class ToolAxisZyxTests
    {
        private const float Tol = 1e-2f;

        // 工具モデルの軸割当 (Follower の既定と同じ)
        private static readonly Vector3 Shaft = Vector3.up;
        private static readonly Vector3 Reference = Vector3.forward;

        /// <summary>ロボット系の上方向。スピンの基準に使う。</summary>
        private static readonly Vector3 RobotUp = new Vector3(0f, 0f, 1f);

        private static Vector3 AxisAt(float aboutUpDeg, float fromUpDeg)
        {
            float t = fromUpDeg * Mathf.Deg2Rad, p = aboutUpDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(t) * Mathf.Cos(p), Mathf.Sin(t) * Mathf.Sin(p), Mathf.Cos(t));
        }

        [TestCase(0f, 40f, 0f)]
        [TestCase(35f, 40f, 25f)]
        [TestCase(120f, 75f, -80f)]
        [TestCase(-70f, 120f, 170f)]
        [TestCase(200f, 95f, 45f)]
        public void 工具軸とスピンは往復できる(float about, float from, float spin)
        {
            Vector3 axis = AxisAt(about, from);

            ZyxEulerAngles e = ZyxEulerAngles.FromToolAxis(axis, spin, Shaft, Reference, RobotUp);
            e.ToToolAxis(Shaft, Reference, RobotUp, out Vector3 backAxis, out float backSpin);

            Assert.AreEqual(0f, Vector3.Distance(axis, backAxis), Tol, "工具軸");
            Assert.AreEqual(0f, Mathf.DeltaAngle(spin, backSpin), Tol, "スピン");
        }

        [TestCase(10f, 30f, 15f)]
        [TestCase(-140f, 110f, -95f)]
        public void 姿勢そのものがクォータニオン経路と一致する(float about, float from, float spin)
        {
            Vector3 axis = AxisAt(about, from);
            ZyxEulerAngles e = ZyxEulerAngles.FromToolAxis(axis, spin, Shaft, Reference, RobotUp);

            // 組み立てた姿勢が、本当にその工具軸を向いているか
            Matrix4x4 m = Matrix4x4.Rotate(e.ToRotation());
            Assert.AreEqual(0f, Vector3.Distance(axis, m.MultiplyVector(Shaft).normalized), Tol);
        }

        [Test]
        public void 座標系をまたぐと軸は入れ替えだけスピンは符号反転()
        {
            var conv = HandednessConversion.SwapYZ;
            int checkedCount = 0;

            for (int i = 0; i < 32; i++)
            {
                Quaternion qUnity = Random.rotation;
                if (!TryUnitySpin(qUnity, out Vector3 axisUnity, out float spinUnity)) continue;

                // 軸は入れ替えるだけ、スピンは符号反転。
                // ローカル軸割当も同じ座標系の規約に揃える必要がある。
                ZyxEulerAngles viaAxis = ZyxEulerAngles.FromToolAxis(
                    conv.ToExternal(axisUnity), -spinUnity,
                    conv.ToExternal(Shaft), conv.ToExternal(Reference), RobotUp);

                ZyxEulerAngles viaQuat = ZyxEulerAngles.FromRotation(conv.ToExternal(qUnity));

                Assert.Less(Quaternion.Angle(viaAxis.ToRotation(), viaQuat.ToRotation()), 0.1f,
                            "2 つの経路が同じ姿勢を指していない");
                checkedCount++;
            }

            Assert.Greater(checkedCount, 10, "縮退の除外で試行がほとんど残っていない");
        }

        [Test]
        public void ローカル軸割当を変換し忘れると百八十度ずれる()
        {
            var conv = HandednessConversion.SwapYZ;
            Quaternion qUnity = Quaternion.Euler(35f, -20f, 70f);
            Assert.IsTrue(TryUnitySpin(qUnity, out Vector3 axisUnity, out float spinUnity));

            // 工具のローカル軸割当だけ Unity の規約のまま渡してしまった場合
            ZyxEulerAngles wrong = ZyxEulerAngles.FromToolAxis(
                conv.ToExternal(axisUnity), -spinUnity, Shaft, Reference, RobotUp);
            ZyxEulerAngles right = ZyxEulerAngles.FromRotation(conv.ToExternal(qUnity));

            // 静かにずれるのではなく、きっちり 180 度ずれる。間違いとして固定しておく
            Assert.AreEqual(180f, Quaternion.Angle(wrong.ToRotation(), right.ToRotation()), 0.5f);
        }

        /// <summary>
        /// Unity 側での工具軸とスピン。スピンの基準が消える縮退なら false。
        /// </summary>
        private static bool TryUnitySpin(Quaternion qUnity, out Vector3 axis, out float spinDeg)
        {
            axis = (qUnity * Shaft).normalized;
            spinDeg = 0f;

            Vector3 zero = Vector3.up - axis * Vector3.Dot(Vector3.up, axis);
            if (zero.sqrMagnitude < 0.25f) return false;

            spinDeg = SignedAbout(zero.normalized, (qUnity * Reference).normalized, axis);
            return true;
        }

        [Test]
        public void ベクトル二本の経路はクォータニオン経路と一致する()
        {
            var conv = HandednessConversion.SwapYZ;

            for (int i = 0; i < 64; i++)
            {
                Quaternion qUnity = Random.rotation;

                // 工具軸と基準方向。どちらもベクトルなので入れ替えるだけで移る
                ZyxEulerAngles viaVectors = ZyxEulerAngles.FromToolAxes(
                    conv.ToExternal((qUnity * Shaft).normalized),
                    conv.ToExternal((qUnity * Reference).normalized),
                    conv.ToExternal(Shaft), conv.ToExternal(Reference));

                ZyxEulerAngles viaQuaternion = ZyxEulerAngles.FromRotation(conv.ToExternal(qUnity));

                Assert.Less(Quaternion.Angle(viaVectors.ToRotation(), viaQuaternion.ToRotation()), 0.1f);
            }
        }

        [TestCase(89f)]
        [TestCase(89.99f)]
        [TestCase(90f)]
        [TestCase(90.5f)]
        public void ロック帯でもベクトル二本なら姿勢が保たれる(float pitch)
        {
            var truth = new ZyxEulerAngles(40f, pitch, 25f);
            Matrix4x4 m = Matrix4x4.Rotate(truth.ToRotation());

            ZyxEulerAngles rebuilt = ZyxEulerAngles.FromToolAxes(
                m.MultiplyVector(Shaft).normalized, m.MultiplyVector(Reference).normalized,
                Shaft, Reference);

            // z と x の分け方は変わってよいが、姿勢そのものは一致すること
            Assert.Less(Quaternion.Angle(rebuilt.ToRotation(), truth.ToRotation()), 0.1f);
        }

        [Test]
        public void ジンバルロックでも工具軸は連続に取り出せる()
        {
            // ZYX が壊れる姿勢 (ピッチ +90) を作る
            var locked = new ZyxEulerAngles(0f, 90f, 30f);
            Assert.IsTrue(locked.IsNearGimbalLock());

            locked.ToToolAxis(Shaft, Reference, RobotUp, out Vector3 axis, out float _);

            // 姿勢は正しいので、工具軸は普通に取り出せる
            Assert.AreEqual(1f, axis.magnitude, Tol);
            Matrix4x4 m = Matrix4x4.Rotate(locked.ToRotation());
            Assert.AreEqual(0f, Vector3.Distance(axis, m.MultiplyVector(Shaft).normalized), Tol);
        }

        [Test]
        public void 工具軸が基準と平行でも姿勢は壊れない()
        {
            // スピンの基準が決まらない縮退。スピンの値は不定でよいが、姿勢は有効であること
            ZyxEulerAngles e = ZyxEulerAngles.FromToolAxis(RobotUp, 0f, Shaft, Reference, RobotUp);

            Matrix4x4 m = Matrix4x4.Rotate(e.ToRotation());
            Assert.AreEqual(0f, Vector3.Distance(RobotUp, m.MultiplyVector(Shaft).normalized), Tol);
        }

        #region ヘルパ

        private static float SignedAbout(Vector3 from, Vector3 to, Vector3 axis)
            => Mathf.Atan2(Vector3.Dot(Vector3.Cross(from, to), axis),
                           Vector3.Dot(from, to)) * Mathf.Rad2Deg;

        #endregion
    }
}
