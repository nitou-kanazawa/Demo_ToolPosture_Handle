using System.Collections.Generic;
using UnityEngine;

namespace ToolRuntimeGizmos.Gizmo
{
    /// <summary>
    /// 当たり判定のコライダーを Gizmos で可視化する方法。
    /// </summary>
    internal enum ColliderGizmoMode
    {
        /// <summary>
        /// 描かない。
        /// </summary>
        Off = 0,

        /// <summary>
        /// チューブの稜線と断面だけを描く。軽くて形が読みやすく、
        /// コライダーの実体が無い (再生していない) ときでも描ける。
        /// </summary>
        Outline = 1,

        /// <summary>
        /// 実際のコライダーメッシュをそのまま描く。面取りまで見えるので、
        /// 見えているものと掴めるものがずれていないかを厳密に確認できる。
        /// 再生中のみ (エディタでは colliderGizmo をこれにすると実体を作る)。
        /// </summary>
        Wireframe = 2,
    }

    /// <summary>
    /// ハンドルの当たり判定用コライダーの生成と追従。
    ///
    /// 円弧ハンドルには円弧に沿ったチューブ (トーラス) を割り当てる。断面が円なので
    /// スクリーン上のシルエット幅が視線角度によらず一定になり、平面内で半径方向の
    /// ずれを見る方式のように「母材面を浅い角度から見ると掴めなくなる」現象が起きない。
    ///
    /// コライダーは Ignore Raycast レイヤーに置き、判定には Physics.Raycast ではなく
    /// Collider.Raycast を直接使う。シーンクエリに一切参加しないので、アプリ側が
    /// 干渉チェック等で回している raycast を汚さない。
    /// </summary>
    internal class GizmoHandleColliders
    {
        #region 定数

        /// <summary>
        /// トーラスの周方向の分割数。
        /// </summary>
        private const int MajorSegments = 48;

        /// <summary>
        /// トーラスの断面の分割数。
        /// </summary>
        private const int MinorSegments = 8;

        /// <summary>
        /// チューブの太さ比がこの割合を超えて変わったらメッシュを作り直す。
        /// 通常は太さ比も半径比も一定なので作り直しは起きない。
        /// </summary>
        private const float RebuildTolerance = 0.02f;

        /// <summary>
        /// 円弧の角度範囲を判定するときの余裕 [deg]。
        /// </summary>
        private const float AngleMarginDeg = 1.5f;

        #endregion

        #region 状態

        private sealed class Entry
        {
            public GizmoHandleBase Handle;
            public Transform Transform;
            public Collider Collider;
            public MeshCollider MeshCollider;
            public Mesh Mesh;
            public float TubeRatio;
            public GizmoHandleShape Shape;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<Collider, Entry> _byCollider = new Dictionary<Collider, Entry>();
        private Transform _root;

        #endregion

        #region 同期

        /// <summary>
        /// ハンドルの現在の形状にコライダーを合わせる。毎フレーム呼ぶ。
        /// </summary>
        public void Sync(RuntimeGizmo gizmo, IList<GizmoHandleBase> handles, GizmoHandleBase active)
        {
            EnsureRoot(gizmo);
            if (_entries.Count != handles.Count) Rebuild(handles);

            float tube = gizmo.PixelToWorld(gizmo.HitPixelWidth) * 0.5f;
            float parentScale = Mathf.Abs(_root.lossyScale.x);
            if (parentScale < 1e-6f) parentScale = 1f;

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                GizmoHandleShape shape = e.Handle.GetShape();
                e.Shape = shape;

                if (e.Collider == null) continue;

                if (shape.Radius < 1e-6f)
                {
                    e.Collider.enabled = false;
                    continue;
                }

                // 掴めないハンドルも位置だけは合わせておく。そうしないと一度も
                // 配置されないまま原点に残り、デバッグ表示に巨大な形が出てしまう。
                if (shape.Kind == GizmoShapeKind.Arc) SyncArc(e, shape, tube, parentScale);
                else if (shape.Kind == GizmoShapeKind.Segment) SyncSegment(e, shape, parentScale);
                else SyncSphere(e, shape, parentScale);

                e.Collider.enabled = e.Handle.Visible &&
                                     (active == null || !gizmo.hideOthersWhileDragging || e.Handle == active);
            }
        }

