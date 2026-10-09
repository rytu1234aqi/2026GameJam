#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Spotlight.Combat;
using Spotlight.Contracts;
using Spotlight.Core;
using Spotlight.Core.Clock;
using Spotlight.Core.Data;
using Spotlight.Core.Events;
using Spotlight.Elements;
using Spotlight.Enemies;
using Spotlight.Enemies.Definitions;
using Spotlight.Bootstrap;
using UnityEditor;
using UnityEngine;

namespace Spotlight.Tests.Integration
{
    // Scheduler acceptance only: real assets/transactions, no movement, attacks or loot ticks.
    // N05 drops are real catalog assets; H02 public enemy/flight integration remains unresolved.
    public sealed class R3BStoryWaveTests
    {
        static readonly string[] GroupIds = { "melee", "ranged", "slime_water", "slime_fire", "slime_thunder", "sandworm", "gargoyle", "falcon", "boss" };
        static readonly string[] EnemyIds = { "enemy.goblin.melee", "enemy.goblin.ranged", "enemy.slime.water", "enemy.slime.fire", "enemy.slime.thunder", "enemy.sandworm", "enemy.gargoyle", "enemy.falcon", "enemy.soulking" };
        static readonly int[][] Counts = {
            new[] { 5,2,2,1,0,0,0,0,0 }, new[] { 6,3,2,2,1,0,0,0,0 },
            new[] { 6,4,2,2,1,2,1,0,0 }, new[] { 7,4,3,3,2,2,2,1,0 },
            new[] { 8,5,3,3,3,3,2,2,1 }
        };
        const string AssetsPath = "Assets/_Game/Data/Prototype/P4/";
        [Serializable] public sealed class Birth
        {
            public long actor; public string enemy, path; public float seconds, maxHp; public bool summon;
        }
        [Serializable] public sealed class CountByType
        {
            public string enemy; public int waveBirths, summonBirths;
        }
        [Serializable] public sealed class Sample
        {
            public float seconds; public int alive, waveBirths, summonBirths, pendingWave, pendingSummon;
        }
        [Serializable] public sealed class Evidence
        {
            public int night; public string scope; public bool passed;
            public string[] publicCatalogErrors;
            public List<Birth> births = new List<Birth>();
            public List<Sample> samples = new List<Sample>();
            public List<CountByType> byType = new List<CountByType>();
        }

