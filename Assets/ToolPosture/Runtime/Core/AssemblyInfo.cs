using System.Runtime.CompilerServices;

// パッケージの内部実装は internal にしてある。
// 同じパッケージ内の他アセンブリとテストにだけ見せる。
// 利用側 (アプリ) には見せない。ここに足す前に、本当に公開すべき API でないかを考えること。
[assembly: InternalsVisibleTo("ToolRuntimeGizmos.Runtime")]
[assembly: InternalsVisibleTo("ToolRuntimeGizmos.Editor")]
[assembly: InternalsVisibleTo("ToolRuntimeGizmos.Tests")]
