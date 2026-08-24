using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ToolPosture.Gizmo;

namespace ToolPosture.EditorTools
{
    /// <summary>
    /// GizmoTheme の描画。
    ///
    /// 40 フィールドが英語名でベタ並びになるのを、ギズモ本体と同じ見出しで
    /// 折りたたみ、日本語ラベルを当てて読めるようにする。
    /// 色は 2 列に詰めて縦を短くする。
    /// </summary>
    [CustomEditor(typeof(GizmoTheme))]
    public class GizmoThemeEditor : Editor
    {
        #region ラベル

        /// <summary>
        /// フィールド名 -&gt; 表示ラベル。載っていないものは既定の英語名で出る。
        /// </summary>
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "gizmoPixelSize", "全体の大きさ" },
            { "arcPixelWidth", "円弧の幅" },
            { "thinPixelWidth", "細線の幅" },
            { "knobPixelRadius", "ノブの半径" },
            { "tipPixelRadius", "先端ボールの半径" },
            { "fallbackArcHalfWidthDeg", "制限なしの円弧の半幅 [deg]" },

            { "frameAxisPixelWidth", "フレーム軸の太さ" },
            { "toolAxisPixelWidth", "工具軸の太さ" },
            { "arrowHeadPixelRadius", "矢じりの半径" },
            { "toolArrowHeadPixelRadius", "矢じりの半径 (工具軸)" },
            { "arrowHeadPixelLength", "矢じりの長さ" },
            { "originDotPixelRadius", "原点の点の半径" },

            { "tickPixelLength", "0 度目盛りの長さ" },
            { "limitTickPixelLength", "端の目盛りの長さ" },
            { "tickPixelWidth", "目盛りの太さ" },
            { "dashPixelLength", "破線の刻み" },

            { "toolAxisLengthRatio", "工具軸の長さ" },
            { "frameAxisLengthRatio", "フレーム軸 M / N の長さ" },
            { "crossFeedAxisLengthRatio", "フレーム軸 L の長さ" },
            { "tiltArcRadiusRatio", "傾斜角の円弧の半径" },
            { "azimuthRingRadiusRatio", "旋回リングの半径" },
            { "spinRingOffsetRatio", "スピンリングの位置" },
            { "spinRingRadiusRatio", "スピンリングの半径" },

            { "hitPixelWidth", "チューブの太さ" },
            { "touchHitPixelWidth", "チューブの太さ (タッチ)" },
            { "tipHitPixelRadius", "先端の当たり半径" },
            { "touchTipHitScale", "先端の倍率 (タッチ)" },