        private void SyncArc(Entry e, GizmoHandleShape shape, float tube, float parentScale)
        {
            // 単位半径のトーラスを localScale で拡縮する。太さ比が変わらない限り
            // メッシュは作り直さない = PhysX の再クックが走らない。
            float ratio = Mathf.Clamp(tube / shape.Radius, 0.005f, 0.5f);
            if (e.MeshCollider == null || Mathf.Abs(ratio - e.TubeRatio) > ratio * RebuildTolerance)
            {
                e.TubeRatio = ratio;
                BuildTorus(e, ratio);
            }

            e.Transform.SetPositionAndRotation(shape.Center,
                                               Quaternion.LookRotation(shape.Normal, shape.V));
            e.Transform.localScale = Vector3.one * (shape.Radius / parentScale);
        }

        /// <summary>
        /// 線分のカプセル。半径と長さを直接与えるので localScale は使わない
        /// (CapsuleCollider は非等倍スケールの扱いが読みにくい)。
        /// </summary>
        private static void SyncSegment(Entry e, GizmoHandleShape shape, float parentScale)
        {
            e.Transform.SetPositionAndRotation(shape.Center, Quaternion.LookRotation(shape.U));
            e.Transform.localScale = Vector3.one / parentScale;

            var capsule = (CapsuleCollider)e.Collider;
            capsule.direction = 2;                  // ローカル Z
            capsule.radius = shape.Radius;
            capsule.height = Mathf.Max(shape.Length, shape.Radius * 2f);
        }

        private static void SyncSphere(Entry e, GizmoHandleShape shape, float parentScale)
        {
            e.Transform.position = shape.Center;
            e.Transform.rotation = Quaternion.identity;
            e.Transform.localScale = Vector3.one * (shape.Radius / parentScale);
        }

        #endregion

        #region 判定

        /// <summary>
        /// レイに当たっているハンドルのうち、最も手前のものを返す。
        ///
        /// Collider.Raycast を各コライダーへ直接撃つので、シーンの物理クエリとは無関係。
        /// 距離は同じ意味 (レイ原点からコライダー表面まで) で揃うため、
        /// 重なったハンドル同士でも手前が正しく勝つ。
        /// </summary>
        public bool TryPick(Ray ray, float maxDistance,
                            out GizmoHandleBase handle, out Vector3 point, out float distance)
        {
            handle = null;
            point = default;
            float best = float.MaxValue;

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.Collider == null || !e.Collider.enabled) continue;
                if (!e.Collider.Raycast(ray, out RaycastHit hit, maxDistance)) continue;

                // コライダーは全周のチューブなので、可動範囲の外はここで落とす
                if (!e.Shape.ContainsAngleOf(hit.point, AngleMarginDeg)) continue;

                if (hit.distance >= best) continue;
                best = hit.distance;
                handle = e.Handle;
                point = hit.point;
            }

