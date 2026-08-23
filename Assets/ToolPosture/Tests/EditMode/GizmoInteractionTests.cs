using NUnit.Framework;
using UnityEngine;
using ToolPosture.Core;
using ToolPosture.Gizmo;
using ToolPosture.Demo;

namespace ToolPosture.Tests
{
    /// <summary>
    /// テスト用の単純なビューポート。ワールドの XY をそのままスクリーンへ写す正射影。
    /// pixelsPerUnit = 100 なので 1m = 100px。
    /// </summary>
    class FlatViewport : IGizmoViewport
    {
        public const float PixelsPerUnit = 100f;
        public static readonly Vector2 Center = new Vector2(400f, 300f);

        public Camera RenderCamera => null;
        public Vector3 EyePosition => new Vector3(0f, 0f, -10f);
        public Vector2 PixelSize => new Vector2(800f, 600f);

        public Ray ScreenPointToRay(Vector2 screenPos)
            => new Ray(new Vector3((screenPos.x - Center.x) / PixelsPerUnit,
                                   (screenPos.y - Center.y) / PixelsPerUnit,
                                   -10f), Vector3.forward);

        public bool TryWorldToScreenPoint(Vector3 worldPos, out Vector2 screenPos)
        {
            screenPos = new Vector2(worldPos.x * PixelsPerUnit + Center.x,
                                    worldPos.y * PixelsPerUnit + Center.y);
            return true;
        }

        public float WorldPerPixel(Vector3 worldPos) => 1f / PixelsPerUnit;
    }

    public class TangentRotationDragTests
    {
        const float Tol = 1e-3f;

        static void Setup(out FlatViewport vp, out Vector3 center, out Vector3 u, out Vector3 v)
        {
            vp = new FlatViewport();
            center = Vector3.zero;
            u = Vector3.right;    // 0 度方向
            v = Vector3.up;       // +90 度方向
        }

        [Test]
        public void 接線方向へのドラッグは弧長を角度に換算する()
        {
            Setup(out var vp, out var c, out var u, out var v);
            float radius = 0.5f;

            // 0 度の位置を掴む。その点の接線は +v (画面では +Y)。
            vp.TryWorldToScreenPoint(c + u * radius, out Vector2 grabScreen);

            var drag = new TangentRotationDrag();
            drag.Begin(vp, c, u, v, radius, 0f, 0f, grabScreen, 0f);
            Assert.IsTrue(drag.IsValid);

            // 50px = 0.5m の弧長 -> 0.5 / 0.5 = 1 rad = 57.2958 度
            Assert.IsTrue(drag.TryGetValue(grabScreen + new Vector2(0f, 50f), out float value));
            Assert.AreEqual(57.2958f, value, 1e-2f);

            // 逆方向なら符号が反転する
            drag.TryGetValue(grabScreen + new Vector2(0f, -50f), out float back);
            Assert.AreEqual(-57.2958f, back, 1e-2f);
        }

        [Test]
        public void 接線と直交する方向へ動かしても角度は変わらない()
        {
            Setup(out var vp, out var c, out var u, out var v);
            vp.TryWorldToScreenPoint(c + u * 0.5f, out Vector2 grabScreen);

            var drag = new TangentRotationDrag();
            drag.Begin(vp, c, u, v, 0.5f, 0f, 12f, grabScreen, 0f);

            drag.TryGetValue(grabScreen + new Vector2(80f, 0f), out float value);
            Assert.AreEqual(12f, value, Tol);
        }

        [Test]
        public void 掴んだ時点の値からの相対で動く()
        {
            Setup(out var vp, out var c, out var u, out var v);
            vp.TryWorldToScreenPoint(c + u * 0.5f, out Vector2 grabScreen);

            var drag = new TangentRotationDrag();
            drag.Begin(vp, c, u, v, 0.5f, 0f, -30f, grabScreen, 0f);

            drag.TryGetValue(grabScreen + new Vector2(0f, 50f), out float value);
            Assert.AreEqual(-30f + 57.2958f, value, 1e-2f);
        }

        [Test]
        public void 半径が大きいほど感度は下がる()
        {
            Setup(out var vp, out var c, out var u, out var v);

            var small = new TangentRotationDrag();
            vp.TryWorldToScreenPoint(c + u * 0.25f, out Vector2 s1);
            small.Begin(vp, c, u, v, 0.25f, 0f, 0f, s1, 0f);

            var large = new TangentRotationDrag();
            vp.TryWorldToScreenPoint(c + u * 1.0f, out Vector2 s2);
            large.Begin(vp, c, u, v, 1.0f, 0f, 0f, s2, 0f);

            Assert.Greater(small.DegreesPerPixel, large.DegreesPerPixel);
            Assert.AreEqual(4f, small.DegreesPerPixel / large.DegreesPerPixel, 1e-2f);
        }