        [TestCase(3,10)] [TestCase(4,14)] [TestCase(5,18)] [TestCase(6,24)] [TestCase(7,30)]
        public void ConfiguredNightSpawnsExactTypesOnIndependentTimers(int night, int total)
        {
            var catalogAsset = AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(PrototypeCatalogFactory.CatalogPath);
            Assert.NotNull(catalogAsset);
            CatalogData data = catalogAsset.BuildCatalogData();
            var evidence = new Evidence {
                night = night,
                scope = "Isolated EnemyServices SpawnAndBoss scheduler with real SO assets and transactions. Test-only enemy registration and Flying path sharing ground Cells. No movement, combat balance, drops, rendering or public-scene acceptance. N05 drops are present; H02 public integration remains.",
                publicCatalogErrors = new List<string>(new GameCatalog(data).ValidateAll()).ToArray()
            };
            var wave = AssetDatabase.LoadAssetAtPath<WaveDefinitionSO>(AssetsPath + "StoryNight" + night + ".asset");
            Assert.NotNull(wave);
            Assert.AreEqual("wave.story." + night, wave.Id);
            Assert.AreEqual(night, wave.DayIndex);
            Assert.AreEqual(1, wave.WaveIndex);
            Assert.AreEqual(0, wave.DelayAfterPreviousWaveSeconds);
            int ordinal = 0;
            for (int i = 0; i < GroupIds.Length; i++)
            {
                if (Counts[night-3][i] == 0) continue;
                SpawnGroupConfig g = wave.Groups[ordinal];
                Assert.AreEqual(GroupIds[i], g.Id);
                Assert.AreEqual(EnemyIds[i], g.EnemyId);
                Assert.AreEqual(Counts[night-3][i], g.Count);
                Assert.AreEqual(i == 8 ? 25 : ordinal * 4, g.StartDelaySeconds);
                Assert.AreEqual(1, g.SpawnIntervalSeconds);
                Assert.AreEqual(i != 8, g.ApplyDayGrowth);
                Assert.AreEqual(1, g.HpMultiplier); Assert.AreEqual(1, g.DamageMultiplier);
                Assert.AreEqual(i == 7 ? "spawn.air" : "spawn.ground", g.SpawnPointId);
                Assert.AreEqual(i == 7 ? "path.air" : "path.ground", g.PathId);
                ordinal++;
            }
            Assert.AreEqual(ordinal, wave.Groups.Length, "Zero-count groups must be omitted");

            data = R3BTestCatalog.Build();
            var catalog = new GameCatalog(data);
            var events = new GameEventBus();
            var core = new CoreServices(catalog, events);
            Assert.AreEqual(ErrorCode.None, core.Store.Initialize(new NewRunRequest { LevelId="level.prototype", Mode=GameMode.Story, Seed=404 }).Error);
            var clock = new GameClock(core.Store.GetFlowSnapshot, events);
            var combat = new CombatServices(catalog, core.Board, core.World, core.Transactions, core.Commands, core.Ids, events, new ElementRuleService(catalog, core.Random), clock);
            var service = new EnemyServices(catalog, core.Board, core.World, core.Motion, core.Transactions, core.Commands, core.Ids, core.Random, combat.Status, combat.Damage, combat.Projectiles, combat.Effects, events, clock);
            var context = new SessionStartContext { RunId=core.Store.GetFlowSnapshot().RunId, LevelId="level.prototype", Mode=GameMode.Story, DayIndex=night, SpringId=core.World.SpringId };
            combat.BeginSession(context); service.BeginSession(context);
            var seen = new HashSet<long>();
            int eventBirths = 0;
            IDisposable subscription = events.Subscribe<ActorSpawnedEvent>(e => { if (e.Kind == ActorKind.Enemy) eventBirths++; });
            try
            {
                Assert.AreEqual(OperationState.Committed, service.StartNight(night, GameMode.Story).State);
                for (int tick = 0; tick <= 200; tick++)
                {
                    float seconds = tick * .25f;
                    service.Tick(new TickContext(tick, seconds, tick == 0 ? 0 : .25f, TickStage.SpawnAndBoss));
                    events.Flush();
                    foreach (EnemySnapshot enemy in service.GetSnapshot())
                    {
                        if (!seen.Add(enemy.ActorId.Value)) continue;
                        ActorSnapshot actor; Assert.IsTrue(core.World.TryGetActor(enemy.ActorId, out actor));
                        evidence.births.Add(new Birth { actor=enemy.ActorId.Value, enemy=enemy.DefinitionId, path=enemy.PathId, seconds=seconds, maxHp=actor.MaxHp, summon=enemy.IsSummon });
                    }
                    Assert.AreEqual(seen.Count, eventBirths, "Every observed birth must have a committed spawn event");
                    if (tick == 20 || tick == 120 || tick == 200) Record(evidence, service, seconds);
                }
                foreach (string enemy in EnemyIds)
                {
                    int i = Array.IndexOf(EnemyIds, enemy), count = 0, summoned = 0;
                    float first = float.PositiveInfinity;
                    foreach (Birth b in evidence.births) if (b.enemy == enemy)
                    {
                        if (b.summon) { summoned++; continue; }
                        count++; first = Math.Min(first, b.seconds);
                        EnemyDefinition definition; Assert.IsTrue(catalog.TryGetEnemy(enemy, out definition));
                        Assert.AreEqual(definition.MaxHp * (i == 8 ? 1 : 1+.25f*(night-1)), b.maxHp);
                        Assert.AreEqual(i == 7 ? "path.air" : "path.ground", b.path);
                    }
                    Assert.AreEqual(Counts[night-3][i], count, enemy);
                    if (count > 0)
                    {
                        float expected = 0;
                        if (i == 8) expected = 25;
                        else for (int j = 0; j < i; j++) if (Counts[night-3][j] > 0) expected += 4;
                        Assert.AreEqual(expected, first, enemy + " first birth time");
                        int sequence = 0;
                        foreach (Birth b in evidence.births) if (b.enemy == enemy && !b.summon)
                            Assert.AreEqual(expected + sequence++, b.seconds, enemy + " one-second interval");
                    }
                    evidence.byType.Add(new CountByType { enemy=enemy, waveBirths=count, summonBirths=summoned });
                }
                Assert.AreEqual(total, evidence.samples[2].waveBirths);
                Assert.AreEqual(night == 7 ? 3 : 0, evidence.samples[2].summonBirths);
                Assert.AreEqual(0, evidence.samples[2].pendingWave);
                // At t=5 the ranged group is spawning although melee actors are still alive.
                Assert.GreaterOrEqual(evidence.samples[0].waveBirths, 7);
                Assert.AreEqual(evidence.samples[0].waveBirths, evidence.samples[0].alive);
                if (night == 7)
                {
                    Assert.AreEqual(30, evidence.samples[1].waveBirths);
                    Assert.AreEqual(0, evidence.samples[1].summonBirths);
                    foreach (Birth b in evidence.births) if (b.enemy == "enemy.soulking") Assert.AreEqual(1000, b.maxHp);
                }
                // Scripted despawn exercises real removal without turning this scheduler test into loot acceptance.
                service.DespawnAll(DeathReason.ScriptedExit); events.Flush();
                Record(evidence, service, 50);
                Assert.AreEqual(0, evidence.samples[3].alive);
                Assert.AreEqual(total, evidence.samples[3].waveBirths);
                Assert.AreEqual(night == 7 ? 3 : 0, evidence.samples[3].summonBirths);
                evidence.passed = true;
            }
            finally
            {
                subscription.Dispose(); service.EndSession(); combat.EndSession(); events.Clear();
                Directory.CreateDirectory("QA/R3B");
                File.WriteAllText("QA/R3B/N04-night" + night + ".json", JsonUtility.ToJson(evidence, true));
            }
        }

        static void Record(Evidence evidence, EnemyServices service, float seconds)
        {
            WaveSnapshot state = service.Waves.GetSnapshot(); int wave=0, summon=0;
            foreach (Birth birth in evidence.births) { if (birth.summon) summon++; else wave++; }
            evidence.samples.Add(new Sample { seconds=seconds, alive=state.AliveCount, waveBirths=wave, summonBirths=summon, pendingWave=state.PendingSpawnCount, pendingSummon=state.PendingSummonCount });
        }
    }
}
#endif