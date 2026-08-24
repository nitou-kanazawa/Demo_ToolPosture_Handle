using System;
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
        /// マウス / タッチ / ペンを自前で読み、カメラからレイを作る。
        /// </summary>
        BuiltIn = 0,

        /// <summary>
        /// 自前では読まず、TryPick / BeginDrag / UpdateDrag / EndDrag の
        /// 呼び出しだけを受ける。2D 重畳ビューなど、2D -&gt; 3D の変換が
        /// アプリ側にある場合に使う。
        /// </summary>
        External = 1,
    }

    /// <summary>
    /// ランタイム用の工具姿勢ギズモ。
    ///
    /// このコンポーネントの役割は「与えられた 1 つのフレーム (L, M, N) に対して
    /// 工具軸ベクトル X と、その軸まわりの回転を定める」ことだけ。
    /// フレームをどこから持ってくるか (経路の補間、区間の選択、法線の直交化など) は
    /// 関知せず、<see cref="Frame"/> へ代入されたものをそのまま使う。
    /// 姿勢の保持は球面表現 (theta, phi, spin) 一本で、投影角 w / t は導出値。
    ///
    /// 当たり判定はハンドルごとのコライダーで行う。円弧には円弧に沿ったチューブを
    /// 巻くので、視線が円弧の平面に寝ても掴み幅が変わらない。コライダーは
    /// Ignore Raycast レイヤーに置き Collider.Raycast を直接撃つため、
    /// アプリ側のシーンクエリには現れない。
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Tool Posture/Tool Posture Gizmo")]
    public class ToolPostureGizmo : MonoBehaviour
    {
        #region プリセット

        [Tooltip("見た目と当たりの太さ")]
        [SerializeField] private GizmoTheme theme;

        [Tooltip("角度規約と可動範囲")]
        [SerializeField] private ToolPostureProfile profile;

        #endregion

        #region 姿勢

        [SerializeField] private ToolPostureAngles angles = ToolPostureAngles.FromProjected(14f, -10f, 25f);

        #endregion

        #region ハンドル表示

        [Tooltip("傾斜角 alpha の円弧。N と工具軸が張る平面に乗る")]
        public bool showTiltArc = true;

        [Tooltip("旋回角 theta のリング。LM 平面 (母材面) に乗る")]
        public bool showAzimuthRing = true;

        public bool showAxisTip = true;
        public bool showSpinRing = true;

        [Tooltip("LMN フレームの矢印を描く")]
        public bool showFrameAxes = true;

        [Tooltip("ドラッグ中は操作していないハンドルを隠す")]
        public bool hideOthersWhileDragging = true;

        #endregion

        #region 表示

        [Tooltip("描画とレイ生成に使うカメラ。未設定なら Camera.main")]
        public Camera targetCamera;

        [Tooltip("targetCamera にだけ描く")]
        public bool restrictToTargetCamera = false;

        [Tooltip("コライダーの形を Gizmos で描く。Game View で見るには Gizmos の表示を有効にすること")]
        public ColliderGizmoMode colliderGizmo = ColliderGizmoMode.Off;

        #endregion

        #region 入力

        [Tooltip("BuiltIn なら自前でポインタを読む。External ならレイを外から渡す")]
        public GizmoInputMode inputMode = GizmoInputMode.BuiltIn;

        [Tooltip("実行中に 1 - 4 / 0 キーでハンドル表示を切り替える")]
        public bool useKeyboardShortcuts = true;

        #endregion

        #region シェーダ

        [Tooltip("未設定なら ToolPosture/GizmoVertexColor を探す")]
        public Shader gizmoShader;

        #endregion

        #region 状態

        private PathFrame _frame;
        private readonly List<GizmoHandleBase> _handles = new List<GizmoHandleBase>();
        private readonly GizmoHandleColliders _colliders = new GizmoHandleColliders();
        private GizmoHandleBase _hovered;
        private GizmoHandleBase _active;
        private bool _pointerIsTouch;
        private ToolPostureAngles _anglesAtDragStart;
        private IGizmoPointerSource _pointerSource;

        private Mesh _mesh;
        private Material _matFront;
        private Material _matBehind;
        private readonly GizmoMeshBuilder _builder = new GizmoMeshBuilder();

        #endregion

        #region 公開プロパティ

        /// <summary>
        /// 見た目と当たりの太さ。未設定なら組み込み既定を返すので null にならない。
        /// 実行中に差し替えれば次のフレームから反映される。
        /// </summary>
        public GizmoTheme Theme
        {
            get => theme != null ? theme : GizmoTheme.Default;
            set => theme = value;
        }

        /// <summary>
        /// 角度規約と可動範囲。未設定なら組み込み既定を返すので null にならない。
        /// </summary>
        public ToolPostureProfile Profile
        {
            get => profile != null ? profile : ToolPostureProfile.Default;
            set => profile = value;
        }

        /// <summary>
        /// 工具姿勢が乗る LMN フレーム。
        ///
        /// このコンポーネントはフレームを計算しない。経路から補間する、カメラの外部パラ
        /// から求める、固定値を使う、いずれの場合も求めた結果をここへ代入する。
        /// 代入が無い間は transform の位置に置いたフォールバックを使う。
        /// </summary>
        public PathFrame Frame
        {
            get
            {
                EnsureFrame();
                return _frame;
            }
            set => _frame = value;
        }

        /// <summary>
        /// フレームが未設定 (または無効) なら transform の位置に置いたフォールバックを使う。
        ///
        /// PathFrame は readonly フィールドを持つ構造体でシリアライズされないので、
        /// ドメインリロードや再コンパイルの直後は既定値 = 無効に戻る。
        /// 有効性で判定しておけば、フラグを別に持つより取りこぼしが無い。
        /// </summary>
        private void EnsureFrame()
        {
            if (!_frame.IsValid) _frame = PathFrame.Fallback(transform.position);
        }

        /// <summary>
        /// 工具姿勢。保持しているのは球面表現 (theta, phi, spin) そのもので、
        /// 旋回角はこの構造体の中に入っている。ここへ代入して読み戻せば、
        /// 垂直姿勢を経由しても旋回角は失われない。
        /// </summary>
        public ToolPostureAngles Angles
        {
            get => angles;
            set
            {
                angles = value;
                PostureChanged?.Invoke(this);
            }
        }

        /// <summary>
        /// 姿勢が変わったときに呼ばれる。
        /// </summary>
        public event Action<ToolPostureGizmo> PostureChanged;

        /// <summary>
        /// 描画の直前に呼ばれる。フレームの供給元が最新の値を <see cref="Frame"/> へ
        /// 渡すためのフック。
        ///
        /// 編集中はエディタが Update を回さないことがあるが、このフックは
        /// 再描画のたびに必ず呼ばれるので、経路上の正しい位置に出せる。
        /// </summary>
        public event Action<ToolPostureGizmo> PreparingFrame;

        /// <summary>
        /// ギズモのメッシュを組み立てるときに呼ばれる。経路の可視化など、
        /// ギズモと一緒に描きたいものがある場合に線を足すためのフック。
        /// </summary>
        public event Action<GizmoMeshBuilder> BuildingExtraGeometry;

        /// <summary>
        /// 描画とレイ生成に使うカメラ。
        /// </summary>
        public Camera Cam => targetCamera != null ? targetCamera : Camera.main;

        /// <summary>
        /// 視点のワールド座標。破線とビルボードの向きに使う。
        /// </summary>
        public Vector3 EyePosition
        {
            get
            {
                Camera c = Cam;
                return c != null ? c.transform.position : Vector3.zero;
            }
        }

        /// <summary>
        /// ギズモのワールド上の大きさ。画面上で Theme.gizmoPixelSize [px] になるよう保つ。
        /// </summary>
        public float Scale => Mathf.Max(1e-4f, Theme.gizmoPixelSize * WorldPerPixel(Cam, _frame.Origin));

        /// <summary>
        /// ギズモ原点における px からワールド長への換算。
        /// </summary>
        public float PixelToWorld(float pixels) => pixels * WorldPerPixel(Cam, _frame.Origin);

        /// <summary>
        /// ポインタ入力の供給元。既定はデバイス直読み。
        /// </summary>
        public IGizmoPointerSource PointerSource
        {
            get => _pointerSource ?? DevicePointerSource.Default;
            set => _pointerSource = value;
        }

        public GizmoHandleId? HoveredHandle => _hovered?.Id;
        public GizmoHandleId? ActiveHandle => _active?.Id;
        public bool IsDragging => _active != null;

        /// <summary>
        /// 直近のポインタがタッチだったか。当たり判定の広さを切り替えるのに使う。
        /// </summary>
        public bool PointerIsTouch => _pointerIsTouch;

        /// <summary>
        /// 現在のポインタ種別に応じたチューブの直径 [px]。
        /// </summary>
        public float HitPixelWidth => _pointerIsTouch ? Theme.touchHitPixelWidth : Theme.hitPixelWidth;

        /// <summary>
        /// 現在のポインタ種別に応じた軸先端の当たり判定半径 [px]。
        /// </summary>
        public float TipHitPixelRadius => Theme.tipHitPixelRadius * (_pointerIsTouch ? Theme.touchTipHitScale : 1f);

        /// <summary>
        /// 指定ワールド座標における 1 ピクセル分のワールド長。
        /// </summary>
        public static float WorldPerPixel(Camera cam, Vector3 worldPoint)
        {
            if (cam == null || cam.pixelHeight <= 0) return 0.01f;

            if (cam.orthographic)
                return 2f * cam.orthographicSize / cam.pixelHeight;

            float d = Vector3.Dot(worldPoint - cam.transform.position, cam.transform.forward);
            d = Mathf.Max(d, cam.nearClipPlane);
            return 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / cam.pixelHeight;
        }

        #endregion

        #region 球面表現 (theta / phi) での入出力

        /// <summary>
        /// 旋回角 theta。母材面内で L 軸正方向から測る。
        /// </summary>
        public float AzimuthDeg => angles.azimuthDeg;

        /// <summary>
        /// 仰角 phi。90 度で工具軸が N に一致する。
        /// </summary>
        public float ElevationDeg => angles.elevationDeg;

        /// <summary>
        /// N からの傾き量 alpha = 90 - phi。
        /// </summary>
        public float TiltFromNormalDeg => angles.TiltFromNormalDeg;

        /// <summary>
        /// 旋回角が工具軸に影響するか。傾きが 0 付近では false になり、
        /// 旋回角は保持値として扱われる。
        /// </summary>
        public bool AzimuthAffectsToolAxis => angles.TiltIsSignificant();

        /// <summary>
        /// 球面表現で姿勢を与える。
        /// </summary>
        public void SetSpherical(float azimuthDeg, float elevationDeg)
        {
            var a = angles;
            a.azimuthDeg = azimuthDeg;
            a.elevationDeg = elevationDeg;
            Angles = a;
        }

        #endregion

        #region 工具軸の出力

        /// <summary>
        /// 工具軸 X のワールド方向。このギズモの主たる出力。
        /// </summary>
        public Vector3 ToolAxisWorld => angles.GetAxisWorld(_frame);

        /// <summary>
        /// 工具軸 X の LMN 成分。
        /// </summary>
        public Vector3 ToolAxisLmn => angles.GetAxisLmn();

        /// <summary>
        /// 動かせる傾斜角の範囲。内部値 (N からの傾き α)。
        /// tiltConvention の可動範囲そのもので、方位には依存しない。
        /// </summary>
        public void GetTiltRange(out float loDeg, out float hiDeg)
            => Profile.tiltConvention.GetArcRange(Theme.fallbackArcHalfWidthDeg, out loDeg, out hiDeg);

        // Quaternion での出力はここには置かない。工具軸から回転を組むには
        // 「向けたい対象のどのローカル軸を工具軸に合わせるか」という対象側の都合が要り、
        // その値はロボットのフランジと工具モデルで別物になる。
        // 対象を持っている側が Core の関数を直接呼ぶこと:
        //
        //   gizmo.Angles.GetToolRotation(gizmo.Frame, shaftAxis, referenceAxis,
        //                                gizmo.Profile.spinReference)
        //
        // 工具モデルを追従させるだけなら ToolPostureFollower が使える。

        #endregion

        #region ライフサイクル

        private void OnEnable()
        {
            BuildHandles();
            EnsureFrame();

            // Graphics.RenderMesh は 1 フレーム限りの投入なので、LateUpdate から呼ぶと
            // 「エディタがティックしていないが再描画はされる」状況 (編集中の Game View や
            // Scene View) で何も出なくなる。カメラの描画直前に投入すれば必ず出る。
            Camera.onPreCull += SubmitFor;                          // Built-in RP
            RenderPipelineManager.beginCameraRendering += SubmitForSrp;   // URP / HDRP
        }

        private void OnDisable()
        {
            Camera.onPreCull -= SubmitFor;
            RenderPipelineManager.beginCameraRendering -= SubmitForSrp;

            _hovered = null;
            _active = null;
            _colliders.Dispose();
            ReleaseResources();
        }

        private void BuildHandles()
        {
            _handles.Clear();
            _handles.Add(new AxisTipHandle(this));
            _handles.Add(new SpinRingHandle(this));
            _handles.Add(new TiltArcHandle(this));      // 傾斜角 alpha
            _handles.Add(new AzimuthRingHandle(this));  // 旋回角 theta
        }

        private void Update()
        {
            EnsureFrame();
            if (!Application.isPlaying) return;

            if (useKeyboardShortcuts) HandleKeyboard();
            if (inputMode == GizmoInputMode.BuiltIn) HandlePointer();
        }

        private void LateUpdate()
        {
            EnsureFrame();

            // 見えているものと掴めるものを一致させるため、1 フレームの終わりの形状で
            // コライダーを更新する。次のフレームの入力はこれに対して判定される。
            // 編集中はコライダーの実体を作らない (シーンの選択操作の邪魔になる)。
            if (Application.isPlaying) SyncColliders();
        }

        /// <summary>
        /// 当たり判定のコライダーを Gizmos で描く。
        ///
        /// ギズモ本体は Graphics.RenderMesh による通常の描画なので Gizmos とは無関係だが、
        /// コライダーは見えないものなのでここで可視化する。
        /// </summary>
        private void OnDrawGizmos()
        {
            if (colliderGizmo == ColliderGizmoMode.Off || _handles.Count == 0) return;

            PreparingFrame?.Invoke(this);
            EnsureFrame();

            if (colliderGizmo == ColliderGizmoMode.Wireframe)
            {
                // 編集中は LateUpdate が回らないので、ここで実体を作り直す
                if (!Application.isPlaying) SyncColliders();

                _colliders.DrawWireframe(Theme.colliderGizmoColor, Theme.colliderGizmoDisabledColor);
                return;
            }

            float tube = PixelToWorld(HitPixelWidth) * 0.5f;
            foreach (var h in _handles)
            {
                bool grabbable = h.Visible &&
                                 (_active == null || !hideOthersWhileDragging || h == _active);

                Gizmos.color = grabbable ? Theme.colliderGizmoColor : Theme.colliderGizmoDisabledColor;
                GizmoHandleColliders.DrawShapeOutline(h.GetShape(), tube);
            }
        }

        /// <summary>
        /// ハンドルのコライダーを今の形状に合わせる。
        /// 通常は LateUpdate で自動的に呼ばれる。同一フレーム内で形状を変えてから
        /// 拾い直したい場合や、Update を回さない環境で使う。
        /// </summary>
        public void SyncColliders()
        {
            _colliders.Sync(this, _handles, _active);
            // Transform を動かすだけなので、物理クエリ前の自動同期に任せる
        }

        #endregion

        #region 入力

        private void HandlePointer()
        {
            Camera cam = Cam;
            if (cam == null) return;

            if (!PointerSource.TryRead(out PointerSample p))
            {
                _hovered = null;
                EndDrag();
                return;
            }

            _pointerIsTouch = p.isTouch;

            Keyboard kb = Keyboard.current;
            bool snap = kb != null && kb.ctrlKey.isPressed;
            Ray ray = cam.ScreenPointToRay(p.position);

            if (_active != null)
            {
                if (p.isDown && !p.releasedThisFrame) UpdateDrag(ray, snap);
                else EndDrag();
                return;
            }

            bool overUI = EventSystem.current != null &&
                          EventSystem.current.IsPointerOverGameObject(p.pointerId);

            // タッチにはホバー段階が無いので、押した瞬間の位置で拾い直す。
            // ホバー結果に頼ると、指を置いた最初のフレームで取りこぼす。
            if (p.pressedThisFrame && !overUI)
            {
                if (TryPick(ray, out GizmoHandleId id, out Vector3 point) && BeginDrag(id, ray, point))
                {
                    _hovered = _active;
                    return;
                }
            }

            // ホバー表示はマウス / ペンのときだけ
            _hovered = (p.isTouch || overUI) ? null : PickHandle(ray);
        }

        private void HandleKeyboard()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.digit1Key.wasPressedThisFrame) showTiltArc = !showTiltArc;
            if (kb.digit2Key.wasPressedThisFrame) showAzimuthRing = !showAzimuthRing;
            if (kb.digit3Key.wasPressedThisFrame) showAxisTip = !showAxisTip;
            if (kb.digit4Key.wasPressedThisFrame) showSpinRing = !showSpinRing;
            if (kb.digit0Key.wasPressedThisFrame) SetSpherical(angles.azimuthDeg, 90f);
        }

        #endregion

        #region 外部ドライブ API (レイ)

        /// <summary>
        /// レイに当たっているハンドルを探す。
        /// アプリが独自の 2D -&gt; 3D 変換を持つ場合は、そこで作ったレイを渡す。
        /// </summary>
        public bool TryPick(Ray ray, out GizmoHandleId id) => TryPick(ray, out id, out _);

        /// <summary>
        /// レイに当たっているハンドルと、その当たった点を返す。
        /// 当たった点を BeginDrag へ渡すと、掴んだ位置が正確に反映される。
        /// </summary>
        public bool TryPick(Ray ray, out GizmoHandleId id, out Vector3 point)
        {
            id = default;
            if (!_colliders.TryPick(ray, Mathf.Infinity, out GizmoHandleBase h, out point)) return false;

            id = h.Id;
            return true;
        }

        /// <summary>
        /// アプリが自前で撃った raycast の結果からハンドルを引く。
        /// ギズモのコライダーは Ignore Raycast レイヤーに居るので、
        /// 拾うには明示的にそのレイヤーを含めたマスクで撃つこと。
        /// </summary>
        public bool TryResolve(Collider collider, out GizmoHandleId id)
        {
            id = default;
            if (!_colliders.TryResolve(collider, out GizmoHandleBase h)) return false;

            id = h.Id;
            return true;
        }

        /// <summary>
        /// 指定ハンドルのドラッグを開始する。掴んだ点が分からない場合はこちら。
        /// </summary>
        public bool BeginDrag(GizmoHandleId id, Ray ray)
        {
            GizmoHandleBase h = FindHandle(id);
            return h != null && BeginDragInternal(h, ray, h.GetShape().Center);
        }

        /// <summary>
        /// 掴んだ点を指定してドラッグを開始する。TryPick が返した点をそのまま渡す。
        /// </summary>
        public bool BeginDrag(GizmoHandleId id, Ray ray, Vector3 grabPoint)
        {
            GizmoHandleBase h = FindHandle(id);
            return h != null && BeginDragInternal(h, ray, grabPoint);
        }

        private bool BeginDragInternal(GizmoHandleBase h, Ray ray, Vector3 grabPoint)
        {
            if (!h.Visible) return false;

            _active = h;
            _anglesAtDragStart = angles;
            h.BeginDrag(ray, grabPoint);
            return true;
        }

        /// <summary>
        /// ドラッグ中の更新。BeginDrag していない場合は何もしない。
        /// </summary>
        public void UpdateDrag(Ray ray, bool snap = false)
        {
            if (_active == null) return;
            _active.Drag(ray, snap);
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
        /// ドラッグを取り消して開始時の姿勢へ戻す。
        /// </summary>
        public void CancelDrag()
        {
            if (_active == null) return;
            _active.EndDrag();
            _active = null;
            Angles = _anglesAtDragStart;
        }

        /// <summary>
        /// ホバー表示を外から指定する。
        /// </summary>
        public void SetHover(GizmoHandleId? id)
            => _hovered = id.HasValue ? FindHandle(id.Value) : null;

        /// <summary>
        /// ホバー判定をレイから行う。
        /// </summary>
        public void UpdateHover(Ray ray) => _hovered = PickHandle(ray);

        /// <summary>
        /// 角度を直接与える。値は AngleConvention を通した表示値。
        /// アプリ側が独自に角度を算出する場合の入口。
        /// </summary>
        public void SetAngleDisplay(GizmoHandleId id, float displayDeg)
        {
            var a = angles;
            switch (id)
            {
                case GizmoHandleId.SpinRing:
                    a.spinAngleDeg = Profile.spinConvention.ClampInternal(Profile.spinConvention.ToInternal(displayDeg));
                    break;
                case GizmoHandleId.AzimuthRing:
                    // 旋回角だけを差し替える。傾きは変わらない。
                    a.azimuthDeg = Profile.azimuthConvention.ToInternal(displayDeg);
                    break;
                case GizmoHandleId.TiltArc:
                    // 傾きだけを差し替える。旋回角は変わらない。
                    a.TiltFromNormalDeg = Profile.tiltConvention.ClampInternal(Profile.tiltConvention.ToInternal(displayDeg));
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
            a.TiltFromNormalDeg = Profile.tiltConvention.ClampInternal(a.TiltFromNormalDeg);
            Angles = a;
        }

        private GizmoHandleBase FindHandle(GizmoHandleId id)
        {
            foreach (var h in _handles)
                if (h.Id == id) return h;
            return null;
        }

        private GizmoHandleBase PickHandle(Ray ray)
            => _colliders.TryPick(ray, Mathf.Infinity, out GizmoHandleBase h, out _) ? h : null;

        #endregion

        #region 描画

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

        private void SubmitForSrp(ScriptableRenderContext context, Camera renderingCamera)
            => SubmitFor(renderingCamera);

        /// <summary>
        /// 指定カメラの描画直前にギズモのメッシュを投入する。
        ///
        /// カメラごとに呼ばれるので、エディタが Update を回していなくても
        /// 再描画のたびに必ず出る。形状は targetCamera を基準に組み立てる
        /// (ビルボードと画面基準の線幅がそこを向く)。
        /// </summary>
        private void SubmitFor(Camera renderingCamera)
        {
            if (renderingCamera == null || !isActiveAndEnabled) return;

            // マテリアルプレビュー等には出さない
            if (renderingCamera.cameraType != CameraType.Game &&
                renderingCamera.cameraType != CameraType.SceneView) return;

            Camera cam = Cam;
            if (cam == null || !EnsureResources()) return;
            if (restrictToTargetCamera && renderingCamera != cam) return;

            PreparingFrame?.Invoke(this);

            EnsureFrame();

            _builder.Clear();
            BuildGeometry(_builder);
            _builder.Apply(_mesh);
            if (_builder.VertexCount == 0) return;

            var bounds = new Bounds(_frame.Origin, Vector3.one * (Scale * 6f));
            _matBehind.SetColor("_Tint", new Color(1f, 1f, 1f, Theme.occludedAlpha));

            var behind = new RenderParams(_matBehind)
            {
                worldBounds = bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = gameObject.layer,
                camera = renderingCamera,
            };
            Graphics.RenderMesh(behind, _mesh, 0, Matrix4x4.identity);

            var front = new RenderParams(_matFront)
            {
                worldBounds = bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = gameObject.layer,
                camera = renderingCamera,
            };
            Graphics.RenderMesh(front, _mesh, 0, Matrix4x4.identity);
        }

        private void BuildGeometry(GizmoMeshBuilder b)
        {
            Camera cam = Cam;
            if (cam == null || !_frame.IsValid) return;

            Vector3 o = _frame.Origin;
            Vector3 camPos = EyePosition;
            float s = Scale;
            float lineHalf = PixelToWorld(Theme.frameAxisPixelWidth) * 0.5f;
            float headR = PixelToWorld(Theme.arrowHeadPixelRadius);
            float headL = PixelToWorld(Theme.arrowHeadPixelLength);

            // 経路の可視化など、ギズモと一緒に描きたいものを外から足す口
            BuildingExtraGeometry?.Invoke(b);

            if (showFrameAxes)
            {
                b.AddArrow(o, _frame.CrossFeed, s * Theme.crossFeedAxisLengthRatio, camPos, lineHalf, headR, headL, Theme.frameColorL);
                b.AddArrow(o, _frame.Feed, s * Theme.frameAxisLengthRatio, camPos, lineHalf, headR, headL, Theme.frameColorM);
                b.AddArrow(o, _frame.Normal, s * Theme.frameAxisLengthRatio, camPos, lineHalf, headR, headL, Theme.frameColorN);
            }

            // 工具軸 X は常に描く (軸先端ハンドルの表示に依存しない)
            Vector3 axis = angles.GetAxisWorld(_frame);
            b.AddArrow(o, axis, s * Theme.toolAxisLengthRatio, camPos, PixelToWorld(Theme.toolAxisPixelWidth) * 0.5f,
                       PixelToWorld(Theme.toolArrowHeadPixelRadius), headL, Theme.axisColor);
            b.AddBillboardDisc(o, cam, PixelToWorld(Theme.originDotPixelRadius), Theme.zeroTickColor);

            foreach (var h in _handles)
            {
                if (!h.Visible) continue;
                // ドラッグ中は操作しているハンドルだけを残す (フレーム軸と工具軸は残す)
                if (_active != null && hideOthersWhileDragging && h != _active) continue;
                h.Draw(b, h == _hovered, h == _active);
            }
        }

        #endregion
    }
}
