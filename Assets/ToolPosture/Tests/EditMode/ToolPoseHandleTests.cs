using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToolRuntimeGizmos.Core;
using ToolRuntimeGizmos.Gizmo;
using ToolRuntimeGizmos.Tool;

namespace ToolRuntimeGizmos.Tests
{
    /// <summary>
    /// 利用側が握る口。ハンドルの詳細を知らずに姿勢を読み書きできること。
    /// </summary>
    public class ToolPoseHandleTests
    {
        private const float Tol = 1e-4f;

        private GameObject _go;
        private GameObject _anchorGo;
        private ToolPoseHandle _handle;
        private ToolPositionGizmo _position;
        private ToolPostureGizmo _posture;

        private readonly List<ToolPoseEvent> _changed = new List<ToolPoseEvent>();
        private readonly List<ToolPoseEvent> _began = new List<ToolPoseEvent>();
        private readonly List<ToolPoseEvent> _ended = new List<ToolPoseEvent>();

        [SetUp]
        public void SetUp()
        {
            _anchorGo = new GameObject("TestAnchor");

            _go = new GameObject("TestHandle");
            _position = _go.AddComponent<ToolPositionGizmo>();
            _posture = _go.AddComponent<ToolPostureGizmo>();
            var follower = _go.AddComponent<ToolPostureFollower>();

            // 位置ハンドルが動かす先を姿勢ハンドルの原点にする、という実運用の配線
            _position.target = _anchorGo.transform;
            _posture.originSource = _anchorGo.transform;

            follower.gizmo = _posture;
            follower.target = _anchorGo.transform;
            follower.shaftAxis = Vector3.up;
            follower.referenceAxis = Vector3.forward;

            _handle = _go.AddComponent<ToolPoseHandle>();
            Wire("positionGizmo", _position);
            Wire("postureGizmo", _posture);
            Wire("follower", follower);

            // ToolPoseHandle は [ExecuteAlways] ではないので、エディットモードでは
            // OnEnable が呼ばれず購読が張られない。実行時に走るのと同じ初期化を明示的に行う。
            Invoke("Subscribe", true);
            Invoke("Apply");

            _changed.Clear();
            _began.Clear();
            _ended.Clear();
            _handle.PoseChanged += _changed.Add;
            _handle.DragBegan += _began.Add;
            _handle.DragEnded += _ended.Add;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_anchorGo);
        }

        private void Wire(string field, Object value)
            => typeof(ToolPoseHandle)
               .GetField(field, System.Reflection.BindingFlags.NonPublic
                              | System.Reflection.BindingFlags.Instance)
               .SetValue(_handle, value);

        private void Invoke(string method, params object[] args)
            => typeof(ToolPoseHandle)
               .GetMethod(method, System.Reflection.BindingFlags.NonPublic
                                | System.Reflection.BindingFlags.Instance)
               .Invoke(_handle, args);

        private static WorkFrame MakeFrame(Vector3 origin, Vector3 travel)
        {
            Assert.IsTrue(WorkFrame.TryCreate(origin, travel, Vector3.up,
                                              CrossFeedSide.RightOfTravel, out WorkFrame f));
            return f;
        }

        // ------------------------------------------------------------------

        [Test]
        public void 位置はフレームの原点そのもの()
        {
            _handle.SetPose(new ToolPose(MakeFrame(new Vector3(1f, 2f, 3f), Vector3.right),
                                         ToolPostureAngles.FromSpherical(10f, 60f, 5f)));

            Assert.AreEqual(0f, Vector3.Distance(new Vector3(1f, 2f, 3f), _handle.Pose.Position), Tol);
            Assert.AreEqual(0f, Vector3.Distance(_handle.Pose.Frame.Origin, _handle.Pose.Position), Tol);
        }

        [Test]
        public void 書き戻しはイベントを発火しない()
        {
            // まず購読が生きていることを確かめる。でないと 0 件の確認が意味を持たない
            _posture.SetSpherical(11f, 55f);
            Assert.AreEqual(1, _changed.Count);
            _changed.Clear();

            _handle.SetPose(new ToolPose(MakeFrame(Vector3.one, Vector3.right),
                                         ToolPostureAngles.FromSpherical(20f, 50f, 8f)));

            // ここで発火すると、IK の結果を戻すたびに次の IK を呼ぶループになる
            Assert.AreEqual(0, _changed.Count);
        }

        [Test]
        public void 姿勢を直接動かせば発火する()
        {
            _posture.SetSpherical(30f, 40f);

            Assert.AreEqual(1, _changed.Count);
            Assert.AreEqual(ToolHandleKind.Posture, _changed[0].Kind);
        }

