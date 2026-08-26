using NUnit.Framework;
using UnityEngine;
using ToolRuntimeGizmos.Core;

namespace ToolRuntimeGizmos.Tests
{
    /// <summary>
    /// Unity と右手系の相互変換。回転は成分の入れ替えだけでは合わないので、
    /// 右手系の基本回転を明示的に組んだ地上真値と突き合わせる。
    /// </summary>
    public class HandednessConversionTests
    {
        private const float Tol = 1e-3f;

        private static readonly HandednessConversion Conv = HandednessConversion.SwapYZ;

        #region 右手系の地上真値 (列ベクトル規約 v' = R v)

        private static Matrix4x4 Rz(float deg)
        {
            float c = Mathf.Cos(deg * Mathf.Deg2Rad), s = Mathf.Sin(deg * Mathf.Deg2Rad);
            Matrix4x4 m = Matrix4x4.identity;
            m[0, 0] = c; m[0, 1] = -s; m[1, 0] = s; m[1, 1] = c;
            return m;
        }

        private static Matrix4x4 Ry(float deg)
        {
            float c = Mathf.Cos(deg * Mathf.Deg2Rad), s = Mathf.Sin(deg * Mathf.Deg2Rad);
            Matrix4x4 m = Matrix4x4.identity;
            m[0, 0] = c; m[0, 2] = s; m[2, 0] = -s; m[2, 2] = c;
            return m;
        }

        private static Matrix4x4 Rx(float deg)
        {
            float c = Mathf.Cos(deg * Mathf.Deg2Rad), s = Mathf.Sin(deg * Mathf.Deg2Rad);
            Matrix4x4 m = Matrix4x4.identity;
            m[1, 1] = c; m[1, 2] = -s; m[2, 1] = s; m[2, 2] = c;
            return m;
        }

        /// <summary>Y と Z を入れ替える基底変換。自分自身が逆変換。</summary>
        private static Matrix4x4 Swap()
        {
            Matrix4x4 m = Matrix4x4.identity;
            m[1, 1] = 0f; m[1, 2] = 1f; m[2, 1] = 1f; m[2, 2] = 0f;
            return m;
        }

        private static float MaxDiff(Matrix4x4 a, Matrix4x4 b)
        {
            float worst = 0f;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    worst = Mathf.Max(worst, Mathf.Abs(a[r, c] - b[r, c]));
            return worst;
        }

        #endregion

        [Test]
        public void 位置は軸を入れ替えるだけ()
        {
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(1f, 3f, 2f),
                                                 Conv.ToExternal(new Vector3(1f, 2f, 3f))), Tol);
        }

        [Test]
        public void 左右手系の反転を検出する()
        {
            Assert.IsTrue(Conv.FlipsHandedness);
            Assert.IsFalse(HandednessConversion.Identity.FlipsHandedness);
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(30f, 0f, 0f)]
        [TestCase(0f, 45f, 0f)]
        [TestCase(0f, 0f, -60f)]
        [TestCase(-19.3f, 58.5f, 168.4f)]
        [TestCase(120f, -70f, 15f)]
        public void 回転は変換してから分解すると地上真値に一致する(float z, float y, float x)
        {
            // ロボット座標での姿勢を、右手系の基本回転から直接組む
            Matrix4x4 robot = Rz(z) * Ry(y) * Rx(x);

            // 同じ姿勢を Unity 座標で表したもの
            Quaternion unity = (Swap() * robot * Swap()).rotation;

            ZyxEulerAngles rpy = ZyxEulerAngles.FromRotation(Conv.ToExternal(unity));
            Matrix4x4 rebuilt = Rz(rpy.zDeg) * Ry(rpy.yDeg) * Rx(rpy.xDeg);

            Assert.Less(MaxDiff(robot, rebuilt), Tol);
        }

        [Test]
        public void 成分を入れ替えるだけでは合わない()
        {
            Matrix4x4 robot = Rz(40f) * Ry(35f) * Rx(20f);
            Quaternion unity = (Swap() * robot * Swap()).rotation;

            // 符号反転を忘れた変換
            var naive = new Quaternion(unity.x, unity.z, unity.y, unity.w);
            ZyxEulerAngles rpy = ZyxEulerAngles.FromRotation(naive);
            Matrix4x4 rebuilt = Rz(rpy.zDeg) * Ry(rpy.yDeg) * Rx(rpy.xDeg);

            // 「それらしく動くが別物」なので、間違いであることを固定しておく
            Assert.Greater(MaxDiff(robot, rebuilt), 0.1f);
        }

        [Test]
        public void 回転は往復できる()
        {
            for (int i = 0; i < 32; i++)
            {
                Quaternion q = Random.rotation;
                Assert.Less(Quaternion.Angle(q, Conv.ToUnity(Conv.ToExternal(q))), 0.01f);
            }
        }

        [Test]
        public void 位置は往復できる()
        {
            var p = new Vector3(0.5f, -1.25f, 3f);
            Assert.AreEqual(0f, Vector3.Distance(p, Conv.ToUnity(Conv.ToExternal(p))), Tol);
        }

        [Test]
        public void 回した結果と座標変換の順序が入れ替えられる()
        {
            for (int i = 0; i < 32; i++)
            {
                Quaternion q = Random.rotation;
                Vector3 v = Random.onUnitSphere;

                // Unity で回してから変換 == 変換してから外部座標で回す
                Assert.AreEqual(0f, Vector3.Distance(Conv.ToExternal(q * v),
                                                     Conv.ToExternal(q) * Conv.ToExternal(v)), Tol);
            }
        }

        [Test]
        public void 恒等変換は何も変えない()
        {
            Quaternion q = Quaternion.Euler(10f, 20f, 30f);
            Assert.Less(Quaternion.Angle(q, HandednessConversion.Identity.ToExternal(q)), 0.01f);
        }
    }
}
