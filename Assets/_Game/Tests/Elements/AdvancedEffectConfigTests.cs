using System;
using System.Collections.Generic;
using Spotlight.Elements;
using NUnit.Framework;
using Spotlight.Contracts;
using Spotlight.Elements.Definitions;

namespace Spotlight.Tests.Elements
{
    public sealed class AdvancedEffectConfigTests
    {
        [Test] public void SerializedDefaultsRemainCompatibleWithSingleHitAssets()
        {
            var config=new EffectConfig();var effect=config.ToDefinition();
            Assert.AreEqual(1,effect.MaxHits);Assert.AreEqual(1f,effect.RetainedDamageMultiplier);
            Assert.IsEmpty(effect.AreaEffects);
        }
        [Test] public void AreaConversionProducesDetachedNestedDefinitions()
        {
            var nested=new AreaEffectConfig { Kind=EffectKind.Damage, PhysicalDamage=7 };
            var config=new EffectConfig { Kind=EffectKind.CreateArea, RadiusInCells=2, DurationSeconds=3,
                TickIntervalSeconds=.5f,LingeringSeconds=1,RequiredStatusId="wet",AreaEffects=new[] { nested } };
            var first=config.ToDefinition();nested.PhysicalDamage=99;first.AreaEffects[0].PhysicalDamage=10;
            Assert.AreEqual(99,config.ToDefinition().AreaEffects[0].PhysicalDamage);
            Assert.AreEqual(2,first.RadiusInCells);Assert.AreEqual(1,first.LingeringSeconds);Assert.AreEqual("wet",first.RequiredStatusId);
        }
        [Test] public void AreaLeafRejectsUnsupportedEffectKinds()
        {
            var config=new EffectConfig { AreaEffects=new[] { new AreaEffectConfig { Kind=EffectKind.CreateArea } } };
            Assert.Throws<InvalidOperationException>(()=>config.ToDefinition());
        }

        [TestCase(ReactionContext.EnemyStatus)]
        [TestCase(ReactionContext.Projectile)]
        public void ResolverMatchesAdvancedOutputsAndDetachesNestedDefinitions(ReactionContext context)
        {
            var area=new EffectSpec { Kind=EffectKind.CreateArea,RadiusInCells=2,DurationSeconds=4,TickIntervalSeconds=1,
                AreaEffects=new[] { new EffectSpec { Kind=EffectKind.Damage,PhysicalDamage=5 } } };
            var effects=new List<EffectSpec> { area,new EffectSpec { Kind=EffectKind.AreaDamage,RadiusInCells=2 },
                new EffectSpec { Kind=EffectKind.ChainDamage,RadiusInCells=2,MaxHits=4 },new EffectSpec { Kind=EffectKind.AddGauge } };
            if(context==ReactionContext.Projectile)effects.Add(new EffectSpec { Kind=EffectKind.ConfigurePiercing,TargetPolicy=EffectTargetPolicy.CurrentProjectile,MaxHits=3 });
            var catalog=new ResolverCatalog { Rule=new ReactionDefinition { Id="advanced",Enabled=true,Context=context,A=ElementType.Fire,
                B=ElementType.Thunder,ConsumeA=1,ConsumeB=1,Effects=effects } };
            var service=new ElementRuleService(catalog,new UnusedRandom());
            var input=new ReactionInput { Context=context,Tokens=new[] { new ReactionToken { TokenId=1,Element=ElementType.Fire },
                new ReactionToken { TokenId=2,Element=ElementType.Thunder } } };
            var plan=service.ResolveReactions(input);Assert.AreEqual(1,plan.Matches.Count);
            Assert.AreEqual(effects.Count,plan.Matches[0].Effects.Count);plan.Matches[0].Effects[0].AreaEffects[0].PhysicalDamage=90;
            Assert.AreEqual(5,service.ResolveReactions(input).Matches[0].Effects[0].AreaEffects[0].PhysicalDamage);
            Assert.AreEqual(4,plan.Matches[0].Effects[2].MaxHits);
        }
        sealed class UnusedRandom : IRandomService
        {
            public RandomState GetState(RandomStream stream) { return default(RandomState); }
            public IRandomCursor CreateCursor(RandomState state) { throw new NotSupportedException(); }
        }
        sealed class ResolverCatalog : IGameCatalog
        {
            public ReactionDefinition Rule;
            public GameSettings GetSettings() { return null; }
            public bool TryGetLevel(string id, out LevelDefinition value) { value=null;return false; }
            public bool TryGetElement(string id, out ElementDefinition value) { value=null;return false; }
            public bool TryGetTower(string id, out TowerDefinition value) { value=null;return false; }
            public bool TryGetProjectile(string id, out ProjectileDefinition value) { value=null;return false; }
            public bool TryGetStatus(string id, out StatusDefinition value) { value=null;return false; }
            public bool TryGetEnemy(string id, out EnemyDefinition value) { value=null;return false; }
            public bool TryGetItem(string id, out ItemDefinition value) { value=null;return false; }
            public bool TryGetResource(string id, out ResourceDefinition value) { value=null;return false; }
            public bool TryGetEvent(string id, out DayEventDefinition value) { value=null;return false; }
            public bool TryGetBossSkill(string id, out BossSkillDefinition value) { value=null;return false; }
            public bool TryGetDropTable(string id, out DropTableDefinition value) { value=null;return false; }
            public bool TryGetSkill(string id, out SpecialSkillDefinition value) { value=null;return false; }
            public EndlessDefinition GetEndlessDefinition() { return null; }
            public IReadOnlyList<ProductionRuleDefinition> GetProductionRules() { return null; }
            public IReadOnlyList<ReactionDefinition> GetReactionRules(ReactionContext context) { return new[] { Rule }; }
            public IReadOnlyList<GaugeDefinition> GetGaugeDefinitions() { return null; }
            public IReadOnlyList<DayEventDefinition> GetDayEvents() { return null; }
            public IReadOnlyList<WaveDefinition> GetWaves(int dayIndex, GameMode mode) { return null; }
            public IReadOnlyList<string> ValidateAll() { return null; }
        }
    }
}
