using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ToolPosture.Gizmo
{
    /// <summary>
    /// ランタイム用ギズモ形状を、ワールド座標の頂点カラー付きメッシュ 1 枚に積む。
    /// エディタ専用の UnityEditor.Handles が使えないランタイムでの描画手段。
    /// マテリアルは Cull Off なので三角形の巻き方向は気にしない。
    /// </summary>
    public class GizmoMeshBuilder
    {
        private readonly List<Vector3> _verts = new List<Vector3>(2048);
        private readonly List<Color32> _colors = new List<Color32>(2048);
        private readonly List<int> _tris = new List<int>(4096);

        public int VertexCount => _verts.Count;

        public void Clear()
        {
            _verts.Clear();
            _colors.Clear();
            _tris.Clear();
        }

        public void Apply(Mesh mesh)
        {
            mesh.Clear();
            if (_verts.Count == 0) return;

            mesh.indexFormat = _verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_verts);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_tris, 0, true);
        }

        private int Push(Vector3 p, Color32 c)
        {
            _verts.Add(p);
            _colors.Add(c);
            return _verts.Count - 1;
        }

        private void Tri(int a, int b, int c)
        {
            _tris.Add(a);
            _tris.Add(b);
            _tris.Add(c);
        }

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col)
        {
            int i0 = Push(a, col), i1 = Push(b, col), i2 = Push(c, col), i3 = Push(d, col);
            Tri(i0, i1, i2);
            Tri(i0, i2, i3);
        }

        /// <summary>
        /// 平面 (u が 0 度, v が +90 度) 上の、中心から半径 radius・角度 angleDeg の点。
        /// </summary>
        public static Vector3 OnCircle(Vector3 center, Vector3 u, Vector3 v, float radius, float angleDeg)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
        }

        private static int ArcSegments(float a0, float a1)
            => Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 3f), 1, 240);

        /// <summary>
        /// 円弧の帯 (リボン)。
        /// </summary>
        public void AddArcBand(Vector3 center, Vector3 u, Vector3 v, float radius, float halfWidth,
                               float a0Deg, float a1Deg, Color32 col)
        {
            int n = ArcSegments(a0Deg, a1Deg);
            float rIn = radius - halfWidth, rOut = radius + halfWidth;
            for (int s = 0; s < n; s++)
            {
                float t0 = Mathf.Lerp(a0Deg, a1Deg, s / (float)n);
                float t1 = Mathf.Lerp(a0Deg, a1Deg, (s + 1) / (float)n);
                AddQuad(
                    OnCircle(center, u, v, rIn, t0),
                    OnCircle(center, u, v, rOut, t0),
                    OnCircle(center, u, v, rOut, t1),
                    OnCircle(center, u, v, rIn, t1),
                    col);
            }
        }

        /// <summary>
        /// 中心から広がる扇形 (角度の塗りつぶし)。
        /// </summary>
        public void AddSector(Vector3 center, Vector3 u, Vector3 v, float radius,
                              float a0Deg, float a1Deg, Color32 col)
        {
            if (Mathf.Abs(a1Deg - a0Deg) < 0.01f) return;
            int n = ArcSegments(a0Deg, a1Deg);
            int ic = Push(center, col);
            int prev = Push(OnCircle(center, u, v, radius, a0Deg), col);
            for (int s = 1; s <= n; s++)
            {
                float t = Mathf.Lerp(a0Deg, a1Deg, s / (float)n);
                int cur = Push(OnCircle(center, u, v, radius, t), col);
                Tri(ic, prev, cur);
                prev = cur;
            }
        }

        /// <summary>
        /// カメラに正対する板でラインを描く (画面上の太さが一定になる)。
        /// </summary>
        public void AddScreenLine(Vector3 a, Vector3 b, Vector3 camPos, float halfWidth, Color32 col)
        {
            Vector3 dir = b - a;
            if (dir.sqrMagnitude < 1e-12f) return;
            Vector3 side = Vector3.Cross(dir, camPos - a);
            if (side.sqrMagnitude < 1e-14f) return;
            side = side.normalized * halfWidth;
            AddQuad(a - side, a + side, b + side, b - side, col);
        }

        /// <summary>
        /// 破線。
        /// </summary>
        public void AddScreenDashedLine(Vector3 a, Vector3 b, Vector3 camPos, float halfWidth,
                                        float dashLength, Color32 col)
        {
            float len = Vector3.Distance(a, b);
            if (len < 1e-6f || dashLength <= 1e-6f) return;
            int n = Mathf.Clamp(Mathf.CeilToInt(len / dashLength), 1, 256);
            for (int s = 0; s < n; s += 2)
            {
                float t0 = s / (float)n;
                float t1 = Mathf.Min((s + 1) / (float)n, 1f);
                AddScreenLine(Vector3.Lerp(a, b, t0), Vector3.Lerp(a, b, t1), camPos, halfWidth, col);
            }
        }

        /// <summary>
        /// 円錐 (矢印の頭)。
        /// </summary>
        public void AddCone(Vector3 baseCenter, Vector3 dir, float radius, float height, Color32 col, int segments = 16)
        {
            dir = dir.normalized;
            Vector3 seed = Mathf.Abs(dir.y) > 0.9f ? Vector3.right : Vector3.up;
            Vector3 u = Vector3.Cross(dir, seed).normalized;
            Vector3 v = Vector3.Cross(dir, u);

            int it = Push(baseCenter + dir * height, col);
            int ic = Push(baseCenter, col);
            int prev = Push(baseCenter + u * radius, col);
            for (int s = 1; s <= segments; s++)
            {
                float a = 360f * s / segments * Mathf.Deg2Rad;
                int cur = Push(baseCenter + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius, col);
                Tri(it, prev, cur);
                Tri(ic, cur, prev);
                prev = cur;
            }
        }

        /// <summary>
        /// カメラに正対する円板 (ノブ / ハンドルの掴み点)。
        /// </summary>
        public void AddBillboardDisc(Vector3 center, Camera cam, float radius, Color32 col, int segments = 24)
        {
            if (cam == null) return;
            Vector3 u = cam.transform.right, v = cam.transform.up;
            int ic = Push(center, col);
            int prev = Push(center + u * radius, col);
            for (int s = 1; s <= segments; s++)
            {
                float a = 360f * s / segments * Mathf.Deg2Rad;
                int cur = Push(center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius, col);
                Tri(ic, prev, cur);
                prev = cur;
            }
        }

        /// <summary>
        /// カメラに正対するリング (輪郭のみ)。
        /// </summary>
        public void AddBillboardRing(Vector3 center, Camera cam, float radius, float halfWidth, Color32 col, int segments = 32)
        {
            if (cam == null) return;
            Vector3 u = cam.transform.right, v = cam.transform.up;
            AddArcBandSegments(center, u, v, radius, halfWidth, 0f, 360f, col, segments);
        }

        private void AddArcBandSegments(Vector3 center, Vector3 u, Vector3 v, float radius, float halfWidth,
                                float a0, float a1, Color32 col, int n)
        {
            float rIn = radius - halfWidth, rOut = radius + halfWidth;
            for (int s = 0; s < n; s++)
            {
                float t0 = Mathf.Lerp(a0, a1, s / (float)n);
                float t1 = Mathf.Lerp(a0, a1, (s + 1) / (float)n);
                AddQuad(
                    OnCircle(center, u, v, rIn, t0),
                    OnCircle(center, u, v, rOut, t0),
                    OnCircle(center, u, v, rOut, t1),
                    OnCircle(center, u, v, rIn, t1),
                    col);
            }
        }

        /// <summary>
        /// 円弧上の目盛り (半径方向の短い線)。
        /// </summary>
        public void AddRadialTick(Vector3 center, Vector3 u, Vector3 v, float radius, float angleDeg,
                                  float length, float halfWidth, Vector3 camPos, Color32 col)
        {
            Vector3 p0 = OnCircle(center, u, v, radius - length * 0.5f, angleDeg);
            Vector3 p1 = OnCircle(center, u, v, radius + length * 0.5f, angleDeg);
            AddScreenLine(p0, p1, camPos, halfWidth, col);
        }

        /// <summary>
        /// 矢印付きの軸。
        /// </summary>
        public void AddArrow(Vector3 origin, Vector3 dir, float length, Vector3 camPos,
                             float lineHalfWidth, float headRadius, float headLength, Color32 col)
        {
            dir = dir.normalized;
            Vector3 tip = origin + dir * length;
            Vector3 headBase = tip - dir * headLength;
            AddScreenLine(origin, headBase, camPos, lineHalfWidth, col);
            AddCone(headBase, dir, headRadius, headLength, col);
        }

        public static Color32 Fade(Color c, float alphaScale)
            => (Color32)new Color(c.r, c.g, c.b, c.a * alphaScale);
    }
}