        [Test]
        public void 感度は上限でクランプされる()
        {
            Setup(out var vp, out var c, out var u, out var v);
            vp.TryWorldToScreenPoint(c + u * 0.02f, out Vector2 grabScreen);

            var drag = new TangentRotationDrag();
            drag.Begin(vp, c, u, v, 0.02f, 0f, 0f, grabScreen, 2f);

            // クランプ無しなら 28.6 deg/px になるところを 2 deg/px に抑える
            Assert.AreEqual(2f, drag.DegreesPerPixel, Tol);
        }

        [Test]
        public void 開始していなければ値を返さない()
        {
            var drag = new TangentRotationDrag();
            Assert.IsFalse(drag.TryGetValue(Vector2.zero, out _));
        }
    }

    /// <summary>
    /// 傾きが 0 になっても旋回角が失われないこと。
    /// 姿勢の保持が球面表現 (theta, phi, spin) 一本なので、theta は姿勢の中にあり、
    /// 別途の保持値やそれを同期する不変条件は存在しない。
    /// </summary>
    public class AzimuthPreservationTests
    {
        const float Tol = 1e-2f;

        GameObject _go;
        ToolPostureGizmo _gizmo;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestGizmo");
            _gizmo = _go.AddComponent<ToolPostureGizmo>();
            _gizmo.RefreshFrame();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void 球面表現で設定して読み戻せる()
        {
            _gizmo.SetSpherical(40f, 70f);

            Assert.AreEqual(40f, _gizmo.AzimuthDeg, Tol);
            Assert.AreEqual(70f, _gizmo.ElevationDeg, Tol);
            Assert.AreEqual(20f, _gizmo.TiltFromNormalDeg, Tol);
            Assert.IsTrue(_gizmo.AzimuthAffectsToolAxis);
        }

        [Test]
        public void 傾きをゼロにしても旋回角は失われない()
        {
            _gizmo.SetSpherical(40f, 70f);

            // 追従円弧を alpha = 0 までドラッグしたのと同じ操作 (phi だけを動かす)
            var a = _gizmo.Angles;
            a.TiltFromNormalDeg = 0f;
            _gizmo.Angles = a;

            Assert.IsFalse(_gizmo.AzimuthAffectsToolAxis, "工具軸には効かなくなる");
            Assert.AreEqual(40f, _gizmo.AzimuthDeg, Tol, "値としては保持される");
            Assert.AreEqual(90f, _gizmo.ElevationDeg, Tol);
            Assert.AreEqual(0f, Vector3.Distance(_gizmo.Angles.GetAxisLmn(), new Vector3(0f, 0f, 1f)), Tol);
        }

        [Test]
        public void 垂直を経由しても元の旋回角で起き上がる()
        {
            _gizmo.SetSpherical(-125f, 55f);

            // 傾きハンドルで phi を 90 度まで持っていって、また戻す
            var a = _gizmo.Angles;
            a.elevationDeg = 90f;
            _gizmo.Angles = a;
            a.elevationDeg = 55f;
            _gizmo.Angles = a;

            Assert.AreEqual(-125f, _gizmo.AzimuthDeg, Tol);
            Assert.AreEqual(55f, _gizmo.ElevationDeg, Tol);
        }

        [Test]
        public void 姿勢の代入は垂直姿勢でも往復する()
        {
            _gizmo.SetSpherical(-125f, 90f);

            // 第三者が書きそうな素朴な往復。ここで theta が落ちないことが要点。
            var copy = _gizmo.Angles;
            _gizmo.Angles = copy;

            Assert.AreEqual(-125f, _gizmo.AzimuthDeg, Tol);
            Assert.AreEqual(90f, _gizmo.ElevationDeg, Tol);
        }

        [Test]
        public void 極でも球面表現は完全に往復する()
        {
            foreach (float azimuth in new[] { -170f, -35f, 0f, 62f, 148f })
            {
                _gizmo.SetSpherical(azimuth, 90f);   // 真上 = 極

                Assert.AreEqual(azimuth, _gizmo.AzimuthDeg, Tol, $"theta={azimuth}");
                Assert.AreEqual(90f, _gizmo.ElevationDeg, Tol);
            }
        }

        [Test]
        public void 投影角で書き換えると旋回角もそれに従う()
        {
            _gizmo.SetSpherical(10f, 60f);

            // w = 0, t = 30 は M 方向へ倒れている = 旋回角 90 度
            _gizmo.Angles = ToolPostureAngles.FromProjected(0f, 30f, 0f);

            Assert.AreEqual(90f, _gizmo.AzimuthDeg, Tol);
        }

        [Test]
        public void 工具軸を直接与えても極では旋回角が保たれる()
        {
            _gizmo.SetSpherical(77f, 65f);
            _gizmo.RefreshFrame();

            // フレームの法線そのもの = 完全な垂直
            _gizmo.SetToolAxisWorld(_gizmo.Frame.Normal);

            Assert.AreEqual(90f, _gizmo.ElevationDeg, Tol);
            Assert.AreEqual(77f, _gizmo.AzimuthDeg, Tol);
        }
    }

