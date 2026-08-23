using UnityEngine;
using ToolPosture.Gizmo;

namespace ToolPosture.Demo
{
    /// <summary>
    /// 実写重畳ビュー用の IGizmoViewport 実装のサンプル。
    ///
    /// 重畳カメラ (外部パラから配置したピンホールカメラ) の投影に、
    /// 半径方向のレンズ歪み (Brown-Conrady の k1 のみ) を重ねたもの。
    /// アプリ側が Unity の Camera では表せない投影を持つ場合の典型例。
    ///
    ///   歪み付与 : n_d = n_u * (1 + k1 * |n_u|^2)
    ///   歪み除去 : n_u = n_d / (1 + k1 * |n_u|^2)   を反復で解く
    ///
    /// 正規化は画像高さの半分を 1 とする (n.y は -1..1、n.x は -aspect..aspect)。
    /// 表示側のシェーダ ToolPosture/OverlayDistort と同じ式なので、
    /// 画面に見えている位置とギズモの当たり判定が一致する。
    /// </summary>
    public class DistortedOverlayViewport : IGizmoViewport
    {
        const int UndistortIterations = 8;

        public Camera Camera;

        /// <summary>半径方向歪み係数。負で樽型、正で糸巻き型。</summary>
        public float K1;

        /// <summary>画像の解像度 [px]。</summary>
        public Vector2 ImageSize;

        public DistortedOverlayViewport(Camera camera, Vector2 imageSize, float k1)
        {
            Camera = camera;
            ImageSize = imageSize;
            K1 = k1;
        }

        public Camera RenderCamera => Camera;

        public Vector3 EyePosition => Camera != null ? Camera.transform.position : Vector3.zero;

        public Vector2 PixelSize => ImageSize;

        // ------------------------------------------------------------------ 正規化

        Vector2 ToNormalized(Vector2 pixel)
        {
            float halfHeight = ImageSize.y * 0.5f;
            return new Vector2((pixel.x - ImageSize.x * 0.5f) / halfHeight,
                               (pixel.y - halfHeight) / halfHeight);
        }

        Vector2 FromNormalized(Vector2 n)
        {
            float halfHeight = ImageSize.y * 0.5f;
            return new Vector2(n.x * halfHeight + ImageSize.x * 0.5f,
                               n.y * halfHeight + halfHeight);
        }

        public static Vector2 Distort(Vector2 undistorted, float k1)
            => undistorted * (1f + k1 * undistorted.sqrMagnitude);

        /// <summary>
        /// 歪み除去が一意に解ける「歪み後の半径」の上限。
        ///
        /// f(r) = r (1 + k1 r^2) は k1 &lt; 0 のとき r = sqrt(-1/(3 k1)) で折り返し、
        /// それ以遠は単調でなくなるので逆写像が一意に決まらない。折り返し点での
        /// 歪み後半径 (2/3) * sqrt(-1/(3 k1)) がそのまま上限になる。
        ///
        /// 画像の隅の正規化半径は sqrt(1 + aspect^2) (4:3 なら約 1.67) なので、
        /// そこまで可逆にしたければ |k1| をおよそ 0.12 以下に抑える必要がある。
        /// </summary>
        public static float MaxUndistortableRadius(float k1)
        {
            if (k1 >= 0f) return float.PositiveInfinity;
            return Mathf.Sqrt(-1f / (3f * k1)) * (2f / 3f);
        }

        /// <summary>
        /// 歪み除去。r(1 + k1 r^2) = r_d をニュートン法で解く。
        /// 単純な不動点反復 u = d / (1 + k1 |u|^2) は折り返し付近で収束が遅く、
        /// 画像の端で 1e-3 程度の誤差が残るのでこちらを使う。
        /// </summary>
        public static Vector2 Undistort(Vector2 distorted, float k1)
        {
            if (k1 == 0f) return distorted;

            // 折り返しの向こう側は解が一意でないので、手前へ丸めてから解く
            float maxRadius = MaxUndistortableRadius(k1);
            float rd = distorted.magnitude;
            if (rd < 1e-9f) return distorted;
            if (rd > maxRadius)
            {
                distorted *= maxRadius / rd;
                rd = maxRadius;
            }

            float r = rd;
            for (int i = 0; i < UndistortIterations; i++)
            {
                float rr = r * r;
                float g = r * (1f + k1 * rr) - rd;
                float dg = 1f + 3f * k1 * rr;
                if (Mathf.Abs(dg) < 1e-5f) break;      // 折り返し点そのもの
                r -= g / dg;
            }

            return distorted * (r / rd);
        }

        // ------------------------------------------------------------------ IGizmoViewport

        /// <summary>歪んだ画像上のピクセル座標から、ワールドの光線へ。</summary>
        public Ray ScreenPointToRay(Vector2 screenPos)
        {
            if (Camera == null) return new Ray(Vector3.zero, Vector3.forward);

            Vector2 undistorted = Undistort(ToNormalized(screenPos), K1);
            return Camera.ScreenPointToRay(FromNormalized(undistorted));
        }

        /// <summary>ワールド座標から、歪んだ画像上のピクセル座標へ。</summary>
        public bool TryWorldToScreenPoint(Vector3 worldPos, out Vector2 screenPos)
        {
            screenPos = default;
            if (Camera == null) return false;

            Vector3 p = Camera.WorldToScreenPoint(worldPos);
            if (p.z <= 0f) return false;

            screenPos = FromNormalized(Distort(ToNormalized(new Vector2(p.x, p.y)), K1));
            return true;
        }

        public float WorldPerPixel(Vector3 worldPos) => GizmoPicker.WorldPerPixel(Camera, worldPos);
    }
}
