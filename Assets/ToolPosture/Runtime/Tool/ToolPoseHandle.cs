using System;
using UnityEngine;
using UnityEngine.InputSystem;
using ToolRuntimeGizmos.Gizmo;

namespace ToolRuntimeGizmos.Tool
{
    /// <summary>
    /// アプリへ組み込むときの入口。位置ギズモと姿勢ギズモを 1 つずつ持ち、
    /// 外からは 3 つだけ切り替える。
    ///
    ///   Mode    … 位置 / 姿勢 のどちらのハンドルを出すか
    ///   Visible … 出すか出さないか
    ///   View    … 3D ビューから触るか、2D 重畳ビューから触るか
    ///
    /// 3D のときはギズモが自分でポインタを読む。2D のときは読まず、
    /// <see cref="ScreenToRay"/> に差した「画面座標 → ワールドのレイ」で動かす。
    /// 当たり判定はワールド上のコライダーなので、投影が歪んでいても掴める
    /// (ギズモ側に投影を教える必要は無い)。
    ///
    /// 姿勢の受け渡しは <see cref="IToolPoseHandle"/> として公開する。利用側は
    /// ハンドルの詳細を知らずに、Pose / SetPose と 3 つのイベントだけを見ればよい。
    ///
    /// 2 つのギズモを同じ場所に出すには、共通の Transform を 1 つ用意して
    /// ToolPositionGizmo.target と ToolPostureGizmo.originSource の両方へ差す。
    /// 位置ギズモがそれを動かし、姿勢ギズモがそこを原点にするので、
    /// 切り替えても場所がずれない。経路など別の供給元は向き (LMN) だけを与える形になる。
    ///
    /// <code>
    /// handle.ScreenToRay = pos => camera.TryScreenToRay(pos, out Ray r) ? r : (Ray?)null;
    /// handle.Mode = ToolPoseHandle.HandleMode.Posture;
    /// handle.View = ToolPoseHandle.ViewMode.View2D;
    /// handle.Visible = true;
    /// </code>
    /// </summary>
    [AddComponentMenu("Tool Posture/Tool Pose Handle")]
    public class ToolPoseHandle : MonoBehaviour, IToolPoseHandle
    {
        public enum HandleMode
        {
            /// <summary>座標軸。軸方向の平行移動だけ。</summary>
            Position = 0,

            /// <summary>傾斜角 / 旋回角 / スピン。</summary>
            Posture = 1,
        }

        public enum ViewMode
        {
            /// <summary>通常の 3D ビュー。ギズモが自分でポインタを読む。</summary>
            View3D = 0,

            /// <summary>2D 重畳ビュー。ScreenToRay で作ったレイで動かす。</summary>
            View2D = 1,
        }


        // 設定 (インスペクタ)

        [SerializeField] private ToolPositionGizmo positionGizmo;
        [SerializeField] private ToolPostureGizmo postureGizmo;

        [Tooltip("世界回転の受け渡しに使う軸割当。未設定ならシーンから探す")]
        [SerializeField] private ToolPostureFollower follower;

        [SerializeField] private HandleMode mode = HandleMode.Posture;
        [SerializeField] private bool active = true;
        [SerializeField] private ViewMode view = ViewMode.View3D;

        [Tooltip("ハンドル全体の倍率。2D ビューの拡大率に合わせるなど、見せ方に応じて動かす")]
        [SerializeField] private float sizeScale = 1f;

        [Tooltip("2D のとき使う「画面座標 → ワールドのレイ」。IGizmoRayProvider を実装したコンポーネント。" +
                 "コードから差す場合は ScreenToRay を使う (そちらが優先)")]
        [SerializeField] private MonoBehaviour rayProvider;

        /// <summary>
        /// 2D ビューの画面座標 → ワールドのレイ。触れない位置なら null を返す。
        /// View2D のときだけ使う。レンズ歪みの補正はこの中で済ませておくこと。
        /// 設定するとインスペクタの rayProvider より優先される。
        /// </summary>
        public Func<Vector2, Ray?> ScreenToRay;

        private IGizmoRayProvider _provider;

        // インスペクタから直接いじられた場合も拾うため、当てた値を覚えておく。
        private HandleMode _appliedMode;
        private bool _appliedActive;
        private ViewMode _appliedView;
        private float _appliedSizeScale;
        private bool _warnedNoRay;



        /// <summary>
        /// ギズモを出すかどうか。false でどちらも消える。
        /// </summary>
        public bool Visible
        {
            get => active;
            set { active = value; Apply(); }
        }

