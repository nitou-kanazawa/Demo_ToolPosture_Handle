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

        private static readonly string[] PostureProps = { "angles" };

        private static readonly string[] HandleProps =
        {
            "showTiltArc", "showAzimuthRing", "showAxisTip", "showSpinRing",
            "showFrameAxes", "hideOthersWhileDragging",
        };

        private static readonly string[] ViewProps =
        {
            "targetCamera", "restrictToTargetCamera", "colliderGizmo",
        };

        private static readonly string[] InputProps = { "inputMode", "useKeyboardShortcuts" };

        private static readonly string[] ToolProps =
        {
            "gizmoShader", "toolVisual", "toolShaftAxis", "toolReferenceAxis",
        };

        private Editor _themeEditor;
        private Editor _profileEditor;

        private void OnDisable()
        {
            if (_themeEditor != null) DestroyImmediate(_themeEditor);
            if (_profileEditor != null) DestroyImmediate(_profileEditor);
        }

        public override void OnInspectorGUI()
        {
            var g = (ToolPostureGizmo)target;
            serializedObject.Update();

            EditorGUILayout.LabelField("プリセット", EditorStyles.boldLabel);
            DrawPresetSlot<GizmoTheme>("theme", "見た目 (Theme)", ref _themeEditor);
            DrawPresetSlot<ToolPostureProfile>("profile", "規約 (Profile)", ref _profileEditor);

            EditorGUILayout.Space();
            DrawGroup("姿勢", PostureProps);
            DrawGroup("ハンドル表示", HandleProps);
            DrawGroup("表示", ViewProps);
            DrawGroup("入力", InputProps, defaultOpen: false);
            DrawGroup("シェーダ / 工具モデル", ToolProps, defaultOpen: false);
            DrawRemaining();

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("出力 (読み取り専用)", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                var a = g.Angles;
                EditorGUILayout.Vector3Field("工具軸 X (LMN)", a.GetAxisLmn());
                EditorGUILayout.Vector3Field("工具軸 X (world)", g.ToolAxisWorld);
                EditorGUILayout.LabelField("導出値 (投影角)",
                    $"w {g.Profile.workConvention.ToDisplay(a.WorkAngleDeg):F2}°   " +
                    $"t {g.Profile.travelConvention.ToDisplay(a.TravelAngleDeg):F2}°   " +
                    $"spin {g.Profile.spinConvention.ToDisplay(a.spinAngleDeg):F2}°");
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

        #region インスペクタのヘルパ

        /// <summary>
        /// 折りたたみの開閉。状態は EditorPrefs に残す。
        /// </summary>
        private static bool Foldout(string title, bool defaultOpen)
        {
            string key = "ToolPostureGizmo.fold." + title;
            bool open = EditorPrefs.GetBool(key, defaultOpen);
            bool now = EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
            if (now != open) EditorPrefs.SetBool(key, now);
            return now;
        }

        private void DrawGroup(string title, string[] props, bool defaultOpen = true)
        {
            if (!Foldout(title, defaultOpen)) return;

            using (new EditorGUI.IndentLevelScope())
            {
                foreach (string name in props)
                {
                    SerializedProperty p = serializedObject.FindProperty(name);
                    if (p != null) EditorGUILayout.PropertyField(p, true);
                }
            }
        }

        /// <summary>
        /// どのグループにも入れていないプロパティを拾う。
        /// フィールドを足したときにインスペクタから消えるのを防ぐための保険。
        /// </summary>
        private void DrawRemaining()
        {
            SerializedProperty it = serializedObject.GetIterator();
            bool header = false;

            for (bool enterChildren = true; it.NextVisible(enterChildren); enterChildren = false)
            {
                if (it.propertyPath == "m_Script") continue;
                if (it.propertyPath == "theme" || it.propertyPath == "profile") continue;
                if (System.Array.IndexOf(PostureProps, it.propertyPath) >= 0) continue;
                if (System.Array.IndexOf(HandleProps, it.propertyPath) >= 0) continue;
                if (System.Array.IndexOf(ViewProps, it.propertyPath) >= 0) continue;
                if (System.Array.IndexOf(InputProps, it.propertyPath) >= 0) continue;
                if (System.Array.IndexOf(ToolProps, it.propertyPath) >= 0) continue;

                if (!header)
                {
                    header = true;
                    EditorGUILayout.LabelField("その他", EditorStyles.boldLabel);
                }
                EditorGUILayout.PropertyField(it, true);
            }
        }

        /// <summary>
        /// プリセットアセットの割当欄。未設定なら組み込み既定を使っている旨を出し、
        /// 割り当て済みならその中身をここで直接編集できるようにする。
        /// </summary>
        private void DrawPresetSlot<T>(string propName, string label, ref Editor cached)
            where T : ScriptableObject
        {
            SerializedProperty prop = serializedObject.FindProperty(propName);
            if (prop == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(prop, new GUIContent(label));
                if (GUILayout.Button("新規", GUILayout.Width(44f)))
                {
                    T created = CreateAsset<T>();
                    if (created != null) prop.objectReferenceValue = created;
                }
            }

            var asset = prop.objectReferenceValue as T;
            if (asset == null)
            {
                EditorGUILayout.LabelField(" ", "組み込み既定を使用中", EditorStyles.miniLabel);
                if (cached != null) { DestroyImmediate(cached); cached = null; }
                return;
            }

            string key = "ToolPostureGizmo.inline." + propName;
            bool open = EditorGUILayout.Foldout(EditorPrefs.GetBool(key, false), "中身を編集", true);
            EditorPrefs.SetBool(key, open);
            if (!open)
            {
                if (cached != null) { DestroyImmediate(cached); cached = null; }
                return;
            }

            if (cached == null || cached.target != asset)
            {
                if (cached != null) DestroyImmediate(cached);
                cached = CreateEditor(asset);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                cached.OnInspectorGUI();
        }

        private static T CreateAsset<T>() where T : ScriptableObject
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "アセットを作成", typeof(T).Name, "asset", "");
            if (string.IsNullOrEmpty(path)) return null;

            var asset = CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
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
            Handles.color = g.Theme.axisColor;
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
                                       alpha, g.Profile.tiltConvention, g.Theme.tiltColor, ref changed);

                if (!Mathf.Approximately(edited, alpha))
                {
                    float limit = Mathf.Atan(TiltLimits.MaxTanTilt(g, azimuth)) * Mathf.Rad2Deg;
                    angles.TiltFromNormalDeg =
                        Mathf.Clamp(g.Profile.tiltConvention.ClampInternal(edited), -limit, limit);
                }
            }

            if (g.showAzimuthRing)
            {
                bool affects = angles.TiltIsSignificant();
                Vector3 u = f.CrossFeed;   // 0 度 = L
                Vector3 v = f.Feed;        // +90 度 = M
                float r = scale * AzimuthRingRadiusScale;

                Handles.color = new Color(g.Theme.azimuthColor.r, g.Theme.azimuthColor.g, g.Theme.azimuthColor.b,
                                          affects ? 0.55f : 0.20f);
                Handles.DrawWireDisc(f.Origin, f.Normal, r);

                // 傾き 0 でも編集できる。旋回角は姿勢の中に保持されるので、
                // 次に倒す向きを先に決められる。
                float na = EditArc(g, f.Origin, u, v, r, angles.azimuthDeg, g.Profile.azimuthConvention,
                                   g.Theme.azimuthColor, ref changed);
                angles.azimuthDeg = na;
            }

            if (g.showSpinRing)
            {
                Vector3 spinAxis = angles.GetAxisWorld(f);
                Vector3 u = g.Profile.spinReference.Resolve(f, spinAxis);
                Vector3 v = Vector3.Cross(spinAxis, u);
                Vector3 center = f.Origin + spinAxis * (scale * 0.86f);

                float s = EditArc(g, center, u, v, scale * SpinRingRadiusScale,
                                  angles.spinAngleDeg, g.Profile.spinConvention, g.Theme.spinColor, ref changed);
                angles.spinAngleDeg = g.Profile.spinConvention.ClampInternal(s);
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
            DrawAxis(f.Origin, f.CrossFeed, scale * 0.95f, g.Theme.frameColorL, "L");
            DrawAxis(f.Origin, f.Feed, scale * 1.25f, g.Theme.frameColorM, "M");
            DrawAxis(f.Origin, f.Normal, scale * 1.25f, g.Theme.frameColorN, "N");
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
            conv.GetArcRange(g.Theme.fallbackArcHalfWidthDeg, out float lo, out float hi);
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
                $"w {g.Profile.workConvention.ToDisplay(a.WorkAngleDeg):F1}°\n" +
                $"t {g.Profile.travelConvention.ToDisplay(a.TravelAngleDeg):F1}°\n" +
                $"spin {g.Profile.spinConvention.ToDisplay(a.spinAngleDeg):F1}°";

            Handles.Label(f.Origin + f.Normal * scale * 1.45f, text, style);

        #endregion

        }
    }
}
