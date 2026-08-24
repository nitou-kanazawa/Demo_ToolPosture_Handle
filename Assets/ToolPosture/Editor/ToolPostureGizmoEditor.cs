using System.Collections.Generic;
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

        /// <summary>
        /// ParticleSystem のモジュール一覧と同じ形で並べるハンドルの表示切替。
        /// 添字は HandleLabels と対応させること。
        /// </summary>
        private static readonly string[] HandleProps =
        {
            "showTiltArc", "showAzimuthRing", "showAxisTip", "showSpinRing", "showFrameAxes",
        };

        private static readonly string[] HandleLabels =
        {
            "傾斜角の円弧   φ", "旋回リング   θ", "軸先端ボール", "スピンリング   spin",
            "LMN フレームの軸",
        };

        private static readonly string[] ViewProps =
        {
            "targetCamera", "restrictToTargetCamera", "colliderGizmo",
        };

        private static readonly string[] InputProps = { "inputMode", "useKeyboardShortcuts" };

        private static readonly string[] ToolProps =
        {
            "gizmoShader",
        };

        private readonly HashSet<string> _drawn = new HashSet<string>();

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
            _drawn.Clear();

            DrawPresets();
            DrawGroup("姿勢   θ / φ / spin", PostureProps);
            DrawHandleModules();
            DrawGroup("表示", ViewProps);
            DrawGroup("入力", InputProps, defaultOpen: false);
            DrawGroup("シェーダ", ToolProps, defaultOpen: false);
            DrawRemaining();

            serializedObject.ApplyModifiedProperties();

            DrawReadout(g);
            DrawActions(g);
        }

        private void DrawPresets()
        {
            // 畳んでいても「その他」へ落ちないよう、先に扱い済みとして記録する
            _drawn.Add("theme");
            _drawn.Add("profile");

            if (!Section("プリセット", true)) return;

            using (ShurikenGUI.Body())
            {
                DrawPresetSlot<GizmoTheme>("theme", "見た目", ref _themeEditor);
                DrawPresetSlot<ToolPostureProfile>("profile", "規約", ref _profileEditor);
            }
        }

        /// <summary>
        /// ハンドルの表示切替を ParticleSystem のモジュール一覧と同じ形で並べる。
        /// </summary>
        private void DrawHandleModules()
        {
            foreach (string name in HandleProps) _drawn.Add(name);
            _drawn.Add("hideOthersWhileDragging");

            if (!Section("ハンドル   実行中は 1 - 4 キーで切替", true)) return;

            using (ShurikenGUI.Body())
            {
                for (int i = 0; i < HandleProps.Length; i++)
                {
                    SerializedProperty p = Take(HandleProps[i]);
                    if (p == null) continue;

                    EditorGUI.BeginChangeCheck();
                    bool on = ShurikenGUI.ToggleRow(HandleLabels[i], p.boolValue);
                    if (EditorGUI.EndChangeCheck()) p.boolValue = on;
                }

                EditorGUILayout.Space(4f);

                float saved = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = GroupLabelWidth;
                EditorGUILayout.PropertyField(Take("hideOthersWhileDragging"),
                                              new GUIContent("ドラッグ中は他を隠す"));
                EditorGUIUtility.labelWidth = saved;
            }
        }

        private static void DrawReadout(ToolPostureGizmo g)
        {
            if (!Section("出力", true)) return;

            var a = g.Angles;
            using (ShurikenGUI.Body())
            {
                float saved = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 132f;

                Row("工具軸 X (LMN)", Components(a.GetAxisLmn()));
                Row("工具軸 X (world)", Components(g.ToolAxisWorld));
                Row("投影角",
                    $"w {a.WorkAngleDeg,7:+0.0;-0.0}°" +
                    $"    t {a.TravelAngleDeg,7:+0.0;-0.0}°");
                Row("トーチ回転 spin",
                    $"{g.Profile.spinConvention.ToDisplay(a.spinAngleDeg),7:+0.0;-0.0}°");
                Row("N からの傾き α",
                    a.TiltIsSignificant()
                        ? $"{a.TiltFromNormalDeg,7:+0.0;-0.0}°"
                        : $"{a.TiltFromNormalDeg,7:+0.0;-0.0}°   θ は効いていない");

                DrawTiltRange(g);

                EditorGUIUtility.labelWidth = saved;
            }
        }

        /// <summary>
        /// 傾斜角がどこまで動かせるかを、表示値と内部値の両方で出す。
        /// tiltConvention の可動範囲そのもの。
        /// </summary>
        private static void DrawTiltRange(ToolPostureGizmo g)
        {
            g.GetTiltRange(out float lo, out float hi);

            AngleConvention conv = g.Profile.tiltConvention;
            float d0 = conv.ToDisplay(lo);
            float d1 = conv.ToDisplay(hi);

            Row("傾斜の可動範囲",
                $"φ {Mathf.Min(d0, d1),6:0.0}° 〜 {Mathf.Max(d0, d1),6:0.0}°" +
                $"   (α {lo,6:0.0} 〜 {hi,6:0.0})");
        }

        private static string Components(Vector3 v)
            => $"{v.x,7:+0.000;-0.000} {v.y,7:+0.000;-0.000} {v.z,7:+0.000;-0.000}";

        private static void Row(string label, string value)
        {
            Rect r = EditorGUILayout.GetControlRect();
            Rect labelRect = new Rect(r.x, r.y, EditorGUIUtility.labelWidth, r.height);
            Rect valueRect = new Rect(r.x + EditorGUIUtility.labelWidth, r.y,
                                      r.width - EditorGUIUtility.labelWidth, r.height);

            EditorGUI.LabelField(labelRect, label);
            EditorGUI.LabelField(valueRect, value, EditorStyles.miniLabel);
        }

        private static void DrawActions(ToolPostureGizmo g)
        {
            EditorGUILayout.Space(2f);
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
        /// 中身の欄が細ってラベルが切れないよう、ビュー幅から決めたラベル幅。
        /// </summary>
        private static float GroupLabelWidth
            => Mathf.Clamp(EditorGUIUtility.currentViewWidth * 0.48f, 150f, 240f);

        /// <summary>
        /// モジュール見出しを描き、開いているかを返す。開閉状態は EditorPrefs に残す。
        /// </summary>
        private static bool Section(string title, bool defaultOpen)
        {
            string key = "ToolPostureGizmo.fold." + title;
            bool open = EditorPrefs.GetBool(key, defaultOpen);

            bool now = ShurikenGUI.Header(title, open);
            if (now != open) EditorPrefs.SetBool(key, now);
            return now;
        }

        private void DrawGroup(string title, string[] props, bool defaultOpen = true)
        {
            foreach (string name in props) _drawn.Add(name);

            if (!Section(title, defaultOpen)) return;

            using (ShurikenGUI.Body())
            {
                float saved = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = GroupLabelWidth;

                foreach (string name in props)
                {
                    SerializedProperty p = Take(name);
                    if (p != null) EditorGUILayout.PropertyField(p, true);
                }

                EditorGUIUtility.labelWidth = saved;
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
                if (_drawn.Contains(it.propertyPath)) continue;

                if (!header)
                {
                    header = true;
                    EditorGUILayout.LabelField("その他", EditorStyles.boldLabel);
                }
                EditorGUILayout.PropertyField(it, true);
            }
        }

        /// <summary>
        /// このフレームで既に描いたプロパティ。DrawRemaining の除外に使う。
        /// 配列に書いた名前と二重に管理すると取りこぼすので、描いた側で記録する。
        /// </summary>
        private SerializedProperty Take(string name)
        {
            SerializedProperty p = serializedObject.FindProperty(name);
            if (p != null) _drawn.Add(name);
            return p;
        }

        /// <summary>
        /// プリセットアセットの割当欄。未設定なら組み込み既定を使っている旨を出し、
        /// 割り当て済みならその中身をここで直接編集できるようにする。
        /// </summary>
        private void DrawPresetSlot<T>(string propName, string label, ref Editor cached)
            where T : ScriptableObject
        {
            SerializedProperty prop = Take(propName);
            if (prop == null) return;

            const float LabelWidth = 56f;

            float saved = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = LabelWidth;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(prop, new GUIContent(label));
                if (GUILayout.Button("新規", EditorStyles.miniButton, GUILayout.Width(40f)))
                {
                    T created = CreateAsset<T>();
                    if (created != null) prop.objectReferenceValue = created;
                }
            }
            EditorGUIUtility.labelWidth = saved;

            var asset = prop.objectReferenceValue as T;
            if (asset == null)
            {
                Rect note = EditorGUILayout.GetControlRect(false, 14f);
                note.xMin += LabelWidth + 4f;
                EditorGUI.LabelField(note, "組み込み既定を使用中", EditorStyles.miniLabel);

                if (cached != null) { DestroyImmediate(cached); cached = null; }
                return;
            }

            string key = "ToolPostureGizmo.inline." + propName;
            bool open = EditorPrefs.GetBool(key, false);

            Rect fold = EditorGUILayout.GetControlRect(false, 16f);
            fold.xMin += LabelWidth + 4f;
            bool now = EditorGUI.Foldout(fold, open, "中身をここで編集", true);
            if (now != open) EditorPrefs.SetBool(key, now);

            if (!now)
            {
                if (cached != null) { DestroyImmediate(cached); cached = null; }
                return;
            }

            if (cached == null || cached.target != asset)
            {
                if (cached != null) DestroyImmediate(cached);
                cached = CreateEditor(asset);
            }

            // 箱の中で indent すると欄が細ってラベルが切れるので、
            // indent は足さずにラベル幅だけ明示的に確保する。
            float inner = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 176f;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                cached.OnInspectorGUI();
            EditorGUIUtility.labelWidth = inner;
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
                    angles.TiltFromNormalDeg = g.Profile.tiltConvention.ClampInternal(edited);
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

            // w / t は導出値なので規約を通さずそのまま出す
            string text =
                $"φ {g.Profile.tiltConvention.ToDisplay(a.TiltFromNormalDeg):F1}°\n" +
                $"θ {g.Profile.azimuthConvention.ToDisplay(a.azimuthDeg):F1}°\n" +
                $"spin {g.Profile.spinConvention.ToDisplay(a.spinAngleDeg):F1}°";

            Handles.Label(f.Origin + f.Normal * scale * 1.45f, text, style);
        }

        #endregion
    }
}
