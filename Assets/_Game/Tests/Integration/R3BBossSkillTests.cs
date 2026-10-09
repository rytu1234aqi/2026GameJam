#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Spotlight.Bootstrap;
using Spotlight.Contracts;
using Spotlight.Enemies;
using UnityEngine;

namespace Spotlight.Tests.Integration
{
    public sealed class R3BBossSkillTests
    {
        [Serializable] public sealed class Cast { public string skill, result; public float seconds, baseDamage; public long source,target; }
        [Serializable] public sealed class Birth { public long actor; public string enemy,path; public float seconds,hp; }
        [Serializable] public sealed class Hit { public float seconds,damage,hp; public long source,target; }
        [Serializable] public sealed class Evidence
        {
            public string test,scope; public bool passed;
            public List<Cast> casts=new List<Cast>();public List<Birth> summons=new List<Birth>();public List<Hit> hits=new List<Hit>();
        }
        ModuleComposition runtime; EnemyServices service; EntityId boss; string directory; float seconds;
        Evidence evidence; IDisposable damageSubscription; readonly HashSet<long> seen=new HashSet<long>();
        sealed class Projectiles : IProjectileService
        {
            readonly IProjectileService inner;readonly R3BBossSkillTests test;
            public Projectiles(IProjectileService value,R3BBossSkillTests owner){inner=value;test=owner;}
            public OperationResult Spawn(ProjectileSpawnRequest request)
            {
                OperationResult result=inner.Spawn(request);
                Assert.AreEqual(DamageOrigin.Boss,request.DamageOrigin);
                test.evidence.casts.Add(new Cast {skill="boss.shot",seconds=test.seconds,baseDamage=request.PhysicalDamage,source=request.Source.Value,target=request.InitialTarget.Value,result=result.State.ToString()});return result;
            }
            public IReadOnlyList<ProjectileSnapshot> GetSnapshot(){return inner.GetSnapshot();}
            public void Clear(){inner.Clear();}
        }
        sealed class Effects : IEffectService
        {
            readonly IEffectService inner;readonly R3BBossSkillTests test;
            public Effects(IEffectService value,R3BBossSkillTests owner){inner=value;test=owner;}
            public ValidationResult Validate(EffectBatchRequest request){return inner.Validate(request);}
            public OperationResult TryExecute(EffectBatchRequest request)
            {
                OperationResult result=inner.TryExecute(request);Assert.AreEqual(DamageOrigin.Boss,request.Origin);
                test.evidence.casts.Add(new Cast {skill="boss.curse",seconds=test.seconds,baseDamage=request.Effects[0].PhysicalDamage,source=request.Source.Value,target=test.runtime.Core.World.SpringId.Value,result=result.State.ToString()});return result;
            }
        }
        [SetUp] public void Setup()
        {
            seconds=0;seen.Clear();evidence=new Evidence {test=TestContext.CurrentContext.Test.Name,scope="Actual Boss SOs and EnemyServices, delegated real projectile/effect services, real damage and transactions. Boss manually spawned at t=0 with active night7 scheduler, no base-wave scheduling while boss remains alive. H02 enemy/flight registration is test-only; no production definitions changed."};
            directory=Path.Combine(Path.GetTempPath(),"Spotlight_N06_"+Guid.NewGuid().ToString("N"));
            runtime=new ModuleComposition(R3BTestCatalog.Build(),directory);var core=runtime.Core;
            Assert.AreEqual(ErrorCode.None,core.Store.Initialize(new NewRunRequest {LevelId="level.prototype",Mode=GameMode.Story,Seed=606}).Error);
            var context=new SessionStartContext {RunId=core.Store.GetFlowSnapshot().RunId,LevelId="level.prototype",Mode=GameMode.Story,DayIndex=7,SpringId=core.World.SpringId};
            runtime.Combat.BeginSession(context);
            service=new EnemyServices(runtime.Catalog,core.Board,core.World,core.Motion,core.Transactions,core.Commands,core.Ids,core.Random,runtime.Combat.Status,runtime.Combat.Damage,new Projectiles(runtime.Combat.Projectiles,this),new Effects(runtime.Combat.Effects,this),runtime.Events,runtime.Clock);
            service.BeginSession(context);Assert.AreEqual(OperationState.Committed,service.StartNight(7,GameMode.Story).State);
            Assert.AreEqual(OperationState.Committed,service.TrySpawn(new EnemySpawnRequest {DefinitionId="enemy.soulking",PathId="path.ground",SpawnPointId="spawn.ground",HpMultiplier=1,DamageMultiplier=1},out boss).State);
            ActorSnapshot actor;Assert.IsTrue(core.World.TryGetActor(boss,out actor));Assert.AreEqual(1000,actor.MaxHp);
            damageSubscription=runtime.Events.Subscribe<DamageAppliedEvent>(e=>evidence.hits.Add(new Hit {seconds=seconds,source=e.Result.Source.Value,target=e.Result.Target.Value,damage=e.Result.FinalDamage,hp=e.Result.RemainingHp}));runtime.Events.Flush();
        }
        [TearDown] public void Cleanup()
        {
            evidence.passed=TestContext.CurrentContext.Result.Outcome.Status==NUnit.Framework.Interfaces.TestStatus.Passed;
            Directory.CreateDirectory("QA/R3B");File.WriteAllText("QA/R3B/N06-"+evidence.test+".json",JsonUtility.ToJson(evidence,true));
            if(damageSubscription!=null)damageSubscription.Dispose();
            if(service!=null){service.DespawnAll(DeathReason.SessionReset);service.EndSession();}
            if(runtime!=null)runtime.Dispose();
            if(directory!=null&&Directory.Exists(directory))
            {
                string path=Path.GetFullPath(directory),root=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                Assert.IsTrue(path.StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(path).StartsWith("Spotlight_N06_",StringComparison.Ordinal));Directory.Delete(path,true);
            }
        }
        void TickBoss(float dt)
        {
            seconds+=dt;service.Tick(new TickContext(0,seconds,dt,TickStage.SpawnAndBoss));
            runtime.Combat.Tick(new TickContext(0,seconds,dt,TickStage.DamageAndReaction));runtime.Events.Flush();
            foreach(EnemySnapshot enemy in service.GetSnapshot())if(enemy.IsSummon&&seen.Add(enemy.ActorId.Value))
            {
                ActorSnapshot actor;Assert.IsTrue(runtime.Core.World.TryGetActor(enemy.ActorId,out actor));
                evidence.summons.Add(new Birth {actor=enemy.ActorId.Value,enemy=enemy.DefinitionId,path=enemy.PathId,seconds=seconds,hp=actor.MaxHp});
            }
        }
        [Test] public void ThreeSkillsKeepExistingPeriodsDamageAndFixedSummons()
        {
            BossSkillDefinition shot,summon,curse;Assert.IsTrue(runtime.Catalog.TryGetBossSkill("boss.shot",out shot));Assert.IsTrue(runtime.Catalog.TryGetBossSkill("boss.summon",out summon));Assert.IsTrue(runtime.Catalog.TryGetBossSkill("boss.curse",out curse));
            Assert.AreEqual(5,shot.PeriodSeconds);Assert.AreEqual(5,shot.FirstDelaySeconds);Assert.AreEqual(15,shot.ProjectileDamage);Assert.AreEqual("projectile.enemy",shot.ProjectileId);
            Assert.AreEqual(20,summon.PeriodSeconds);Assert.AreEqual(20,summon.FirstDelaySeconds);Assert.AreEqual(1,summon.Summons.Count);
            SpawnGroupDefinition group=summon.Summons[0];Assert.AreEqual("enemy.goblin.melee",group.EnemyId);Assert.AreEqual(3,group.Count);Assert.AreEqual(0,group.StartDelaySeconds);Assert.AreEqual(1,group.SpawnIntervalSeconds);Assert.IsFalse(group.ApplyDayGrowth);Assert.AreEqual(1,group.HpMultiplier);Assert.AreEqual(1,group.DamageMultiplier);
            Assert.AreEqual(40,curse.PeriodSeconds);Assert.AreEqual(40,curse.FirstDelaySeconds);Assert.AreEqual(1,curse.Effects.Count);Assert.AreEqual(EffectKind.Damage,curse.Effects[0].Kind);Assert.AreEqual(EffectTargetPolicy.Spring,curse.Effects[0].TargetPolicy);Assert.AreEqual(10,curse.Effects[0].PhysicalDamage);
            for(int i=0;i<340;i++)TickBoss(.25f);
            var shots=evidence.casts.FindAll(c=>c.skill=="boss.shot");var curses=evidence.casts.FindAll(c=>c.skill=="boss.curse");
            Assert.AreEqual(17,shots.Count);for(int i=0;i<shots.Count;i++){Assert.AreEqual((i+1)*5,shots[i].seconds);Assert.AreEqual(15,shots[i].baseDamage);Assert.AreEqual(boss.Value,shots[i].source);Assert.AreEqual(runtime.Core.World.SpringId.Value,shots[i].target);Assert.AreEqual("Committed",shots[i].result);}
            Assert.AreEqual(2,curses.Count);for(int i=0;i<curses.Count;i++){Assert.AreEqual((i+1)*40,curses[i].seconds);Assert.AreEqual(10,curses[i].baseDamage);Assert.AreEqual("Committed",curses[i].result);}
            Assert.AreEqual(12,evidence.summons.Count);
            for(int i=0;i<12;i++)
            {
                Birth birth=evidence.summons[i];float first=(i/3+1)*20;
                Assert.AreEqual(first+(i%3==0?.25f:i%3),birth.seconds);Assert.AreEqual("enemy.goblin.melee",birth.enemy);Assert.AreEqual("path.ground",birth.path);Assert.AreEqual(30,birth.hp);
            }
            Assert.AreEqual(2,evidence.hits.Count);Assert.AreEqual(40,evidence.hits[0].seconds);Assert.AreEqual(80,evidence.hits[1].seconds);
            foreach(Hit hit in evidence.hits){Assert.AreEqual(boss.Value,hit.source);Assert.AreEqual(runtime.Core.World.SpringId.Value,hit.target);Assert.AreEqual(10,hit.damage);}
            Assert.AreEqual(80,evidence.hits[1].hp);Assert.AreEqual(13,service.Waves.GetSnapshot().AliveCount);
            // Projectiles are created but not advanced in this cadence test, isolating curse damage.
        }
        [Test] public void ActualBossProjectileHitsSpringForFifteen()
        {
            for(int i=0;i<20;i++)TickBoss(.25f);
            Assert.AreEqual(1,evidence.casts.Count);Assert.AreEqual(1,runtime.Combat.Projectiles.GetSnapshot().Count);
            for(int i=0;i<80&&evidence.hits.Count==0;i++)
            {
                seconds+=.1f;runtime.Combat.Tick(new TickContext(i,seconds,.1f,TickStage.Projectiles));runtime.Combat.Tick(new TickContext(i,seconds,.1f,TickStage.DamageAndReaction));runtime.Events.Flush();
            }
            Assert.AreEqual(1,evidence.hits.Count);Assert.AreEqual(boss.Value,evidence.hits[0].source);Assert.AreEqual(runtime.Core.World.SpringId.Value,evidence.hits[0].target);Assert.AreEqual(15,evidence.hits[0].damage);Assert.AreEqual(85,evidence.hits[0].hp);
            Assert.AreEqual(0,runtime.Combat.Projectiles.GetSnapshot().Count);
        }
        [Test] public void PauseAndBossCleanupStopSkillsAndPendingSummons()
        {
            Guid pause=runtime.Clock.AcquirePause(PauseReason.Player);TickBoss(100);
            Assert.AreEqual(0,evidence.casts.Count);Assert.AreEqual(0,evidence.summons.Count);runtime.Clock.ReleasePause(pause);seconds=0;
            for(int i=0;i<80;i++)TickBoss(.25f);
            Assert.AreEqual(4,evidence.casts.Count);Assert.AreEqual(3,service.Waves.GetSnapshot().PendingSummonCount);Assert.AreEqual(0,evidence.summons.Count);
            service.DespawnAll(DeathReason.ScriptedExit);Assert.AreEqual(0,service.Waves.GetSnapshot().PendingSummonCount);
            // Stop the now-empty test scheduler so it cannot begin night7's unrelated base wave.
            service.Stop();for(int i=0;i<320;i++)TickBoss(.25f);
            Assert.AreEqual(4,evidence.casts.Count);Assert.AreEqual(0,evidence.summons.Count);Assert.AreEqual(0,service.GetSnapshot().Count);
        }
    }
}
#endif