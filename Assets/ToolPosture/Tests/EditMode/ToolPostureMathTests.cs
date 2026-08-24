using NUnit.Framework;
using UnityEngine;
using ToolRuntimeGizmos.Core;

namespace ToolRuntimeGizmos.Tests
{
    public class PathFrameTests
    {
        private const float Tol = 1e-4f;

        [Test]
        public void 生の法線が進行方向と直交していなくても正規直交フレームになる()
        {
            // 進行方向 +X、法線は進行方向成分を含む
            bool ok = PathFrame.TryCreate(Vector3.zero, new Vector3(2f, 0f, 0f), new Vector3(0.3f, 1f, 0f),
                                          CrossFeedSide.RightOfTravel, out var f);

            Assert.IsTrue(ok);
            Assert.AreEqual(1f, f.Feed.magnitude, Tol);
            Assert.AreEqual(1f, f.Normal.magnitude, Tol);
            Assert.AreEqual(1f, f.CrossFeed.magnitude, Tol);
            Assert.AreEqual(0f, Vector3.Dot(f.Feed, f.Normal), Tol);
            Assert.AreEqual(0f, Vector3.Dot(f.Feed, f.CrossFeed), Tol);
            Assert.AreEqual(0f, Vector3.Dot(f.Normal, f.CrossFeed), Tol);

            // 直交化後の法線は +Y になるはず
            Assert.AreEqual(0f, Vector3.Distance(f.Normal, Vector3.up), Tol);
        }

        [Test]
        public void L_は進行方向の右側を向く()
        {
            PathFrame.TryCreate(Vector3.zero, Vector3.right, Vector3.up,
                                CrossFeedSide.RightOfTravel, out var f);

            // +X 方向を向き +Y が上のとき、右側は -Z (Unity は左手系)
            Assert.AreEqual(0f, Vector3.Distance(f.CrossFeed, Vector3.back), Tol);
        }

        [Test]
        public void L_の向きは反転できる()
        {
            PathFrame.TryCreate(Vector3.zero, Vector3.right, Vector3.up, CrossFeedSide.RightOfTravel, out var r);
            PathFrame.TryCreate(Vector3.zero, Vector3.right, Vector3.up, CrossFeedSide.LeftOfTravel, out var l);

            Assert.AreEqual(0f, Vector3.Distance(r.CrossFeed, -l.CrossFeed), Tol);
        }

        [Test]
        public void 区間長ゼロでは構築に失敗する()
        {
            Assert.IsFalse(PathFrame.TryCreate(Vector3.zero, Vector3.zero, Vector3.up,
                                               CrossFeedSide.RightOfTravel, out _));
        }

        [Test]
        public void 法線が進行方向と平行なら構築に失敗する()
        {
            Assert.IsFalse(PathFrame.TryCreate(Vector3.zero, Vector3.right, Vector3.right,
                                               CrossFeedSide.RightOfTravel, out _));
        }

        [Test]
        public void 構築失敗時は前のフレームを引き継ぐ()
        {
            PathFrame.TryCreate(Vector3.zero, Vector3.right, Vector3.up, CrossFeedSide.RightOfTravel, out var prev);
            var next = PathFrame.CreateOrInherit(Vector3.one, Vector3.zero, Vector3.up,
                                                 CrossFeedSide.RightOfTravel, prev);

            Assert.AreEqual(0f, Vector3.Distance(next.Feed, prev.Feed), Tol);
            Assert.AreEqual(0f, Vector3.Distance(next.Origin, Vector3.one), Tol);
        }
    }

    public class ProjectedAngleTests
    {
        private const float Tol = 1e-3f;

        private static PathFrame UnitFrame()
        {
            // L = +X, M = +Y, N = +Z となる人工フレーム (LMN 成分とワールド成分が一致する)
            PathFrame.TryFromBasis(Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, out var f);
            return f;
        }

        [Test]
        public void 工具軸は手計算値と一致する()
        {
            // w = 10, t = 15 -> X = normalize(tan10, tan15, 1)
            var a = ToolPostureAngles.FromProjected(10f, 15f, 0f);
            Vector3 x = a.GetAxisLmn();

            Assert.AreEqual(0.167902f, x.x, Tol);
            Assert.AreEqual(0.255145f, x.y, Tol);
            Assert.AreEqual(0.952214f, x.z, Tol);
            Assert.AreEqual(1f, x.magnitude, Tol);
        }