    public class DistortedViewportTests
    {
        [Test]
        public void 歪みの付与と除去は往復する()
        {
            // 4:3 画像の隅の正規化半径 sqrt(1 + (4/3)^2) = 1.667 まで可逆になる k1
            float k1 = -0.10f;
            Assert.Greater(DistortedOverlayViewport.MaxUndistortableRadius(k1),
                           Mathf.Sqrt(1f + (4f / 3f) * (4f / 3f)) * (1f + k1 * 2.778f),
                           "画像の隅まで可逆であること");

            foreach (var n in new[]
                     {
                         new Vector2(0f, 0f),
                         new Vector2(0.3f, -0.2f),
                         new Vector2(1.0f, 0.9f),
                         new Vector2(4f / 3f, 1f),      // 画像の隅
                     })
            {
                Vector2 d = DistortedOverlayViewport.Distort(n, k1);
                Vector2 back = DistortedOverlayViewport.Undistort(d, k1);
                Assert.AreEqual(n.x, back.x, 1e-4f, $"n={n}");
                Assert.AreEqual(n.y, back.y, 1e-4f, $"n={n}");
            }
        }

        [Test]
        public void 折り返しの向こう側は上限へ丸められる()
        {
            // f(r) = r(1 + k1 r^2) は k1 < 0 で単調でなくなるので、
            // その先を素直に反復すると別の解に落ちる。上限で丸めて防ぐ。
            float k1 = -0.22f;
            float maxRadius = DistortedOverlayViewport.MaxUndistortableRadius(k1);

            Assert.AreEqual(Mathf.Sqrt(-1f / (3f * k1)) * (2f / 3f), maxRadius, 1e-4f);

            float foldRadius = Mathf.Sqrt(-1f / (3f * k1));
            Vector2 far = new Vector2(maxRadius * 3f, 0f);
            Vector2 u = DistortedOverlayViewport.Undistort(far, k1);

            // 折り返しの手前の枝に留まること (反対側の解へ飛ばない)
            Assert.LessOrEqual(u.magnitude, foldRadius + 1e-2f, "折り返しを越えないこと");
            Assert.Greater(u.magnitude, foldRadius * 0.9f, "上限付近まで解けていること");
            Assert.LessOrEqual(DistortedOverlayViewport.Distort(u, k1).magnitude, maxRadius + 1e-3f);
        }

        [Test]
        public void 歪み係数が正なら上限は無い()
        {
            Assert.IsTrue(float.IsPositiveInfinity(DistortedOverlayViewport.MaxUndistortableRadius(0.15f)));
            Assert.IsTrue(float.IsPositiveInfinity(DistortedOverlayViewport.MaxUndistortableRadius(0f)));
        }

        [Test]
        public void 歪み係数がゼロなら何もしない()
        {
            var n = new Vector2(0.7f, -0.4f);
            Assert.AreEqual(0f, Vector2.Distance(n, DistortedOverlayViewport.Distort(n, 0f)), 1e-6f);
            Assert.AreEqual(0f, Vector2.Distance(n, DistortedOverlayViewport.Undistort(n, 0f)), 1e-6f);
        }

        [Test]
        public void ワールドから歪み画像座標へ変換して光線に戻せる()
        {
            var go = new GameObject("TestOverlayCam");
            var cam = go.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 0f, -3f);
            cam.transform.rotation = Quaternion.identity;
            cam.fieldOfView = 45f;
            var rt = new RenderTexture(640, 480, 16);
            cam.targetTexture = rt;

            try
            {
                var vp = new DistortedOverlayViewport(cam, new Vector2(640f, 480f), -0.10f);

                foreach (var world in new[] { Vector3.zero, new Vector3(0.4f, 0.3f, 0.2f), new Vector3(-0.7f, -0.5f, 0.1f) })
                {
                    Assert.IsTrue(vp.TryWorldToScreenPoint(world, out Vector2 pixel), "投影できること");

                    Ray ray = vp.ScreenPointToRay(pixel);
                    float distance = Vector3.Cross(ray.direction.normalized, world - ray.origin).magnitude;
                    Assert.Less(distance, 1e-3f, $"光線が元の点を通ること world={world}");
                }
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        [Test]
        public void 視点の背後は投影できない()
        {
            var go = new GameObject("TestOverlayCam2");
            var cam = go.AddComponent<Camera>();
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = Quaternion.identity;
            var rt = new RenderTexture(320, 240, 16);
            cam.targetTexture = rt;

            try
            {
                var vp = new DistortedOverlayViewport(cam, new Vector2(320f, 240f), -0.1f);
                Assert.IsFalse(vp.TryWorldToScreenPoint(new Vector3(0f, 0f, -5f), out _));
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
