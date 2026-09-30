using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor.Tests {
    /// <summary>
    /// Covers the graph's save-profile identity: a graph id bound to a game-side id must
    /// compose the exact profile the game selects, auto-minted ids must stay unique, and
    /// reading a profile name must not dirty the asset.
    /// </summary>
    [TestFixture]
    public class UnlockGraphLayoutDataTests {
        private UnlockGraphLayoutData _data;

        [SetUp]
        public void SetUp() {
            _data = ScriptableObject.CreateInstance<UnlockGraphLayoutData>();
        }

        [TearDown]
        public void TearDown() {
            Object.DestroyImmediate(_data);
        }

        private static void BindGraphId(UnlockGraphLayoutData data, string id) {
            var so = new SerializedObject(data);
            so.FindProperty("_graphId").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void BoundGraphId_ComposesTheSameProfileTheGameComposes() {
            // Capitals on both segments: the previous string interpolation re-encoded the whole
            // id and could never match SaveProfile.Id.
            BindGraphId(_data, "Release");
            _data.saveSlots = new List<string> { "Agamemnon" };
            _data.activeSlotIndex = 0;

            Assert.AreEqual(SaveProfile.Id("Release", "Agamemnon"), _data.ActiveSaveProfile);
        }

        [Test]
        public void AutoMintedGraphIds_AreDistinctPerGraph() {
            var other = ScriptableObject.CreateInstance<UnlockGraphLayoutData>();
            try {
                Assert.IsNotEmpty(_data.GraphId, "OnEnable should mint an id.");
                Assert.AreNotEqual(_data.GraphId, other.GraphId);
                Assert.AreNotEqual(_data.ActiveSaveProfile, other.ActiveSaveProfile);
            }
            finally {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void ResetGraphId_MintsAFreshId() {
            string before = _data.GraphId;
            _data.ResetGraphId();

            Assert.IsNotEmpty(_data.GraphId, "A duplicated graph must not end up with an empty id.");
            Assert.AreNotEqual(before, _data.GraphId);
        }

        [Test]
        public void ReadingActiveSaveProfile_DoesNotDirtyTheAsset() {
            EditorUtility.ClearDirty(_data);

            _ = _data.ActiveSaveProfile;

            Assert.IsFalse(EditorUtility.IsDirty(_data),
                "Reading a profile name must not mark the asset dirty.");
        }
    }
}
