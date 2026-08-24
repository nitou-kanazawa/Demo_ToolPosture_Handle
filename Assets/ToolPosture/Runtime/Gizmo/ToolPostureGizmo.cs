using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using ToolPosture.Core;

namespace ToolPosture.Gizmo
{
    /// <summary>
    /// ポインタ入力を誰が読むか。
    /// </summary>
    public enum GizmoInputMode
    {
        /// <summary>
        /// マウス / タッチ / ペンを自前で読む。
        /// </summary>
        BuiltIn = 0,

        /// <summary>
        /// 自前では読まず、TryPick / BeginDrag / UpdateDrag / EndDrag の
        /// 呼び出しだけを受ける。2D 重畳ビューなど、画面座標の生成が
        /// アプリ側にある場合に使う。
        /// </summary>
        External = 1,
    }

    /// <summary>
    /// ランタイム用の工具姿勢ギズモ。
    ///
    /// 外部から見たこのコンポーネントの役割は「あるフレーム (L, M, N) に対して
    /// 工具軸ベクトル X と、その軸まわりの回転を定める」ことだけ。
    /// 姿勢の保持は球面表現 (theta, phi, spin) 一本。投影角 w / t は導出値。
    ///
    /// UnityEditor.Handles はエディタ専用なので、描画は頂点カラーメッシュの
    /// 手続き生成、当たり判定は GizmoPicker による自前実装で行う。
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Tool Posture/Tool Posture Gizmo")]
    public class ToolPostureGizmo : MonoBehaviour
    {
        // ------------------------------------------------------------------ 経路

        [Header("経路")]
        [Tooltip("フレームの供給元。コードから FrameSource を設定した場合はそちらが優先される")]
        [SerializeField] private Demo.WeldPath path;

        [Tooltip("対象の区間 (0 起点)")]
        public int segmentIndex = 2;

        [Range(0f, 1f)]
        [Tooltip("区間内の位置")]
        public float segmentU = 0.5f;

        // ------------------------------------------------------------------ 姿勢

        [Header("姿勢 (保持は球面表現 theta / phi / spin)")]
        [SerializeField] private ToolPostureAngles angles = ToolPostureAngles.FromProjected(14f, -10f, 25f);

        [Header("角度規約 (0 度位置 / 回転方向 / 可動範囲)")]
        public AngleConvention workConvention = AngleConvention.Ranged(-60f, 60f);
        public AngleConvention travelConvention = AngleConvention.Ranged(-60f, 60f);
        public AngleConvention spinConvention = AngleConvention.Unlimited();

        [Tooltip("LM 平面上の旋回角。0 度は L 軸正方向")]
        public AngleConvention azimuthConvention = AngleConvention.Unlimited();

        [Tooltip("N からの傾き角。既定は表示値を仰角 φ = 90 - α に変換する規約")]
        public AngleConvention tiltConvention = AngleConvention.Elevation();

        [Header("狙い角ハンドルの平面")]
        [Tooltip("FixedCrossFeed = LN 平面に固定 (円弧の角がそのまま狙い角 w)。" +
                 "FollowToolAxis = N と現在の工具軸が張る平面 (円弧の角は N からの傾き α)")]
        public WorkArcPlaneMode workArcPlane = WorkArcPlaneMode.FollowToolAxis;

        // ------------------------------------------------------------------ ハンドル表示

        [Header("ハンドル表示 (実行中は 1 - 5 キーで切替)")]
        public bool showWorkArc = true;
        public bool showTravelArc = true;
        public bool showAxisTip = true;
        public bool showSpinRing = true;

        [Tooltip("LM 平面上に寝かせた旋回リング")]
        public bool showAzimuthRing = true;

        public bool showFrameAxes = true;

        [Tooltip("ハンドルをドラッグしている間、他のハンドルを隠す")]
        public bool hideOthersWhileDragging = true;

        [Tooltip("経路の点列と法線をランタイムでも描画する")]
        public bool showPath = true;

        // ------------------------------------------------------------------ 表示設定

        [Header("表示")]
        public Camera targetCamera;

        [Tooltip("ギズモの画面上の大きさ [px]。カメラ距離によらず一定に保たれる")]
        public float gizmoPixelSize = 130f;

        public float arcPixelWidth = 8f;

        [Tooltip("マウス / ペンでの当たり判定の幅 [px]")]
        public float hitPixelWidth = 11f;

        [Tooltip("タッチでの当たり判定の幅 [px]。指は狙いが粗いのでマウスより広く取る")]
        public float touchHitPixelWidth = 28f;

