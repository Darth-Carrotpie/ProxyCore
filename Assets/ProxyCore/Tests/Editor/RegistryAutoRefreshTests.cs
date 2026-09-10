using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ProxyCore.Editor.Tests
{
    /// <summary>
    /// Covers the two non-obvious pieces of automatic registry refresh: resolving which
    /// definition type a registry holds, and deciding whether a refresh actually changed
    /// anything. The second one is what keeps an import-driven refresh from rewriting every
    /// registry asset on every import.
    ///
    /// In-memory ScriptableObjects only — no asset I/O, so the project is never touched.
    /// </summary>
    [TestFixture]
    public sealed class RegistryAutoRefreshTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
                if (obj != null) Object.DestroyImmediate(obj);
            _created.Clear();
        }

        private EventMessage NewEvent()
        {
            var evt = ScriptableObject.CreateInstance<EventMessage>();
            _created.Add(evt);
            return evt;
        }

        // ── Which definition type does a registry hold? ───────────────────────────────

        [Test]
        public void GetDefinitionType_ResolvesGenericArgumentThroughConcreteRegistry()
        {
            Assert.AreEqual(typeof(EventMessage),
                RefreshAllRegistries.GetDefinitionType(typeof(EventCoordinator)),
                "EventCoordinator is a BaseRegistry<EventMessage>, so its definition type is EventMessage.");
        }

        [Test]
        public void GetDefinitionType_IsNullForNonRegistries()
        {
            Assert.IsNull(RefreshAllRegistries.GetDefinitionType(typeof(EventMessage)),
                "A definition is not a registry.");
            Assert.IsNull(RefreshAllRegistries.GetDefinitionType(typeof(ScriptableObject)));
        }

        // ── Did the refresh change anything? ──────────────────────────────────────────

        [Test]
        public void SameDefinitions_TrueForIdenticalLists()
        {
            var a = NewEvent();
            var b = NewEvent();

            Assert.IsTrue(BaseRegistry<EventMessage>.SameDefinitions(
                new List<EventMessage> { a, b },
                new List<EventMessage> { a, b }));

            Assert.IsTrue(BaseRegistry<EventMessage>.SameDefinitions(
                new List<EventMessage>(),
                new List<EventMessage>()),
                "Two empty lists are unchanged — an empty project must not dirty every registry.");
        }

        [Test]
        public void SameDefinitions_FalseWhenContentOrderOrHolesDiffer()
        {
            var a = NewEvent();
            var b = NewEvent();

            Assert.IsFalse(BaseRegistry<EventMessage>.SameDefinitions(
                new List<EventMessage> { a },
                new List<EventMessage> { a, b }),
                "A newly imported definition must be detected.");

            Assert.IsFalse(BaseRegistry<EventMessage>.SameDefinitions(
                new List<EventMessage> { a, b },
                new List<EventMessage> { b, a }),
                "Order is part of the serialized list.");

            // The deletion case: the asset is gone, leaving a null hole the registry must purge.
            Assert.IsFalse(BaseRegistry<EventMessage>.SameDefinitions(
                new List<EventMessage> { a, null },
                new List<EventMessage> { a }));

            Assert.IsFalse(BaseRegistry<EventMessage>.SameDefinitions(
                null,
                new List<EventMessage> { a }));
        }
    }
}
