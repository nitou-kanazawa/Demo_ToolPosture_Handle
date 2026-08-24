using UnityEngine;

namespace ToolPosture.Gizmo
{
    /// <summary>
    /// ギズモが必要とする「画面 &lt;-&gt; ワールド」の変換。
    ///
    /// ギズモ本体は Camera に直接依存せず、この interface だけを見る。
    /// 実写画像への重畳ビューのように、投影がアプリ独自 (レンズ歪み補正など) の場合は
    /// これを実装して差し込めば、当たり判定・回転操作・定スクリーンサイズのすべてが
    /// そのマッピングに従う。
    ///
    /// 標準実装は CameraViewport (Unity の Camera をそのまま使う)。
    /// </summary>
    public interface IGizmoViewport
    {
        /// <summary>
        /// 描画に使うカメラ。ビルボードの向きと RenderParams.camera に使う。
        /// </summary>
        Camera RenderCamera { get; }

        /// <summary>
        /// 視点位置。スクリーン幅一定の線を張るのに使う。
        /// </summary>
        Vector3 EyePosition { get; }

        /// <summary>
        /// このビューポートの解像度 [px]。
        /// </summary>
        Vector2 PixelSize { get; }

        /// <summary>
        /// スクリーン座標 [px] からワールドの光線へ。
        /// </summary>
        Ray ScreenPointToRay(Vector2 screenPos);

        /// <summary>
        /// ワールド座標からスクリーン座標 [px] へ。視点の背後など投影できない場合は false。
        /// 接線投影方式の回転ドラッグがこれを使う。
        /// </summary>
        bool TryWorldToScreenPoint(Vector3 worldPos, out Vector2 screenPos);

        /// <summary>
        /// 指定ワールド座標における 1 ピクセル分のワールド長。
        /// </summary>
        float WorldPerPixel(Vector3 worldPos);
    }

    /// <summary>
    /// Unity の Camera をそのまま使う標準ビューポート。
    /// </summary>
    public class CameraViewport : IGizmoViewport
    {
        public Camera Camera { get; set; }

        public CameraViewport(Camera camera) { Camera = camera; }

        public Camera RenderCamera => Camera;

        public Vector3 EyePosition => Camera != null ? Camera.transform.position : Vector3.zero;

        public Vector2 PixelSize => Camera != null
            ? new Vector2(Camera.pixelWidth, Camera.pixelHeight)
            : Vector2.one;

        public Ray ScreenPointToRay(Vector2 screenPos)
            => Camera != null ? Camera.ScreenPointToRay(screenPos) : new Ray(Vector3.zero, Vector3.forward);

        public bool TryWorldToScreenPoint(Vector3 worldPos, out Vector2 screenPos)
        {
            screenPos = default;
            if (Camera == null) return false;

            Vector3 p = Camera.WorldToScreenPoint(worldPos);
            if (p.z <= 0f) return false;      // 視点の背後

            screenPos = new Vector2(p.x, p.y);
            return true;
        }

        public float WorldPerPixel(Vector3 worldPos) => GizmoPicker.WorldPerPixel(Camera, worldPos);
    }
}