            distance = handle != null ? best : 0f;
            return handle != null;
        }

        /// <summary>
        /// その GameObject がどれかのギズモの当たり判定用コライダーか。
        ///
        /// カメラに PhysicsRaycaster が付いていると、このコライダーも EventSystem の
        /// レイキャストに出てくる。「手前に何かあるか」を調べるときに自分自身を
        /// 数えてしまうと、ハンドルの上にポインタを置いた瞬間に自分で自分を塞ぐ。
        /// </summary>
        public static bool IsHandleCollider(GameObject go)
        {
            if (go == null) return false;

            for (int i = 0; i < _allRoots.Count; i++)
            {
                Transform root = _allRoots[i];
                if (root != null && go.transform.IsChildOf(root)) return true;
            }
            return false;
        }

        /// <summary>
        /// アプリが自前で撃った raycast の結果からハンドルを引く。
        /// </summary>
        public bool TryResolve(Collider collider, out GizmoHandleBase handle)
        {
            handle = null;
            if (collider == null) return false;
            if (!_byCollider.TryGetValue(collider, out Entry e)) return false;

            handle = e.Handle;
            return true;
        }

        #endregion

        #region 構築と破棄

        /// <summary>
        /// 生成済みのコライダー置き場すべて。IsHandleCollider の判定に使う。
        /// </summary>
        private static readonly List<Transform> _allRoots = new List<Transform>();

        private void EnsureRoot(RuntimeGizmo gizmo)
        {
            if (_root != null) return;

            var go = new GameObject("ToolPostureHandleColliders");
            go.transform.SetParent(gizmo.transform, false);

            // Ignore Raycast レイヤー。シーンの Physics.Raycast の既定マスクから外れる。
            go.layer = 2;
            go.hideFlags = HideFlags.DontSave;
            _root = go.transform;
            _allRoots.Add(_root);
        }

        private void Rebuild(IList<GizmoHandleBase> handles)
        {
            Clear();

            for (int i = 0; i < handles.Count; i++)
            {
                var go = new GameObject(handles[i].Id.ToString());
                go.transform.SetParent(_root, false);
                go.layer = _root.gameObject.layer;
                go.hideFlags = HideFlags.DontSave;

                var e = new Entry { Handle = handles[i], Transform = go.transform };

                GizmoShapeKind kind = handles[i].GetShape().Kind;
                if (kind == GizmoShapeKind.Sphere)
                {
                    var sc = go.AddComponent<SphereCollider>();
                    sc.radius = 1f;
                    e.Collider = sc;
                }
                else if (kind == GizmoShapeKind.Segment)
                {
                    e.Collider = go.AddComponent<CapsuleCollider>();
                }
                else
                {
                    var mc = go.AddComponent<MeshCollider>();
                    mc.convex = false;
                    e.MeshCollider = mc;
                    e.Collider = mc;
                }

                e.Collider.enabled = false;
                _entries.Add(e);
                _byCollider[e.Collider] = e;
            }
        }

        /// <summary>
        /// 生成したコライダーとメッシュを破棄する。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Destroy(_entries[i].Mesh);
                if (_entries[i].Transform != null) Destroy(_entries[i].Transform.gameObject);
            }
            _entries.Clear();
            _byCollider.Clear();
        }

        /// <summary>
        /// 根ごと破棄する。
        /// </summary>
        public void Dispose()
        {
            Clear();
            if (_root != null)
            {
                _allRoots.Remove(_root);
                Destroy(_root.gameObject);
            }
            _root = null;
        }

        private static void Destroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        #endregion

        #region デバッグ表示

        /// <summary>
        /// 形状の輪郭を Gizmos で描く。コライダーの実体が無くても描けるので、
        /// 再生していないときでも当たり判定の太さと位置を確認できる。
        /// </summary>
        /// <param name="shape">描く形状。</param>
        /// <param name="tube">チューブの半径 [world]。</param>
        public static void DrawShapeOutline(GizmoHandleShape shape, float tube)
        {
            if (shape.Kind == GizmoShapeKind.Sphere)
            {
                Gizmos.DrawWireSphere(shape.Center, shape.Radius);
                return;
            }

            if (shape.Kind == GizmoShapeKind.Segment)
            {
                DrawCapsuleOutline(shape);
                return;
            }

            float from = Mathf.Min(shape.FromDeg, shape.ToDeg);
            float to = Mathf.Max(shape.FromDeg, shape.ToDeg);

            // チューブの 4 本の稜線 (平面内で内外、法線方向に上下)
            DrawArcLine(shape, shape.Radius + tube, 0f, from, to);
            DrawArcLine(shape, shape.Radius - tube, 0f, from, to);
            DrawArcLine(shape, shape.Radius, tube, from, to);
            DrawArcLine(shape, shape.Radius, -tube, from, to);

            // 断面の円
            Vector3 n = shape.Normal;
            int sections = Mathf.Clamp(Mathf.RoundToInt((to - from) / 30f), 2, 12);
            for (int i = 0; i <= sections; i++)
            {
                float a = Mathf.Lerp(from, to, i / (float)sections) * Mathf.Deg2Rad;
                Vector3 radial = shape.U * Mathf.Cos(a) + shape.V * Mathf.Sin(a);
                DrawCircle(shape.Center + radial * shape.Radius, radial, n, tube, 12);
            }
        }

        /// <summary>
        /// 線分のカプセル。両端の輪と、それを結ぶ 4 本の稜線で形が読める。
        /// </summary>
        private static void DrawCapsuleOutline(GizmoHandleShape shape)
        {
            Vector3 axis = shape.U;
            Vector3 a = axis == Vector3.up ? Vector3.right : Vector3.Cross(axis, Vector3.up).normalized;
            Vector3 b = Vector3.Cross(axis, a).normalized;

            float half = Mathf.Max(0f, shape.Length * 0.5f - shape.Radius);
            Vector3 p0 = shape.Center - axis * half;
            Vector3 p1 = shape.Center + axis * half;

            DrawCircle(p0, a, b, shape.Radius, 16);
            DrawCircle(p1, a, b, shape.Radius, 16);

            foreach (Vector3 side in new[] { a, -a, b, -b })
                Gizmos.DrawLine(p0 + side * shape.Radius, p1 + side * shape.Radius);

            // 端のドーム
            Gizmos.DrawWireSphere(p0, shape.Radius);
            Gizmos.DrawWireSphere(p1, shape.Radius);
        }

        private static void DrawArcLine(GizmoHandleShape shape, float radius, float normalOffset,
                                        float fromDeg, float toDeg)
        {
            Vector3 offset = shape.Normal * normalOffset;
            int seg = Mathf.Clamp(Mathf.RoundToInt((toDeg - fromDeg) / 6f), 8, 96);

            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)seg) * Mathf.Deg2Rad;
                Vector3 p = shape.Center
                          + (shape.U * Mathf.Cos(a) + shape.V * Mathf.Sin(a)) * radius
                          + offset;
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }

        private static void DrawCircle(Vector3 center, Vector3 a, Vector3 b, float radius, int segments)
        {
            Vector3 prev = center + a * radius;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                Vector3 p = center + (a * Mathf.Cos(t) + b * Mathf.Sin(t)) * radius;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }

        /// <summary>
        /// 実際のコライダーメッシュをそのまま描く。無効になっているコライダーは
        /// 別の色で描くので、ドラッグ中にどれが生きているかも分かる。
        /// </summary>
        public void DrawWireframe(Color enabledColor, Color disabledColor)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.Collider == null || e.Transform == null) continue;

                Gizmos.color = e.Collider.enabled ? enabledColor : disabledColor;

                if (e.Mesh != null)
                    Gizmos.DrawWireMesh(e.Mesh, e.Transform.position, e.Transform.rotation,
                                        e.Transform.lossyScale);
                else
                    Gizmos.DrawWireSphere(e.Transform.position, Mathf.Abs(e.Transform.lossyScale.x));
            }
        }

        #endregion

        #region トーラス生成

        /// <summary>
        /// 主半径 1、副半径 minorRadius のトーラスを XY 平面上に作る。
        /// 法線は +Z、0 度方向は +X。
        /// </summary>
        private static void BuildTorus(Entry e, float minorRadius)
        {
            if (e.Mesh == null)
            {
                e.Mesh = new Mesh { name = "GizmoHandleTube", hideFlags = HideFlags.DontSave };
                e.Mesh.MarkDynamic();
            }

            int vcount = MajorSegments * MinorSegments;
            var verts = new Vector3[vcount];
            var tris = new int[MajorSegments * MinorSegments * 6];

            for (int i = 0; i < MajorSegments; i++)
            {
                float u = i / (float)MajorSegments * Mathf.PI * 2f;
                float cu = Mathf.Cos(u), su = Mathf.Sin(u);

                // 断面の基底: 半径方向 (cu, su, 0) と平面法線 (0, 0, 1)
                for (int j = 0; j < MinorSegments; j++)
                {
                    float v = j / (float)MinorSegments * Mathf.PI * 2f;
                    float r = 1f + minorRadius * Mathf.Cos(v);
                    verts[i * MinorSegments + j] =
                        new Vector3(cu * r, su * r, minorRadius * Mathf.Sin(v));
                }
            }

            int t = 0;
            for (int i = 0; i < MajorSegments; i++)
            {
                int ni = (i + 1) % MajorSegments;
                for (int j = 0; j < MinorSegments; j++)
                {
                    int nj = (j + 1) % MinorSegments;
                    int a = i * MinorSegments + j;
                    int b = ni * MinorSegments + j;
                    int c = ni * MinorSegments + nj;
                    int d = i * MinorSegments + nj;

                    tris[t++] = a; tris[t++] = b; tris[t++] = c;
                    tris[t++] = a; tris[t++] = c; tris[t++] = d;
                }
            }

            e.Mesh.Clear();
            e.Mesh.vertices = verts;
            e.Mesh.triangles = tris;
            e.Mesh.RecalculateBounds();

            // sharedMesh を入れ直すと PhysX が再クックする。太さ比が変わったときだけ。
            e.MeshCollider.sharedMesh = null;
            e.MeshCollider.sharedMesh = e.Mesh;
        }

        #endregion
    }
}
