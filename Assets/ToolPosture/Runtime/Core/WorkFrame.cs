using UnityEngine;

namespace ToolRuntimeGizmos.Core
{
    /// <summary>
    /// L (直交方向) を進行方向のどちら側に取るか。
    /// </summary>
    public enum CrossFeedSide
    {
        /// <summary>
        /// 進行方向を向いたときの右側。右手系での L = M x N と同じ幾何。
        /// </summary>
        RightOfTravel = 0,

        /// <summary>
        /// 進行方向を向いたときの左側。
        /// </summary>
        LeftOfTravel = 1,
    }

    /// <summary>
    /// 母材側の正規直交フレーム (L, M, N)。工具の角度はすべてこの上で測る。
    ///   M = Feed      進行方向
    ///   N = Normal    面法線     M に対して直交化済み
    ///   L = CrossFeed 直交方向   M と N の両方に直交
    ///
    /// 一般名称との対応:
    ///   M ... feed direction / travel direction / 接線 T
    ///   N ... surface normal / 面法線
    ///   L ... cross-feed / 従法線 B
    ///
    /// このパッケージは経路を知らない。持つのは常に 1 つのフレームだけで、
    /// それをどこから作るか (溶接線、面、治具) は利用側の仕事。
    /// 工具側の軸割当は <see cref="ToolAxes"/> が持つ。
    ///
    /// 注意: 右手系での定義は L = M x N だが Unity は左手系なので、
    /// 同じ幾何 (進行方向の右側) を得るには Vector3.Cross(N, M) を使う。
    /// 別の座標系のフレームを持ち込む場合、side を指定する <see cref="TryCreate"/> では
    /// 側が裏返る。L が分かっているなら <see cref="TryFromBasis"/> を使うこと。
    /// </summary>
    public readonly struct WorkFrame
    {
        private const float Eps = 1e-6f;

        public readonly Vector3 Origin;
        public readonly Vector3 CrossFeed;
        public readonly Vector3 Feed;
        public readonly Vector3 Normal;
        public readonly bool IsValid;

        /// <summary>
        /// 検証を行わない生成。正規直交であることが保証できる内部からのみ使う。
        /// 外部からは TryCreate / TryFromBasis / Fallback を通すこと。
        /// </summary>
        private WorkFrame(Vector3 origin, Vector3 crossFeed, Vector3 feed, Vector3 normal)
        {
            Origin = origin;
            CrossFeed = crossFeed;
            Feed = feed;
            Normal = normal;
            IsValid = true;
        }

        /// <summary>
        /// 退化した区間に使う既定フレーム (M = +Z, N = +Y)。
        /// </summary>
        public static WorkFrame Fallback(Vector3 origin)
            => new WorkFrame(origin, Vector3.right, Vector3.forward, Vector3.up);

        /// <summary>
        /// 区間ベクトルと生の法線からフレームを構築する。
        /// 生の法線は進行方向と直交しているとは限らないので Gram-Schmidt で直交化する。
        /// 区間長ゼロ、または法線が進行方向と平行な場合は false を返す。
        /// </summary>
        public static bool TryCreate(Vector3 origin, Vector3 travel, Vector3 rawNormal, CrossFeedSide side, out WorkFrame frame)
        {
            frame = default;

            float travelLen = travel.magnitude;
            if (travelLen < Eps) return false;              // p(i+1) == p(i)
            Vector3 m = travel / travelLen;

            Vector3 n = rawNormal - Vector3.Dot(rawNormal, m) * m;
            float nLen = n.magnitude;
            if (nLen < Eps) return false;                   // 法線が進行方向と平行
            n /= nLen;

            Vector3 l = side == CrossFeedSide.RightOfTravel
                ? Vector3.Cross(n, m)
                : Vector3.Cross(m, n);

            frame = new WorkFrame(origin, l.normalized, m, n);
            return true;
        }

        /// <summary>
        /// 既に (ほぼ) 正規直交な三つ組からフレームを作る。
        ///
        /// 外部の回転行列やロボットのツール座標系から持ち込んだ基底など、L も含めて
        /// 分かっている場合の入口。M と N を正規直交化した上で L を計算し直すので、
        /// 多少の数値誤差は吸収される。L は与えられたベクトルと同じ側になる。
        /// </summary>
        public static bool TryFromBasis(Vector3 origin, Vector3 crossFeed, Vector3 feed, Vector3 normal,
                                        out WorkFrame frame)
        {
            CrossFeedSide side = Vector3.Dot(crossFeed, Vector3.Cross(normal, feed)) >= 0f
                ? CrossFeedSide.RightOfTravel
                : CrossFeedSide.LeftOfTravel;

            return TryCreate(origin, feed, normal, side, out frame);
        }

        /// <summary>
        /// 向きはそのままに原点だけ差し替える。
        /// 姿勢の基準は母材側から、位置は工具側から、と供給元が分かれている場合に使う。
        /// </summary>
        public WorkFrame WithOrigin(Vector3 origin)
            => IsValid ? new WorkFrame(origin, CrossFeed, Feed, Normal) : Fallback(origin);

        /// <summary>
        /// LMN 成分 (x = L, y = M, z = N) をワールド方向に変換する。
        /// </summary>
        public Vector3 LmnToWorldDirection(Vector3 lmn)
            => CrossFeed * lmn.x + Feed * lmn.y + Normal * lmn.z;

        /// <summary>
        /// ワールド方向を LMN 成分 (x = L, y = M, z = N) に変換する。
        /// </summary>
        public Vector3 WorldDirectionToLmn(Vector3 dir) => new Vector3(
            Vector3.Dot(dir, CrossFeed),
            Vector3.Dot(dir, Feed),
            Vector3.Dot(dir, Normal));

        /// <summary>
        /// フレームの姿勢 (ローカル +X = L, +Y = N, +Z = M)。
        /// </summary>
        public Quaternion Rotation => Quaternion.LookRotation(Feed, Normal);
    }
}
