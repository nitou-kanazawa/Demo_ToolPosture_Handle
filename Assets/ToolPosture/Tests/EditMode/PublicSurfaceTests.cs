using System.Linq;
using NUnit.Framework;
using ToolRuntimeGizmos.Core;
using ToolRuntimeGizmos.Gizmo;
using ToolRuntimeGizmos.Tool;

namespace ToolRuntimeGizmos.Tests
{
    /// <summary>
    /// 利用側 (アプリ) から見える型を固定する。
    ///
    /// 実装の詳細が public のままだと、アプリ側の補完に大量の型が出てきて
    /// 「このパッケージで何ができるのか」が読めなくなる。公開範囲を広げるのは
    /// 一方通行なので、増やすときは意図してこの一覧に足すこと。
    /// </summary>
    public class PublicSurfaceTests
    {
        /// <summary>アプリに見せる型。ここに無いものは internal であるべき。</summary>
        private static readonly string[] CorePublicTypes =
        {
            // 姿勢の表し方と、その間の変換
            "ToolRuntimeGizmos.Core.ZyxEulerAngles",
            "ToolRuntimeGizmos.Core.ToolAxisSpin",
            "ToolRuntimeGizmos.Core.TorchAngles",
            "ToolRuntimeGizmos.Core.LmnFrame",
            "ToolRuntimeGizmos.Core.ToolAxes",
            "ToolRuntimeGizmos.Core.TorchAngleIssues",
            "ToolRuntimeGizmos.Core.AxisRotation",
            "ToolRuntimeGizmos.Core.RobotPostureConvert",

            // ハンドルが持つ姿勢
            "ToolRuntimeGizmos.Core.PathFrame",
            "ToolRuntimeGizmos.Core.CrossFeedSide",
            "ToolRuntimeGizmos.Core.ToolPostureAngles",

            // 規約
            "ToolRuntimeGizmos.Core.HandednessConversion",
            "ToolRuntimeGizmos.Core.SpinReference",
            "ToolRuntimeGizmos.Core.SpinReferenceMode",
            "ToolRuntimeGizmos.Core.AngleConvention",
            "ToolRuntimeGizmos.Core.ToolPostureProfile",
        };

        private static readonly string[] RuntimePublicTypes =
        {
            // アプリが触る入口
            "ToolRuntimeGizmos.Tool.IToolPoseHandle",
            "ToolRuntimeGizmos.Tool.ToolPoseHandle",
            "ToolRuntimeGizmos.Tool.ToolPoseHandle+HandleMode",
            "ToolRuntimeGizmos.Tool.ToolPoseHandle+ViewMode",
            "ToolRuntimeGizmos.Tool.ToolPose",
            "ToolRuntimeGizmos.Tool.ToolPoseEvent",
            "ToolRuntimeGizmos.Tool.ToolHandleKind",
            "ToolRuntimeGizmos.Tool.ToolPostureFollower",

            // シーンに置く / インスペクタで設定するもの
            "ToolRuntimeGizmos.Gizmo.RuntimeGizmo",
            "ToolRuntimeGizmos.Gizmo.ToolPositionGizmo",
            "ToolRuntimeGizmos.Gizmo.ToolPostureGizmo",
            "ToolRuntimeGizmos.Gizmo.GizmoTheme",
            "ToolRuntimeGizmos.Gizmo.GizmoInputMode",
            "ToolRuntimeGizmos.Gizmo.GizmoAxisSpace",
            "ToolRuntimeGizmos.Gizmo.GizmoHandleId",
            "ToolRuntimeGizmos.Gizmo.GizmoDragResult",

            // 外部入力 (2D ビューなど) のアダプタが実装・利用するもの
            "ToolRuntimeGizmos.Gizmo.IGizmoRayProvider",
            "ToolRuntimeGizmos.Gizmo.IGizmoPointerSource",
            "ToolRuntimeGizmos.Gizmo.GizmoPointer",
            "ToolRuntimeGizmos.Gizmo.PointerSample",

            // BuildingExtraGeometry で自前の描画を足すためのもの
            "ToolRuntimeGizmos.Gizmo.GizmoMeshBuilder",
        };

        private static string[] ExportedTypesOf(System.Type anyTypeInAssembly)
            => anyTypeInAssembly.Assembly.GetExportedTypes()
                                .Select(t => t.FullName)
                                .OrderBy(n => n, System.StringComparer.Ordinal)
                                .ToArray();

        private static string[] Sorted(string[] names)
            => names.OrderBy(n => n, System.StringComparer.Ordinal).ToArray();

        [Test]
        public void Coreが公開している型は一覧どおり()
            => CollectionAssert.AreEqual(Sorted(CorePublicTypes), ExportedTypesOf(typeof(PathFrame)),
                                         "公開範囲が変わっている。意図した変更なら一覧を更新すること");

        [Test]
        public void Runtimeが公開している型は一覧どおり()
            => CollectionAssert.AreEqual(Sorted(RuntimePublicTypes), ExportedTypesOf(typeof(RuntimeGizmo)),
                                         "公開範囲が変わっている。意図した変更なら一覧を更新すること");

        [Test]
        public void 描画の後始末はアプリから呼べない()
        {
            // Clear / Apply は毎フレームの組み立て側が呼ぶもの。
            // BuildingExtraGeometry の中から呼ばれるとメッシュが壊れる
            foreach (string name in new[] { "Clear", "Apply", "VertexCount" })
                Assert.IsNull(typeof(GizmoMeshBuilder).GetMember(name,
                                  System.Reflection.BindingFlags.Public
                                | System.Reflection.BindingFlags.Instance).FirstOrDefault(),
                              name + " が公開されている");
        }
    }
}
