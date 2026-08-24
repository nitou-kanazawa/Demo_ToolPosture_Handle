using UnityEngine;

namespace ToolPosture.Core
{
    /// <summary>
    /// 工具姿勢の角度規約と可動範囲。アセットにして使い回す。
    ///
    /// 工程 (MIG / TIG)、開先形状、材料などで変わる「姿勢そのものの決まりごと」を
    /// まとめたもの。見た目 (GizmoTheme) とは切り替えたい単位が違うので分けてある。
    ///
    /// 実行中にこのアセットの値を書き換えないこと。アセットなので
    /// エディタでは変更が永続化される。
    /// </summary>
    [CreateAssetMenu(menuName = "Tool Posture/Posture Profile", fileName = "ToolPostureProfile")]
    public class ToolPostureProfile : ScriptableObject
    {
        #region 組み込み既定

        private static ToolPostureProfile _default;

        /// <summary>
        /// アセットを 1 つも作らなくても動くための組み込み既定。
        /// ToolPostureGizmo.Profile は未設定のときこれを返す。
        /// </summary>
        public static ToolPostureProfile Default
        {
            get
            {
                if (_default == null)
                {
                    _default = CreateInstance<ToolPostureProfile>();
                    _default.name = "ToolPostureProfile (Built-in)";
                    _default.hideFlags = HideFlags.HideAndDontSave;
                }
                return _default;
            }
        }

        #endregion

        #region 保持する角の規約

        [Header("保持する角 (0 度位置 / 回転方向 / 可動範囲 / スナップ幅)")]
        [Tooltip("旋回角 theta。母材面内の方位なので既定は無制限")]
        public AngleConvention azimuthConvention = AngleConvention.Unlimited();

        [Tooltip("傾斜角 alpha = 90 - phi。N からの倒し量")]
        public AngleConvention tiltConvention = AngleConvention.Elevation();

        [Tooltip("トーチ回転角 spin")]
        public AngleConvention spinConvention = AngleConvention.Unlimited();

        #endregion

        #region 投影角の可動範囲

        [Header("投影角の可動範囲 (溶接規格側の制限)")]
        [Tooltip("狙い角 w の許容範囲。ハンドルは持たないが、方位ごとの傾斜上限を決める")]
        public AngleConvention workConvention = AngleConvention.Ranged(-60f, 60f);

        [Tooltip("進行角 t の許容範囲。ハンドルは持たないが、方位ごとの傾斜上限を決める")]
        public AngleConvention travelConvention = AngleConvention.Ranged(-60f, 60f);

        #endregion

        #region 基準

        [Header("基準")]
        [Tooltip("トーチ回転角 0 度の基準")]
        public SpinReference spinReference = SpinReference.FeedProjected;

        #endregion
    }
}
