using System;
using UnityEngine;

namespace ToolPosture.Core
{
    /// <summary>
    /// フレーム (L, M, N) に対する工具姿勢。
    ///
    /// 保持するのは球面表現そのもの:
    ///   azimuthDeg   theta : LM 平面上の旋回角。L 軸正方向が 0 度、M 方向が +90 度
    ///   elevationDeg phi   : LM 平面からの仰角。90 度で工具軸が N に一致 (垂直姿勢)
    ///   spinAngleDeg       : 工具軸まわりの回転
    ///
    /// 工具軸 X は「TCP から工具本体へ向かう向き」= 母材から離れる向き。
    ///   X = (cos(phi) cos(theta),  cos(phi) sin(theta),  sin(phi))   … LMN 成分
    ///
    /// AWS の狙い角 w / 前進後退角 t は工具軸からの導出値で、WorkAngleDeg /
    /// TravelAngleDeg プロパティとして読み書きできる。保持はしない。
    ///
    /// ■ 極 (phi = 90 度) の扱い
    /// 工具軸が N に一致すると「どちら向きに倒れているか」という情報は姿勢の中に
    /// 存在しなくなり、theta は姿勢に影響しなくなる。この構造体は theta をそのまま
    /// 保持し続けるので、垂直を経由しても旋回角は失われない。
    /// 逆に言うと (theta=30, phi=90) と (theta=100, phi=90) は同じ工具軸を表す。
    /// これは意図した冗長性で、編集操作の連続性のために必要。
    /// 一意な値が要るときは Normalize() を明示的に呼ぶ。
    ///
    /// ■ phi は 90 度を超えてよい
    /// theta を固定したまま phi を 90 度より大きくすると、工具軸は反対側へ倒れる。
    /// (theta, 95) は (theta+180, 85) と同じ姿勢だが、前者は phi を連続に動かせるので
    /// 垂直をまたぐドラッグで theta が 180 度飛ばない。
    /// </summary>
    [Serializable]
    public struct ToolPostureAngles
    {

        #region フィールドと構築

        /// <summary>
        /// 投影角 (w / t) として設定できる絶対値の上限。tan の発散を避ける。
        /// </summary>
        public const float MaxProjectedAngleDeg = 85f;

        /// <summary>
        /// これ未満の水平成分では旋回角が決まらないとみなす閾値。
        /// </summary>
        public const float PoleEpsilon = 1e-4f;

        [Tooltip("旋回角 theta : LM 平面上で L 軸正方向から測った角 [deg]")]
        public float azimuthDeg;

        [Tooltip("仰角 phi : LM 平面から測った角 [deg]。90 度で工具軸が N に一致")]
        public float elevationDeg;

        [Tooltip("トーチ回転角 : 工具軸まわりの回転 [deg]")]
        public float spinAngleDeg;

        public ToolPostureAngles(float azimuthDeg, float elevationDeg, float spinAngleDeg)
        {
            this.azimuthDeg = azimuthDeg;
            this.elevationDeg = elevationDeg;
            this.spinAngleDeg = spinAngleDeg;
        }

        /// <summary>
        /// 球面表現から構築する。コンストラクタと同じだが呼び出し側で意図が読める。
        /// </summary>
        public static ToolPostureAngles FromSpherical(float azimuthDeg, float elevationDeg, float spinDeg)
            => new ToolPostureAngles(azimuthDeg, elevationDeg, spinDeg);

        /// <summary>
        /// 投影角 (狙い角 / 前進後退角) から構築する。
        /// </summary>
        public static ToolPostureAngles FromProjected(float workDeg, float travelDeg, float spinDeg)
        {
            var r = new ToolPostureAngles(0f, 90f, spinDeg);
            r.SetProjected(workDeg, travelDeg);
            return r;
        }

        /// <summary>
        /// 垂直姿勢 (旋回角 0)。旋回角を保ちたい場合は elevationDeg だけを 90 にする。
        /// </summary>
        public static ToolPostureAngles Vertical => new ToolPostureAngles(0f, 90f, 0f);

        #endregion

        #region 工具軸

        /// <summary>
        /// 工具軸 X の LMN 成分 (x = L, y = M, z = N)。定義から常に単位ベクトル。
        /// </summary>
        public Vector3 GetAxisLmn()
        {
            float th = azimuthDeg * Mathf.Deg2Rad;
            float ph = elevationDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(ph);
            return new Vector3(c * Mathf.Cos(th), c * Mathf.Sin(th), Mathf.Sin(ph));
        }

        /// <summary>
        /// 工具軸 X のワールド方向。
        /// </summary>
        public Vector3 GetAxisWorld(in PathFrame frame)
            => frame.LmnToWorldDirection(GetAxisLmn()).normalized;

