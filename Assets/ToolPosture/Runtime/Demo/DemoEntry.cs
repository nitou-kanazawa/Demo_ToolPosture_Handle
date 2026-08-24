using System;
using UnityEngine;
using UnityEngine.InputSystem;
using ToolRuntimeGizmos.Gizmo;

namespace ToolRuntimeGizmos.Demo
{
    /// <summary>
    /// アプリへ組み込むときの入口。位置ギズモと姿勢ギズモを 1 つずつ持ち、
    /// 外からは 3 つだけ切り替える。
    ///
    ///   Mode   … 位置 / 姿勢 のどちらのハンドルを出すか
    ///   Active … 出すか出さないか
    ///   View   … 3D ビューから触るか、2D 重畳ビューから触るか
    ///
    /// 3D のときはギズモが自分でポインタを読む。2D のときは読まず、
    /// <see cref="ScreenToRay"/> に差した「画面座標 → ワールドのレイ」で動かす。
    /// 当たり判定はワールド上のコライダーなので、投影が歪んでいても掴める
    /// (ギズモ側に投影を教える必要は無い)。
    ///
    /// 操作結果は各ギズモの PositionChanged / PostureChanged を直接購読すること。
    /// ここでは中継しない。
    ///
    /// 2 つのギズモを同じ場所に出すには、共通の Transform を 1 つ用意して
    /// ToolPositionGizmo.target と ToolPostureGizmo.originSource の両方へ差す。
    /// 位置ギズモがそれを動かし、姿勢ギズモがそこを原点にするので、
    /// 切り替えても場所がずれない。経路など別の供給元は向き (LMN) だけを与える形になる。
    ///
    /// <code>
    /// entry.ScreenToRay = pos => camera.TryScreenToRay(pos, out Ray r) ? r : (Ray?)null;
    /// entry.Mode = DemoEntry.HandleMode.Posture;
    /// entry.View = DemoEntry.ViewMode.View2D;
    /// entry.Active = true;
    /// </code>
    /// </summary>
    [AddComponentMenu("Tool Posture/Demo Entry")]
    public class DemoEntry : MonoBehaviour
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

        #region 設定

        [SerializeField] private ToolPositionGizmo positionGizmo;
        [SerializeField] private ToolPostureGizmo postureGizmo;

        [SerializeField] private HandleMode mode = HandleMode.Posture;
        [SerializeField] private bool active = true;
        [SerializeField] private ViewMode view = ViewMode.View3D;

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
        private bool _warnedNoRay;

        #endregion

        #region 3 つのつまみ

        /// <summary>どちらのハンドルを出すか。</summary>
        public HandleMode Mode
        {
            get => mode;
            set { mode = value; Apply(); }
        }

        /// <summary>ギズモを出すかどうか。false でどちらも消える。</summary>
        public bool Active
        {
            get => active;
            set { active = value; Apply(); }
        }

        /// <summary>3D ビューから触るか、2D 重畳ビューから触るか。</summary>
        public ViewMode View
        {
            get => view;
            set { view = value; Apply(); }
        }

        #endregion

        #region 状態

        /// <summary>今出ているギズモ。Active が false でも「どちらか」は返す。</summary>
        public RuntimeGizmo Current
            => mode == HandleMode.Position ? (RuntimeGizmo)positionGizmo : postureGizmo;

        /// <summary>何かを掴んで動かしている間 true。カメラ操作を止めるのに使う。</summary>
        public bool IsDragging => Active && Current != null && Current.IsDragging;

        #endregion

        #region ライフサイクル

        private void OnEnable()
        {
            _provider = rayProvider as IGizmoRayProvider;
            if (rayProvider != null && _provider == null)
                Debug.LogWarning("DemoEntry: rayProvider が IGizmoRayProvider を実装していない", this);

            Apply();
        }

        private void Update()
        {
            // インスペクタでの直接編集に追従する。プロパティ経由なら空振りする。
            if (mode != _appliedMode || active != _appliedActive || view != _appliedView) Apply();

            if (active && view == ViewMode.View2D) DriveFrom2D();
        }

        #endregion

        #region 切り替え

        private void Apply()
        {
            _appliedMode = mode;
            _appliedActive = active;
            _appliedView = view;

            Setup(positionGizmo, active && mode == HandleMode.Position);
            Setup(postureGizmo, active && mode == HandleMode.Posture);
        }

        private void Setup(RuntimeGizmo gizmo, bool on)
        {
            if (gizmo == null) return;

            // 消す前に掴んだままのものを戻す。放置すると次に出したとき掴んだ状態から始まる。
            if (!on && gizmo.IsDragging) gizmo.CancelDrag();

            gizmo.inputMode = view == ViewMode.View2D ? GizmoInputMode.External : GizmoInputMode.BuiltIn;
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
                Debug.LogWarning("DemoEntry: View2D だが 2D → レイ の変換が未設定のため操作できない", this);
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
