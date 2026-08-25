using NUnit.Framework;
using UnityEngine;

namespace ToolRuntimeGizmos.Demo.Tests
{
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
