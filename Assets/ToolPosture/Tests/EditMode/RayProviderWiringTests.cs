using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToolRuntimeGizmos.Gizmo;
using ToolRuntimeGizmos.Tool;

namespace ToolRuntimeGizmos.Tests
{
    /// <summary>
    /// 2D のレイ供給元をインスペクタから配線できること。
    ///
    /// フィールドを MonoBehaviour 型にしていたときは、GameObject を落とすと Unity が
    /// その中のコンポーネントを 1 つ勝手に選んでいた。実装していないものが入っても
    /// 型としては通るので、黙って null になり 2D の操作が丸ごと効かなくなっていた。
    /// </summary>
    public class RayProviderWiringTests
    {
        private GameObject _handleGo;
        private GameObject _providerGo;
        private ToolPoseHandle _handle;

        /// <summary>供給元の実装。順序の影響を見るため他のコンポーネントと同居させる。</summary>
        private class StubRayProvider : MonoBehaviour, IGizmoRayProvider
        {
            public bool TryScreenToRay(Vector2 screenPosition, out Ray ray)
            {
                ray = new Ray(Vector3.zero, Vector3.forward);
                return true;
            }
        }

        private class Bystander : MonoBehaviour { }

        [SetUp]
        public void SetUp()
        {
            _handleGo = new GameObject("TestHandle");
            _handle = _handleGo.AddComponent<ToolPoseHandle>();
            _providerGo = new GameObject("TestProvider");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_handleGo);
            Object.DestroyImmediate(_providerGo);
        }

        private void SetRayProvider(GameObject go)
            => typeof(ToolPoseHandle).GetField("rayProvider", BindingFlags.NonPublic | BindingFlags.Instance)
                                     .SetValue(_handle, go);

        private object Resolve()
        {
            typeof(ToolPoseHandle)
                .GetMethod("ResolveRayProvider", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_handle, null);

            return typeof(ToolPoseHandle)
                   .GetField("_provider", BindingFlags.NonPublic | BindingFlags.Instance)
                   .GetValue(_handle);
        }

        [Test]
        public void 実装しているコンポーネントを持つGameObjectから引ける()
        {
            StubRayProvider provider = _providerGo.AddComponent<StubRayProvider>();
            SetRayProvider(_providerGo);

            Assert.AreSame(provider, Resolve());
        }

        [Test]
        public void 他のコンポーネントが先に付いていても取り違えない()
        {
            // MonoBehaviour 型のフィールドだと、ここで Bystander が入りうる形だった
            _providerGo.AddComponent<Bystander>();
            StubRayProvider provider = _providerGo.AddComponent<StubRayProvider>();
            _providerGo.AddComponent<Bystander>();

            SetRayProvider(_providerGo);

            Assert.AreSame(provider, Resolve());
        }

        [Test]
        public void 実装が無ければ警告して未設定のままにする()
        {
            _providerGo.AddComponent<Bystander>();
            SetRayProvider(_providerGo);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("IGizmoRayProvider"));

            Assert.IsNull(Resolve(), "実装が無いのに何かが入っている");
        }

        [Test]
        public void 未設定なら黙って未設定のままにする()
        {
            SetRayProvider(null);
            Assert.IsNull(Resolve());
        }

        [Test]
        public void 差し替えると引き直す()
        {
            SetRayProvider(_providerGo);
            _providerGo.AddComponent<StubRayProvider>();
            Assert.IsNotNull(Resolve());

            SetRayProvider(null);
            Assert.IsNull(Resolve(), "外しても前の供給元が残っている");
        }
    }
}
