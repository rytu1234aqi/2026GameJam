#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;
using Spotlight.Bootstrap;
using UnityEditor;
using UnityEngine;

namespace Spotlight.Integration.Tests
{
    public sealed class AdvancedEffectAuthoringTests
    {
        [Test]
        public void SetupRepeatedlyPreservesConfiguredFormalAssets()
        {
            Type tool = Type.GetType("Spotlight.EditorTools.AdvancedEffectSetupTool, Spotlight.Editor", true);
            MethodInfo setup = tool.GetMethod("CreateOrComplete", BindingFlags.Static | BindingFlags.Public);
            setup.Invoke(null, null);
            PrototypeCatalogSO formal = AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>("Assets/_Game/Data/Prototype/P1/PrototypeCatalog.asset");
            string[] reactionJson = Array.ConvertAll(formal.Reactions, r => EditorJsonUtility.ToJson(r));
            string[] statusJson = Array.ConvertAll(formal.Statuses, s => EditorJsonUtility.ToJson(s));
            setup.Invoke(null, null);
            CollectionAssert.AreEqual(reactionJson, Array.ConvertAll(formal.Reactions, r => EditorJsonUtility.ToJson(r)));
            CollectionAssert.AreEqual(statusJson, Array.ConvertAll(formal.Statuses, s => EditorJsonUtility.ToJson(s)));
            Assert.That(formal.Validate(), Is.Empty);
        }
        [Test]
        public void FormalCatalogEffectCollectionIsIdempotentAndPreservesExistingConfiguration()
        {
            PrototypeCatalogSO formal = AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>("Assets/_Game/Data/Prototype/P1/PrototypeCatalog.asset");
            Assert.That(formal, Is.Not.Null);
            PrototypeCatalogSO copy = UnityEngine.Object.Instantiate(formal);
            try
            {
                string[] before = Array.ConvertAll(copy.Reactions, r => r == null ? null : EditorJsonUtility.ToJson(r));
                copy.CollectEffectAssetsInEditor();
                var reactions = copy.Reactions; var statuses = copy.Statuses;
                copy.CollectEffectAssetsInEditor();
                CollectionAssert.AreEqual(reactions, copy.Reactions); CollectionAssert.AreEqual(statuses, copy.Statuses);
                for (int i = 0; i < before.Length; i++) Assert.That(EditorJsonUtility.ToJson(copy.Reactions[i]), Is.EqualTo(before[i]));
                Assert.That(copy.Validate(), Is.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
        [Test]
        public void FormalCatalogCanBeValidatedWithoutMutation()
        {
            Type tool = Type.GetType("Spotlight.EditorTools.AdvancedEffectSetupTool, Spotlight.Editor", true);
            object result = tool.GetMethod("ValidateFormalCatalog", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
            Assert.That(result, Is.Empty);
        }
    }
}
#endif