        [Test]
        public void 投影角は工具軸から往復できる()
        {
            foreach (float w in new[] { -70f, -33.3f, 0f, 7.5f, 61f })
            foreach (float t in new[] { -55f, -12f, 0f, 24.9f, 68f })
            {
                var a = ToolPostureAngles.FromProjected(w, t, 0f);
                ToolPostureAngles.AnglesFromAxisLmn(a.GetAxisLmn(), out float w2, out float t2);

                Assert.AreEqual(w, w2, Tol, $"work w={w} t={t}");
                Assert.AreEqual(t, t2, Tol, $"travel w={w} t={t}");
            }
        }

        [Test]
        public void 球面表現は手計算値と一致する()
        {
            // phi = atan(1 / sqrt(tan10^2 + tan15^2)),  theta = atan(tan15 / tan10)
            var a = ToolPostureAngles.FromProjected(10f, 15f, 0f);

            Assert.AreEqual(72.2160f, a.elevationDeg, 1e-2f);
            Assert.AreEqual(56.6527f, a.azimuthDeg, 1e-2f);
        }

        [Test]
        public void 球面表現から構築して往復できる()
        {
            foreach (float w in new[] { -40f, -6.5f, 0f, 10f, 52f })
            foreach (float t in new[] { -33f, 0f, 15f, 47.5f })
            {
                var src = ToolPostureAngles.FromProjected(w, t, 0f);
                var dst = ToolPostureAngles.FromSpherical(src.azimuthDeg, src.elevationDeg, 0f);

                Assert.AreEqual(w, dst.WorkAngleDeg, 1e-2f, $"work w={w} t={t}");
                Assert.AreEqual(t, dst.TravelAngleDeg, 1e-2f, $"travel w={w} t={t}");
            }
        }

        [Test]
        public void 垂直姿勢は投影角ゼロで仰角90度になる()
        {
            var a = ToolPostureAngles.Vertical;

            Assert.AreEqual(0f, a.WorkAngleDeg, Tol);
            Assert.AreEqual(0f, a.TravelAngleDeg, Tol);
            Assert.AreEqual(90f, a.elevationDeg, 1e-2f);

            Vector3 x = a.GetAxisLmn();
            Assert.AreEqual(0f, Vector3.Distance(x, new Vector3(0f, 0f, 1f)), Tol);
        }

        [Test]
        public void 垂直付近では方位角が暴れるが投影角は暴れない()
        {
            // 傾きを 1/10 ずつ小さくしても theta はほぼ変化しない = 極の特異性
            var big = ToolPostureAngles.FromProjected(1f, 1.5f, 0f);
            var small = ToolPostureAngles.FromProjected(0.1f, 0.15f, 0f);

            Assert.AreEqual(big.azimuthDeg, small.azimuthDeg, 0.1f);
            Assert.AreEqual(88.2f, big.elevationDeg, 0.1f);
            Assert.AreEqual(89.82f, small.elevationDeg, 0.1f);

            // 一方 (w, t) は素直に原点へ向かう
            Assert.AreEqual(0.1f, small.WorkAngleDeg, Tol);
            Assert.AreEqual(0.15f, small.TravelAngleDeg, Tol);
        }

        [Test]
        public void 投影角から構築して球面表現経由で復元できる()
        {
            foreach (float w in new[] { -50f, -8f, 0f, 12f, 44f })
            foreach (float t in new[] { -37f, 0f, 19f, 55f })
            {
                var src = ToolPostureAngles.FromProjected(w, t, 0f);
                var dst = ToolPostureAngles.FromSpherical(src.azimuthDeg, src.elevationDeg, 0f);

                Assert.AreEqual(w, dst.WorkAngleDeg, Tol, $"work w={w} t={t}");
                Assert.AreEqual(t, dst.TravelAngleDeg, Tol, $"travel w={w} t={t}");
            }
        }

