using UnityEngine;

namespace ToolPosture.Gizmo
{
    /// <summary>
    /// ランタイムでのハンドル当たり判定。UnityEditor.HandleUtility の代替。
    /// 当たり判定の許容幅はワールド長ではなくピクセル基準で与えることで、
    /// カメラ距離が変わっても掴み心地が一定になる。
    /// </summary>
    public static class GizmoPicker
    {
        /// <summary>
        /// 指定ワールド座標における 1 ピクセル分のワールド長。
        /// </summary>
        public static float WorldPerPixel(Camera cam, Vector3 worldPoint)
        {
            if (cam == null || cam.pixelHeight <= 0) return 0.01f;

            if (cam.orthographic)
                return 2f * cam.orthographicSize / cam.pixelHeight;

            float d = Vector3.Dot(worldPoint - cam.transform.position, cam.transform.forward);
            d = Mathf.Max(d, cam.nearClipPlane);
            return 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / cam.pixelHeight;
        }

        public static bool RayPlane(Ray ray, Vector3 planePoint, Vector3 planeNormal,
                                    out Vector3 hit, out float dist)
        {
            hit = default;
            dist = 0f;
            float denom = Vector3.Dot(ray.direction, planeNormal);
            if (Mathf.Abs(denom) < 1e-6f) return false;      // 平面と視線が平行

            dist = Vector3.Dot(planePoint - ray.origin, planeNormal) / denom;
            if (dist < 0f) return false;                     // 背後
            hit = ray.origin + ray.direction * dist;
            return true;
        }

        /// <summary>
        /// 平面 (u が 0 度方向, v が +90 度方向) 上での交点の極座標を返す。
        /// </summary>
        public static bool RayPlanePolar(Ray ray, Vector3 center, Vector3 u, Vector3 v,
                                         out float angleDeg, out float radius, out float dist)
        {
            angleDeg = 0f;
            radius = 0f;

            Vector3 n = Vector3.Cross(u, v);
            if (!RayPlane(ray, center, n, out var hit, out dist)) return false;

            Vector3 d = hit - center;
            float x = Vector3.Dot(d, u);
            float y = Vector3.Dot(d, v);
            radius = Mathf.Sqrt(x * x + y * y);
            if (radius < 1e-6f) return false;

            angleDeg = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>
        /// 円弧の帯に当たったか。当たった場合は円弧上の角度 (角度範囲へ折り返し済み) を返す。
        /// </summary>
        public static bool PickArc(Ray ray, Vector3 center, Vector3 u, Vector3 v, float radius,
                                   float a0Deg, float a1Deg, float worldTolerance,
                                   out float angleDeg, out float dist)
        {
            angleDeg = 0f;
            if (!RayPlanePolar(ray, center, u, v, out float a, out float r, out dist)) return false;
            if (Mathf.Abs(r - radius) > worldTolerance) return false;

            float lo = Mathf.Min(a0Deg, a1Deg) - 1f;
            float hi = Mathf.Max(a0Deg, a1Deg) + 1f;
            if (hi - lo < 359f)
            {
                while (a < lo) a += 360f;
                while (a > hi) a -= 360f;
                if (a < lo) return false;
            }

            angleDeg = a;
            return true;
        }

        public static bool PickSphere(Ray ray, Vector3 center, float radius, out float dist)
        {
            dist = 0f;
            Vector3 oc = ray.origin - center;
            float b = Vector3.Dot(oc, ray.direction);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float disc = b * b - c;
            if (disc < 0f) return false;

            float s = Mathf.Sqrt(disc);
            float t0 = -b - s;
            float t1 = -b + s;
            dist = t0 >= 0f ? t0 : t1;
            return dist >= 0f;
        }

        /// <summary>
        /// 中心 center・半径 radius の球面上で、視線に最も近い点の方向を返す。
        /// 球から外れていても最近点を球面に射影するので、ドラッグが途切れない。
        /// </summary>
        public static Vector3 ClosestDirectionOnSphere(Ray ray, Vector3 center, float radius)
        {
            if (PickSphere(ray, center, radius, out float d))
            {
                Vector3 hit = ray.origin + ray.direction * d - center;
                if (hit.sqrMagnitude > 1e-10f) return hit.normalized;
            }

            float t = Mathf.Max(0f, Vector3.Dot(center - ray.origin, ray.direction));
            Vector3 p = ray.origin + ray.direction * t - center;
            return p.sqrMagnitude < 1e-10f ? ray.direction.normalized : p.normalized;
        }
    }
}