        /// <summary>
        /// どちらのハンドルを出すか。
        /// </summary>
        public HandleMode Mode
        {
            get => mode;
            set { mode = value; Apply(); }
        }

        /// <summary>
        /// 3D ビューから触るか、2D 重畳ビューから触るか。
        /// </summary>
        public ViewMode View
        {
            get => view;
            set { view = value; Apply(); }
        }

        /// <summary>
        /// ハンドル全体の倍率。2 つのギズモへまとめて当てる。
        /// </summary>
        public float SizeScale
        {
            get => sizeScale;
            set { sizeScale = value; Apply(); }
        }


        // 状態

        /// <summary>今出ているギズモ。Visible が false でも「どちらか」は返す。</summary>
        public RuntimeGizmo Current
            => mode == HandleMode.Position ? (RuntimeGizmo)positionGizmo : postureGizmo;

        /// <summary>何かを掴んで動かしている間 true。カメラ操作を止めるのに使う。</summary>
        public bool IsDragging => Visible && Current != null && Current.IsDragging;


        #region 姿勢の受け渡し

        public event Action<ToolPoseEvent> DragBegan;
        public event Action<ToolPoseEvent> PoseChanged;
        public event Action<ToolPoseEvent> DragEnded;

        // 書き戻しの最中は PoseChanged を止める。戻した値がそのまま次の計算を呼ぶループを防ぐ。
        private bool _applying;
        private bool _warnedNoFollower;

        private ToolPostureFollower Follower
            => follower != null ? follower : (follower = FindAnyObjectByType<ToolPostureFollower>());

        /// <summary>
        /// 今の姿勢。位置はフレームの原点で、originSource を差してあれば
        /// 位置ハンドルが動かした先に追従している。
        /// </summary>
        public ToolPose Pose
            => postureGizmo != null
                ? new ToolPose(postureGizmo.Frame, postureGizmo.Angles)
                : default;

        public Quaternion WorldRotation
        {
            get
            {
                ToolPostureFollower f = Follower;
                return f != null ? f.Rotation : Quaternion.identity;
            }
        }

        /// <summary>
        /// 姿勢を与える。フレームと角度を同時に差し替えるので中間状態を作らない。
        /// イベントは発火しない。
        /// </summary>
        public void SetPose(ToolPose pose)
        {
            if (postureGizmo == null || !pose.IsValid) return;

            _applying = true;
            try
            {
                // 原点は EnsureState が originSource から毎フレーム上書きするので、
                // フレームだけ差し替えても戻される。実体の方も動かす。
                if (postureGizmo.originSource != null)
                    postureGizmo.originSource.position = pose.Position;
                if (positionGizmo != null) positionGizmo.Position = pose.Position;

                postureGizmo.Frame = pose.Frame;
                postureGizmo.Angles = pose.Angles;
            }
            finally { _applying = false; }
        }

        /// <summary>
        /// 世界回転から姿勢を逆算する。垂直姿勢でも旋回角が失われない経路を通る。
        /// </summary>
        public void SetWorldRotation(Quaternion rotation)
        {
            ToolPostureFollower f = Follower;
            if (f == null)
            {
                if (_warnedNoFollower) return;
                _warnedNoFollower = true;
                Debug.LogWarning("ToolPoseHandle: 軸割当を持つ ToolPostureFollower が無いので世界回転を戻せない", this);
                return;
            }

            _applying = true;
            try { f.ApplyRotation(rotation); }
            finally { _applying = false; }
        }

        /// <summary>
        /// レイが今出ているハンドルに当たるか。アプリ側の選択処理より先に見ること。
        /// </summary>
        public bool Raycast(Ray ray, out float distance)
        {
            distance = 0f;

            RuntimeGizmo gizmo = Current;
            if (!Visible || gizmo == null || !gizmo.enabled) return false;

            return gizmo.TryPick(ray, out GizmoHandleId _, out Vector3 _, out distance);
        }

        private ToolHandleKind KindOf(RuntimeGizmo gizmo)
        {
            if (gizmo == (RuntimeGizmo)positionGizmo) return ToolHandleKind.Position;
            if (gizmo == (RuntimeGizmo)postureGizmo) return ToolHandleKind.Posture;
            return ToolHandleKind.None;
        }

        private void RaiseChanged(ToolHandleKind kind)
        {
            if (_applying) return;
            PoseChanged?.Invoke(new ToolPoseEvent(Pose, kind));
        }

        private void OnPositionChanged(ToolPositionGizmo _) => RaiseChanged(ToolHandleKind.Position);

        private void OnPostureChanged(ToolPostureGizmo _) => RaiseChanged(ToolHandleKind.Posture);