        public float tipPixelRadius = 8f;

        [Tooltip("可動範囲を使わない角度の円弧の描画半幅 [deg]")]
        public float fallbackArcHalfWidthDeg = 75f;

        [Tooltip("回転ドラッグ感度の上限 [deg/px]。接線が視線と平行に近いときの暴れ止め")]
        public float maxDegreesPerPixel = 2f;

        // ------------------------------------------------------------------ 入力

        [Header("入力")]
        [Tooltip("BuiltIn = マウス / タッチを自前で読む。External = スクリプトからの API 呼び出しだけを受ける")]
        public GizmoInputMode inputMode = GizmoInputMode.BuiltIn;

        [Header("色")]
        public Color frameColorL = new Color(1.00f, 0.48f, 0.32f, 0.95f);
        public Color frameColorM = new Color(0.42f, 0.90f, 0.48f, 0.95f);
        public Color frameColorN = new Color(0.36f, 0.64f, 1.00f, 0.95f);
        public Color workColor = new Color(1.00f, 0.45f, 0.74f, 0.95f);
        public Color travelColor = new Color(0.32f, 0.86f, 0.92f, 0.95f);
        public Color axisColor = new Color(1.00f, 0.83f, 0.26f, 0.95f);
        public Color spinColor = new Color(0.74f, 0.62f, 1.00f, 0.95f);
        public Color azimuthColor = new Color(0.55f, 0.95f, 0.60f, 0.95f);
        public Color highlightColor = new Color(1.00f, 1.00f, 0.72f, 1.00f);
        public Color zeroTickColor = new Color(1.00f, 1.00f, 1.00f, 0.90f);
        public Color limitColor = new Color(1.00f, 0.38f, 0.32f, 0.95f);
        public Color pathColor = new Color(0.85f, 0.87f, 0.92f, 0.70f);
        public Color pathNormalColor = new Color(0.36f, 0.64f, 1.00f, 0.45f);

        [Tooltip("他のオブジェクトに隠れている部分の不透明度")]
        [Range(0f, 1f)] public float occludedAlpha = 0.22f;

        [Tooltip("ビューポートのカメラにだけ描画する。2D 重畳ビューと 3D ビューを使い分けるときに使う")]
        public bool restrictToViewportCamera = false;

        [Header("シェーダ / 工具モデル")]
        [Tooltip("未設定なら Shader.Find で解決する")]
        public Shader gizmoShader;

        [Tooltip("任意。設定すると工具軸に合わせて姿勢が更新される")]
        public Transform toolVisual;

        [Tooltip("工具モデルのローカル軸のうち、工具軸 X に一致させる軸")]
        public Vector3 toolShaftAxis = Vector3.up;

        [Tooltip("工具モデルのローカル軸のうち、スピン基準に一致させる軸 (シャフト軸と直交)")]
        public Vector3 toolReferenceAxis = Vector3.forward;

        // ------------------------------------------------------------------ 状態

        private PathFrame _frame;
        private IPathFrameSource _explicitSource;
        private readonly List<GizmoHandleBase> _handles = new List<GizmoHandleBase>();
        private GizmoHandleBase _hovered;
        private GizmoHandleBase _active;
        private bool _pointerIsTouch;
        private IGizmoViewport _viewport;
        private CameraViewport _defaultViewport;
        private ToolPostureAngles _anglesAtDragStart;

        private Mesh _mesh;
        private Material _matFront;
        private Material _matBehind;
        private readonly GizmoMeshBuilder _builder = new GizmoMeshBuilder();

        // ------------------------------------------------------------------ 公開プロパティ

        /// <summary>
        /// コードからフレーム供給元を差し替える。null なら インスペクタの WeldPath を使う。
        /// </summary>
        public IPathFrameSource FrameSource
        {
            get => _explicitSource ?? path;
            set => _explicitSource = value;
        }

        public PathFrame Frame => _frame;

        /// <summary>
        /// 工具姿勢。保持しているのは球面表現 (theta, phi, spin) そのもので、
        /// 旋回角はこの構造体の中に入っている。ここへ代入して読み戻せば、
        /// 垂直姿勢を経由しても旋回角は失われない。
        /// </summary>
        public ToolPostureAngles Angles
        {
            get => angles;
            set => angles = value;
        }

        // ------------------------------------------------- 球面表現 (theta / phi) での入出力

        /// <summary>
        /// 旋回角 theta [deg]。L 軸正方向が 0 度。
        /// </summary>
        public float AzimuthDeg => angles.azimuthDeg;

