#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Spotlight.Bootstrap;
using Spotlight.Contracts;
using Spotlight.Enemies;
using Spotlight.Enemies.Definitions;
using UnityEditor;
using UnityEngine;

namespace Spotlight.Tests.Integration
{
    public sealed class R3BLootTests
    {
        [Serializable] public sealed class Quantity { public string bucket, resource; public long amount; }
        [Serializable] public sealed class Reward { public string source; public long enemy; public List<Quantity> quantities=new List<Quantity>(); }
        [Serializable] public sealed class Sample { public string stage; public long water, fire, thunder, gold; public int lootEvents, productionEvents; }
        [Serializable] public sealed class Evidence
        {
            public string test, scope; public bool passed;
            public List<Reward> rewards=new List<Reward>(); public List<Sample> samples=new List<Sample>();
        }
        ModuleComposition runtime; string directory; Evidence evidence;
        readonly List<IDisposable> subscriptions=new List<IDisposable>();
        int lootEvents, productionEvents;
        const float Step=1f/30;
        [SetUp] public void Setup()
        {
            lootEvents=productionEvents=0;
            evidence=new Evidence { test=TestContext.CurrentContext.Test.Name, scope="Real assets, ModuleComposition, damage/death/loot transactions and dawn production. H02 enemy/flight registration only is supplied in memory by R3BTestCatalog; no production assets, HP, damage or drop results are altered." };
            directory=Path.Combine(Path.GetTempPath(),"Spotlight_N05_"+Guid.NewGuid().ToString("N"));
            runtime=new ModuleComposition(R3BTestCatalog.Build(),directory);
            subscriptions.Add(runtime.Events.Subscribe<LootGrantedEvent>(e=>{lootEvents++;AddReward("kill.drop",e.Enemy.Value,e.Rewards);}));
            subscriptions.Add(runtime.Events.Subscribe<DawnSettledEvent>(e=>{productionEvents++;AddReward("night.production",0,e.Rewards);}));
            Committed(runtime.Flow.TryNewRun(Command(),new NewRunRequest { LevelId="level.prototype",Mode=GameMode.Story,Seed=505 }));runtime.Advance(0);
            DayEventSnapshot day=runtime.Gameplay.DayEvents.GetSnapshot();
            Committed(runtime.Gameplay.DayEvents.TryChoose(Command(),day.State.InstanceId,day.Options[0].Id));runtime.Advance(0);
            Committed(runtime.Flow.TryDismissTutorial(Command()));runtime.Advance(0);
            Assert.AreEqual(GamePhase.Build,runtime.Flow.GetSnapshot().Phase);
        }
        [TearDown] public void Cleanup()
        {
            evidence.passed=TestContext.CurrentContext.Result.Outcome.Status==NUnit.Framework.Interfaces.TestStatus.Passed;
            Directory.CreateDirectory("QA/R3B");
            File.WriteAllText("QA/R3B/N05-"+Regex.Replace(evidence.test,"[^a-zA-Z0-9_-]","_")+".json",JsonUtility.ToJson(evidence,true));
            foreach(IDisposable subscription in subscriptions)subscription.Dispose();subscriptions.Clear();
            if(runtime!=null)runtime.Dispose(); runtime=null;
            if(directory!=null&&Directory.Exists(directory))
            {
                string path=Path.GetFullPath(directory),root=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                Assert.IsTrue(path.StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(path).StartsWith("Spotlight_N05_",StringComparison.Ordinal));
                Directory.Delete(path,true);
            }
        }
        CommandContext Command(){return runtime.Core.Commands.Create(runtime.Core.Transactions.Revision);}
        static void Committed(OperationResult result){Assert.AreEqual(OperationState.Committed,result.State,result.Error.ToString());}
        long Hand(string element){return runtime.Core.Resources.GetQuantity(ResourceBucket.Hand,"elm."+element);}
        long Gold(){return runtime.Core.Resources.GetQuantity(ResourceBucket.Inventory,"res.gold");}
        void AddReward(string source,long enemy,IReadOnlyList<ResourceAmount> quantities)
        {
            var reward=new Reward {source=source,enemy=enemy};
            foreach(ResourceAmount q in quantities)reward.quantities.Add(new Quantity {bucket=q.Bucket.ToString(),resource=q.DefinitionId,amount=q.Amount});
            evidence.rewards.Add(reward);
        }
        void Record(string stage){evidence.samples.Add(new Sample {stage=stage,water=Hand("water"),fire=Hand("fire"),thunder=Hand("thunder"),gold=Gold(),lootEvents=lootEvents,productionEvents=productionEvents});}
        EntityId Spawn(string element,EnemyServices service=null)
        {
            EntityId id;Committed((service??runtime.Enemies).TrySpawn(new EnemySpawnRequest {DefinitionId="enemy.slime."+element,SpawnPointId="spawn.ground",PathId="path.ground",HpMultiplier=1,DamageMultiplier=1},out id));runtime.Events.Flush();return id;
        }
        DamagePacket Kill(EntityId id)
        {
            var packet=new DamagePacket {PacketId="n05:"+Guid.NewGuid().ToString("N"),Source=runtime.Core.World.SpringId,Target=id,Origin=DamageOrigin.Tower,PhysicalDamage=10000};
            Assert.AreEqual(OperationState.Queued,runtime.Combat.Damage.Enqueue(packet).State);return packet;
        }
        [TestCase("water","Water")][TestCase("fire","Fire")][TestCase("thunder","Thunder")]
        public void RealKillAwardsOneHandOnceAndDawnProductionIsSeparate(string element,string suffix)
        {
            var drop=AssetDatabase.LoadAssetAtPath<DropTableSO>("Assets/_Game/Data/Prototype/P4/DropSlime"+suffix+".asset");
            Assert.NotNull(drop);Assert.AreEqual("drop.slime."+element,drop.Id);Assert.AreEqual(1,drop.Rolls);Assert.AreEqual(1,drop.Entries.Length);
            DropEntry entry=drop.ToDefinition().Entries[0];Assert.AreEqual(1,entry.Weight);Assert.AreEqual(ResourceBucket.Hand,entry.Bucket);Assert.AreEqual("elm."+element,entry.ResourceId);Assert.AreEqual(1,entry.MinAmount);Assert.AreEqual(1,entry.MaxAmountInclusive);
            EnemyDefinition definition;Assert.IsTrue(runtime.Catalog.TryGetEnemy("enemy.slime."+element,out definition));Assert.AreEqual(drop.Id,definition.DropTableId);
            foreach(string id in new[]{"enemy.goblin.melee","enemy.goblin.ranged","enemy.sandworm","enemy.gargoyle","enemy.falcon","enemy.soulking"})
            {Assert.IsTrue(runtime.Catalog.TryGetEnemy(id,out definition));Assert.AreEqual("drop.gold",definition.DropTableId);}
            Committed(runtime.Core.Deployment.TryPlace(Command(),new PlacementRequest {DefinitionId="elm."+element,Kind=OccupantKind.ElementBlock,Cell=new CellCoord(2,4)}));runtime.Advance(0);
            Committed(runtime.Flow.TryStartNight(Command()));runtime.Advance(0);
            EntityId actor=Spawn(element);long before=Hand(element),gold=Gold();Record("before.kill");
            DamageResult lethal=null;
            using(runtime.Events.Subscribe<DamageAppliedEvent>(e=>{if(e.Result.Target.Equals(actor))lethal=e.Result;}))
            {
                DamagePacket packet=Kill(actor);runtime.Advance(Step);
                Assert.NotNull(lethal);Assert.IsTrue(lethal.NewlyKilled);Assert.AreEqual(0,lethal.RemainingHp);
                Assert.AreEqual(before+1,Hand(element));Assert.AreEqual(gold,Gold());Assert.AreEqual(1,lootEvents);Assert.AreEqual(0,productionEvents);
                ActorSnapshot gone;Assert.IsFalse(runtime.Core.World.TryGetActor(actor,out gone));
                Record("after.kill.drop.only");
                Committed(runtime.Combat.Damage.Enqueue(packet)); // Duplicate packet accepted without another execution.
                for(int i=0;i<4;i++){runtime.Enemies.Tick(new TickContext(i,0,0,TickStage.DeathAndLoot));runtime.Advance(Step);}
                Assert.AreEqual(before+1,Hand(element));Assert.AreEqual(gold,Gold());Assert.AreEqual(1,lootEvents);Record("after.repeated.death.processing");
            }
            // Finish the actual baseline night through real damage, without modifying wave/tower stats.
            for(int tick=0;tick<300&&runtime.Flow.GetSnapshot().Phase==GamePhase.Night;tick++)
            {
                foreach(EnemySnapshot enemy in runtime.Enemies.GetSnapshot())Kill(enemy.ActorId);
                runtime.Advance(Step);
            }
            Assert.AreEqual(GamePhase.NightResult,runtime.Flow.GetSnapshot().Phase);
            Assert.AreEqual(before+1,Hand(element));Record("before.dawn");
            int dropCount=lootEvents;long beforeDawn=Hand(element),goldBeforeDawn=Gold();
            CommandContext confirm=Command();Committed(runtime.Flow.TryConfirmNightResult(confirm));runtime.Advance(0);
            Assert.AreEqual(beforeDawn+1,Hand(element));Assert.AreEqual(goldBeforeDawn,Gold());Assert.AreEqual(dropCount,lootEvents);Assert.AreEqual(1,productionEvents);Record("after.dawn.production.only");
            Committed(runtime.Flow.TryConfirmNightResult(confirm));runtime.Advance(0);
            Assert.AreEqual(beforeDawn+1,Hand(element));Assert.AreEqual(1,productionEvents);
            Reward slime=evidence.rewards.Find(r=>r.source=="kill.drop"&&r.enemy==actor.Value);
            Assert.NotNull(slime);Assert.AreEqual(1,slime.quantities.Count);Assert.AreEqual("elm."+element,slime.quantities[0].resource);Assert.AreEqual("Hand",slime.quantities[0].bucket);Assert.AreEqual(1,slime.quantities[0].amount);
            Reward production=evidence.rewards.Find(r=>r.source=="night.production");Assert.NotNull(production);Assert.AreEqual(1,production.quantities.Count);Assert.AreEqual("elm."+element,production.quantities[0].resource);Assert.AreEqual(1,production.quantities[0].amount);
        }
        [TestCase("water",DeathReason.SessionReset)][TestCase("fire",DeathReason.SessionReset)][TestCase("thunder",DeathReason.SessionReset)]
        [TestCase("water",DeathReason.VictoryCleanup)][TestCase("fire",DeathReason.VictoryCleanup)][TestCase("thunder",DeathReason.VictoryCleanup)]
        [TestCase("water",DeathReason.ScriptedExit)][TestCase("fire",DeathReason.ScriptedExit)][TestCase("thunder",DeathReason.ScriptedExit)]
        public void CleanupDoesNotReward(string element,DeathReason reason)
        {
            EntityId actor=Spawn(element);long before=Hand(element),gold=Gold();Record("before.cleanup");
            runtime.Enemies.DespawnAll(reason);runtime.Events.Flush();
            runtime.Enemies.DespawnAll(reason);runtime.Enemies.Tick(new TickContext(0,0,0,TickStage.DeathAndLoot));runtime.Events.Flush();
            Assert.AreEqual(before,Hand(element));Assert.AreEqual(gold,Gold());Assert.AreEqual(0,lootEvents);Assert.AreEqual(0,productionEvents);
            ActorSnapshot gone;Assert.IsFalse(runtime.Core.World.TryGetActor(actor,out gone));Record("after.cleanup.twice");
        }
        [Test] public void ReturningToMenuWithLiveSlimesDoesNotGrantLoot()
        {
            foreach(string element in new[]{"water","fire","thunder"})Spawn(element);
            Record("before.return.to.menu");Committed(runtime.Flow.TryReturnToMenu(Command()));runtime.Advance(0);
            Assert.AreEqual(GamePhase.Menu,runtime.Flow.GetSnapshot().Phase);Assert.AreEqual(0,lootEvents);Assert.AreEqual(0,productionEvents);
            Assert.AreEqual(0,runtime.Core.World.GetActors(ActorKind.Enemy).Count);Assert.AreEqual(0,runtime.Enemies.GetSnapshot().Count);Record("after.return.to.menu.resources.reset");
        }
        [Test] public void DisposingRuntimeWithLiveSlimesDoesNotReward()
        {
            foreach(string element in new[]{"water","fire","thunder"})Spawn(element);
            long water=Hand("water"),fire=Hand("fire"),thunder=Hand("thunder"),gold=Gold();Record("before.runtime.dispose");
            runtime.Dispose();
            Assert.AreEqual(water,Hand("water"));Assert.AreEqual(fire,Hand("fire"));Assert.AreEqual(thunder,Hand("thunder"));Assert.AreEqual(gold,Gold());
            Assert.AreEqual(0,lootEvents);Assert.AreEqual(0,productionEvents);Assert.AreEqual(0,runtime.Enemies.GetSnapshot().Count);Record("after.runtime.dispose.no.rewards");
        }
        sealed class RejectFirstRemoval : IStateTransactionService
        {
            readonly IStateTransactionService inner; bool rejected;
            public RejectFirstRemoval(IStateTransactionService value){inner=value;}
            public long Revision {get{return inner.Revision;}}
            public ValidationResult Validate(StateMutationBatch batch){return inner.Validate(batch);}
            public OperationResult TryCommit(StateMutationBatch batch)
            {
                if(!rejected&&batch.Reason=="enemy.remove") {rejected=true;return new OperationResult(OperationState.Rejected,ErrorCode.VersionConflict,batch.Context.CommandId,Revision,"n05.test.retry");}
                return inner.TryCommit(batch);
            }
        }
        [TestCase("water")][TestCase("fire")][TestCase("thunder")]
        public void RepeatedDeathAfterRemovalFailureDoesNotAwardTwice(string element)
        {
            var core=runtime.Core;
            var service=new EnemyServices(runtime.Catalog,core.Board,core.World,core.Motion,new RejectFirstRemoval(core.Transactions),core.Commands,core.Ids,core.Random,runtime.Combat.Status,runtime.Combat.Damage,runtime.Combat.Projectiles,runtime.Combat.Effects,runtime.Events,runtime.Clock);
            service.BeginSession(new SessionStartContext {RunId=runtime.Flow.GetSnapshot().RunId,Mode=GameMode.Story,LevelId="level.prototype",DayIndex=1,SpringId=core.World.SpringId});
            try
            {
                EntityId actor=Spawn(element,service);long before=Hand(element),gold=Gold();Record("before.retry.kill");Kill(actor);
                runtime.Combat.Tick(new TickContext(0,0,0,TickStage.DamageAndReaction));
                service.Tick(new TickContext(0,0,0,TickStage.DeathAndLoot));runtime.Events.Flush();
                ActorSnapshot dead;Assert.IsTrue(core.World.TryGetActor(actor,out dead));Assert.AreEqual(0,dead.CurrentHp);Assert.IsTrue(dead.DeathResolved);
                Assert.AreEqual(before+1,Hand(element));Assert.AreEqual(1,lootEvents);Record("loot.committed.removal.rejected");
                service.Tick(new TickContext(1,0,0,TickStage.DeathAndLoot));runtime.Events.Flush();
                Assert.IsFalse(core.World.TryGetActor(actor,out dead));Assert.AreEqual(before+1,Hand(element));Assert.AreEqual(gold,Gold());Assert.AreEqual(1,lootEvents);Record("repeated.death.removal.succeeded.no.extra.loot");
            }
            finally {service.DespawnAll(DeathReason.SessionReset);service.EndSession();}
        }
    }
}
#endif