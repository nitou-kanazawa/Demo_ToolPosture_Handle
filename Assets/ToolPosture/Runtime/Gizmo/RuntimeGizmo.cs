using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

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
    /// ランタイムギズモの共通部分。
    ///
    /// 姿勢ギズモと位置ギズモで、描画・当たり判定・入力・スケールの扱いは同じなので
    /// ここにまとめてある。派生側は「どんなハンドルを持つか」と
    /// 「何を描くか」だけを実装する。
    ///
    /// 当たり判定はハンドルごとのコライダーで行う。コライダーは Ignore Raycast
    /// レイヤーに置き Collider.Raycast を直接撃つため、アプリ側のシーンクエリには現れない。
    /// 描画はカメラの描画コールバックから投入するので、エディタが Update を
    /// 回していない編集中でも出る。
    /// </summary>
    [ExecuteAlways]
    public abstract class RuntimeGizmo : MonoBehaviour
    {
        #region プリセット

        [Tooltip("見た目と当たりの太さ")]
        [SerializeField] private GizmoTheme theme;

        #endregion

        #region 表示

        [Tooltip("描画とレイ生成に使うカメラ。未設定なら Camera.main")]
        public Camera targetCamera;

        [Tooltip("targetCamera にだけ描く")]
        public bool restrictToTargetCamera = false;

        [Tooltip("コライダーの形を Gizmos で描く。Game View で見るには Gizmos の表示を有効にすること")]
        public ColliderGizmoMode colliderGizmo = ColliderGizmoMode.Off;

        [Tooltip("ドラッグ中は操作していないハンドルを隠す")]
        public bool hideOthersWhileDragging = true;

        #endregion

        #region 入力

        [Tooltip("BuiltIn なら自前でポインタを読む。External ならレイを外から渡す")]
        public GizmoInputMode inputMode = GizmoInputMode.BuiltIn;

        [Tooltip("実行中にキーでハンドル表示を切り替える")]
        public bool useKeyboardShortcuts = true;

        #endregion

        #region シェーダ

        [Tooltip("未設定なら ToolPosture/GizmoVertexColor を探す")]
        public Shader gizmoShader;

        #endregion

        #region 状態

        protected readonly List<GizmoHandleBase> Handles = new List<GizmoHandleBase>();

        private readonly GizmoHandleColliders _colliders = new GizmoHandleColliders();
        private readonly GizmoMeshBuilder _builder = new GizmoMeshBuilder();

        private GizmoHandleBase _hovered;
        private GizmoHandleBase _active;
        private bool _pointerIsTouch;
        private IGizmoPointerSource _pointerSource;

        private Mesh _mesh;
        private Material _matFront;
        private Material _matBehind;

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
        /// 描画の直前に呼ばれる。状態の供給元が最新の値を渡すためのフック。
        ///
        /// 編集中はエディタが Update を回さないことがあるが、このフックは
        /// 再描画のたびに必ず呼ばれる。
        /// </summary>
        public event Action<RuntimeGizmo> Preparing;

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
        /// ギズモが置かれるワールド座標。大きさの基準にも使う。
        /// </summary>
        public abstract Vector3 Origin { get; }

        /// <summary>
        /// ギズモのワールド上の大きさ。画面上で Theme.gizmoPixelSize [px] になるよう保つ。
        /// </summary>
        public float Scale => Mathf.Max(1e-4f, Theme.gizmoPixelSize * WorldPerPixel(Cam, Origin));

        /// <summary>
        /// ギズモ原点における px からワールド長への換算。
        /// </summary>
        public float PixelToWorld(float pixels) => pixels * WorldPerPixel(Cam, Origin);

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
        public float TipHitPixelRadius
            => Theme.tipHitPixelRadius * (_pointerIsTouch ? Theme.touchTipHitScale : 1f);

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

        #region 派生側が実装するもの

        /// <summary>
        /// このギズモが持つハンドルを <see cref="Handles"/> へ並べる。
        /// </summary>
        protected abstract void BuildHandles();

        /// <summary>
        /// ハンドル以外に描くもの (軸の矢印など)。
        /// ハンドル自体はこのあと共通処理で描かれる。
        /// </summary>
        protected abstract void BuildBaseGeometry(GizmoMeshBuilder b);

        /// <summary>
        /// 描画やコライダー更新の前に状態を整える。
        /// </summary>
        protected virtual void EnsureState() { }

        protected virtual void HandleKeyboard() { }

        /// <summary>
        /// ドラッグ開始時。取り消し用に現在値を控えるのに使う。
        /// </summary>
        protected virtual void OnDragBegan(GizmoHandleBase handle) { }

        /// <summary>
        /// ドラッグ取り消し時。控えた値へ戻すのに使う。
        /// </summary>
        protected virtual void OnDragCancelled(GizmoHandleBase handle) { }

        #endregion

        #region ライフサイクル

        protected virtual void OnEnable()
        {
            BuildHandles();
            EnsureState();

            // Graphics.RenderMesh は 1 フレーム限りの投入なので、LateUpdate から呼ぶと
            // 「エディタがティックしていないが再描画はされる」状況で何も出なくなる。
            Camera.onPreCull += SubmitFor;                                // Built-in RP
            RenderPipelineManager.beginCameraRendering += SubmitForSrp;   // URP / HDRP
        }

        protected virtual void OnDisable()
        {
            Camera.onPreCull -= SubmitFor;
            RenderPipelineManager.beginCameraRendering -= SubmitForSrp;

            _hovered = null;
            _active = null;
            _colliders.Dispose();
            ReleaseResources();
        }

        protected virtual void Update()
        {
            EnsureState();
            if (!Application.isPlaying) return;

            if (useKeyboardShortcuts) HandleKeyboard();
            if (inputMode == GizmoInputMode.BuiltIn) HandlePointer();
        }

        protected virtual void LateUpdate()
        {
            EnsureState();

            // 見えているものと掴めるものを一致させるため、1 フレームの終わりの形状で
            // コライダーを更新する。編集中は実体を作らない (シーンの選択操作の邪魔になる)。
            if (Application.isPlaying) SyncColliders();
        }

        /// <summary>
        /// ハンドルのコライダーを今の形状に合わせる。通常は LateUpdate で自動的に呼ばれる。
        ///
        /// Transform を動かしたあとは明示的に SyncTransforms すること。
        /// Collider.Raycast は物理クエリ前の自動同期に必ずしも乗らず、
        /// 動かした直後に撃つと古い姿勢のまま外れることがある (実測)。
        /// </summary>
        public void SyncColliders()
        {
            _colliders.Sync(this, Handles, _active);
            Physics.SyncTransforms();
        }

        /// <summary>
        /// 当たり判定のコライダーを Gizmos で描く。
        /// ギズモ本体は通常の描画なので Gizmos とは無関係だが、コライダーは見えないため。
        /// </summary>
        protected virtual void OnDrawGizmos()
        {
            if (colliderGizmo == ColliderGizmoMode.Off || Handles.Count == 0) return;

            Preparing?.Invoke(this);
            EnsureState();

            if (colliderGizmo == ColliderGizmoMode.Wireframe)
            {
                // 編集中は LateUpdate が回らないので、ここで実体を作り直す
                if (!Application.isPlaying) SyncColliders();

                _colliders.DrawWireframe(Theme.colliderGizmoColor, Theme.colliderGizmoDisabledColor);
                return;
            }

            float tube = PixelToWorld(HitPixelWidth) * 0.5f;
            foreach (var h in Handles)
            {
                Gizmos.color = IsGrabbable(h) ? Theme.colliderGizmoColor : Theme.colliderGizmoDisabledColor;
                GizmoHandleColliders.DrawShapeOutline(h.GetShape(), tube);
            }
        }

        private bool IsGrabbable(GizmoHandleBase h)
            => h.Visible && (_active == null || !hideOthersWhileDragging || h == _active);

        #endregion

        #region 入力

        private void HandlePointer()
        {
            Camera cam = Cam;
            if (cam == null) return;

            Keyboard kb = Keyboard.current;
            bool snap = kb != null && kb.ctrlKey.isPressed;

            if (!PointerSource.TryRead(out PointerSample p))
            {
                DrivePointer(default, null, snap);
                return;
            }

            DrivePointer(p, cam.ScreenPointToRay(p.position), snap);
        }

        /// <summary>
        /// 外から読んだポインタでホバーとドラッグを進める。
        /// GizmoInputMode.External のときの入口。
        ///
        /// ray はポインタ位置からアプリが作ったワールドのレイ。画像の外などで
        /// レイを作れないときは null を渡すと、新しく掴むのは止まるが、
        /// 進行中のドラッグは指を離すまで続く。
        /// </summary>
        public void DrivePointer(PointerSample pointer, Ray? ray, bool snap = false)
        {
            if (!pointer.valid)
            {
                _hovered = null;
                EndDrag();
                return;
            }

            _pointerIsTouch = pointer.isTouch;

            if (_active != null)
            {
                bool held = pointer.isDown && !pointer.releasedThisFrame;
                if (!held) EndDrag();
                else if (ray.HasValue) UpdateDrag(ray.Value, snap);
                return;
            }

            if (!ray.HasValue)
            {
                _hovered = null;
                return;
            }

            bool overUI = EventSystem.current != null &&
                          EventSystem.current.IsPointerOverGameObject(pointer.pointerId);

            // タッチにはホバー段階が無いので、押した瞬間の位置で拾い直す。
            if (pointer.pressedThisFrame && !overUI)
            {
                if (TryPick(ray.Value, out GizmoHandleId id, out Vector3 point) &&
                    BeginDrag(id, ray.Value, point))
                {
                    _hovered = _active;
                    return;
                }
            }

            // ホバー表示はマウス / ペンのときだけ
            _hovered = (pointer.isTouch || overUI) ? null : PickHandle(ray.Value);
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
            OnDragBegan(h);
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
        /// ドラッグを取り消して開始時の値へ戻す。
        /// </summary>
        public void CancelDrag()
        {
            if (_active == null) return;

            GizmoHandleBase h = _active;
            h.EndDrag();
            _active = null;
            OnDragCancelled(h);
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

        protected GizmoHandleBase FindHandle(GizmoHandleId id)
        {
            foreach (var h in Handles)
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
                _mesh = new Mesh { name = "RuntimeGizmo", hideFlags = HideFlags.HideAndDontSave };
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
        /// カメラごとに呼ばれるので、エディタが Update を回していなくても必ず出る。
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

            Preparing?.Invoke(this);
            EnsureState();

            _builder.Clear();
            BuildGeometry(_builder);
            _builder.Apply(_mesh);
            if (_builder.VertexCount == 0) return;

            var bounds = new Bounds(Origin, Vector3.one * (Scale * 6f));
            _matBehind.SetColor("_Tint", new Color(1f, 1f, 1f, Theme.occludedAlpha));

            Submit(_matBehind, bounds, renderingCamera);
            Submit(_matFront, bounds, renderingCamera);
        }

        private void Submit(Material material, Bounds bounds, Camera renderingCamera)
        {
            var p = new RenderParams(material)
            {
                worldBounds = bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = gameObject.layer,
                camera = renderingCamera,
            };
            Graphics.RenderMesh(p, _mesh, 0, Matrix4x4.identity);
        }

        private void BuildGeometry(GizmoMeshBuilder b)
        {
            if (Cam == null) return;

            // 経路の可視化など、ギズモと一緒に描きたいものを外から足す口
            BuildingExtraGeometry?.Invoke(b);

            BuildBaseGeometry(b);

            foreach (var h in Handles)
            {
                if (!h.Visible) continue;
                if (_active != null && hideOthersWhileDragging && h != _active) continue;

                h.Draw(b, h == _hovered, h == _active);
            }
        }

        #endregion
    }
}
