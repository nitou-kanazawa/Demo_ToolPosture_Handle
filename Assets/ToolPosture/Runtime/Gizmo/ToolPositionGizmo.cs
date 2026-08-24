using System;
using UnityEngine;

namespace ToolRuntimeGizmos.Gizmo
{
    /// <summary>
    /// 軸の向きをどこから取るか。
    /// </summary>
    public enum GizmoAxisSpace
    {
        /// <summary>
        /// ワールド軸 (X / Y / Z)。
        /// </summary>
        World = 0,

        /// <summary>
        /// この GameObject のローカル軸。
        /// </summary>
        Local = 1,

        /// <summary>
        /// コードから <see cref="ToolPositionGizmo.AxisRotation"/> に与えた向き。
        /// 経路のフレーム (L, M, N) に沿って動かしたい場合などに使う。
        /// </summary>
        Explicit = 2,
    }

    /// <summary>
    /// 座標軸に沿った平行移動だけを行うギズモ。
    ///
    /// 描画・当たり判定・入力は <see cref="RuntimeGizmo"/> と共通で、
    /// ハンドルは 3 本の軸だけ。回転もスケールも扱わない。
    /// 当たり判定は軸に沿ったカプセルなので、視線が軸方向に寝ても掴み幅が変わらない。
    /// </summary>
    [AddComponentMenu("Tool Posture/Tool Position Gizmo")]
    public class ToolPositionGizmo : RuntimeGizmo
    {
        #region 位置

        [Tooltip("このギズモが指す位置")]
        [SerializeField] private Vector3 position;

        [Tooltip("設定すると、この Transform の位置を読み書きする。未設定なら値だけを持つ")]
        public Transform target;

        #endregion

        #region 軸

        [Tooltip("軸の向きをどこから取るか")]
        [SerializeField] private GizmoAxisSpace axisSpace = GizmoAxisSpace.World;

        [Tooltip("X 軸のハンドルを出す")]
        public bool showAxisX = true;

        [Tooltip("Y 軸のハンドルを出す")]
        public bool showAxisY = true;

        [Tooltip("Z 軸のハンドルを出す")]
        public bool showAxisZ = true;

        [Tooltip("Ctrl を押しながらのドラッグで丸める幅 [world]。0 以下でスナップ無効")]
        public float snapStep = 0.01f;

        private Quaternion _explicitRotation = Quaternion.identity;
        private Vector3 _positionAtDragStart;

        #endregion

        #region 公開プロパティ

        /// <summary>
        /// このギズモが指す位置。target を設定している場合はそちらと同期する。
        /// </summary>
        public Vector3 Position
        {
            get => position;
            set
            {
                position = value;
                if (target != null) target.position = value;
                PositionChanged?.Invoke(this);
            }
        }

        /// <summary>
        /// 位置が変わったときに呼ばれる。
        /// </summary>
        public event Action<ToolPositionGizmo> PositionChanged;

        /// <summary>
        /// 軸の向き。代入すると <see cref="GizmoAxisSpace.Explicit"/> に切り替わる。
        /// </summary>
        public Quaternion AxisRotation
        {
            get
            {
                if (axisSpace == GizmoAxisSpace.Local) return transform.rotation;
                if (axisSpace == GizmoAxisSpace.Explicit) return _explicitRotation;
                return Quaternion.identity;
            }
            set
            {
                _explicitRotation = value;
                axisSpace = GizmoAxisSpace.Explicit;
            }
        }

        public override Vector3 Origin => position;

        /// <summary>
        /// 軸方向 (0 = X, 1 = Y, 2 = Z)。
        /// </summary>
        public Vector3 AxisDirection(int index)
        {
            Quaternion r = AxisRotation;
            if (index == 0) return r * Vector3.right;
            if (index == 1) return r * Vector3.up;
            return r * Vector3.forward;
        }

        /// <summary>
        /// 軸の色。フレーム軸の色を X / Y / Z に流用する。
        /// </summary>
        public Color AxisColor(int index)
        {
            if (index == 0) return Theme.frameColorL;
            if (index == 1) return Theme.frameColorN;
            return Theme.frameColorM;
        }

        public bool IsAxisVisible(int index)
        {
            if (index == 0) return showAxisX;
            if (index == 1) return showAxisY;
            return showAxisZ;
        }