        /// <summary>
        /// 仰角 phi [deg]。LM 平面から測る (90 度で工具軸が N に一致)。
        /// </summary>
        public float ElevationDeg => angles.elevationDeg;

        /// <summary>
        /// N からの傾き角 alpha [deg] (= 90 - phi)。
        /// </summary>
        public float TiltFromNormalDeg => angles.TiltFromNormalDeg;

        /// <summary>
        /// 旋回角が工具軸に影響する程度に傾いているか。
        /// false のときも旋回角の値そのものは保持されている (極では姿勢に効かないだけ)。
        /// </summary>
        public bool AzimuthAffectsToolAxis => angles.TiltIsSignificant();

        /// <summary>
        /// 球面表現で姿勢を設定する。
        /// </summary>
        public void SetSpherical(float azimuthDeg, float elevationDeg)
        {
            var a = angles;
            a.azimuthDeg = azimuthDeg;
            a.elevationDeg = elevationDeg;
            angles = a;
        }

        /// <summary>
        /// フレームに対する工具軸ベクトル X (ワールド)。このツールの主たる出力。
        /// </summary>
        public Vector3 ToolAxisWorld => angles.GetAxisWorld(_frame);

        /// <summary>
        /// 工具軸ベクトル X の LMN 成分 (x = L, y = M, z = N)。
        /// </summary>
        public Vector3 ToolAxisLmn => angles.GetAxisLmn();

        /// <summary>
        /// スピンまで含めた工具の完全な姿勢。
        /// </summary>
        public Quaternion ToolRotation => angles.GetToolRotation(_frame, toolShaftAxis, toolReferenceAxis);

        /// <summary>
        /// 画面 &lt;-&gt; ワールドの変換。既定は targetCamera (未設定なら Camera.main) をそのまま使う
        /// CameraViewport。実写重畳ビューのようにアプリ独自の投影を使う場合は、
        /// IGizmoViewport を実装したものをここに差し込む。null を代入すると既定へ戻る。
        /// </summary>
        public IGizmoViewport Viewport
        {
            get
            {
                if (_viewport != null) return _viewport;

                Camera cam = targetCamera != null ? targetCamera : Camera.main;
                if (_defaultViewport == null) _defaultViewport = new CameraViewport(cam);
                else _defaultViewport.Camera = cam;
                return _defaultViewport;
            }
            set => _viewport = value;
        }

        /// <summary>
        /// 描画に使うカメラ (ビューポート由来)。
        /// </summary>
        public Camera Cam => Viewport.RenderCamera;

        /// <summary>
        /// ギズモのワールド半径 (画面上の大きさを一定に保つ)。
        /// </summary>
        public float Scale => Mathf.Max(1e-4f, gizmoPixelSize * Viewport.WorldPerPixel(_frame.Origin));

        public float PixelToWorld(float pixels) => pixels * Viewport.WorldPerPixel(_frame.Origin);

        public GizmoHandleId? HoveredHandle => _hovered?.Id;
        public GizmoHandleId? ActiveHandle => _active?.Id;
        public bool IsDragging => _active != null;

        /// <summary>
        /// 直近のポインタがタッチだったか。当たり判定の広さを切り替えるのに使う。
        /// </summary>
        public bool PointerIsTouch => _pointerIsTouch;

        /// <summary>
        /// 現在のポインタ種別に応じた円弧・リングの当たり判定幅 [px]。
        /// </summary>
        public float HitPixelWidth => _pointerIsTouch ? touchHitPixelWidth : hitPixelWidth;

        /// <summary>
        /// 現在のポインタ種別に応じた軸先端の当たり判定半径 [px]。
        /// </summary>
        public float TipHitPixelRadius => tipPixelRadius * (_pointerIsTouch ? 2.8f : 1.8f);

        public float ClampProjected(float deg)
            => Mathf.Clamp(deg, -ToolPostureAngles.MaxProjectedAngleDeg, ToolPostureAngles.MaxProjectedAngleDeg);

        // ------------------------------------------------------------------ ライフサイクル

        private void OnEnable()
        {
            BuildHandles();
            RefreshFrame();
        }

        private void OnDisable()
        {
            _hovered = null;
            _active = null;
            ReleaseResources();
        }

