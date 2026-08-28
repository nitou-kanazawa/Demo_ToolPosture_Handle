using UnityEngine;

namespace ToolRuntimeGizmos.Core
{
    /// <summary>工具軸ベクトルと、その軸まわりの回転角。ツール姿勢の表し方のひとつ。</summary>
    /// <remarks>
    /// <see cref="ZyxEulerAngles"/> や <see cref="TorchAngles"/> と同じ姿勢を別の形で表したもの。
    ///
    /// 軸だけでは姿勢は決まらない (軸まわりの自由度が残る) ので回転角が要る。そしてその
    /// 回転角は「どこを 0 とするか」を決めないと意味を持たない。零基準は変換のたびに
    /// spinZeroReference で与える。ここが送り側と受け側で食い違うと、姿勢は正しいのに
    /// 角度だけずれる。
    ///
    /// ZYX と違いジンバルロックを持たない代わりに、工具軸が零基準と平行になると
    /// 回転角が決まらなくなる。特異点の位置が違うので、両方を持っておくと
    /// 片方が不定でももう片方は連続。
    /// </remarks>
    public readonly struct ToolAxisSpin
    {
        /// <summary>工具軸。<see cref="ToolAxes.Shaft"/> が向く方向。</summary>
        public readonly Vector3 Axis;
        /// <summary>工具軸まわりの回転角。右ねじの向きが正。</summary>
        public readonly float SpinDeg;

        public ToolAxisSpin(Vector3 axis, float spinDeg)
        {
            Axis = axis; SpinDeg = spinDeg;
        }

        public override string ToString() => string.Format("axis={0} spin={1:F2}", Axis, SpinDeg);
    }

    /// <summary>
    /// 姿勢全体を 1 回の回転で表したときの、回転軸と回転量。ロドリゲスの式にそのまま渡せる。
    /// </summary>
    /// <remarks>
    /// これはツール姿勢の表し方ではない。<see cref="ToolAxisSpin"/> とは別物で、
    /// ここの <see cref="Axis"/> は工具軸と一致しない。
    /// 姿勢を人が読む用途ではなく、回転を 1 回の回転として渡す用途のもの。
    /// </remarks>
    public readonly struct AxisRotation
    {
        public readonly Vector3 Axis;
        public readonly float AngleDeg;

        public AxisRotation(Vector3 axis, float angleDeg)
        {
            Axis = axis; AngleDeg = angleDeg;
        }

        public override string ToString() => string.Format("axis={0} angle={1:F2}", Axis, AngleDeg);
    }


    /// <summary>LMN 上のトーチ姿勢。</summary>
    public readonly struct TorchAngles
    {
        /// <summary>狙い角。LN 平面で N から L 方向へ測る。</summary>
        public readonly float WorkDeg;
        /// <summary>前進後退角。MN 平面で N から M 方向へ測る。</summary>
        public readonly float TravelDeg;
        /// <summary>工具軸まわりの回転。零基準は M を工具軸に直交させた向き。</summary>
        public readonly float SpinDeg;

        public TorchAngles(float workDeg, float travelDeg, float spinDeg)
        {
            WorkDeg = workDeg; TravelDeg = travelDeg; SpinDeg = spinDeg;
        }

        public override string ToString()
            => string.Format("work={0:F2} travel={1:F2} spin={2:F2}", WorkDeg, TravelDeg, SpinDeg);
    }

