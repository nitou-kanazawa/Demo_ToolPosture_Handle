using UnityEngine;
using ToolRuntimeGizmos.Core;
using ToolRuntimeGizmos.Tool;

namespace ToolRuntimeGizmos.Demo
{
    /// <summary>
    /// ハンドルの動きを毎フレーム IK に流し、成功したときだけロボットを更新する。
    /// ドラッグが終わったら、最後に成功した姿勢へハンドルを戻す。
    ///
    /// アプリ側に書くことになるコードの雛形。IK の中身だけ差し替えれば使える。
    /// </summary>
    /// <remarks>
    /// ドラッグ中は書き戻さない。ドラッグは掴んだ瞬間の基準から毎回引き直すので、
    /// 途中で値を戻すと次のフレームの再計算と殴り合う。SetPose はドラッグ外専用で、
    /// 戻した値がそのまま次の IK を呼ぶループを避けるためイベントを発火しない。
    ///
    /// 併進と回転で戻し方が違う。併進では位置とフレームを戻す。このとき工具の世界姿勢は
    /// フレームと一緒に回るが、溶接角は継目基準であるべきなのでそれが正しい。
    /// 回転では姿勢だけを戻す。
    /// </remarks>
    [AddComponentMenu("Tool Posture/IK Snap Back Demo")]
    public class IkSnapBackDemo : MonoBehaviour
    {
        // 参照
        [SerializeField] private ToolPoseHandle handle;
        [Tooltip("ロボット座標への変換。未設定ならシーンから探す")]
        [SerializeField] private RobotPoseInterop interop;

        // 擬似 IK の設定 (実アプリでは本物の IK に差し替える)
        [Tooltip("この点から届く範囲を可動域とみなす")]
        public Transform reachCenter;
        [Tooltip("可動域の半径 [m]。併進を制限する")]
        public float reachRadius = 0.35f;

        [Tooltip("工具軸がこの向きから離れられる角度 [deg]。手首の可動限界を模し、回転を制限する。" +
                 "0 以下で制限なし")]
        public float maxAxisTiltDeg = 30f;
        [Tooltip("工具軸の基準向き。未設定なら開始時の工具軸を使う")]
        public Transform axisReference;

        private Vector3 _axisAtStart;

        /// <summary>Unity とロボットの座標系の対応。</summary>
        public HandednessConversion Conversion = HandednessConversion.SwapYZ;

        /// <summary>直近のドラッグで IK が成功した回数と失敗した回数。確認用。</summary>
        public int SolvedCount { get; private set; }
        public int FailedCount { get; private set; }

        private bool _hasGood;
        private Vector3 _goodPosition;
        private Quaternion _goodRotation;

        #region Lifecycle

        private void Awake()
        {
            if (handle == null) handle = FindAnyObjectByType<ToolPoseHandle>();
            if (interop == null) interop = FindAnyObjectByType<RobotPoseInterop>();
        }

        /// <summary>
        /// 工具軸の基準向き。Awake ではまだフレームが供給されておらずフォールバックの軸しか
        /// 取れないので、最初に必要になった時点で覚える。
        /// </summary>
        private Vector3 AxisReference
        {
            get
            {
                if (axisReference != null) return axisReference.up;
                if (_axisAtStart == Vector3.zero) _axisAtStart = handle.ToolAxisWorld;
                return _axisAtStart;
            }
        }

        private void OnEnable()
        {
            if (handle == null) return;
            handle.DragBegan += OnDragBegan;
            handle.PoseChanged += OnPoseChanged;
            handle.DragEnded += OnDragEnded;
        }

        private void OnDisable()
        {
            if (handle == null) return;
            handle.DragBegan -= OnDragBegan;
            handle.PoseChanged -= OnPoseChanged;
            handle.DragEnded -= OnDragEnded;
        }

        #endregion

        #region ハンドルからの受け取り

        private void OnDragBegan(ToolPoseEvent e)
        {
            // 掴んだ時点を「最後に成功した姿勢」の初期値にしておく。
            // 一度も成功しないまま離された場合はここへ戻る。
            _goodPosition = e.Pose.Position;
            _goodRotation = handle.WorldRotation;
            _hasGood = true;

            SolvedCount = 0;
            FailedCount = 0;
        }

        private void OnPoseChanged(ToolPoseEvent e)
        {
            // ハンドルの値が変わっただけで、ロボットはまだ動いていない。
            // ロボット座標への変換は Interop に任せる
            interop.GetRobotPose(out Vector3 tcp, out ZyxEulerAngles rpy);

            if (!TrySolve(tcp, rpy))
            {
                FailedCount++;
                return;                     // 失敗したらロボットを動かさない
            }

            SolvedCount++;
            ApplyToRobot(tcp, rpy);

            _goodPosition = e.Pose.Position;
            _goodRotation = handle.WorldRotation;
            _hasGood = true;
        }

        private void OnDragEnded(ToolPoseEvent e)
        {
            if (e.Cancelled || !_hasGood) return;

            if (e.Kind == ToolHandleKind.Position)
            {
                // 位置を戻し、その位置でのフレームに更新する。
                // フレームは位置の関数なので、先に位置を確定させてから求めること。
                ToolPose snapped = e.Pose.WithPosition(_goodPosition);
                if (TryGetFrameAt(_goodPosition, out PathFrame frame))
                    snapped = snapped.WithFrame(frame);

                // 位置と向きを 1 回で渡す。中間状態を作らない
                handle.SetPose(snapped);
            }
            else
            {
                // 姿勢だけ戻す。垂直姿勢でも旋回角が失われない経路を通る
                handle.SetWorldRotation(_goodRotation);
            }
        }

        #endregion

        #region 差し替える部分 (擬似 IK)

        /// <summary>
        /// 実アプリではここを本物の IK にする。届かない姿勢では false を返す。
        ///
        /// ここでは 2 つの制限を模している。位置は可動域の球、姿勢は工具軸の円錐。
        /// 位置だけを見ていると、回転ドラッグでは判定が動かず必ず成功してしまい、
        /// 姿勢側のスナップバックを確かめられない。
        /// </summary>
        private bool TrySolve(Vector3 tcpExternal, ZyxEulerAngles rpy)
        {
            if (reachCenter != null)
            {
                Vector3 center = Conversion.ToExternal(reachCenter.position);
                if (Vector3.Distance(tcpExternal, center) > reachRadius) return false;
            }

            if (maxAxisTiltDeg > 0f
                && Vector3.Angle(handle.ToolAxisWorld, AxisReference) > maxAxisTiltDeg)
                return false;

            return true;
        }

        /// <summary>実アプリでは解いた関節角をロボットへ送る。</summary>
        private void ApplyToRobot(Vector3 tcpExternal, ZyxEulerAngles rpy) { }

        /// <summary>
        /// 実アプリでは母材の形状からその位置のフレームを求める。
        /// ここでは経路が持っているものをそのまま使う。
        /// </summary>
        private bool TryGetFrameAt(Vector3 position, out PathFrame frame)
        {
            frame = default;
            return false;
        }

        #endregion
    }
}
