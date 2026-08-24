using NUnit.Framework;
using UnityEngine;
using ToolPosture.Core;
using ToolPosture.Gizmo;
using ToolPosture.Demo;

namespace ToolPosture.Tests
{
    /// <summary>
    /// 円弧ハンドルの回転ドラッグ。掴んだ点の接線へレイを投影する方式。
    /// </summary>
    public class RayTangentDragTests
    {
        private const float Tol = 1e-3f;

        /// <summary>
        /// -Z から +Z を覗く平行光線。ワールドの XY 平面がそのまま操作面になる。
        /// </summary>
        private static Ray RayAt(float x, float y)
            => new Ray(new Vector3(x, y, -10f), Vector3.forward);

        /// <summary>
        /// XY 平面上の全周リング。0 度方向 +X、+90 度方向 +Y。
        /// </summary>
        private static GizmoHandleShape Ring(float radius)
            => GizmoHandleShape.Arc(Vector3.zero, Vector3.right, Vector3.up, radius, 0f, 360f);

        [Test]
        public void 接線方向へのドラッグは弧長を角度に換算する()
        {
            // 0 度の位置 (0.5, 0, 0) を掴む。その点の接線は +Y。
            var drag = new RayTangentDrag();
            drag.Begin(Ring(0.5f), 0f, 0f, RayAt(0.5f, 0f));

            // 接線方向へ 0.5m = 0.5 / 0.5 = 1 rad = 57.2958 度
            Assert.IsTrue(drag.TryGetValue(RayAt(0.5f, 0.5f), out float value));
            Assert.AreEqual(57.2958f, value, 1e-2f);

            drag.TryGetValue(RayAt(0.5f, -0.5f), out float back);
            Assert.AreEqual(-57.2958f, back, 1e-2f);
        }

        [Test]
        public void 接線と直交する方向へ動かしても角度は変わらない()
        {
            var drag = new RayTangentDrag();
            drag.Begin(Ring(0.5f), 0f, 12f, RayAt(0.5f, 0f));

            drag.TryGetValue(RayAt(1.3f, 0f), out float value);
            Assert.AreEqual(12f, value, Tol);
        }

        [Test]
        public void 掴んだ時点の値からの相対で動く()
        {
            var drag = new RayTangentDrag();
            drag.Begin(Ring(0.5f), 0f, -30f, RayAt(0.5f, 0f));

            drag.TryGetValue(RayAt(0.5f, 0.5f), out float value);
            Assert.AreEqual(-30f + 57.2958f, value, 1e-2f);
        }

        [Test]
        public void 半径が大きいほど感度は下がる()
        {
            var small = new RayTangentDrag();
            small.Begin(Ring(0.25f), 0f, 0f, RayAt(0.25f, 0f));
            small.TryGetValue(RayAt(0.25f, 0.1f), out float sv);

            var large = new RayTangentDrag();
            large.Begin(Ring(1.0f), 0f, 0f, RayAt(1.0f, 0f));
            large.TryGetValue(RayAt(1.0f, 0.1f), out float lv);

            Assert.Greater(Mathf.Abs(sv), Mathf.Abs(lv));
            Assert.AreEqual(4f, sv / lv, 1e-3f);
        }

        [Test]
        public void 視線が円弧の平面に寝ても感度は変わらない()
        {
            // 光線と円弧平面の交点から極角を取る方式は、ここで発散する。
            GizmoHandleShape shape = Ring(0.5f);
            Vector3 anchor = shape.PointAt(0f);          // (0.5, 0, 0)
            Vector3 tangent = shape.TangentAt(0f);       // +Y

            foreach (float tiltDeg in new[] { 0f, 60f, 88f, 89.9f })
            {
                // 接線とは直交させたまま、視線だけを円弧の平面へ寝かせる
                Vector3 dir = Quaternion.AngleAxis(tiltDeg, tangent) * Vector3.forward;

                var drag = new RayTangentDrag();
                drag.Begin(shape, 0f, 0f, new Ray(anchor - dir * 10f, dir));

                Vector3 moved = anchor + tangent * 0.5f;
                Assert.IsTrue(drag.TryGetValue(new Ray(moved - dir * 10f, dir), out float value),
                              $"tilt={tiltDeg}");
                Assert.AreEqual(57.2958f, value, 1e-2f, $"tilt={tiltDeg}");
            }
        }

