namespace ToolPosture.Core
{
    /// <summary>
    /// 工具姿勢ギズモへフレーム (L, M, N) を供給する側のインターフェース。
    ///
    /// ギズモが必要とするのはフレームだけで、経路の持ち方 (点列 / スプライン /
    /// 外部システムからの受信) には依存しない。実データ構造がある場合は
    /// これを実装したアダプタを差し込めばよい。
    /// </summary>
    public interface IPathFrameSource
    {
        /// <summary>区間数 (点列なら 点数 - 1)。</summary>
        int SegmentCount { get; }

        /// <summary>区間 segment の位置 u (0..1) におけるフレーム。</summary>
        PathFrame GetFrame(int segment, float u);
    }
}
