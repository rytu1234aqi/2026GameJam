#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spotlight.Bootstrap;
using Spotlight.Contracts;
using Spotlight.Core;
using Spotlight.Core.Data;
using Spotlight.Elements;
using Spotlight.Elements.Definitions;
using UnityEditor;
using UnityEngine;

namespace Spotlight.Integration.Tests
{
    public sealed class ElementCombinationAuthoringTests
    {
        private const string CatalogPath = "Assets/_Game/Data/Prototype/P1/PrototypeCatalog.asset";
        private static Type Tool { get { return Type.GetType("Spotlight.EditorTools.AdvancedEffectSetupTool, Spotlight.Editor", true); } }
        private static Type Lab { get { return Type.GetType("Spotlight.EditorTools.ElementCombinationLab, Spotlight.Editor", true); } }
        private static PrototypeCatalogSO Formal { get { return AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(CatalogPath); } }
        private static void Complete() { Tool.GetMethod("CreateOrComplete").Invoke(null, null); }
        [Test]
        public void FormalCombinationsValidateAndRepeatedCompletionPreservesEveryAuthoredValue()
        {
            Complete(); var asset = Formal;
            var reactions = asset.Reactions.Select(r => EditorJsonUtility.ToJson(r)).ToArray();
            var statuses = asset.Statuses.Select(s => EditorJsonUtility.ToJson(s)).ToArray();
            var production = asset.ProductionRules.Select(p => EditorJsonUtility.ToJson(p)).ToArray();
            Complete();
            CollectionAssert.AreEqual(reactions, asset.Reactions.Select(r => EditorJsonUtility.ToJson(r)).ToArray());
            CollectionAssert.AreEqual(statuses, asset.Statuses.Select(s => EditorJsonUtility.ToJson(s)).ToArray());
            CollectionAssert.AreEqual(production, asset.ProductionRules.Select(p => EditorJsonUtility.ToJson(p)).ToArray());
            Assert.That(asset.Validate(), Is.Empty);
            string listing = (string)Lab.GetMethod("DescribeCombinations").Invoke(null, new object[] { asset });
            Assert.AreEqual(21, listing.Split('\n').Count(line => line.Contains("EnemyStatus=")));
            Assert.That(listing, Does.Contain("Earth+Earth"));
            foreach (ReactionContext context in new[] { ReactionContext.Projectile, ReactionContext.EnemyStatus })
            for (int a = 1; a <= 6; a++) for (int b = a; b <= 6; b++)
                Assert.AreEqual(1, asset.Reactions.Count(r => r.Context == context && ((r.A == (ElementType)a && r.B == (ElementType)b) || (r.A == (ElementType)b && r.B == (ElementType)a))));
        }
        [Test]
        public void FormalWindWindIsEnabledAndEarthEarthRemainsDisabled()
        {
            Complete();
            var wind = Formal.Reactions.Single(r => r.Id == "reaction.Projectile.Wind.Wind");
            Assert.IsTrue(wind.Enabled);
            Assert.That(wind.Effects.Any(e => e.Kind == EffectKind.ConfigurePiercing && e.MaxHits == 3), Is.True);
            Assert.That(wind.Effects.Any(e => e.Kind == EffectKind.ApplyStatus && e.TargetPolicy == EffectTargetPolicy.SourceActor && e.ReferenceId == "status.combo.haste"), Is.True);
            Assert.That(Formal.Reactions.Where(r => r.A == ElementType.Earth && r.B == ElementType.Earth).All(r => !r.Enabled), Is.True);
            Assert.That(Formal.Reactions.Single(r => r.Id == "reaction.EnemyStatus.Wind.Wind").Enabled, Is.False);
            Assert.That(Formal.ProductionRules.Single(p => p.Id == "production.wood_wood_bonus").OutputCount, Is.EqualTo(1));
            Assert.That(Formal.ProductionRules.Single(p => p.Id == "production.wood_water_bonus").OutputCount, Is.EqualTo(1));
        }
        [Test]
        public void LegacyTemplateRecognitionRejectsDesignerAdjustments()
        {
            var slot = ScriptableObject.CreateInstance<ReactionDefinitionSO>();
            try
            {
                slot.Id = "reaction.Projectile.Wind.Wind"; slot.Context = ReactionContext.Projectile; slot.A = slot.B = ElementType.Wind;
                slot.Effects = new[] { new EffectConfig { Id = "effect.piercing.template", Kind = EffectKind.ConfigurePiercing, TargetPolicy = EffectTargetPolicy.CurrentProjectile, MaxHits = 3, RetainedDamageMultiplier = .8f } };
                var check = Tool.GetMethod("IsLegacyPiercingTemplate");
                Assert.AreEqual(true, check.Invoke(null, new object[] { slot }));
                slot.Effects[0].MaxHits = 4; Assert.AreEqual(false, check.Invoke(null, new object[] { slot }));
                slot.Effects[0].MaxHits = 3; slot.Priority = 99; Assert.AreEqual(false, check.Invoke(null, new object[] { slot }));
            }
            finally { UnityEngine.Object.DestroyImmediate(slot); }
        }
        [TestCase("reaction.Projectile.Wind.Wind")]
        [TestCase("reaction.Projectile.Fire.Fire")]
        [TestCase("reaction.Projectile.Fire.Wood")]
        [TestCase("reaction.EnemyStatus.Wood.Wood")]
        [TestCase("reaction.EnemyStatus.Earth.Thunder")]
        [TestCase("reaction.EnemyStatus.Water.Water")]
        [TestCase("reaction.EnemyStatus.Water.Fire")]
        [TestCase("reaction.EnemyStatus.Water.Earth")]
        [TestCase("reaction.EnemyStatus.Water.Wood")]
        [TestCase("reaction.EnemyStatus.Water.Wind")]
        [TestCase("reaction.EnemyStatus.Water.Thunder")]
        [TestCase("reaction.EnemyStatus.Fire.Earth")]
        [TestCase("reaction.EnemyStatus.Fire.Wind")]
        [TestCase("reaction.EnemyStatus.Fire.Thunder")]
        [TestCase("reaction.EnemyStatus.Earth.Wind")]
        [TestCase("reaction.EnemyStatus.Wood.Wind")]
        [TestCase("reaction.EnemyStatus.Wood.Thunder")]
        [TestCase("reaction.EnemyStatus.Wind.Thunder")]
        [TestCase("reaction.EnemyStatus.Thunder.Thunder")]
        public void FormalReactionExecutesInIsolatedCompositionWithoutAssetChanges(string id)
        {
            Complete(); var asset = Formal;
            string before = EditorJsonUtility.ToJson(asset);
            var reaction = asset.Reactions.Single(r => r.Id == id);
            string result = (string)Lab.GetMethod("RunSandbox").Invoke(null, new object[] { asset, reaction });
            Assert.That(result, Does.Contain("实际反应=" + id));
            Assert.That(result, Does.Contain("HP="));
            Assert.That(result, Does.Contain("t=6秒"));
            Assert.AreEqual(before, EditorJsonUtility.ToJson(asset));
            if (id == "reaction.Projectile.Wind.Wind") Assert.That(result, Does.Contain("攻速倍率=1.3"));
            if (id == "reaction.EnemyStatus.Wood.Wood") Assert.That(result, Does.Contain("status.entangle"));
        }
        [TestCase(false, 3)]
        [TestCase(true, 2)]
        public void WoodWoodProductionUsesFourNeighborsAndKeepsExistingSingleBlockRewards(bool diagonal, int expectedWood)
        {
            Complete();
            var catalog = new GameCatalog(Formal.BuildCatalogData());
            var core = new CoreServices(catalog, null);
            Assert.AreEqual(ErrorCode.None, core.Store.Initialize(new NewRunRequest { LevelId = "level.prototype", Mode = GameMode.Story, Seed = 123 }).Error);
            var input = new ProductionInput
            {
                SettlementId = "combo-production", CompletedNight = 1,
                Random = core.Random.GetState(RandomStream.Production),
                Board = new BoardSnapshot
                {
                    LevelId = "level.prototype", Cells = new[]
                    {
                        new CellSnapshot { Cell = new CellCoord(1, 1), OccupantId = new EntityId(10), OccupantKind = OccupantKind.ElementBlock, DefinitionId = "elm.wood" },
                        new CellSnapshot { Cell = new CellCoord(2, diagonal ? 2 : 1), OccupantId = new EntityId(11), OccupantKind = OccupantKind.ElementBlock, DefinitionId = "elm.wood" }
                    }
                }
            };
            var result = new ElementRuleService(catalog, core.Random).ResolveProduction(input);
            Assert.AreEqual(expectedWood, result.HandDeltas.Where(r => r.DefinitionId == "elm.wood").Sum(r => r.Amount));
            Assert.AreEqual(!diagonal, result.AppliedRuleIds.Contains("production.wood_wood_bonus"));
            Assert.That(result.Board, Is.Empty, "The new non-consuming production pair must not remove blocks.");
        }
    }
}
#endif