        [Test]
        public void レイが接線と平行に近いと値を更新しない()
        {
            var drag = new RayTangentDrag();
            drag.Begin(Ring(0.5f), 0f, 25f, RayAt(0.5f, 0f));

            // 接線 (+Y) に沿って覗き込む = 最近接点が発散する
            var alongTangent = new Ray(new Vector3(0.5f, -10f, 0f), Vector3.up);
            Assert.IsFalse(drag.TryGetValue(alongTangent, out float value));
            Assert.AreEqual(25f, value, Tol, "直前の値を保つ");
        }

        [Test]
        public void 掴んだ瞬間が退化していても回復する()
        {
            var drag = new RayTangentDrag();

            // 掴んだ瞬間は接線と平行 = 起点が取れない
            drag.Begin(Ring(0.5f), 0f, 40f, new Ray(new Vector3(0.5f, -10f, 0f), Vector3.up));

            // 次に有効なレイが来たところを起点にし直す
            Assert.IsTrue(drag.TryGetValue(RayAt(0.5f, 0f), out float first));
            Assert.AreEqual(40f, first, Tol);

            Assert.IsTrue(drag.TryGetValue(RayAt(0.5f, 0.5f), out float value));
            Assert.AreEqual(40f + 57.2958f, value, 1e-2f);
        }
    }

    /// <summary>
    /// コライダーによる当たり判定。円弧に巻いたチューブは断面が円なので、
    /// 掴み幅が視線角度に依存しない。
    /// </summary>
    public class ColliderPickTests
    {
        private GameObject _go;
        private GameObject _camGo;
        private Camera _cam;
        private RenderTexture _rt;
        private ToolPostureGizmo _gizmo;

        [SetUp]
        public void SetUp()
        {
            // Scale は targetCamera の WorldPerPixel から決まる。Camera.main に任せると
            // 開いているシーン次第で値が変わるので、解像度まで固定した専用カメラを使う。
            _camGo = new GameObject("TestCam");
            _cam = _camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 5f;
            _rt = new RenderTexture(800, 600, 16);
            _cam.targetTexture = _rt;
            _cam.enabled = false;

            _go = new GameObject("TestGizmo");
            _gizmo = _go.AddComponent<ToolPostureGizmo>();
            _gizmo.targetCamera = _cam;

            // 旋回リングだけを残す
            _gizmo.showAxisTip = false;
            _gizmo.showSpinRing = false;
            _gizmo.showTiltArc = false;
            _gizmo.SyncColliders();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            _cam.targetTexture = null;
            Object.DestroyImmediate(_camGo);
            _rt.Release();
            Object.DestroyImmediate(_rt);
        }

        /// <summary>
        /// リング上の点を、視線と接線の両方に直交する向きへずらしながら撃ち、
        /// 当たらなくなるまでの距離 (= 実効的な掴み半幅) を返す。
        /// あわせて、最後に当たった点が円弧の中心円からどれだけ離れていたかも返す。
        /// </summary>
        private float MeasureHalfWidth(Vector3 point, Vector3 tangent, Vector3 viewDir,
                                       float step, out float hitDistanceFromArc)
        {
            PathFrame f = _gizmo.Frame;
            float radius = _gizmo.Scale * 1.42f;
            Vector3 perp = Vector3.Cross(viewDir, tangent).normalized;

            float half = -1f;
            hitDistanceFromArc = -1f;

            for (float d = 0f; d <= 1f; d += step)
            {
                var ray = new Ray(point + perp * d - viewDir * 20f, viewDir);
                if (!_gizmo.TryPick(ray, out GizmoHandleId id, out Vector3 hit)) break;
                if (id != GizmoHandleId.AzimuthRing) break;

                half = d;

                Vector3 rel = hit - f.Origin;
                float inPlane = Vector3.ProjectOnPlane(rel, f.Normal).magnitude - radius;
                float outPlane = Vector3.Dot(rel, f.Normal);
                hitDistanceFromArc = Mathf.Sqrt(inPlane * inPlane + outPlane * outPlane);
            }
            return half;
        }