        /// <summary>
        /// 軸ハンドルの長さ。
        /// </summary>
        public float AxisLength => Scale * Theme.toolAxisLengthRatio;

        #endregion

        #region ライフサイクル

        protected override void BuildHandles()
        {
            Handles.Clear();
            Handles.Add(new AxisTranslateHandle(this, 0, GizmoHandleId.TranslateX));
            Handles.Add(new AxisTranslateHandle(this, 1, GizmoHandleId.TranslateY));
            Handles.Add(new AxisTranslateHandle(this, 2, GizmoHandleId.TranslateZ));
        }

        /// <summary>
        /// target を付けている場合は、外から動かされた分をここで取り込む。
        /// </summary>
        protected override void EnsureState()
        {
            if (target != null && !IsDragging) position = target.position;
        }

        protected override void HandleKeyboard()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;

            if (kb.digit1Key.wasPressedThisFrame) showAxisX = !showAxisX;
            if (kb.digit2Key.wasPressedThisFrame) showAxisY = !showAxisY;
            if (kb.digit3Key.wasPressedThisFrame) showAxisZ = !showAxisZ;
        }

        protected override void OnDragBegan(GizmoHandleBase handle) => _positionAtDragStart = position;

        protected override void OnDragCancelled(GizmoHandleBase handle) => Position = _positionAtDragStart;

        #endregion

        #region 描画

        protected override void BuildBaseGeometry(GizmoMeshBuilder b)
        {
            Camera cam = Cam;
            if (cam == null) return;

            GizmoTheme th = Theme;
            b.AddBillboardDisc(position, cam, PixelToWorld(th.originDotPixelRadius), th.zeroTickColor);
        }

        #endregion
    }

    /// <summary>
    /// 1 本の軸に沿った平行移動ハンドル。
    ///
    /// 当たり判定は軸に沿ったカプセルなので、視線が軸に寝ても掴み幅が潰れない。
    /// ドラッグはレイと軸直線の最近接点で決めるので、カメラに依存しない。
    /// </summary>
    public class AxisTranslateHandle : GizmoHandleBase
    {
        private readonly ToolPositionGizmo _g;
        private readonly int _axis;
        private RayAxisDrag _drag;

        public AxisTranslateHandle(ToolPositionGizmo owner, int axisIndex, GizmoHandleId id)
            : base(owner, id)
        {
            _g = owner;
            _axis = axisIndex;
        }

        public override bool Visible => _g.IsAxisVisible(_axis);

        private Vector3 Direction => _g.AxisDirection(_axis);
        private Vector3 Tip => _g.Position + Direction * _g.AxisLength;

        public override GizmoHandleShape GetShape()
            => GizmoHandleShape.Line(_g.Position, Tip, _g.PixelToWorld(_g.HitPixelWidth) * 0.5f);

        public override void BeginDrag(Ray ray, Vector3 grabPoint)
            => _drag.Begin(grabPoint, Direction, _g.Position, ray);

        public override void Drag(Ray ray, bool snap)
        {
            if (!_drag.TryGetPosition(ray, out Vector3 p)) return;
            _g.Position = snap ? _drag.Snap(_g.snapStep) : p;
        }

        public override void Draw(GizmoMeshBuilder b, bool hover, bool active)
        {
            Camera cam = _g.Cam;
            GizmoTheme th = _g.Theme;
            if (cam == null) return;

            Color col = (hover || active) ? th.highlightColor : _g.AxisColor(_axis);

            b.AddArrow(_g.Position, Direction, _g.AxisLength, _g.EyePosition,
                       _g.PixelToWorld(th.toolAxisPixelWidth) * 0.5f,
                       _g.PixelToWorld(th.toolArrowHeadPixelRadius),
                       _g.PixelToWorld(th.arrowHeadPixelLength),
                       col);

            // 掴んでいる間は移動量を目盛りとして残す
            if (active && Mathf.Abs(_drag.Offset) > 1e-5f)
                b.AddScreenDashedLine(_g.Position - Direction * _drag.Offset, _g.Position,
                                      _g.EyePosition,
                                      _g.PixelToWorld(th.thinPixelWidth),
                                      _g.PixelToWorld(th.dashPixelLength),
                                      GizmoMeshBuilder.Fade(col, 0.6f));
        }
    }
}
