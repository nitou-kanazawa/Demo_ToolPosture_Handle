using System.Collections.Generic;
using UnityEngine;

namespace ToolPosture.Gizmo
{
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
    public class GizmoHandleColliders
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
        public void Sync(ToolPostureGizmo gizmo, IList<GizmoHandleBase> handles, GizmoHandleBase active)
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

                bool on = e.Handle.Visible &&
                          (active == null || !gizmo.hideOthersWhileDragging || e.Handle == active);
                if (!on)
                {
                    if (e.Collider != null) e.Collider.enabled = false;
                    continue;
                }

                if (shape.Radius < 1e-6f)
                {
                    if (e.Collider != null) e.Collider.enabled = false;
                    continue;
                }

                if (shape.Kind == GizmoShapeKind.Arc)
                    SyncArc(e, shape, tube, parentScale);
                else
                    SyncSphere(e, shape, parentScale);

                e.Collider.enabled = true;
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
                            out GizmoHandleBase handle, out Vector3 point)
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

            return handle != null;
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

        private void EnsureRoot(ToolPostureGizmo gizmo)
        {
            if (_root != null) return;

            var go = new GameObject("ToolPostureHandleColliders");
            go.transform.SetParent(gizmo.transform, false);

            // Ignore Raycast レイヤー。シーンの Physics.Raycast の既定マスクから外れる。
            go.layer = 2;
            go.hideFlags = HideFlags.DontSave;
            _root = go.transform;
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

                if (handles[i].GetShape().Kind == GizmoShapeKind.Sphere)
                {
                    var sc = go.AddComponent<SphereCollider>();
                    sc.radius = 1f;
                    e.Collider = sc;
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
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
        }

        private static void Destroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
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
