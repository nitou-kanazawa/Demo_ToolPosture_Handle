using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ToolPosture.Core;
using ToolPosture.Gizmo;

namespace ToolPosture.Demo
{
    /// <summary>
    /// デモ用のダミー溶接経路。点列と各点の生の法線を保持し、
    /// 区間 p(i) -&gt; p(i+1) 上の任意位置でフレーム (L, M, N) を返す。
    ///
    /// フレームの計算はこちら側の責務で、ギズモはその結果を受け取るだけ。
    /// 実データ構造がある場合は GetFrame を同じシグネチャで実装したアダプタに
    /// 差し替えればよい。
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]     // ギズモより先にフレームを確定させる
    [AddComponentMenu("Tool Posture/Weld Path")]
    public class WeldPath : MonoBehaviour, IPathFrameSource
    {
        #region 経路データ

        [SerializeField] private List<Vector3> points = new List<Vector3>();

        [Tooltip("各点の生の法線 (進行方向と直交していなくてよい。フレーム構築時に直交化される)")]
        [SerializeField] private List<Vector3> normals = new List<Vector3>();

        [Tooltip("L を進行方向のどちら側に取るか")]
        public CrossFeedSide crossFeedSide = CrossFeedSide.RightOfTravel;

        public int PointCount => points.Count;
        public int SegmentCount => Mathf.Max(0, points.Count - 1);

        #endregion

        #region ギズモの駆動

        [Header("ギズモの駆動")]
        [Tooltip("この経路上にギズモを乗せる。フレームは毎フレームここから供給される")]
        public ToolPostureGizmo gizmo;

        [Tooltip("ギズモを乗せる区間 (0 起点)")]
        public int segmentIndex = 2;

        [Tooltip("区間内の位置")]
        [Range(0f, 1f)] public float segmentU = 0.5f;

        [Tooltip("実行中に矢印キーで区間と位置を動かす")]
        public bool useKeyboardShortcuts = true;

        [Tooltip("1 回のキー入力で動く区間内の位置")]
        public float uStep = 0.1f;

        #endregion

        #region 表示

        [Header("表示")]
        public bool drawPathGizmo = true;
        public float normalGizmoLength = 0.35f;

        [Tooltip("実行中もギズモのメッシュに乗せて経路を描く")]
        public bool drawPathAtRuntime = true;

        public Color pathColor = new Color(0.85f, 0.87f, 0.92f, 0.70f);
        public Color pathNormalColor = new Color(0.36f, 0.64f, 1.00f, 0.45f);

        #endregion

        #region ライフサイクル

        private void Reset() => BuildDefaultPath();

        private void OnValidate()
        {
            // 法線の数を点の数に合わせる
            while (normals.Count < points.Count) normals.Add(Vector3.up);
            while (normals.Count > points.Count) normals.RemoveAt(normals.Count - 1);

            segmentIndex = Mathf.Clamp(segmentIndex, 0, Mathf.Max(0, SegmentCount - 1));
        }

        private void OnEnable()
        {
            if (gizmo == null) return;
            gizmo.PreparingFrame += PushFrame;
            gizmo.BuildingExtraGeometry += DrawPath;
        }

        private void OnDisable()
        {
            if (gizmo == null) return;
            gizmo.PreparingFrame -= PushFrame;
            gizmo.BuildingExtraGeometry -= DrawPath;
        }

        private void Update()
        {
            if (gizmo == null) return;

            if (Application.isPlaying && useKeyboardShortcuts) HandleKeyboard();
            PushFrame(gizmo);
        }

        /// <summary>
        /// 現在の区間・位置のフレームをギズモへ渡す。
        ///
        /// Update からだけでなく描画直前にも呼ばれる。編集中はエディタが Update を
        /// 回さないことがあり、それだけに頼るとギズモがフォールバック位置に出てしまう。
        /// </summary>
        private void PushFrame(ToolPostureGizmo target)
        {
            segmentIndex = Mathf.Clamp(segmentIndex, 0, Mathf.Max(0, SegmentCount - 1));
            target.Frame = GetFrame(segmentIndex, segmentU);
        }

        private void HandleKeyboard()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || SegmentCount == 0) return;

            if (kb.leftArrowKey.wasPressedThisFrame) segmentIndex = Mathf.Max(0, segmentIndex - 1);
            if (kb.rightArrowKey.wasPressedThisFrame)
                segmentIndex = Mathf.Min(SegmentCount - 1, segmentIndex + 1);

            if (kb.upArrowKey.wasPressedThisFrame) segmentU = Mathf.Clamp01(segmentU + uStep);
            if (kb.downArrowKey.wasPressedThisFrame) segmentU = Mathf.Clamp01(segmentU - uStep);
        }

        #endregion

        #region フレーム

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

        #endregion

        #region 描画

        /// <summary>
        /// ギズモのメッシュに経路の線を足す。OnDrawGizmos はシーンビュー限定なので
        /// 実行時の表示はこちらで行う。
        /// </summary>
        private void DrawPath(GizmoMeshBuilder b)
        {
            if (!drawPathAtRuntime || gizmo == null || points.Count < 2) return;

            Camera cam = gizmo.Cam;
            if (cam == null) return;

            Vector3 camPos = gizmo.EyePosition;
            float lineHalf = gizmo.PixelToWorld(1.4f);

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = GetWorldPoint(i);

                if (i + 1 < points.Count)
                    b.AddScreenLine(p, GetWorldPoint(i + 1), camPos, lineHalf, pathColor);

                b.AddScreenDashedLine(p, p + GetWorldNormal(i).normalized * normalGizmoLength,
                                      camPos, gizmo.PixelToWorld(1f), gizmo.PixelToWorld(7f),
                                      pathNormalColor);
                b.AddBillboardDisc(p, cam, gizmo.PixelToWorld(3f), pathColor);
            }
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

        #endregion
    }
}