        [Test]
        public void 旋回だけを変えても_N_からの傾きは保たれる()
        {
            var a = ToolPostureAngles.FromProjected(14f, -10f, 0f);
            float tiltBefore = a.TiltFromNormalDeg;

            a.azimuthDeg = 40f;   // 旋回角だけ差し替える

            Assert.AreEqual(tiltBefore, a.TiltFromNormalDeg, Tol);
            Assert.AreEqual(40f, a.azimuthDeg, Tol);
        }

        [Test]
        public void 符号付き傾き角は自分の旋回方位で傾き量に一致する()
        {
            var a = ToolPostureAngles.FromProjected(14f, -10f, 0f);

            Assert.AreEqual(a.TiltFromNormalDeg, a.SignedTiltInPlaneDeg(a.azimuthDeg), Tol);
        }

        [Test]
        public void 符号付き傾き角は方位を反転すると符号が反転する()
        {
            var a = ToolPostureAngles.FromProjected(14f, -10f, 0f);

            Assert.AreEqual(-a.TiltFromNormalDeg, a.SignedTiltInPlaneDeg(a.azimuthDeg + 180f), Tol);
        }

        [Test]
        public void 仰角は90度を超えられ旋回角は飛ばない()
        {
            // 垂直をまたいで倒し込んでも theta は固定のまま連続に変化する
            float azimuth = 35f;
            foreach (float alpha in new[] { 40f, 10f, 0.5f, 0f, -0.5f, -10f, -40f })
            {
                var a = ToolPostureAngles.FromSpherical(azimuth, 90f - alpha, 0f);

                Assert.AreEqual(azimuth, a.azimuthDeg, Tol, $"alpha={alpha}");
                Assert.AreEqual(alpha, a.TiltFromNormalDeg, Tol, $"alpha={alpha}");
                Assert.AreEqual(alpha, a.SignedTiltInPlaneDeg(azimuth), Tol, $"alpha={alpha}");
            }
        }

        [Test]
        public void 負の傾きは方位を180度回した姿勢と一致する()
        {
            var negative = ToolPostureAngles.FromSpherical(40f, 90f + 25f, 0f);
            var positive = ToolPostureAngles.FromSpherical(40f + 180f, 90f - 25f, 0f);

            Assert.AreEqual(0f, Vector3.Distance(negative.GetAxisLmn(), positive.GetAxisLmn()), Tol);
        }

        [Test]
        public void 正規化すると仰角が90度以内に収まる()
        {
            var a = ToolPostureAngles.FromSpherical(40f, 115f, 0f);
            Vector3 before = a.GetAxisLmn();

            a.Normalize();

            Assert.AreEqual(65f, a.elevationDeg, Tol);
            Assert.AreEqual(-140f, a.azimuthDeg, Tol);
            Assert.AreEqual(0f, Vector3.Distance(before, a.GetAxisLmn()), Tol, "姿勢は変わらない");
        }

        [Test]
        public void 傾きだけ変えても旋回角は保たれる()
        {
            var a = ToolPostureAngles.FromProjected(14f, -10f, 0f);
            float azimuthBefore = a.azimuthDeg;

            a.TiltFromNormalDeg = 45f;

            Assert.AreEqual(azimuthBefore, a.azimuthDeg, Tol);
            Assert.AreEqual(45f, a.TiltFromNormalDeg, Tol);
        }

        [Test]
        public void 垂直姿勢では傾き量がゼロになる()
        {
            Assert.AreEqual(0f, ToolPostureAngles.Vertical.TiltFromNormalDeg, Tol);
            Assert.IsFalse(ToolPostureAngles.Vertical.TiltIsSignificant());
        }

        [Test]
        public void 工具軸から設定しても極では旋回角が保たれる()
        {
            var a = ToolPostureAngles.FromSpherical(-125f, 55f, 0f);

            a.SetAxisLmn(new Vector3(0f, 0f, 1f));   // 完全な垂直

            Assert.AreEqual(90f, a.elevationDeg, Tol);
            Assert.AreEqual(-125f, a.azimuthDeg, Tol, "極では旋回角がそのまま残る");
        }

        [Test]
        public void N_からの傾きは仰角の余角になる()
        {
            var a = ToolPostureAngles.FromProjected(23f, -31f, 0f);

            Assert.AreEqual(90f - a.elevationDeg, a.TiltFromNormalDeg, 1e-2f);
        }

