using UnityEditor;
using UnityEngine;
using ToolRuntimeGizmos.Core;

namespace ToolRuntimeGizmos.EditorTools
{
    /// <summary>
    /// AngleConvention の描画。
    ///
    /// minDeg / maxDeg は表示値なので、0 度位置がずれていたり回転方向が反転していると
    /// 内部値では違う範囲になる (例: 0 度位置 90 度で表示 ±90 は内部 -180 〜 0)。
    /// 数字だけ見ても分からないので、換算結果を必ず並べて出す。
    /// </summary>
    [CustomPropertyDrawer(typeof(AngleConvention))]
    public class AngleConventionDrawer : PropertyDrawer
    {
        private static readonly string[] Fields =
        {
            "zeroOffsetDeg", "invertDirection", "useLimits", "minDeg", "maxDeg", "snapDeg",
        };

        private static float LineStep
            => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded) return EditorGUIUtility.singleLineHeight;

            // 折りたたみ + 各フィールド + 換算行
            return LineStep * (1 + Fields.Length) + EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var r = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(r, property.isExpanded, label, true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;

                foreach (string name in Fields)
                {
                    SerializedProperty p = property.FindPropertyRelative(name);
                    if (p == null) continue;

                    r.y += LineStep;
                    EditorGUI.PropertyField(r, p);
                }

                r.y += LineStep;
                DrawRangeSummary(r, property);

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// 可動範囲を表示値と内部値の両方で出す。
        /// </summary>
        private static void DrawRangeSummary(Rect r, SerializedProperty property)
        {
            SerializedProperty limitsProp = property.FindPropertyRelative("useLimits");
            if (limitsProp == null) return;

            if (!limitsProp.boolValue)
            {
                EditorGUI.LabelField(r, " ", "可動範囲: 制限なし", EditorStyles.miniLabel);
                return;
            }

            float zero = Value(property, "zeroOffsetDeg");
            bool invert = property.FindPropertyRelative("invertDirection").boolValue;
            float min = Value(property, "minDeg");
            float max = Value(property, "maxDeg");

            float a = ToInternal(min, zero, invert);
            float b = ToInternal(max, zero, invert);

            string text = string.Format("表示 {0:0.#} 〜 {1:0.#}    内部 {2:0.#} 〜 {3:0.#}",
                                        Mathf.Min(min, max), Mathf.Max(min, max),
                                        Mathf.Min(a, b), Mathf.Max(a, b));

            EditorGUI.LabelField(r, " ", text, EditorStyles.miniLabel);
        }

        private static float Value(SerializedProperty property, string name)
        {
            SerializedProperty p = property.FindPropertyRelative(name);
            return p != null ? p.floatValue : 0f;
        }

        private static float ToInternal(float displayDeg, float zeroOffsetDeg, bool invert)
        {
            float v = displayDeg - zeroOffsetDeg;
            return invert ? -v : v;
        }
    }
}