        private void BuildHandles()
        {
            _handles.Clear();
            // 掴みやすさの優先順: 小さい的から先に判定する
            _handles.Add(new AxisTipHandle(this));
            _handles.Add(new SpinRingHandle(this));
            _handles.Add(new ArcAngleHandle(this, isWork: true));   // LN 平面固定版
            _handles.Add(new TiltArcHandle(this));                  // 工具軸追従版
            _handles.Add(new ArcAngleHandle(this, isWork: false));
            _handles.Add(new AzimuthRingHandle(this));
        }

        /// <summary>
        /// 現在の区間・位置からフレームを再計算する。
        /// </summary>
        public void RefreshFrame()
        {
            var src = FrameSource;
            if (src == null || src.SegmentCount == 0)
            {
                _frame = PathFrame.Fallback(transform.position);
                return;
            }

            segmentIndex = Mathf.Clamp(segmentIndex, 0, src.SegmentCount - 1);
            _frame = src.GetFrame(segmentIndex, segmentU);
        }

        private void Update()
        {
            RefreshFrame();

            if (Application.isPlaying)
            {
                HandleKeyboard();
                if (inputMode == GizmoInputMode.BuiltIn) HandlePointer();
            }

            ApplyToolVisual();
        }

        private void LateUpdate()
        {
            RefreshFrame();
            Render();
        }

        // ------------------------------------------------------------------ 入力

        private void HandlePointer()
        {
            if (Cam == null) return;

            if (!GizmoPointer.TryRead(out PointerSample p))
            {
                _hovered = null;
                EndDrag();
                return;
            }

            _pointerIsTouch = p.isTouch;

            Keyboard kb = Keyboard.current;
            bool snap = kb != null && kb.ctrlKey.isPressed;

            if (_active != null)
            {
                if (p.isDown && !p.releasedThisFrame) UpdateDrag(p.position, snap);
                else EndDrag();
                return;
            }

            bool overUI = EventSystem.current != null &&
                          EventSystem.current.IsPointerOverGameObject(p.pointerId);

            // タッチにはホバー段階が無いので、押した瞬間の位置で拾い直す。
            // ホバー結果に頼ると、指を置いた最初のフレームで取りこぼす。
            if (p.pressedThisFrame && !overUI)
            {
                if (TryPick(p.position, out GizmoHandleId id) && BeginDrag(id, p.position))
                {
                    _hovered = _active;
                    return;
                }
            }

            // ホバー表示はマウス / ペンのときだけ
            _hovered = (p.isTouch || overUI) ? null : PickHandle(p.position);
        }

        // ------------------------------------------------------- 外部ドライブ API (2D ビュー等)

        /// <summary>
        /// スクリーン座標にあるハンドルを探す。座標は Viewport のピクセル空間で与える。
        /// 2D 重畳ビューから呼ぶ場合は、その画面座標を重畳カメラのピクセル座標へ
        /// 変換してから渡すか、変換を含んだ IGizmoViewport を Viewport に差し込む。
        /// </summary>
        public bool TryPick(Vector2 screenPos, out GizmoHandleId id)
        {
            GizmoHandleBase h = PickHandle(screenPos);
            id = h != null ? h.Id : default;
            return h != null;
        }

        /// <summary>
        /// 指定ハンドルのドラッグを開始する。掴み位置と現在値が記録される。
        /// </summary>
        public bool BeginDrag(GizmoHandleId id, Vector2 screenPos)
        {
            GizmoHandleBase h = FindHandle(id);
            if (h == null || !h.Visible) return false;

            _active = h;
            _anglesAtDragStart = angles;
            h.BeginDrag(screenPos);
            return true;
        }

        /// <summary>
        /// ドラッグ中の更新。BeginDrag していない場合は何もしない。
        /// </summary>
        public void UpdateDrag(Vector2 screenPos, bool snap = false)
        {
            if (_active == null) return;
            _active.Drag(screenPos, snap);
        }

        /// <summary>
        /// ドラッグを確定して終了する。
        /// </summary>
        public void EndDrag()
        {
            if (_active == null) return;
            _active.EndDrag();
            _active = null;
        }

        /// <summary>
        /// ドラッグを中断し、BeginDrag 時点の姿勢へ戻す。
        /// </summary>
        public void CancelDrag()
        {
            if (_active == null) return;
            Angles = _anglesAtDragStart;
            _active.EndDrag();
            _active = null;
        }

        /// <summary>
        /// ホバー表示を外から指定する (2D ビューのカーソル位置など)。
        /// </summary>
        public void SetHover(GizmoHandleId? id)
            => _hovered = id.HasValue ? FindHandle(id.Value) : null;

        /// <summary>
        /// ホバー判定をスクリーン座標から行う。
        /// </summary>
        public void UpdateHover(Vector2 screenPos) => _hovered = PickHandle(screenPos);