        [Test]
        public void 工具軸のワールド変換はフレームに追従する()
        {
            var frame = UnitFrame();
            var a = ToolPostureAngles.FromProjected(10f, 15f, 0f);

            Vector3 lmn = a.GetAxisLmn();
            Vector3 world = a.GetAxisWorld(frame);

            // このフレームでは LMN 成分がそのままワールド成分になる
            Assert.AreEqual(0f, Vector3.Distance(lmn, world), Tol);
        }

        [Test]
        public void ワールド方向から姿勢を設定できる()
        {
            PathFrame.TryCreate(Vector3.zero, new Vector3(1f, 0f, 1f), Vector3.up,
                                CrossFeedSide.RightOfTravel, out var frame);

            var src = ToolPostureAngles.FromProjected(23f, -17f, 0f);
            Vector3 world = src.GetAxisWorld(frame);

            var dst = default(ToolPostureAngles);
            dst.SetAxisWorld(frame, world);

            Assert.AreEqual(src.WorkAngleDeg, dst.WorkAngleDeg, Tol);
            Assert.AreEqual(src.TravelAngleDeg, dst.TravelAngleDeg, Tol);
        }
    }

    public class ToolRotationTests
    {
        private const float Tol = 1e-3f;

        [Test]
        public void 工具姿勢はシャフト軸を工具軸に一致させる()
        {
            PathFrame.TryCreate(Vector3.zero, new Vector3(1f, 0.2f, 0.6f), Vector3.up,
                                CrossFeedSide.RightOfTravel, out var frame);
            var a = ToolPostureAngles.FromProjected(18f, -25f, 40f);

            Quaternion rot = a.GetToolRotation(frame, Vector3.up, Vector3.forward);

            Assert.AreEqual(0f, Vector3.Distance(rot * Vector3.up, a.GetAxisWorld(frame)), Tol);
            Assert.AreEqual(0f, Vector3.Distance(rot * Vector3.forward, a.GetToolReferenceWorld(frame)), Tol);
        }

        [Test]
        public void スピン基準は工具軸に直交する()
        {
            PathFrame.TryCreate(Vector3.zero, Vector3.right, Vector3.up,
                                CrossFeedSide.RightOfTravel, out var frame);
            var a = ToolPostureAngles.FromProjected(30f, 20f, 77f);

            Vector3 axis = a.GetAxisWorld(frame);
            Assert.AreEqual(0f, Vector3.Dot(axis, a.GetSpinZeroReferenceWorld(frame)), Tol);
            Assert.AreEqual(0f, Vector3.Dot(axis, a.GetToolReferenceWorld(frame)), Tol);
        }

        [Test]
        public void 工具軸が進行方向と平行ならスピン基準はLへフォールバックする()
        {
            PathFrame.TryCreate(Vector3.zero, Vector3.right, Vector3.up,
                                CrossFeedSide.RightOfTravel, out var frame);

            Vector3 reference = ToolPostureAngles.SpinZeroReference(frame, frame.Feed);

            Assert.AreEqual(0f, Vector3.Distance(reference, frame.CrossFeed), Tol);
        }

        [Test]
        public void スピンは工具軸まわりの回転になる()
        {
            PathFrame.TryCreate(Vector3.zero, Vector3.right, Vector3.up,
                                CrossFeedSide.RightOfTravel, out var frame);
            var a = ToolPostureAngles.FromProjected(12f, 8f, 90f);

            Vector3 axis = a.GetAxisWorld(frame);
            Vector3 zero = a.GetSpinZeroReferenceWorld(frame);
            Vector3 expected = Quaternion.AngleAxis(90f, axis) * zero;

            Assert.AreEqual(0f, Vector3.Distance(a.GetToolReferenceWorld(frame), expected), Tol);
        }
    }

    public class AngleConventionTests
    {
        private const float Tol = 1e-3f;

        [Test]
        public void 表示値と内部値は往復できる()
        {
            var c = new AngleConvention { zeroOffsetDeg = 45f, invertDirection = true };

            foreach (float v in new[] { -73.2f, -5f, 0f, 11.7f, 90f })
                Assert.AreEqual(v, c.ToInternal(c.ToDisplay(v)), Tol);
        }

