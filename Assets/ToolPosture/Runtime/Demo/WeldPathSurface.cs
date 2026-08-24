using System.Collections.Generic;
using UnityEngine;
using ToolPosture.Core;

namespace ToolPosture.Demo
{
    /// <summary>
    /// WeldPath のフレームを使って母材面を生成する。
    /// 各サンプル点で L 方向 (cross-feed) に幅を持たせた帯を張るので、
    /// 生成された面は各点の LM 平面そのものになる。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Tool Posture/Weld Path Surface")]
    public class WeldPathSurface : MonoBehaviour
    {
        public WeldPath path;

        [Tooltip("溶接線から左右への幅")]
        public float halfWidth = 0.5f;

        [Tooltip("区間あたりの分割数")]
        [Range(1, 32)] public int subdivisions = 10;

        [Tooltip("溶接線が面に埋まらないよう法線方向に下げる量")]
        public float sinkDepth = 0.012f;

        private Mesh _mesh;
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<int> _tris = new List<int>();

        private void OnEnable() => Rebuild();

        private void Update()
        {
            if (!Application.isPlaying) Rebuild();
        }

        [ContextMenu("再生成")]
        public void Rebuild()
        {
            if (path == null || path.SegmentCount == 0) return;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "WeldPathSurface", hideFlags = HideFlags.DontSave };
                GetComponent<MeshFilter>().sharedMesh = _mesh;
            }

            _verts.Clear();
            _normals.Clear();
            _tris.Clear();

            int perSegment = Mathf.Max(1, subdivisions);
            int sampleCount = path.SegmentCount * perSegment + 1;
            Matrix4x4 toLocal = transform.worldToLocalMatrix;

            for (int k = 0; k < sampleCount; k++)
            {
                int segment = Mathf.Min(k / perSegment, path.SegmentCount - 1);
                float u = (k - segment * perSegment) / (float)perSegment;

                PathFrame f = path.GetFrame(segment, u);
                Vector3 center = f.Origin - f.Normal * sinkDepth;

                _verts.Add(toLocal.MultiplyPoint3x4(center - f.CrossFeed * halfWidth));
                _verts.Add(toLocal.MultiplyPoint3x4(center + f.CrossFeed * halfWidth));

                Vector3 n = toLocal.MultiplyVector(f.Normal).normalized;
                _normals.Add(n);
                _normals.Add(n);
            }

            for (int k = 0; k < sampleCount - 1; k++)
            {
                int a = k * 2, b = a + 1, c = a + 2, d = a + 3;
                _tris.Add(a); _tris.Add(c); _tris.Add(b);
                _tris.Add(b); _tris.Add(c); _tris.Add(d);
                // 裏側からも見えるように逆巻きも張る
                _tris.Add(a); _tris.Add(b); _tris.Add(c);
                _tris.Add(b); _tris.Add(d); _tris.Add(c);
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_normals);
            _mesh.SetTriangles(_tris, 0, true);
            _mesh.RecalculateBounds();
        }
    }
}