        /// <summary>
        /// 角度を直接与える。値は AngleConvention を通した表示値。
        /// アプリ側が独自に角度を算出する場合の入口。
        /// </summary>
        public void SetAngleDisplay(GizmoHandleId id, float displayDeg)
        {
            var a = angles;
            switch (id)
            {
                case GizmoHandleId.WorkArc:
                    a.WorkAngleDeg = workConvention.ClampInternal(workConvention.ToInternal(displayDeg));
                    break;
                case GizmoHandleId.TravelArc:
                    a.TravelAngleDeg = travelConvention.ClampInternal(travelConvention.ToInternal(displayDeg));
                    break;
                case GizmoHandleId.SpinRing:
                    a.spinAngleDeg = spinConvention.ClampInternal(spinConvention.ToInternal(displayDeg));
                    break;
                case GizmoHandleId.AzimuthRing:
                    // 旋回角だけを差し替える。傾きは変わらない。
                    a.azimuthDeg = azimuthConvention.ToInternal(displayDeg);
                    break;
                case GizmoHandleId.TiltArc:
                    // 傾きだけを差し替える。旋回角は変わらない。
                    a.TiltFromNormalDeg = tiltConvention.ClampInternal(tiltConvention.ToInternal(displayDeg));
                    break;
                default:
                    return;   // AxisTip は角度 1 つでは決まらない
            }
            Angles = a;
        }

        /// <summary>
        /// 工具軸をワールド方向で直接与える。
        /// </summary>
        public void SetToolAxisWorld(Vector3 worldDirection)
        {
            Vector3 lmn = _frame.WorldDirectionToLmn(worldDirection.normalized);
            if (lmn.z < 0.03f) lmn.z = 0.03f;

            var a = angles;
            a.SetAxisLmn(lmn);      // 極付近では旋回角がそのまま保たれる
            a.WorkAngleDeg = workConvention.ClampInternal(a.WorkAngleDeg);
            a.TravelAngleDeg = travelConvention.ClampInternal(a.TravelAngleDeg);
            Angles = a;
        }

        private GizmoHandleBase FindHandle(GizmoHandleId id)
        {
            foreach (var h in _handles)
                if (h.Id == id) return h;
            return null;
        }

        private GizmoHandleBase PickHandle(Vector2 screenPos)
        {
            GizmoHandleBase best = null;
            float bestDist = float.MaxValue;

            foreach (var h in _handles)
            {
                if (!h.Visible) continue;
                if (h.HitTest(screenPos, out float d) && d < bestDist)
                {
                    bestDist = d;
                    best = h;
                }
            }
            return best;
        }

        private void HandleKeyboard()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.digit1Key.wasPressedThisFrame) showWorkArc = !showWorkArc;
            if (kb.digit2Key.wasPressedThisFrame) showTravelArc = !showTravelArc;
            if (kb.digit3Key.wasPressedThisFrame) showAxisTip = !showAxisTip;
            if (kb.digit4Key.wasPressedThisFrame) showSpinRing = !showSpinRing;
            if (kb.digit5Key.wasPressedThisFrame) showAzimuthRing = !showAzimuthRing;
            if (kb.digit0Key.wasPressedThisFrame) { var v = angles; v.elevationDeg = 90f; angles = v; }

            var src = FrameSource;
            if (src != null && src.SegmentCount > 0)
            {
                if (kb.leftArrowKey.wasPressedThisFrame)
                    segmentIndex = Mathf.Max(0, segmentIndex - 1);
                if (kb.rightArrowKey.wasPressedThisFrame)
                    segmentIndex = Mathf.Min(src.SegmentCount - 1, segmentIndex + 1);
            }

            if (kb.upArrowKey.isPressed) segmentU = Mathf.Clamp01(segmentU + Time.deltaTime * 0.6f);
            if (kb.downArrowKey.isPressed) segmentU = Mathf.Clamp01(segmentU - Time.deltaTime * 0.6f);

