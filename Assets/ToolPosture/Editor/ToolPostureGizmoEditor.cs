using UnityEditor;
using UnityEngine;
using ToolPosture.Core;
using ToolPosture.Gizmo;

namespace ToolPosture.EditorTools
{
    /// <summary>
    /// シーンビュー用のギズモ。ランタイム版と同じ数学 (投影角) を使うが、
    /// 描画と当たり判定は UnityEditor.Handles に任せる。
    /// ランタイム版の挙動を編集時に確認する用途と、非実行時の姿勢調整に使う。
    /// </summary>
    [CustomEditor(typeof(ToolPostureGizmo))]
    public class ToolPostureGizmoEditor : Editor
    {
        #region 定数

        private const float WorkArcRadiusScale = 0.74f;
        private const float TravelArcRadiusScale = 1.0f;
        private const float SpinRingRadiusScale = 0.50f;
        private const float AzimuthRingRadiusScale = 1.42f;

        #endregion

        #region インスペクタ

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var g = (ToolPostureGizmo)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("出力 (読み取り専用)", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                var a = g.Angles;
                EditorGUILayout.Vector3Field("工具軸 X (LMN)", a.GetAxisLmn());
                EditorGUILayout.Vector3Field("工具軸 X (world)", g.ToolAxisWorld);
                EditorGUILayout.LabelField("導出値 (投影角)",
                    $"w {g.workConvention.ToDisplay(a.WorkAngleDeg):F2}°   " +
                    $"t {g.travelConvention.ToDisplay(a.TravelAngleDeg):F2}°   " +
                    $"spin {g.spinConvention.ToDisplay(a.spinAngleDeg):F2}°");
                EditorGUILayout.LabelField("N からの傾き α",
                    a.TiltIsSignificant()
                        ? $"{a.TiltFromNormalDeg:F2}°"
                        : $"{a.TiltFromNormalDeg:F2}°（θ は工具軸に効いていない）");
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("垂直姿勢へ (φ = 90°)"))
                {
                    Undo.RecordObject(g, "Reset tool posture");
                    var a = g.Angles;
                    a.elevationDeg = 90f;    // 旋回角はそのまま残る
                    g.Angles = a;
                    EditorUtility.SetDirty(g);
                }
                if (GUILayout.Button("スピンを 0 に"))
                {
                    Undo.RecordObject(g, "Reset tool spin");
                    var a = g.Angles;
                    a.spinAngleDeg = 0f;
                    g.Angles = a;
                    EditorUtility.SetDirty(g);
                }
            }
        }

        #endregion

        #region シーンビュー

        private void OnSceneGUI()
        {
            var g = (ToolPostureGizmo)target;

            PathFrame f = g.Frame;
            if (!f.IsValid) return;

            float scale = HandleUtility.GetHandleSize(f.Origin) * 0.9f;
            var angles = g.Angles;

            DrawFrameAxes(g, f, scale);

            Vector3 axis = angles.GetAxisWorld(f);
            Handles.color = g.axisColor;
            Handles.DrawAAPolyLine(4f, f.Origin, f.Origin + axis * scale * 1.25f);

            bool changed = false;

            if (g.showTiltArc)
            {
                // N と現在の工具軸が張る平面 (旋回角に追従) で N からの傾きを編集する
                float azimuth = angles.azimuthDeg;
                float ar = azimuth * Mathf.Deg2Rad;
                Vector3 tiltDir = (f.CrossFeed * Mathf.Cos(ar) + f.Feed * Mathf.Sin(ar)).normalized;

                float alpha = angles.TiltFromNormalDeg;
                float edited = EditArc(g, f.Origin, f.Normal, tiltDir, scale * WorkArcRadiusScale,
                                       alpha, g.tiltConvention, g.tiltColor, ref changed);

                if (!Mathf.Approximately(edited, alpha))
                {
                    float limit = Mathf.Atan(TiltLimits.MaxTanTilt(g, azimuth)) * Mathf.Rad2Deg;
                    angles.TiltFromNormalDeg =
                        Mathf.Clamp(g.tiltConvention.ClampInternal(edited), -limit, limit);
                }
            }

            if (g.showAzimuthRing)
            {
                bool affects = angles.TiltIsSignificant();
                Vector3 u = f.CrossFeed;   // 0 度 = L
                Vector3 v = f.Feed;        // +90 度 = M
                float r = scale * AzimuthRingRadiusScale;

                Handles.color = new Color(g.azimuthColor.r, g.azimuthColor.g, g.azimuthColor.b,
                                          affects ? 0.55f : 0.20f);
                Handles.DrawWireDisc(f.Origin, f.Normal, r);

                // 傾き 0 でも編集できる。旋回角は姿勢の中に保持されるので、
                // 次に倒す向きを先に決められる。
                float na = EditArc(g, f.Origin, u, v, r, angles.azimuthDeg, g.azimuthConvention,
                                   g.azimuthColor, ref changed);
                angles.azimuthDeg = na;
            }

            if (g.showSpinRing)
            {
                Vector3 spinAxis = angles.GetAxisWorld(f);
                Vector3 u = g.spinReference.Resolve(f, spinAxis);
                Vector3 v = Vector3.Cross(spinAxis, u);
                Vector3 center = f.Origin + spinAxis * (scale * 0.86f);

                float s = EditArc(g, center, u, v, scale * SpinRingRadiusScale,
                                  angles.spinAngleDeg, g.spinConvention, g.spinColor, ref changed);
                angles.spinAngleDeg = g.spinConvention.ClampInternal(s);
            }

            if (changed)
            {
                Undo.RecordObject(g, "Edit tool posture");
                g.Angles = angles;
                EditorUtility.SetDirty(g);
            }

            DrawLabel(g, f, angles, scale);
        }

        #endregion

        #region 描画ヘルパ

        private static void DrawFrameAxes(ToolPostureGizmo g, PathFrame f, float scale)
        {
            DrawAxis(f.Origin, f.CrossFeed, scale * 0.95f, g.frameColorL, "L");
            DrawAxis(f.Origin, f.Feed, scale * 1.25f, g.frameColorM, "M");
            DrawAxis(f.Origin, f.Normal, scale * 1.25f, g.frameColorN, "N");
        }

        private static void DrawAxis(Vector3 origin, Vector3 dir, float length, Color color, string label)
        {
            Handles.color = color;
            Vector3 tip = origin + dir * length;
            Handles.DrawAAPolyLine(3f, origin, tip);
            Handles.ConeHandleCap(0, tip, Quaternion.LookRotation(dir), length * 0.12f, EventType.Repaint);
            Handles.Label(tip + dir * (length * 0.12f), label);
        }

        /// <summary>
        /// 円弧の描画とノブによる編集。戻り値は編集後の角度 (内部値)。
        /// </summary>
        private static float EditArc(ToolPostureGizmo g, Vector3 center, Vector3 u, Vector3 v, float radius,
                             float angleDeg, AngleConvention conv, Color color, ref bool changed)
        {
            conv.GetArcRange(g.fallbackArcHalfWidthDeg, out float lo, out float hi);
            Vector3 normal = Vector3.Cross(u, v);

            Handles.color = new Color(color.r, color.g, color.b, 0.14f);
            Handles.DrawSolidArc(center, normal, u, angleDeg, radius);

            Handles.color = new Color(color.r, color.g, color.b, 0.55f);
            Handles.DrawWireArc(center, normal, u, hi - lo, radius);
            Handles.DrawWireArc(center, normal,
                                Quaternion.AngleAxis(lo, normal) * u, hi - lo, radius);

            Handles.color = Color.white;
            Handles.DrawAAPolyLine(2f, center, center + u * radius * 1.12f);

            Vector3 knob = center + (u * Mathf.Cos(angleDeg * Mathf.Deg2Rad) +
                                     v * Mathf.Sin(angleDeg * Mathf.Deg2Rad)) * radius;
            Handles.color = color;
            float size = HandleUtility.GetHandleSize(knob) * 0.07f;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(knob, size, Vector3.zero, Handles.SphereHandleCap);
            if (!EditorGUI.EndChangeCheck()) return angleDeg;

            changed = true;
            Vector3 d = moved - center;
            float raw = Mathf.Atan2(Vector3.Dot(d, v), Vector3.Dot(d, u)) * Mathf.Rad2Deg;
            return Event.current.control ? conv.SnapInternal(raw) : raw;
        }

        private static void DrawLabel(ToolPostureGizmo g, PathFrame f, ToolPostureAngles a, float scale)
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Color.white },
                richText = true,
            };

            string text =
                $"w {g.workConvention.ToDisplay(a.WorkAngleDeg):F1}°\n" +
                $"t {g.travelConvention.ToDisplay(a.TravelAngleDeg):F1}°\n" +
                $"spin {g.spinConvention.ToDisplay(a.spinAngleDeg):F1}°";

            Handles.Label(f.Origin + f.Normal * scale * 1.45f, text, style);

        #endregion

        }
    }
}
