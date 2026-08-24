using UnityEngine;

namespace ToolRuntimeGizmos.Core
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

        // 投影角 (狙い角 w / 進行角 t) の可動範囲はここには置かない。
        //
        // 以前は w / t にも範囲を持たせ、そこから方位ごとの傾斜上限を逆算していたが、
        // 傾斜を縛っているのが別の欄で、しかも方位で変わるため、
        // 「傾斜の範囲を広げても動かない」という追いにくい状態になっていた。
        // 保持している角 (theta / phi / spin) だけを縛る形に単純化してある。

        #region 基準

        [Header("基準")]
        [Tooltip("トーチ回転角 0 度の基準")]
        public SpinReference spinReference = SpinReference.FeedProjected;

        #endregion

        #region 検証

        /// <summary>
        /// 下限が上限を超えた状態で残らないようにする。
        /// 逆転したままだと円弧の描画と実際のクランプが食い違う。
        /// </summary>
        private void OnValidate()
        {
            azimuthConvention?.Validate();
            tiltConvention?.Validate();
            spinConvention?.Validate();
        }

        #endregion
    }
}
