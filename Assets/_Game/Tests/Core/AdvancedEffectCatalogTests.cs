using System;
using System.Linq;
using NUnit.Framework;
using Spotlight.Contracts;
using Spotlight.Core.Data;

namespace Spotlight.Tests.Core
{
    public sealed class AdvancedEffectCatalogTests
    {
        static CatalogData Data(EffectSpec effect, ReactionContext context = ReactionContext.EnemyStatus)
        {
            return new CatalogData
            {
                Reactions = new[] { new ReactionDefinition { Id="advanced", Enabled=true, Context=context,
                    A=ElementType.Fire, B=ElementType.Thunder, ConsumeA=1, ConsumeB=1, Effects=new[] { effect } } },
                Statuses = new[] { new StatusDefinition { Id="paralysis", DurationSeconds=2, TickIntervalSeconds=1, MaxStacks=1 },
                    new StatusDefinition { Id="input", IsReactionInput=true, DurationSeconds=2, TickIntervalSeconds=1, MaxStacks=1 } }
            };
        }
        static string[] Errors(CatalogData data)
        {
            return new GameCatalog(data).ValidateAll().Where(x=>x.StartsWith("advanced:")||x.StartsWith("advanced area:")).ToArray();
        }
        static EffectSpec Area()
        {
            return new EffectSpec { Kind=EffectKind.CreateArea, RadiusInCells=2, DurationSeconds=4, TickIntervalSeconds=1,
                AreaEffects=new[] { new EffectSpec { Kind=EffectKind.Damage, PhysicalDamage=3 } } };
        }
        [Test] public void LegacyDefinitionsKeepOneHitAndFullDamageDefaults()
        {
            var definition=new ProjectileDefinition(); var effect=new EffectSpec();
            Assert.IsFalse(definition.PiercingEnabled); Assert.AreEqual(1,definition.MaxHits);
            Assert.AreEqual(1f,definition.RetainedDamageMultiplier); Assert.AreEqual(1,effect.MaxHits);
            Assert.AreEqual(1f,effect.RetainedDamageMultiplier);
            Assert.IsEmpty(Errors(Data(new EffectSpec { Kind=EffectKind.Damage, PhysicalDamage=1 })));
        }
        [Test] public void AreaRejectsNonFiniteTimingAndTicksBelowLogicInterval()
        {
            foreach(float invalid in new[] { float.NaN,float.PositiveInfinity,-1f })
            {
                var effect=Area();effect.DurationSeconds=invalid;Assert.IsNotEmpty(Errors(Data(effect)));
            }
            var fast=Area();fast.TickIntervalSeconds=.001f;Assert.IsNotEmpty(Errors(Data(fast)));
            Assert.IsEmpty(Errors(Data(Area())));
        }
        [Test] public void AreaCannotRecursivelySpawnOrFeedReactionInput()
        {
            var effect=Area();effect.AreaEffects=new[] { Area() };Assert.IsNotEmpty(Errors(Data(effect)));
            effect.AreaEffects=new[] { new EffectSpec { Kind=EffectKind.ApplyStatus, ReferenceId="missing", Stacks=1 } };
            Assert.IsNotEmpty(Errors(Data(effect)));
            effect.AreaEffects=new[] { new EffectSpec { Kind=EffectKind.ApplyStatus, ReferenceId="input", Stacks=1 } };
            Assert.IsNotEmpty(Errors(Data(effect)));
            effect.AreaEffects=new[] { new EffectSpec { Kind=EffectKind.ApplyStatus, ReferenceId="paralysis", Stacks=1 } };
            Assert.IsEmpty(Errors(Data(effect)));
        }
        [Test] public void ChainRequiresKnownFiltersAndBoundedHitBudget()
        {
            var effect=new EffectSpec { Kind=EffectKind.ChainDamage, RadiusInCells=2, MaxHits=4,
                RetainedDamageMultiplier=.5f, RequiredStatusId="paralysis", ReferenceId="paralysis", Stacks=1 };
            Assert.IsEmpty(Errors(Data(effect)));
            effect.RequiredStatusId="missing";Assert.IsNotEmpty(Errors(Data(effect)));effect.RequiredStatusId=null;
            effect.MaxHits=65;Assert.IsNotEmpty(Errors(Data(effect)));effect.MaxHits=4;
            effect.RetainedDamageMultiplier=float.NaN;Assert.IsNotEmpty(Errors(Data(effect)));
        }
        [Test] public void PiercingIsProjectileOnlyAndGaugeOutputIsAllowedInEnemyDomain()
        {
            var effect=new EffectSpec { Kind=EffectKind.ConfigurePiercing, TargetPolicy=EffectTargetPolicy.CurrentProjectile, MaxHits=3, RetainedDamageMultiplier=.8f };
            Assert.IsNotEmpty(Errors(Data(effect)));Assert.IsEmpty(Errors(Data(effect,ReactionContext.Projectile)));
            Assert.IsEmpty(Errors(Data(new EffectSpec { Kind=EffectKind.AddGauge, Gauge=new ElementAmounts(0,1,0,0,0,0) })));
        }
        [Test] public void CatalogReturnsDetachedNestedEffects()
        {
            var source=Area();var catalog=new GameCatalog(Data(source));source.AreaEffects[0].PhysicalDamage=90;
            var first=catalog.GetReactionRules(ReactionContext.EnemyStatus)[0].Effects[0];
            Assert.AreEqual(3,first.AreaEffects[0].PhysicalDamage);first.AreaEffects[0].PhysicalDamage=80;first.RadiusInCells=50;
            var second=catalog.GetReactionRules(ReactionContext.EnemyStatus)[0].Effects[0];
            Assert.AreEqual(3,second.AreaEffects[0].PhysicalDamage);Assert.AreEqual(2,second.RadiusInCells);
        }
    }
}