        [Test]
        public void 書き戻した位置は原点の追従に消されない()
        {
            _handle.SetPose(new ToolPose(MakeFrame(new Vector3(0.5f, 0f, 0f), Vector3.right),
                                         ToolPostureAngles.FromSpherical(0f, 60f, 0f)));

            // originSource は毎フレーム原点を上書きするので、実体側も動いていないと戻される
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(0.5f, 0f, 0f), _anchorGo.transform.position), Tol);
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(0.5f, 0f, 0f), _handle.Pose.Position), Tol);
        }

        [Test]
        public void フレームを変えると姿勢はフレーム相対に保たれる()
        {
            var angles = ToolPostureAngles.FromSpherical(0f, 60f, 0f);
            _handle.SetPose(new ToolPose(MakeFrame(Vector3.zero, Vector3.right), angles));
            Vector3 before = _posture.ToolAxisWorld;

            // 進行方向が 90 度変われば、工具軸も一緒に回る (溶接角が継目基準ということ)
            _handle.SetPose(new ToolPose(MakeFrame(Vector3.zero, Vector3.forward), angles));

            Assert.AreEqual(_handle.Pose.Angles.elevationDeg, angles.elevationDeg, Tol);
            Assert.Greater(Vector3.Angle(before, _posture.ToolAxisWorld), 1f);
        }

        [Test]
        public void 垂直姿勢でも世界回転の往復で旋回角が残る()
        {
            _posture.Angles = ToolPostureAngles.FromSpherical(37f, 90f, 15f);   // 傾斜 0
            _changed.Clear();

            _handle.SetWorldRotation(_handle.WorldRotation);

            Assert.AreEqual(37f, _handle.Pose.Angles.azimuthDeg, 1e-2f);
            Assert.AreEqual(15f, _handle.Pose.Angles.spinAngleDeg, 1e-2f);
            Assert.AreEqual(0, _changed.Count);
        }

        [Test]
        public void 掴んで離すと開始と終了が一度ずつ届く()
        {
            _handle.Mode = ToolPoseHandle.HandleMode.Position;
            _position.SyncColliders();

            _position.BeginDrag(GizmoHandleId.TranslateX,
                                new Ray(_position.Position + Vector3.up, Vector3.down));
            _position.EndDrag();

            Assert.AreEqual(1, _began.Count);
            Assert.AreEqual(1, _ended.Count);
            Assert.IsFalse(_ended[0].Cancelled);
            Assert.AreEqual(ToolHandleKind.Position, _ended[0].Kind);
        }

        [Test]
        public void 終了イベントの中で書き戻せる()
        {
            _handle.Mode = ToolPoseHandle.HandleMode.Position;
            _position.SyncColliders();

            var snapped = new Vector3(0.25f, 0f, 0f);
            _handle.DragEnded += e => _handle.SetPose(e.Pose.WithPosition(snapped));

            _position.BeginDrag(GizmoHandleId.TranslateX,
                                new Ray(_position.Position + Vector3.up, Vector3.down));
            _position.EndDrag();

            // 解除してから発火しているので、ドラッグ中扱いで弾かれていない
            Assert.AreEqual(0f, Vector3.Distance(snapped, _handle.Pose.Position), Tol);
        }

        [Test]
        public void 当たり判定は距離を返す()
        {
            _handle.Mode = ToolPoseHandle.HandleMode.Position;
            _position.SyncColliders();

            var origin = _position.Position + Vector3.up * 3f;
            Assert.IsTrue(_handle.Raycast(new Ray(origin, Vector3.down), out float distance));

            // アプリが自前の raycast と優先順位を比べられるよう、意味の揃った距離であること
            Assert.Greater(distance, 0f);
            Assert.Less(distance, 3f);
        }

        [Test]
        public void 出していないときは当たり判定を返さない()
        {
            _handle.Visible = false;

            Assert.IsFalse(_handle.Raycast(new Ray(_position.Position + Vector3.up * 3f, Vector3.down),
                                           out _));
        }

        [Test]
        public void ハンドルのコライダーは自分のものと分かる()
        {
            _handle.Mode = ToolPoseHandle.HandleMode.Position;
            _position.SyncColliders();

            var root = _position.transform.Find("ToolPostureHandleColliders");
            Assert.IsNotNull(root, "コライダー置き場が見つからない");
            Assert.Greater(root.childCount, 0);

            // PhysicsRaycaster 越しに自分のコライダーを他人と数えると、自分で自分を塞ぐ
            Assert.IsTrue(GizmoHandleColliders.IsHandleCollider(root.GetChild(0).gameObject));
            Assert.IsFalse(GizmoHandleColliders.IsHandleCollider(_anchorGo));
        }

        [Test]
        public void 取り消しは開始前の値へ戻り取り消しとして届く()
        {
            _handle.Mode = ToolPoseHandle.HandleMode.Position;
            _position.SyncColliders();
            Vector3 before = _handle.Pose.Position;

            _position.BeginDrag(GizmoHandleId.TranslateX,
                                new Ray(_position.Position + Vector3.up, Vector3.down));
            _position.Position = before + new Vector3(0.4f, 0f, 0f);
            _position.CancelDrag();

            Assert.AreEqual(1, _ended.Count);
            Assert.IsTrue(_ended[0].Cancelled);
            Assert.AreEqual(0f, Vector3.Distance(before, _handle.Pose.Position), Tol);
        }
    }
}
