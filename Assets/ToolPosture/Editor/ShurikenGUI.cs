using UnityEditor;
using UnityEngine;

namespace ToolPosture.EditorTools
{
    /// <summary>
    /// ParticleSystem のモジュール一覧と同じ見た目の見出しを描くためのヘルパ。
    ///
    /// エディタスキンに組み込みで入っている "Shuriken*" スタイル
    /// (Shuriken は ParticleSystem の開発コード名) をそのまま借りている。
    /// 公開 API ではないので、将来のバージョンで名前が変わる可能性はある。
    /// 見つからない場合は素の Foldout / HelpBox に落ちるようにしてある。
    /// </summary>
    internal static class ShurikenGUI
    {
        #region スタイル

        private const float HeaderHeight = 22f;
        private const float MarkSize = 13f;
        private const float MarkLeft = 5f;

        private static GUIStyle _title;
        private static GUIStyle _body;
        private static bool _resolved;
        private static bool _resolvedProSkin;

        /// <summary>
        /// 現在のエディタスキンからスタイルを引く。
        ///
        /// EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector) は
        /// テーマに関係なく light スキンを返すので使わないこと
        /// (ダークテーマで淡いバーになる)。OnGUI 中の GUI.skin が現在のテーマの skin。
        /// FindStyle なら見つからなくても警告を出さずに null が返る。
        /// </summary>
        private static void Resolve()
        {
            if (_resolved && _resolvedProSkin == EditorGUIUtility.isProSkin) return;
            if (GUI.skin == null) return;      // OnGUI の外

            _resolved = true;
            _resolvedProSkin = EditorGUIUtility.isProSkin;
            _title = null;
            _body = null;

            GUIStyle titleSrc = GUI.skin.FindStyle("ShurikenModuleTitle");
            GUIStyle bodySrc = GUI.skin.FindStyle("ShurikenModuleBg");
            if (titleSrc == null || bodySrc == null) return;   // 素のスタイルへ落ちる

            _title = new GUIStyle(titleSrc)
            {
                font = EditorStyles.boldLabel.font,
                fontSize = EditorStyles.boldLabel.fontSize,
                border = new RectOffset(15, 7, 4, 4),
                fixedHeight = HeaderHeight,
                contentOffset = new Vector2(22f, -2f),
            };

            _body = new GUIStyle(bodySrc)
            {
                padding = new RectOffset(10, 8, 6, 6),
            };
        }

        /// <summary>
        /// 組み込みスタイルが見つかったか。false なら素の見た目で描かれる。
        /// </summary>
        public static bool Available
        {
            get
            {
                Resolve();
                return _title != null;
            }
        }

        /// <summary>
        /// モジュールの中身を包む枠。using と一緒に使う。
        /// </summary>
        public static GUI.Scope Body()
        {
            Resolve();
            return new EditorGUILayout.VerticalScope(_body ?? EditorStyles.helpBox);
        }

        #endregion

        #region 見出し

        /// <summary>
        /// 折りたたみだけの見出し。行のどこを押しても開閉する。
        /// </summary>
        public static bool Header(string title, bool expanded)
        {
            Resolve();
            if (_title == null)
                return EditorGUILayout.Foldout(expanded, title, true, EditorStyles.foldoutHeader);

            Rect rect = GUILayoutUtility.GetRect(16f, HeaderHeight, _title);
            GUI.Box(rect, title, _title);

            Event e = Event.current;
            var arrow = new Rect(rect.x + MarkLeft - 1f, rect.y + 4f, MarkSize, MarkSize);
            if (e.type == EventType.Repaint)
                EditorStyles.foldout.Draw(arrow, false, false, expanded, false);

            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                expanded = !expanded;
                e.Use();
            }
            return expanded;
        }

        /// <summary>
        /// チェックボックス付きの行。ParticleSystem のモジュール一覧と同じ形。
        /// 展開する中身が無いので折りたたみの三角は出さない。
        /// 行のどこを押しても入り切りする。
        /// </summary>
        public static bool ToggleRow(string title, bool enabled)
        {
            Resolve();
            if (_title == null) return EditorGUILayout.ToggleLeft(title, enabled);

            Rect rect = GUILayoutUtility.GetRect(16f, HeaderHeight, _title);
            GUI.Box(rect, title, _title);

            Event e = Event.current;
            var mark = new Rect(rect.x + MarkLeft, rect.y + 4f, MarkSize, MarkSize);
            if (e.type == EventType.Repaint)
                EditorStyles.toggle.Draw(mark, GUIContent.none, false, false, enabled, false);

            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                enabled = !enabled;
                GUI.changed = true;
                e.Use();
            }
            return enabled;
        }

        #endregion
    }
}
