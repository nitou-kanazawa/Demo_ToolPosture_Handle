using UnityEngine;

namespace ToolPosture.Gizmo
{
    /// <summary>
    /// 接線投影方式の回転ドラッグ。UnityEditor.Handles の回転ギズモや
    /// Runtime Transform Gizmos と同じ考え方。
    ///
    ///   1. 掴んだ瞬間に「回転中心」「掴んだ点」「その点での接線」を固定する
    ///   2. 接線をスクリーンへ投影し、1 ピクセルあたりの角度を求める
    ///   3. 以降はドラッグ開始点からのスクリーン移動量を接線方向へ投影し、
    ///      弧長とみなして 角度 = 弧長 / 半径 に換算する
    ///
    /// 光線とハンドル平面の交点から極角を取る方式と違い、視線がハンドル平面に
    /// 寝ても破綻しない。平面方式は視線が平面と 2 度で 17 度/5px、1 度で 55 度/5px、
    /// 真横では交点自体が消えて操作不能になるが、この方式は最悪でも公称の
    /// 4-5 倍程度に鈍るだけで済む。
    ///
    /// 差分の積算ではなくドラッグ開始点からの絶対量で計算するので、ドリフトしない。
    /// </summary>
    public struct TangentRotationDrag
    {
        /// <summary>接線をスクリーンへ投影するときの微小移動量 [m]。</summary>
        const float ProbeDistance = 0.01f;

        float _startValue;
        Vector2 _startScreen;
        Vector2 _tangentDir;     // スクリーン上の接線方向 (正規化済み)
        float _degPerPixel;
        bool _valid;

        public bool IsValid => _valid;
        public float StartValue => _startValue;

        /// <summary>1 ピクセルあたりの角度 [deg]。感度の確認・テスト用。</summary>
        public float DegreesPerPixel => _degPerPixel;

        /// <summary>
        /// ドラッグ開始。
        /// </summary>
        /// <param name="center">回転中心 (ワールド)</param>
        /// <param name="u">平面の 0 度方向 (単位ベクトル)</param>
        /// <param name="v">平面の +90 度方向 (単位ベクトル、u と直交)</param>
        /// <param name="radius">円弧の半径 [m]</param>
        /// <param name="grabAngleDeg">掴んだ点の平面内極角 [deg]</param>
        /// <param name="startValue">掴んだ時点の角度値 (内部値)</param>
        /// <param name="screenPos">掴んだスクリーン座標 [px]</param>
        /// <param name="maxDegPerPixel">感度の上限。接線が視線と平行に近いときの暴れ止め。0 以下で無効</param>
        public void Begin(IGizmoViewport viewport, Vector3 center, Vector3 u, Vector3 v,
                          float radius, float grabAngleDeg, float startValue,
                          Vector2 screenPos, float maxDegPerPixel)
        {
            _valid = false;
            _startValue = startValue;
            _startScreen = screenPos;
            _tangentDir = Vector2.zero;
            _degPerPixel = 0f;

            if (viewport == null || radius <= 1e-6f) return;

            float a = grabAngleDeg * Mathf.Deg2Rad;
            Vector3 grabPoint = center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;

            // p(a) = c + r(u cos a + v sin a) を a で微分した向き。+ 方向が角度の増加方向。
            Vector3 tangent = -u * Mathf.Sin(a) + v * Mathf.Cos(a);

            if (!viewport.TryWorldToScreenPoint(grabPoint, out Vector2 s0)) return;
            if (!viewport.TryWorldToScreenPoint(grabPoint + tangent * ProbeDistance, out Vector2 s1)) return;

            Vector2 d = s1 - s0;
            float pixels = d.magnitude;
            if (pixels < 1e-5f) return;      // 接線が視線とほぼ平行

            _tangentDir = d / pixels;

            float pixelsPerWorld = pixels / ProbeDistance;
            _degPerPixel = Mathf.Rad2Deg / (pixelsPerWorld * radius);
            if (maxDegPerPixel > 0f) _degPerPixel = Mathf.Min(_degPerPixel, maxDegPerPixel);

            _valid = true;
        }

        /// <summary>現在のスクリーン座標から角度値を求める。</summary>
        public bool TryGetValue(Vector2 screenPos, out float value)
        {
            value = _startValue;
            if (!_valid) return false;

            float pixels = Vector2.Dot(screenPos - _startScreen, _tangentDir);
            value = _startValue + pixels * _degPerPixel;
            return true;
        }

        public void Cancel() { _valid = false; }
    }
}