    /// <summary>工具モデルのどのローカル軸を何に向けるかの取り決め。</summary>
    /// <remarks>
    /// 使う座標系の軸で指定すること。ここだけ変換し忘れると、他が正しくても
    /// ちょうど 180 度ずれる (実測)。
    /// </remarks>
    public readonly struct ToolAxes
    {
        /// <summary>工具軸に向けるローカル軸。</summary>
        public readonly Vector3 Shaft;
        /// <summary>スピンの基準に向けるローカル軸。Shaft に直交化される。</summary>
        public readonly Vector3 Reference;

        public ToolAxes(Vector3 shaft, Vector3 reference)
        {
            Shaft = shaft.normalized;
            Reference = RobotPostureConvert.Reject(reference, Shaft);
        }

        /// <summary>Unity 側の既定 (up / forward)。</summary>
        public static ToolAxes Unity => new ToolAxes(Vector3.up, Vector3.forward);

        /// <summary>Z 上向きのロボット座標での既定。<see cref="Unity"/> を Y と Z の入れ替えで移したもの。</summary>
        public static ToolAxes Robot => new ToolAxes(new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f));
    }

    /// <summary>LMN + トーチ姿勢では表しきれなかったこと。</summary>
    [System.Flags]
    public enum TorchAngleIssues
    {
        None = 0,
        /// <summary>
        /// 工具軸が面の表側を向いていない。狙い角と前進後退角の組では復元できない
        /// (この 2 つは常に N 側を向く軸しか作れない)。
        /// </summary>
        AxisNotAboveSurface = 1,
        /// <summary>工具軸が進行方向 M と平行。スピンの零基準が決まらず値は不定。</summary>
        SpinUndefined = 2,
    }

    /// <summary>
    /// ツール姿勢の 3 つの表し方を相互に変換する。
    ///
    ///   1. ZYX オイラー角              <see cref="ZyxEulerAngles"/>
    ///   2. 工具軸 + 軸まわりの回転角    <see cref="ToolAxisSpin"/>
    ///   3. 母材フレーム + トーチ姿勢      <see cref="WorkFrame"/> + <see cref="TorchAngles"/>
    ///
    /// どれも回転行列を経由する。3 表現 x 3 表現を個別に書かず、行列との出入りだけを持つ。
    ///
    /// 2 と 3 は姿勢そのものに加えて規約が要る。工具モデルの軸割当 (<see cref="ToolAxes"/>)、
    /// 2 では回転角の零基準、3 ではフレーム。規約が食い違うと姿勢は正しいのに数値だけずれる。
    ///
    /// <see cref="AxisRotation"/> はこの 3 つには含まれない。姿勢の表し方ではなく、
    /// 回転を 1 回の回転として外へ渡すための形。
    /// </summary>
    /// <remarks>
    /// クォータニオンを一切使わない。三角関数と内積・外積だけで閉じている。
    ///
    /// 行列は成分の入れ物でしかないので、右手系・左手系の区別はここには現れない。
    /// 入力をすべて同じ座標系で揃えれば、出力もその座標系の値になる。座標系をまたぐ場合は
    /// <see cref="HandednessConversion"/> でベクトルを入れ替えてからここへ渡すこと。
    /// </remarks>
    public static class RobotPostureConvert
    {
        /// <summary>ジンバルロックとみなす cos(ピッチ) の閾値。</summary>
        private const float GimbalLockEpsilon = 1e-5f;
        /// <summary>回転が無いとみなす閾値 (1 - cos)。</summary>
        private const float IdentityEpsilon = 1e-6f;
        /// <summary>射影が消えたとみなす閾値 (長さの 2 乗)。</summary>
        private const float DegenerateEpsilon = 1e-8f;

        #region 1. ZYX オイラー角

        /// <summary>ZYX オイラー角から回転行列を組む。R = Rz * Ry * Rx。</summary>
        public static Matrix4x4 FromZyx(ZyxEulerAngles e)
        {
            float cz = Mathf.Cos(e.zDeg * Mathf.Deg2Rad), sz = Mathf.Sin(e.zDeg * Mathf.Deg2Rad);
            float cy = Mathf.Cos(e.yDeg * Mathf.Deg2Rad), sy = Mathf.Sin(e.yDeg * Mathf.Deg2Rad);
            float cx = Mathf.Cos(e.xDeg * Mathf.Deg2Rad), sx = Mathf.Sin(e.xDeg * Mathf.Deg2Rad);

            return Rows(cz * cy, cz * sy * sx - sz * cx, cz * sy * cx + sz * sx,
                        sz * cy, sz * sy * sx + cz * cx, sz * sy * cx - cz * sx,
                        -sy, cy * sx, cy * cx);
        }

        /// <summary>回転行列を ZYX オイラー角に分解する。</summary>
        /// <remarks>
        /// ピッチ ±90 度では z と x が同じ回転を作り分離できない。その場合は z に 0 を寄せる。
        /// 姿勢そのものは正しいが、この帯をまたぐと数値が飛ぶ。連続性が要るなら
        /// <see cref="ToTorch"/> か <see cref="ToAxisRotation"/> を併用すること。
        /// </remarks>
        public static ZyxEulerAngles ToZyx(Matrix4x4 m)
        {
            // cp は 1 - sin^2 からではなく行列要素から直接求める。
            // ピッチが ±90 度ちょうどのとき前者は float で 1e-4 程度の誤差が残り、
            // 通常分岐に落ちて atan2(≈0, ≈0) を評価してしまう
            float cp = Mathf.Sqrt(m[0, 0] * m[0, 0] + m[1, 0] * m[1, 0]);
            float sp = -m[2, 0];

            if (cp < GimbalLockEpsilon)
            {
                // ロック時は z と x が (ピッチ +90 なら差、-90 なら和) の形でしか決まらない。
                // z = 0 に寄せて x を解く。符号はピッチの向きで変わる
                //   +90 : m01 = sin(x - z),  m11 = cos(x - z)  -> x = atan2( m01, m11)
                //   -90 : m01 = -sin(x + z), m11 = cos(x + z)  -> x = atan2(-m01, m11)
                bool positivePitch = sp >= 0f;
                float x = positivePitch ? Mathf.Atan2(m[0, 1], m[1, 1])
                                        : Mathf.Atan2(-m[0, 1], m[1, 1]);

                return new ZyxEulerAngles(0f, positivePitch ? 90f : -90f, x * Mathf.Rad2Deg);
            }

            return new ZyxEulerAngles(Mathf.Atan2(m[1, 0], m[0, 0]) * Mathf.Rad2Deg,
                                      Mathf.Atan2(sp, cp) * Mathf.Rad2Deg,
                                      Mathf.Atan2(m[2, 1], m[2, 2]) * Mathf.Rad2Deg);
        }

        /// <summary>ピッチが ±90 度に近く、z と x の分け方が信用できないか。</summary>
        public static bool IsNearGimbalLock(Matrix4x4 m, float marginDeg = 1f)
            => Mathf.Sqrt(m[0, 0] * m[0, 0] + m[1, 0] * m[1, 0]) < Mathf.Sin(marginDeg * Mathf.Deg2Rad);

        #endregion

        #region 2. 工具軸 + 軸まわりの回転角

        /// <summary>工具軸と、その軸まわりの回転角から回転行列を組む。</summary>
        /// <param name="posture">工具軸と回転角。</param>
        /// <param name="tool">工具モデルの軸割当。</param>
        /// <param name="spinZeroReference">
        /// 回転角 0 のときに <see cref="ToolAxes.Reference"/> が向く方向。
        /// 工具軸に平行な成分は落として使う。
        /// </param>
        public static Matrix4x4 FromToolAxisSpin(ToolAxisSpin posture, ToolAxes tool,
                                                 Vector3 spinZeroReference)
        {
            Vector3 axis = posture.Axis.normalized;
            Vector3 zero = Reject(spinZeroReference, axis);

            float c = Mathf.Cos(posture.SpinDeg * Mathf.Deg2Rad);
            float s = Mathf.Sin(posture.SpinDeg * Mathf.Deg2Rad);
            Vector3 reference = zero * c + Vector3.Cross(axis, zero) * s;

            return FromToolAxes(axis, reference, tool);
        }

        /// <summary>回転行列を工具軸と軸まわりの回転角に分解する。</summary>
        /// <remarks>
        /// 工具軸は常に求まる。決まらなくなるのは回転角の方だけで、その場合も
        /// <paramref name="posture"/> の軸は使える。
        /// </remarks>
        /// <returns>回転角が定まったか。工具軸が零基準と平行なら false。</returns>
        public static bool ToToolAxisSpin(Matrix4x4 m, ToolAxes tool, Vector3 spinZeroReference,
                                          out ToolAxisSpin posture)
        {
            Vector3 axis = m.MultiplyVector(tool.Shaft).normalized;
            Vector3 reference = m.MultiplyVector(tool.Reference).normalized;

            Vector3 zero = spinZeroReference - axis * Vector3.Dot(spinZeroReference, axis);
            if (zero.sqrMagnitude < DegenerateEpsilon)
            {
                posture = new ToolAxisSpin(axis, 0f);
                return false;
            }
            zero.Normalize();

            // reference は工具軸に直交しているので、そのまま符号付き角が取れる
            float spinDeg = Mathf.Atan2(Vector3.Dot(Vector3.Cross(zero, reference), axis),
                                        Vector3.Dot(zero, reference)) * Mathf.Rad2Deg;

            posture = new ToolAxisSpin(axis, spinDeg);
            return true;
        }

        #endregion

        #region 姿勢を 1 回の回転として渡す (3 表現とは別)

        /// <summary>回転軸と回転量から回転行列を組む (ロドリゲスの式)。</summary>
        public static Matrix4x4 FromAxisRotation(AxisRotation a)
        {
            Vector3 k = a.Axis.normalized;
            float c = Mathf.Cos(a.AngleDeg * Mathf.Deg2Rad);
            float s = Mathf.Sin(a.AngleDeg * Mathf.Deg2Rad);
            float t = 1f - c;

            // R = I + sin θ K + (1 - cos θ) K^2
            return Rows(c + k.x * k.x * t, k.x * k.y * t - k.z * s, k.x * k.z * t + k.y * s,
                        k.y * k.x * t + k.z * s, c + k.y * k.y * t, k.y * k.z * t - k.x * s,
                        k.z * k.x * t - k.y * s, k.z * k.y * t + k.x * s, c + k.z * k.z * t);
        }

        /// <summary>回転行列を回転軸と回転量に分解する。</summary>
        /// <remarks>回転量が 0 のときは軸が定まらないので (1, 0, 0) を返す。</remarks>
        public static AxisRotation ToAxisRotation(Matrix4x4 m)
        {
            float trace = m[0, 0] + m[1, 1] + m[2, 2];
            float cos = Mathf.Clamp((trace - 1f) * 0.5f, -1f, 1f);

            if (1f - cos < IdentityEpsilon) return new AxisRotation(Vector3.right, 0f);

            float angleDeg = Mathf.Acos(cos) * Mathf.Rad2Deg;

            var skew = new Vector3(m[2, 1] - m[1, 2], m[0, 2] - m[2, 0], m[1, 0] - m[0, 1]);
            if (skew.sqrMagnitude > DegenerateEpsilon)
                return new AxisRotation(skew.normalized, angleDeg);

            // 180 度付近。歪対称部が消えるので R + I の最も長い列から取る
            return new AxisRotation(LargestColumnOfSymmetricPart(m), angleDeg);
        }

        /// <summary>
        /// 工具軸と基準方向から、直接 回転軸と回転量を求める。
        /// ロドリゲスの式を取る API (Foo(Vector3 axis, float angle)) への入口。
        /// </summary>
        public static AxisRotation ToolAxesToAxisRotation(Vector3 axis, Vector3 reference, ToolAxes tool)
            => ToAxisRotation(FromToolAxes(axis, reference, tool));

        #endregion

        #region 3. LMN フレーム + トーチ姿勢

        /// <summary>母材フレームとトーチ姿勢から回転行列を組む。</summary>
        public static Matrix4x4 FromTorch(WorkFrame f, TorchAngles t, ToolAxes tool)
        {
            float cw = Mathf.Cos(t.WorkDeg * Mathf.Deg2Rad), sw = Mathf.Sin(t.WorkDeg * Mathf.Deg2Rad);
            float ct = Mathf.Cos(t.TravelDeg * Mathf.Deg2Rad), st = Mathf.Sin(t.TravelDeg * Mathf.Deg2Rad);

            // 工具軸。(tan w, tan t, 1) と同じ向きだが、cos w cos t を掛けてあるので
            // 角度が 90 度でも発散しない
            Vector3 axis = (sw * ct * f.CrossFeed + cw * st * f.Feed + cw * ct * f.Normal).normalized;

            // 回転角の零基準は進行方向。工具軸が決まればあとは軸まわりの回転
            return FromToolAxisSpin(new ToolAxisSpin(axis, t.SpinDeg), tool, f.Feed);
        }

        /// <summary>回転行列を母材フレーム上のトーチ姿勢に分解する。</summary>
        /// <returns>表しきれなかったこと。<see cref="TorchAngleIssues.None"/> なら往復できる。</returns>
        public static TorchAngleIssues ToTorch(Matrix4x4 m, WorkFrame f, ToolAxes tool,
                                               out TorchAngles angles)
        {
            // 回転角の零基準は進行方向。ここは工具軸 + 回転角の分解そのもの
            bool spinDefined = ToToolAxisSpin(m, tool, f.Feed, out ToolAxisSpin posture);
            Vector3 axis = posture.Axis;

            float dn = Vector3.Dot(axis, f.Normal);
            angles = new TorchAngles(Mathf.Atan2(Vector3.Dot(axis, f.CrossFeed), dn) * Mathf.Rad2Deg,
                                     Mathf.Atan2(Vector3.Dot(axis, f.Feed), dn) * Mathf.Rad2Deg,
                                     posture.SpinDeg);

            TorchAngleIssues issues = dn > 0f ? TorchAngleIssues.None
                                              : TorchAngleIssues.AxisNotAboveSurface;
            return spinDefined ? issues : issues | TorchAngleIssues.SpinUndefined;
        }

        #endregion

        #region 3 表現を直結する 6 通り

        public static bool ZyxToToolAxisSpin(ZyxEulerAngles e, ToolAxes tool,
                                             Vector3 spinZeroReference, out ToolAxisSpin posture)
            => ToToolAxisSpin(FromZyx(e), tool, spinZeroReference, out posture);

        public static ZyxEulerAngles ToolAxisSpinToZyx(ToolAxisSpin posture, ToolAxes tool,
                                                       Vector3 spinZeroReference)
            => ToZyx(FromToolAxisSpin(posture, tool, spinZeroReference));

        public static TorchAngleIssues ZyxToTorch(ZyxEulerAngles e, WorkFrame f, ToolAxes tool,
                                                  out TorchAngles angles)
            => ToTorch(FromZyx(e), f, tool, out angles);

        public static ZyxEulerAngles TorchToZyx(WorkFrame f, TorchAngles t, ToolAxes tool)
            => ToZyx(FromTorch(f, t, tool));

        /// <remarks>
        /// トーチ姿勢の回転角の零基準は進行方向 M なので、
        /// <paramref name="spinZeroReference"/> に M を渡せば
        /// <see cref="ToolAxisSpin.SpinDeg"/> と <see cref="TorchAngles.SpinDeg"/> は一致する。
        /// </remarks>
        public static bool TorchToToolAxisSpin(WorkFrame f, TorchAngles t, ToolAxes tool,
                                               Vector3 spinZeroReference, out ToolAxisSpin posture)
            => ToToolAxisSpin(FromTorch(f, t, tool), tool, spinZeroReference, out posture);

        public static TorchAngleIssues ToolAxisSpinToTorch(ToolAxisSpin posture, ToolAxes tool,
                                                           Vector3 spinZeroReference,
                                                           WorkFrame f, out TorchAngles angles)
            => ToTorch(FromToolAxisSpin(posture, tool, spinZeroReference), f, tool, out angles);

        #endregion

        #region 部品 (他の変換もここを使う)

        /// <summary>
        /// 工具軸と、その軸まわりの捻れを表す基準方向から回転行列を組む。
        /// R = [x u x*u] * [s1 s2 s1*s2]^T (工具のローカル基底をこの座標系の基底へ移す)。
        /// </summary>
        public static Matrix4x4 FromToolAxes(Vector3 axis, Vector3 reference, ToolAxes tool)
        {
            Vector3 x = axis.normalized;
            Vector3 u = Reject(reference, x);

            return Basis(x, u, Vector3.Cross(x, u))
                 * Basis(tool.Shaft, tool.Reference, Vector3.Cross(tool.Shaft, tool.Reference)).transpose;
        }

        /// <summary>
        /// v から axis 成分を抜いて正規化する。平行で取り出せない場合は
        /// 軸に直交する適当な向きを返す (基準が決まらない縮退)。
        /// </summary>
        internal static Vector3 Reject(Vector3 v, Vector3 axis)
        {
            Vector3 a = axis.normalized;
            Vector3 r = v - a * Vector3.Dot(v, a);
            if (r.sqrMagnitude > 1e-10f) return r.normalized;

            Vector3 seed = Mathf.Abs(a.x) > 0.9f ? Vector3.up : Vector3.right;
            return Vector3.Cross(a, seed).normalized;
        }

        /// <summary>3 本の列ベクトルから行列を組む。</summary>
        internal static Matrix4x4 Basis(Vector3 c0, Vector3 c1, Vector3 c2)
        {
            Matrix4x4 m = Matrix4x4.identity;
            m.SetColumn(0, new Vector4(c0.x, c0.y, c0.z, 0f));
            m.SetColumn(1, new Vector4(c1.x, c1.y, c1.z, 0f));
            m.SetColumn(2, new Vector4(c2.x, c2.y, c2.z, 0f));
            return m;
        }

        #endregion

        #region 内部

        private static Vector3 LargestColumnOfSymmetricPart(Matrix4x4 m)
        {
            // R + I の列のうち、最も長いものが回転軸に平行
            Vector3 best = Vector3.right;
            float bestLen = -1f;

            for (int c = 0; c < 3; c++)
            {
                var col = new Vector3(m[0, c] + (c == 0 ? 1f : 0f),
                                      m[1, c] + (c == 1 ? 1f : 0f),
                                      m[2, c] + (c == 2 ? 1f : 0f));
                if (col.sqrMagnitude <= bestLen) continue;
                bestLen = col.sqrMagnitude;
                best = col;
            }
            return best.normalized;
        }

        private static Matrix4x4 Rows(float m00, float m01, float m02,
                                      float m10, float m11, float m12,
                                      float m20, float m21, float m22)
        {
            Matrix4x4 m = Matrix4x4.identity;
            m[0, 0] = m00; m[0, 1] = m01; m[0, 2] = m02;
            m[1, 0] = m10; m[1, 1] = m11; m[1, 2] = m12;
            m[2, 0] = m20; m[2, 1] = m21; m[2, 2] = m22;
            return m;
        }

        #endregion
    }
}
