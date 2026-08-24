using UnityEngine;

namespace ToolPosture.Gizmo
{
    /// <summary>
    /// ギズモの見た目と当たりの太さ。アセットにして使い回す。
    ///
    /// 寸法はすべて「画面上の px」で書く。ワールド長への換算は
    /// ToolPostureGizmo.PixelToWorld が Scale から行うので、ここに Camera は出てこない。
    ///
    /// 当たり判定の太さもここに置いてある。タッチ向けに「太いチューブ + 大きいノブ」を
    /// まとめて切り替えたいことが多く、見た目と操作感を別アセットに分けると
    /// 2 枚を必ず対で差し替える運用になって面倒だからである。
    ///
    /// 実行中にこのアセットの値を書き換えないこと。アセットなので
    /// エディタでは変更が永続化される。インスタンスごとに変えたい場合は
    /// Instantiate してから ToolPostureGizmo.Theme に差し替える。
    /// </summary>
    [CreateAssetMenu(menuName = "Tool Posture/Gizmo Theme", fileName = "GizmoTheme")]
    public class GizmoTheme : ScriptableObject
    {
        #region 組み込み既定

        private static GizmoTheme _default;

        /// <summary>
        /// アセットを 1 つも作らなくても動くための組み込み既定。
        /// ToolPostureGizmo.Theme は未設定のときこれを返す。
        /// </summary>
        public static GizmoTheme Default
        {
            get
            {
                if (_default == null)
                {
                    _default = CreateInstance<GizmoTheme>();
                    _default.name = "GizmoTheme (Built-in)";
                    _default.hideFlags = HideFlags.HideAndDontSave;
                }
                return _default;
            }
        }

        #endregion

        #region 寸法

        [Tooltip("ギズモの画面上の大きさ。カメラ距離によらず一定に保たれる")]
        public float gizmoPixelSize = 130f;

        [Tooltip("円弧・リングの描画幅")]
        public float arcPixelWidth = 8f;

        [Tooltip("破線・目盛りの線幅")]
        public float thinPixelWidth = 1.2f;

        [Tooltip("ノブの半径。ホバー・操作中はこの値、通常はこれの 0.7 倍")]
        public float knobPixelRadius = 8f;

        [Tooltip("軸先端のボールの描画半径")]
        public float tipPixelRadius = 8f;

        [Tooltip("可動範囲を使わない角度の円弧の描画半幅 [deg]")]
        public float fallbackArcHalfWidthDeg = 75f;

        #endregion

        #region 軸と矢印 [px]

        [Tooltip("LMN フレームの矢印の太さ")]
        public float frameAxisPixelWidth = 3.2f;

        [Tooltip("工具軸 X の矢印の太さ")]
        public float toolAxisPixelWidth = 4.8f;

        [Tooltip("フレームの矢印の頭の半径")]
        public float arrowHeadPixelRadius = 5.5f;

        [Tooltip("工具軸の矢印の頭の半径")]
        public float toolArrowHeadPixelRadius = 6.5f;

        [Tooltip("矢印の頭の長さ")]
        public float arrowHeadPixelLength = 16f;

        [Tooltip("原点の点の半径")]
        public float originDotPixelRadius = 3.5f;

        #endregion

        #region 目盛りと破線 [px]

        [Tooltip("0 度目盛りの長さ")]
        public float tickPixelLength = 16f;

        [Tooltip("可動範囲の端の目盛りの長さ")]
        public float limitTickPixelLength = 10f;

        [Tooltip("0 度目盛りの太さ")]
        public float tickPixelWidth = 1.6f;

        [Tooltip("破線の 1 区切りの長さ")]
        public float dashPixelLength = 9f;

        #endregion

        #region 配置 (全体の大きさに対する比率)

        [Tooltip("工具軸 X の矢印の長さ。軸先端ボールもこの位置に乗る")]
        public float toolAxisLengthRatio = 1.25f;

        [Tooltip("面法線 N と進行方向 M の矢印の長さ")]
        public float frameAxisLengthRatio = 1.25f;

        [Tooltip("直交方向 L の矢印の長さ。旋回リングと重なりにくいよう既定では短い")]
        public float crossFeedAxisLengthRatio = 0.95f;

        [Tooltip("傾斜角の円弧の半径")]
        public float tiltArcRadiusRatio = 0.74f;

        [Tooltip("旋回リングの半径")]
        public float azimuthRingRadiusRatio = 1.42f;

        [Tooltip("スピンリングを工具軸に沿ってどこに置くか")]
        public float spinRingOffsetRatio = 0.86f;

        [Tooltip("スピンリングの半径")]
        public float spinRingRadiusRatio = 0.50f;

        #endregion

        #region 当たり判定 [px]

        [Tooltip("円弧・リングに巻くチューブの直径。この幅は視線角度によらず一定")]
        public float hitPixelWidth = 20f;

        [Tooltip("タッチでのチューブの直径。指は狙いが粗いのでマウスより太くする")]
        public float touchHitPixelWidth = 40f;

        [Tooltip("軸先端の当たり判定の半径")]
        public float tipHitPixelRadius = 14f;

        [Tooltip("タッチでの軸先端の当たり判定の倍率")]
        public float touchTipHitScale = 1.9f;

        #endregion

        #region 色

        public Color frameColorL = new Color(1.00f, 0.48f, 0.32f, 0.95f);
        public Color frameColorM = new Color(0.42f, 0.90f, 0.48f, 0.95f);
        public Color frameColorN = new Color(0.36f, 0.64f, 1.00f, 0.95f);

        [Tooltip("傾斜角の円弧")]
        public Color tiltColor = new Color(1.00f, 0.45f, 0.74f, 0.95f);

        [Tooltip("スピンリング")]
        public Color spinColor = new Color(0.74f, 0.62f, 1.00f, 0.95f);

        [Tooltip("旋回リング")]
        public Color azimuthColor = new Color(0.55f, 0.95f, 0.60f, 0.95f);

        [Tooltip("工具軸と軸先端ボール")]
        public Color axisColor = new Color(1.00f, 0.83f, 0.26f, 0.95f);

        [Tooltip("ホバー・操作中")]
        public Color highlightColor = new Color(1.00f, 1.00f, 0.72f, 1.00f);

        [Tooltip("0 度の目盛り")]
        public Color zeroTickColor = new Color(1.00f, 1.00f, 1.00f, 0.90f);

        [Tooltip("可動範囲の端")]
        public Color limitColor = new Color(1.00f, 0.38f, 0.32f, 0.95f);

        [Tooltip("手前の物体に隠れている部分の濃さ")]
        [Range(0f, 1f)] public float occludedAlpha = 0.22f;

        public Color colliderGizmoColor = new Color(0.20f, 0.90f, 1.00f, 0.85f);

        [Tooltip("今は掴めないハンドル (非表示 / ドラッグ中に隠れている) の色")]
        public Color colliderGizmoDisabledColor = new Color(0.45f, 0.50f, 0.55f, 0.35f);

        #endregion
    }
}