        /// <summary>
        /// LMN 成分から姿勢を設定する。
        /// 極付近 (工具軸が N に一致) では旋回角が決まらないので、現在の値をそのまま残す。
        /// この扱いを構造体の中に閉じ込めてあるので、どの経路から設定しても旋回角は失われない。
        /// </summary>
        public void SetAxisLmn(Vector3 lmn)
        {
            lmn = lmn.normalized;
            elevationDeg = Mathf.Asin(Mathf.Clamp(lmn.z, -1f, 1f)) * Mathf.Rad2Deg;

            float horizontal = Mathf.Sqrt(lmn.x * lmn.x + lmn.y * lmn.y);
            if (horizontal > PoleEpsilon)
                azimuthDeg = Mathf.Atan2(lmn.y, lmn.x) * Mathf.Rad2Deg;
        }

        public void SetAxisWorld(in PathFrame frame, Vector3 worldDir)
            => SetAxisLmn(frame.WorldDirectionToLmn(worldDir.normalized));

        #endregion

        #region 投影角 (導出値)

        /// <summary>
        /// 狙い角 w [deg]。LN 平面上で N から L 方向へ測った角 (AWS work angle)。
        /// 設定すると前進後退角 t を保ったまま工具軸が変わる。
        /// </summary>
        public float WorkAngleDeg
        {
            get
            {
                Vector3 x = GetAxisLmn();
                return Mathf.Atan2(x.x, Mathf.Max(x.z, 1e-5f)) * Mathf.Rad2Deg;
            }
            set => SetProjected(value, TravelAngleDeg);
        }

        /// <summary>
        /// 前進後退角 t [deg]。MN 平面上で N から M 方向へ測った角 (AWS travel angle)。
        /// 設定すると狙い角 w を保ったまま工具軸が変わる。
        /// </summary>
        public float TravelAngleDeg
        {
            get
            {
                Vector3 x = GetAxisLmn();
                return Mathf.Atan2(x.y, Mathf.Max(x.z, 1e-5f)) * Mathf.Rad2Deg;
            }
            set => SetProjected(WorkAngleDeg, value);
        }

        /// <summary>
        /// 投影角の組から工具軸を決める。X = normalize(tan(w) L + tan(t) M + N)。
        /// </summary>
        public void SetProjected(float workDeg, float travelDeg)
        {
            float w = Mathf.Clamp(workDeg, -MaxProjectedAngleDeg, MaxProjectedAngleDeg) * Mathf.Deg2Rad;
            float t = Mathf.Clamp(travelDeg, -MaxProjectedAngleDeg, MaxProjectedAngleDeg) * Mathf.Deg2Rad;
            SetAxisLmn(new Vector3(Mathf.Tan(w), Mathf.Tan(t), 1f));
        }

        /// <summary>
        /// LMN 成分から投影角を求める (姿勢を持たない純粋な変換)。
        /// </summary>
        public static void AnglesFromAxisLmn(Vector3 lmn, out float workDeg, out float travelDeg)
        {
            float n = Mathf.Max(lmn.z, 1e-5f);
            workDeg = Mathf.Atan2(lmn.x, n) * Mathf.Rad2Deg;
            travelDeg = Mathf.Atan2(lmn.y, n) * Mathf.Rad2Deg;
        }

        #endregion

        #region 傾き

        /// <summary>
        /// 面法線 N から工具軸までの傾き角 alpha [deg] (= 90 - phi)。
        /// phi が 90 度を超えると負になる (反対側へ倒れている)。
        /// </summary>
        public float TiltFromNormalDeg
        {
            get => 90f - elevationDeg;
            set => elevationDeg = 90f - value;
        }

        /// <summary>
        /// 旋回角が姿勢に影響する程度に傾いているか。
        /// </summary>
        public bool TiltIsSignificant(float thresholdDeg = 0.5f)
            => Mathf.Abs(TiltFromNormalDeg) > thresholdDeg;

