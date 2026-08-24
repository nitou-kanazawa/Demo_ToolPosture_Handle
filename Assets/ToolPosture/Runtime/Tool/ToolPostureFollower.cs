using UnityEngine;
using ToolPosture.Core;
using ToolPosture.Gizmo;

namespace ToolPosture.Tool
{
    /// <summary>
    /// ギズモが定めた姿勢に工具モデルを追従させる。
    ///
    /// ギズモの出力は「工具軸ベクトル X と軸まわりの回転」までで、そこから
    /// Quaternion を作るには「モデルのどのローカル軸を工具軸に向けるか」という
    /// 対象側の都合が要る。その都合はモデルを持っている側 = ここが持つ。
    ///
    /// ロボットのフランジに向ける場合も同じ形で、軸の値だけが変わる
    /// (デモの RobotPostureBridge はロボット側の軸で同じ計算をしている)。
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Tool Posture/Tool Posture Follower")]
    public class ToolPostureFollower : MonoBehaviour
    {
        #region 設定

        [Tooltip("姿勢の供給元")]
        public ToolPostureGizmo gizmo;

        [Tooltip("追従させる Transform。未設定なら自分自身")]
        public Transform target;

        [Header("モデルのローカル軸")]
        [Tooltip("工具軸 X に向けるローカル軸")]
        public Vector3 shaftAxis = Vector3.up;

        [Tooltip("スピン基準に向けるローカル軸。shaftAxis と直交していること")]
        public Vector3 referenceAxis = Vector3.forward;

        [Tooltip("位置も原点に合わせる。false なら回転だけ追従させる")]
        public bool followPosition = true;

        #endregion

        #region 出力

        private Transform Target => target != null ? target : transform;

        /// <summary>
        /// この軸割当でのワールド回転。
        /// </summary>
        public Quaternion Rotation
            => gizmo != null
                ? gizmo.Angles.GetToolRotation(gizmo.Frame, shaftAxis, referenceAxis,
                                               gizmo.Profile.spinReference)
                : Quaternion.identity;

        /// <summary>
        /// 回転を与えて姿勢を逆算し、ギズモへ書き戻す。
        /// ロボット等から姿勢が降ってくる場合の入口。
        /// </summary>
        public void ApplyRotation(Quaternion worldRotation)
        {
            if (gizmo == null) return;

            var a = gizmo.Angles;
            a.SetToolRotation(gizmo.Frame, worldRotation, shaftAxis, referenceAxis,
                              gizmo.Profile.spinReference);
            gizmo.Angles = a;
        }

        #endregion

        #region ライフサイクル

        private void OnEnable()
        {
            if (gizmo == null) gizmo = FindAnyObjectByType<ToolPostureGizmo>();
            Apply();
        }

        private void LateUpdate() => Apply();

        private void Apply()
        {
            if (gizmo == null) return;

            Transform t = Target;
            if (followPosition) t.SetPositionAndRotation(gizmo.Frame.Origin, Rotation);
            else t.rotation = Rotation;
        }

        #endregion
    }
}
