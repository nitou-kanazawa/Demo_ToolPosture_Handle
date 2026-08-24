using System.Collections.Generic;
using UnityEngine;
using ToolPosture.Core;

namespace ToolPosture.Demo
{
    /// <summary>
    /// デモ用のダミー溶接経路。点列と各点の生の法線を保持し、
    /// 区間 p(i) -> p(i+1) 上の任意位置でフレーム (L, M, N) を返す。
    ///
    /// 本ツールの外部インターフェースはここだけ。実データ構造がある場合は
    /// GetFrame を同じシグネチャで実装したアダプタに差し替えればよい。
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Tool Posture/Weld Path")]
    public class WeldPath : MonoBehaviour, IPathFrameSource
    {
        [SerializeField] private List<Vector3> points = new List<Vector3>();

        [Tooltip("各点の生の法線 (進行方向と直交していなくてよい。フレーム構築時に直交化される)")]
        [SerializeField] private List<Vector3> normals = new List<Vector3>();

        [Tooltip("L を進行方向のどちら側に取るか")]
        public CrossFeedSide crossFeedSide = CrossFeedSide.RightOfTravel;

        [Header("シーンビュー表示")]
        public bool drawPathGizmo = true;
        public float normalGizmoLength = 0.35f;

        public int PointCount => points.Count;
        public int SegmentCount => Mathf.Max(0, points.Count - 1);

        private void Reset() => BuildDefaultPath();

        private void OnValidate()
        {
            // 法線の数を点の数に合わせる
            while (normals.Count < points.Count) normals.Add(Vector3.up);
            while (normals.Count > points.Count) normals.RemoveAt(normals.Count - 1);
        }

        [ContextMenu("ダミー経路を生成")]
        public void BuildDefaultPath()
        {
            points = new List<Vector3>
            {
                new Vector3(-2.0f, 0.00f,  0.00f),
                new Vector3(-1.0f, 0.00f,  0.35f),
                new Vector3( 0.0f, 0.15f,  0.50f),
                new Vector3( 1.0f, 0.30f,  0.35f),
                new Vector3( 2.0f, 0.35f,  0.00f),
            };

            // わざと進行方向と直交していない法線を混ぜてある (直交化の確認用)
            normals = new List<Vector3>
            {
                new Vector3(0.00f, 1f,  0.00f),
                new Vector3(0.10f, 1f,  0.00f),
                new Vector3(0.22f, 1f, -0.05f),
                new Vector3(0.35f, 1f, -0.12f),
                new Vector3(0.45f, 1f, -0.20f),
            };
            for (int i = 0; i < normals.Count; i++) normals[i] = normals[i].normalized;
        }

        public Vector3 GetWorldPoint(int index)
            => transform.TransformPoint(points[Mathf.Clamp(index, 0, points.Count - 1)]);

        public Vector3 GetWorldNormal(int index)
            => transform.TransformDirection(normals[Mathf.Clamp(index, 0, normals.Count - 1)]);

        /// <summary>
        /// 区間 segment (0 起点) の位置 u (0..1) におけるフレームを返す。
        /// 区間内では M は一定 (直線区間)、N は端点の法線を球面補間する。
        /// </summary>
        public PathFrame GetFrame(int segment, float u)
        {
            if (SegmentCount == 0 || normals.Count < points.Count)
                return PathFrame.Fallback(transform.position);

            segment = Mathf.Clamp(segment, 0, SegmentCount - 1);
            u = Mathf.Clamp01(u);

            Vector3 p0 = GetWorldPoint(segment);
            Vector3 p1 = GetWorldPoint(segment + 1);
            Vector3 n0 = GetWorldNormal(segment).normalized;
            Vector3 n1 = GetWorldNormal(segment + 1).normalized;

            Vector3 origin = Vector3.Lerp(p0, p1, u);
            Vector3 rawNormal = Vector3.Slerp(n0, n1, u);

            if (PathFrame.TryCreate(origin, p1 - p0, rawNormal, crossFeedSide, out var frame))
                return frame;
            return PathFrame.Fallback(origin);
        }

        private void OnDrawGizmos()
        {
            if (!drawPathGizmo || points.Count < 2 || normals.Count < points.Count) return;

            Gizmos.color = new Color(1f, 1f, 1f, 0.65f);
            for (int i = 0; i < points.Count - 1; i++)
                Gizmos.DrawLine(GetWorldPoint(i), GetWorldPoint(i + 1));

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = GetWorldPoint(i);
                Gizmos.color = new Color(1f, 1f, 1f, 0.85f);
                Gizmos.DrawSphere(p, 0.03f);
                Gizmos.color = new Color(0.35f, 0.65f, 1f, 0.8f);
                Gizmos.DrawLine(p, p + GetWorldNormal(i).normalized * normalGizmoLength);
            }
        }
    }
}