        private void OnDragBegan(RuntimeGizmo gizmo, GizmoHandleId _)
            => DragBegan?.Invoke(new ToolPoseEvent(Pose, KindOf(gizmo)));

        private void OnDragEnded(RuntimeGizmo gizmo, GizmoHandleId _, GizmoDragResult result)
            => DragEnded?.Invoke(new ToolPoseEvent(Pose, KindOf(gizmo),
                                                   result == GizmoDragResult.Cancelled));

        private void Subscribe(bool on)
        {
            // 値の変化イベントはギズモごとに型が違うのでここで個別に、
            // ドラッグの開始と終了は RuntimeGizmo 共通なのでまとめて扱う。
            if (positionGizmo != null)
            {
                if (on) positionGizmo.PositionChanged += OnPositionChanged;
                else positionGizmo.PositionChanged -= OnPositionChanged;
            }

            if (postureGizmo != null)
            {
                if (on) postureGizmo.PostureChanged += OnPostureChanged;
                else postureGizmo.PostureChanged -= OnPostureChanged;
            }

            SubscribeDrag(positionGizmo, on);
            SubscribeDrag(postureGizmo, on);
        }

        private void SubscribeDrag(RuntimeGizmo gizmo, bool on)
        {
            if (gizmo == null) return;

            if (on)
            {
                gizmo.DragBegan += OnDragBegan;
                gizmo.DragEnded += OnDragEnded;
            }
            else
            {
                gizmo.DragBegan -= OnDragBegan;
                gizmo.DragEnded -= OnDragEnded;
            }
        }

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
            _provider = rayProvider as IGizmoRayProvider;
            if (rayProvider != null && _provider == null)
                Debug.LogWarning("ToolPoseHandle: rayProvider が IGizmoRayProvider を実装していない", this);

            Subscribe(true);
            Apply();
        }

        private void OnDisable() => Subscribe(false);

        private void Update()
        {
            // インスペクタでの直接編集に追従する。プロパティ経由なら空振りする。
            if (mode != _appliedMode || active != _appliedActive || view != _appliedView
                || !Mathf.Approximately(sizeScale, _appliedSizeScale)) Apply();

            if (active && view == ViewMode.View2D) DriveFrom2D();
        }

        #endregion

        #region モード切り替え

        private void Apply()
        {
            _appliedMode = mode;
            _appliedActive = active;
            _appliedView = view;
            _appliedSizeScale = sizeScale;

            Setup(positionGizmo, active && mode == HandleMode.Position);
            Setup(postureGizmo, active && mode == HandleMode.Posture);
        }

        private void Setup(RuntimeGizmo gizmo, bool on)
        {
            if (gizmo == null) return;

            // 消す前に掴んだままのものを戻す。放置すると次に出したとき掴んだ状態から始まる。
            if (!on && gizmo.IsDragging) gizmo.CancelDrag();

            gizmo.inputMode = view == ViewMode.View2D ? GizmoInputMode.External : GizmoInputMode.BuiltIn;
            gizmo.sizeScale = sizeScale;
            gizmo.enabled = on;
            if (!on) gizmo.SetHover(null);
        }

        #endregion

        #region 2D からの操作

        private void DriveFrom2D()
        {
            RuntimeGizmo gizmo = Current;
            if (gizmo == null || !gizmo.enabled) return;

            if (ScreenToRay == null && _provider == null)
            {
                if (_warnedNoRay) return;
                _warnedNoRay = true;
                Debug.LogWarning("ToolPoseHandle: View2D だが 2D → レイ の変換が未設定のため操作できない", this);
                return;
            }

            Keyboard kb = Keyboard.current;
            bool snap = kb != null && kb.ctrlKey.isPressed;

            // ポインタの読み取りはアプリ側の入力抽象に置き換えてよい。
            // ギズモに渡すのは「押下状態」と「そこから作ったレイ」の 2 つだけ。
            if (!GizmoPointer.TryRead(out PointerSample pointer))
            {
                gizmo.DrivePointer(default, null, snap);
                return;
            }

            gizmo.DrivePointer(pointer, MakeRay(pointer.position), snap);
        }

        /// <summary>
        /// 画面座標をワールドのレイにする。触れない位置なら null。
        /// </summary>
        private Ray? MakeRay(Vector2 screenPosition)
        {
            if (ScreenToRay != null) return ScreenToRay(screenPosition);

            return _provider.TryScreenToRay(screenPosition, out Ray ray) ? ray : (Ray?)null;
        }

        #endregion
    }
}