            // ハンドルを隠したらホバー状態も落とす
            if (_hovered != null && !_hovered.Visible) _hovered = null;
        }

        private void ApplyToolVisual()
        {
            if (toolVisual == null) return;
            toolVisual.SetPositionAndRotation(_frame.Origin, ToolRotation);
        }

        // ------------------------------------------------------------------ 描画

        private bool EnsureResources()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "ToolPostureGizmo", hideFlags = HideFlags.HideAndDontSave };
                _mesh.MarkDynamic();
            }

            if (_matFront != null && _matBehind != null) return true;

            Shader sh = gizmoShader != null ? gizmoShader : Shader.Find("ToolPosture/GizmoVertexColor");
            if (sh == null) return false;

            _matFront = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _matFront.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            _matFront.SetColor("_Tint", Color.white);
            _matFront.renderQueue = 3010;

            _matBehind = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _matBehind.SetFloat("_ZTest", (float)CompareFunction.Greater);
            _matBehind.renderQueue = 3000;
            return true;
        }

        private void ReleaseResources()
        {
            if (_mesh != null) DestroyResource(_mesh);
            if (_matFront != null) DestroyResource(_matFront);
            if (_matBehind != null) DestroyResource(_matBehind);
            _mesh = null;
            _matFront = null;
            _matBehind = null;
        }

        private static void DestroyResource(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        private void Render()
        {
            Camera cam = Cam;
            if (cam == null || !EnsureResources()) return;

            _builder.Clear();
            BuildGeometry(_builder);
            _builder.Apply(_mesh);
            if (_builder.VertexCount == 0) return;

            var bounds = new Bounds(_frame.Origin, Vector3.one * (Scale * 6f));

            _matBehind.SetColor("_Tint", new Color(1f, 1f, 1f, occludedAlpha));

            var behind = new RenderParams(_matBehind)
            {
                worldBounds = bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = gameObject.layer,
                camera = restrictToViewportCamera ? cam : null,
            };
            Graphics.RenderMesh(behind, _mesh, 0, Matrix4x4.identity);

            var front = new RenderParams(_matFront)
            {
                worldBounds = bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = gameObject.layer,
                camera = restrictToViewportCamera ? cam : null,
            };
            Graphics.RenderMesh(front, _mesh, 0, Matrix4x4.identity);
        }

        private void BuildGeometry(GizmoMeshBuilder b)
        {
            Camera cam = Cam;
            if (cam == null || !_frame.IsValid) return;

            Vector3 o = _frame.Origin;
            Vector3 camPos = Viewport.EyePosition;
            float s = Scale;
            float lineHalf = PixelToWorld(1.6f);
            float headR = PixelToWorld(5.5f);
            float headL = PixelToWorld(16f);

            if (showPath) BuildPathGeometry(b, camPos);

            if (showFrameAxes)
            {
                b.AddArrow(o, _frame.CrossFeed, s * 0.95f, camPos, lineHalf, headR, headL, frameColorL);
                b.AddArrow(o, _frame.Feed, s * 1.25f, camPos, lineHalf, headR, headL, frameColorM);
                b.AddArrow(o, _frame.Normal, s * 1.25f, camPos, lineHalf, headR, headL, frameColorN);
            }

            // 工具軸 X は常に描く (軸先端ハンドルの表示に依存しない)
            Vector3 axis = angles.GetAxisWorld(_frame);
            b.AddArrow(o, axis, s * 1.25f, camPos, PixelToWorld(2.4f), PixelToWorld(6.5f), headL, axisColor);
            b.AddBillboardDisc(o, cam, PixelToWorld(3.5f), zeroTickColor);

            foreach (var h in _handles)
            {
                if (!h.Visible) continue;
                // ドラッグ中は操作しているハンドルだけを残す (フレーム軸と工具軸は残す)
                if (_active != null && hideOthersWhileDragging && h != _active) continue;
                h.Draw(b, h == _hovered, h == _active);
            }
        }

        /// <summary>
        /// 経路の点列と各点の法線。WeldPath.OnDrawGizmos はシーンビュー限定なので実行時用に描く。
        /// </summary>
        private void BuildPathGeometry(GizmoMeshBuilder b, Vector3 camPos)
        {
            Camera cam = Cam;
            if (cam == null || path == null || path.PointCount < 2) return;

            float lineHalf = PixelToWorld(1.4f);
            float normalLen = path.normalGizmoLength;

            for (int i = 0; i < path.PointCount; i++)
            {
                Vector3 p = path.GetWorldPoint(i);

                if (i + 1 < path.PointCount)
                    b.AddScreenLine(p, path.GetWorldPoint(i + 1), camPos, lineHalf, pathColor);

                b.AddScreenDashedLine(p, p + path.GetWorldNormal(i).normalized * normalLen,
                                      camPos, PixelToWorld(1f), PixelToWorld(7f), pathNormalColor);
                b.AddBillboardDisc(p, cam, PixelToWorld(3f), pathColor);
            }
        }
    }
}