            { "frameColorL", "L 軸" },
            { "frameColorM", "M 軸" },
            { "frameColorN", "N 軸" },
            { "tiltColor", "傾斜角の円弧" },
            { "azimuthColor", "旋回リング" },
            { "spinColor", "スピンリング" },
            { "axisColor", "工具軸" },
            { "highlightColor", "ホバー / 操作中" },
            { "zeroTickColor", "0 度目盛り" },
            { "limitColor", "可動範囲の端" },
            { "occludedAlpha", "隠れている部分の濃さ" },
            { "colliderGizmoColor", "コライダー" },
            { "colliderGizmoDisabledColor", "コライダー (掴めない)" },
        };

        #endregion

        #region グループ

        private static readonly string[] SizeProps =
        {
            "gizmoPixelSize", "arcPixelWidth", "thinPixelWidth",
            "knobPixelRadius", "tipPixelRadius", "fallbackArcHalfWidthDeg",
            "frameAxisPixelWidth", "toolAxisPixelWidth",
            "arrowHeadPixelRadius", "toolArrowHeadPixelRadius", "arrowHeadPixelLength",
            "originDotPixelRadius",
            "tickPixelLength", "limitTickPixelLength", "tickPixelWidth", "dashPixelLength",
        };

        private static readonly string[] LayoutProps =
        {
            "toolAxisLengthRatio", "frameAxisLengthRatio", "crossFeedAxisLengthRatio",
            "tiltArcRadiusRatio", "azimuthRingRadiusRatio",
            "spinRingOffsetRatio", "spinRingRadiusRatio",
        };

        private static readonly string[] HitProps =
        {
            "hitPixelWidth", "touchHitPixelWidth", "tipHitPixelRadius", "touchTipHitScale",
        };

        /// <summary>
        /// 2 列に詰めて出す色。
        /// </summary>
        private static readonly string[] ColorProps =
        {
            "frameColorL", "frameColorM", "frameColorN", "axisColor",
            "tiltColor", "azimuthColor", "spinColor", "highlightColor",
            "zeroTickColor", "limitColor",
        };

        private readonly HashSet<string> _drawn = new HashSet<string>();

        #endregion

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            _drawn.Clear();

            if (Section("色", true))
            {
                using (ShurikenGUI.Body())
                {
                    DrawColorGrid();
                    EditorGUILayout.Space(2f);
                    DrawRows(new[] { "occludedAlpha" });
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField("コライダーのデバッグ表示", EditorStyles.miniBoldLabel);
                    DrawRows(new[] { "colliderGizmoColor", "colliderGizmoDisabledColor" });
                }
            }
            else
            {
                MarkDrawn(ColorProps);
                MarkDrawn(new[] { "occludedAlpha", "colliderGizmoColor", "colliderGizmoDisabledColor" });
            }

            DrawGroup("太さ [px]", SizeProps);
            DrawGroup("配置 (全体の大きさに対する比率)", LayoutProps);
            DrawGroup("当たり判定 [px]", HitProps);
            DrawRemaining();

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(2f);
            if (GUILayout.Button("既定値に戻す")) ResetToDefault();
        }

        #region 描画

        private static bool Section(string title, bool defaultOpen)
        {
            string key = "GizmoTheme.fold." + title;
            bool open = EditorPrefs.GetBool(key, defaultOpen);

            bool now = ShurikenGUI.Header(title, open);
            if (now != open) EditorPrefs.SetBool(key, now);
            return now;
        }

        private void DrawGroup(string title, string[] props, bool defaultOpen = false)
        {
            MarkDrawn(props);
            if (!Section(title, defaultOpen)) return;

            using (ShurikenGUI.Body())
                DrawRows(props);
        }

        private void DrawRows(string[] props)
        {
            float saved = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth * 0.5f, 160f, 250f);

            foreach (string name in props)
            {
                SerializedProperty p = serializedObject.FindProperty(name);
                if (p == null) continue;

                _drawn.Add(name);
                EditorGUILayout.PropertyField(p, Label(name));
            }

            EditorGUIUtility.labelWidth = saved;
        }

        /// <summary>
        /// 色を 2 列に詰める。縦に 10 行並べると読みにくい。
        /// </summary>
        private void DrawColorGrid()
        {
            float saved = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth * 0.22f, 80f, 130f);

            for (int i = 0; i < ColorProps.Length; i += 2)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawColorCell(ColorProps[i]);
                    if (i + 1 < ColorProps.Length) DrawColorCell(ColorProps[i + 1]);
                }
            }

            EditorGUIUtility.labelWidth = saved;
        }

        private void DrawColorCell(string name)
        {
            SerializedProperty p = serializedObject.FindProperty(name);
            if (p == null) return;

            _drawn.Add(name);
            EditorGUILayout.PropertyField(p, Label(name));
        }

        private static GUIContent Label(string name)
            => Labels.TryGetValue(name, out string text) ? new GUIContent(text) : null;

        private void MarkDrawn(string[] props)
        {
            foreach (string name in props) _drawn.Add(name);
        }

        /// <summary>
        /// どのグループにも入れていないものを拾う。
        /// フィールドを足したときにインスペクタから消えるのを防ぐ保険。
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

        #endregion

        private void ResetToDefault()
        {
            if (!EditorUtility.DisplayDialog("既定値に戻す",
                    "このテーマの内容をすべて既定値に戻します。元に戻せません (Undo は効きます)。",
                    "戻す", "やめる"))
                return;

            var fresh = CreateInstance<GizmoTheme>();
            Undo.RecordObject(target, "Reset gizmo theme");
            EditorUtility.CopySerialized(fresh, target);
            DestroyImmediate(fresh);
            EditorUtility.SetDirty(target);
        }
    }
}