        [Test]
        public void 反転とオフセットが表示値に反映される()
        {
            var c = new AngleConvention { zeroOffsetDeg = 45f, invertDirection = true };

            Assert.AreEqual(45f, c.ToDisplay(0f), Tol);
            Assert.AreEqual(35f, c.ToDisplay(10f), Tol);
        }

        [Test]
        public void スナップは表示値の刻みで行われる()
        {
            var c = new AngleConvention { zeroOffsetDeg = 45f, invertDirection = true, snapDeg = 5f };

            // 内部 12.3 -> 表示 32.7 -> 35 に丸め -> 内部 10
            Assert.AreEqual(10f, c.SnapInternal(12.3f), Tol);
            Assert.AreEqual(35f, c.ToDisplay(c.SnapInternal(12.3f)), Tol);
        }

        [Test]
        public void スナップ幅ゼロなら値は変わらない()
        {
            var c = new AngleConvention { snapDeg = 0f };
            Assert.AreEqual(12.34f, c.SnapInternal(12.34f), Tol);
        }

        [Test]
        public void 可動範囲でクランプされる()
        {
            var c = AngleConvention.Ranged(-30f, 45f);

            Assert.AreEqual(-30f, c.ClampInternal(-90f), Tol);
            Assert.AreEqual(45f, c.ClampInternal(120f), Tol);
            Assert.AreEqual(10f, c.ClampInternal(10f), Tol);
        }

        [Test]
        public void 制限なしなら円弧の描画範囲は既定値になる()
        {
            AngleConvention.Unlimited().GetArcRange(75f, out float lo, out float hi);

            Assert.AreEqual(-75f, lo, Tol);
            Assert.AreEqual(75f, hi, Tol);
        }

        [Test]
        public void 可動範囲は表示値で与える()
        {
            // 仰角 60 - 90 度 = N からの傾き 0 - 30 度
            var c = AngleConvention.Elevation(0f, 30f);

            Assert.AreEqual(60f, c.minDeg, Tol, "表示値で保持される");
            Assert.AreEqual(90f, c.maxDeg, Tol);
            Assert.AreEqual(0f, c.MinInternal, Tol, "内部値では傾き 0");
            Assert.AreEqual(30f, c.MaxInternal, Tol);
        }

        [Test]
        public void 反転していても可動範囲の大小は内部値で整列される()
        {
            // invertDirection があると表示の下限が内部の上限になる
            var c = new AngleConvention
            {
                zeroOffsetDeg = 90f, invertDirection = true,
                useLimits = true, minDeg = 60f, maxDeg = 90f,
            };

            Assert.Less(c.MinInternal, c.MaxInternal);
            Assert.AreEqual(0f, c.ClampInternal(-10f), Tol);
            Assert.AreEqual(30f, c.ClampInternal(50f), Tol);
        }

        [Test]
        public void 下限が上限を超えていてもクランプが逆側へ張り付かない()
        {
            // Mathf.Clamp は min > max を渡すと逆側の端を返す。
            // 整列してから渡していないと、範囲内の値まで端へ飛ばされる。
            var c = new AngleConvention { useLimits = true, minDeg = 136.6f, maxDeg = -23.26f };

            Assert.AreEqual(-23.26f, c.MinInternal, Tol);
            Assert.AreEqual(136.6f, c.MaxInternal, Tol);

            Assert.AreEqual(0f, c.ClampInternal(0f), Tol, "範囲内なのでそのまま");
            Assert.AreEqual(136.6f, c.ClampInternal(200f), Tol);
            Assert.AreEqual(-23.26f, c.ClampInternal(-90f), Tol);

            // 描画範囲とクランプが食い違わないこと
            c.GetArcRange(75f, out float lo, out float hi);
            Assert.AreEqual(c.MinInternal, lo, Tol);
            Assert.AreEqual(c.MaxInternal, hi, Tol);
        }

        [Test]
        public void 逆転した可動範囲は詰められる()
        {
            var c = new AngleConvention { minDeg = 40f, maxDeg = 10f };
            c.Validate();

            Assert.AreEqual(10f, c.minDeg, Tol);
            Assert.AreEqual(10f, c.maxDeg, Tol);
        }
    }
}