        /// <summary>
        /// N と、LM 平面上の指定方位 d(theta) が張る平面の中で、
        /// N から工具軸までを測った符号付きの傾き角 [deg]。
        /// planeAzimuthDeg が自分の旋回角と一致していれば TiltFromNormalDeg と同じ。
        /// </summary>
        public float SignedTiltInPlaneDeg(float planeAzimuthDeg)
        {
            float a = planeAzimuthDeg * Mathf.Deg2Rad;
            Vector3 x = GetAxisLmn();
            float along = x.x * Mathf.Cos(a) + x.y * Mathf.Sin(a);
            return Mathf.Atan2(along, x.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 同じ姿勢を表す値のうち、phi を [-90, 90]、theta を [-180, 180) に収めた形にする。
        /// 編集中は連続性のために範囲外の値を許しているので、外部へ渡す前などに明示的に呼ぶ。
        /// </summary>
        public void Normalize()
        {
            float phi = Mathf.Repeat(elevationDeg + 180f, 360f) - 180f;   // [-180, 180)
            float theta = azimuthDeg;

            if (phi > 90f) { phi = 180f - phi; theta += 180f; }
            else if (phi < -90f) { phi = -180f - phi; theta += 180f; }

            elevationDeg = phi;
            azimuthDeg = Mathf.Repeat(theta + 180f, 360f) - 180f;
        }

        #endregion

        #region スピン

        /// <summary>
        /// スピン 0 度の基準ベクトル (工具軸に直交)。既定は進行方向 M を工具軸直交面へ投影したもの。
        /// 工具軸が M と平行になる退化時は L、さらに退化する場合は N へフォールバックする。
        /// </summary>
        public static Vector3 SpinZeroReference(in PathFrame frame, Vector3 axisWorld)
        {
            Vector3 r = frame.Feed - Vector3.Dot(frame.Feed, axisWorld) * axisWorld;
            if (r.sqrMagnitude < 1e-8f)
                r = frame.CrossFeed - Vector3.Dot(frame.CrossFeed, axisWorld) * axisWorld;
            if (r.sqrMagnitude < 1e-8f)
                r = frame.Normal - Vector3.Dot(frame.Normal, axisWorld) * axisWorld;
            return r.normalized;
        }

        public Vector3 GetSpinZeroReferenceWorld(in PathFrame frame, SpinReference spinReference = default)
            => spinReference.Resolve(frame, GetAxisWorld(frame));

        /// <summary>
        /// スピン適用後の工具基準ベクトル (工具軸に直交)。
        /// </summary>
        public Vector3 GetToolReferenceWorld(in PathFrame frame, SpinReference spinReference = default)
        {
            Vector3 x = GetAxisWorld(frame);
            return Quaternion.AngleAxis(spinAngleDeg, x) * spinReference.Resolve(frame, x);
        }

        #endregion

        #region 工具姿勢

        /// <summary>
        /// 工具の完全な姿勢。工具モデルのローカル軸 toolShaftAxis がワールドの工具軸 X に、
        /// toolReferenceAxis がスピン基準ベクトルに一致する回転を返す。
        /// toolShaftAxis と toolReferenceAxis は互いに直交していること。
        /// </summary>
        public Quaternion GetToolRotation(in PathFrame frame, Vector3 toolShaftAxis, Vector3 toolReferenceAxis,
                                          SpinReference spinReference = default)
        {
            Vector3 x = GetAxisWorld(frame);
            Vector3 u = Quaternion.AngleAxis(spinAngleDeg, x) * spinReference.Resolve(frame, x);

            Quaternion world = Quaternion.LookRotation(u, x);
            Quaternion local = Quaternion.LookRotation(toolReferenceAxis.normalized, toolShaftAxis.normalized);
            return world * Quaternion.Inverse(local);
        }

        /// <summary>
        /// 工具の姿勢 (回転) から旋回角・仰角・スピンを復元する。GetToolRotation の逆変換。
        ///
        /// 工具軸が N に一致する (極) 場合は旋回角が回転から決まらないので、
        /// SetAxisLmn と同じく現在の値をそのまま残す。
        /// </summary>
        public void SetToolRotation(in PathFrame frame, Quaternion toolRotation,
                                    Vector3 toolShaftAxis, Vector3 toolReferenceAxis,
                                    SpinReference spinReference = default)
        {
            Vector3 x = (toolRotation * toolShaftAxis.normalized).normalized;
            Vector3 u = (toolRotation * toolReferenceAxis.normalized).normalized;

            SetAxisWorld(frame, x);
            spinAngleDeg = Vector3.SignedAngle(spinReference.Resolve(frame, x), u, x);
        }

        /// <summary>
        /// 工具の姿勢 (回転) から新しい ToolPostureAngles を作る。
        /// </summary>
        public static ToolPostureAngles FromToolRotation(in PathFrame frame, Quaternion toolRotation,
                                                         Vector3 toolShaftAxis, Vector3 toolReferenceAxis,
                                    SpinReference spinReference = default)
        {
            var a = new ToolPostureAngles();
            a.SetToolRotation(frame, toolRotation, toolShaftAxis, toolReferenceAxis, spinReference);
            return a;
        }

        public override string ToString()
            => string.Format("theta={0:F2} phi={1:F2} spin={2:F2}", azimuthDeg, elevationDeg, spinAngleDeg);

        #endregion
    }
}