        [Test]
        public void 掴み幅は視線角度によらず一定()
        {
            PathFrame f = _gizmo.Frame;
            Vector3 L = f.CrossFeed, N = f.Normal;

            float radius = _gizmo.Scale * 1.42f;
            float tube = _gizmo.PixelToWorld(_gizmo.HitPixelWidth) * 0.5f;
            float step = tube / 40f;

            // 半径方向が視線と揃う最悪の点。半径方向 L の側を狙う。
            var shape = GizmoHandleShape.Arc(f.Origin, L, f.Feed, radius, 0f, 360f);
            Vector3 point = shape.PointAt(0f);
            Vector3 tangent = shape.TangentAt(0f);

            foreach (float elevDeg in new[] { 90f, 45f, 20f, 5f, 1f })
            {
                float e = elevDeg * Mathf.Deg2Rad;
                Vector3 viewDir = -(L * Mathf.Cos(e) + N * Mathf.Sin(e)).normalized;

                float half = MeasureHalfWidth(point, tangent, viewDir, step, out float hitDist);

                // 平面内で半径方向のずれを見る方式は、ここが仰角と共に 0 へ潰れていた
                Assert.Greater(half, tube * 0.85f, $"仰角 {elevDeg} 度で掴み幅が保たれること");

                // 当たった点は必ずチューブの表面 = 円弧から tube の距離にある。
                // 掴み幅の上限は縛らない。視線が寝ると円弧が湾曲して逃げるので、
                // 直交方向へ tube 以上ずらしてもチューブを掠り続ける (緩い方向のずれ)。
                Assert.LessOrEqual(hitDist, tube * 1.05f,
                                   $"仰角 {elevDeg} 度で当たり点が円弧から tube 以内にあること");
            }
        }

        [Test]
        public void 非表示のハンドルは掴めない()
        {
            PathFrame f = _gizmo.Frame;
            float radius = _gizmo.Scale * 1.42f;
            Vector3 point = f.Origin + f.CrossFeed * radius;
            var ray = new Ray(point + f.Normal * 20f, -f.Normal);

            Assert.IsTrue(_gizmo.TryPick(ray, out GizmoHandleId id));
            Assert.AreEqual(GizmoHandleId.AzimuthRing, id);

            _gizmo.showAzimuthRing = false;
            _gizmo.SyncColliders();

            Assert.IsFalse(_gizmo.TryPick(ray, out _));
        }

        [Test]
        public void 掴んだ点はリング上に乗っている()
        {
            PathFrame f = _gizmo.Frame;
            float radius = _gizmo.Scale * 1.42f;
            Vector3 point = f.Origin + f.CrossFeed * radius;
            var ray = new Ray(point + f.Normal * 20f, -f.Normal);

            Assert.IsTrue(_gizmo.TryPick(ray, out _, out Vector3 hit));

            float tube = _gizmo.PixelToWorld(_gizmo.HitPixelWidth) * 0.5f;
            float r = Vector3.ProjectOnPlane(hit - f.Origin, f.Normal).magnitude;
            Assert.AreEqual(radius, r, tube * 1.2f, "リングの近傍で当たること");
        }

        [Test]
        public void コライダーは無視レイヤーに置かれる()
        {
            // アプリ側のシーンクエリを汚さないための約束
            Transform root = _go.transform.Find("ToolPostureHandleColliders");
            Assert.IsNotNull(root, "コライダーの根が作られること");
            Assert.AreEqual(2, root.gameObject.layer);

            foreach (Transform child in root)
                Assert.AreEqual(2, child.gameObject.layer, child.name);
        }
    }

    /// <summary>
    /// 傾きが 0 になっても旋回角が失われないこと。
    /// 姿勢の保持が球面表現 (theta, phi, spin) 一本なので、theta は姿勢の中にあり、
    /// 別途の保持値やそれを同期する不変条件は存在しない。
    /// </summary>
    public class AzimuthPreservationTests
    {
        private const float Tol = 1e-2f;

        private GameObject _go;
        private ToolPostureGizmo _gizmo;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestGizmo");
            _gizmo = _go.AddComponent<ToolPostureGizmo>();
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

            // フレームの法線そのもの = 完全な垂直
            _gizmo.SetToolAxisWorld(_gizmo.Frame.Normal);

            Assert.AreEqual(90f, _gizmo.ElevationDeg, Tol);
            Assert.AreEqual(77f, _gizmo.AzimuthDeg, Tol);
        }

        [Test]
        public void フレームは代入したものがそのまま使われる()
        {
            // ギズモは経路を知らない。与えられたフレームを使うだけ。
            Assert.IsTrue(PathFrame.TryCreate(new Vector3(3f, 1f, -2f), Vector3.right, Vector3.forward,
                                              CrossFeedSide.RightOfTravel, out PathFrame f));
            _gizmo.Frame = f;

            Assert.AreEqual(0f, Vector3.Distance(f.Origin, _gizmo.Frame.Origin), 1e-5f);
            Assert.AreEqual(0f, Vector3.Distance(f.Normal, _gizmo.Frame.Normal), 1e-5f);
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
