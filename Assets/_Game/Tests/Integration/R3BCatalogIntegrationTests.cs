#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spotlight.Bootstrap;
using Spotlight.Contracts;
using Spotlight.Core;
using Spotlight.Core.Data;
using Spotlight.Core.Save;
using Spotlight.Elements;
using Spotlight.Combat;
using Spotlight.Enemies;
using Spotlight.Core.Clock;
using Spotlight.Core.Events;
using UnityEditor;
using UnityEngine;

namespace Spotlight.Tests.Integration
{
    public sealed class R3BCatalogIntegrationTests
    {
        private PrototypeCatalogSO asset;
        private GameCatalog catalog;
        [SetUp] public void Setup()
        {
            asset = AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(PrototypeCatalogFactory.CatalogPath);
            Assert.NotNull(asset);
            catalog = new GameCatalog(asset.BuildCatalogData());
        }
        [Test] public void FormalCatalogResolvesAllRegisteredReferences()
        {
            CollectionAssert.IsEmpty(catalog.ValidateAll());
            CollectionAssert.IsEmpty(asset.Validate());
            CollectionAssert.AreEquivalent(new[]
            {
                "production.water", "production.fire", "production.earth", "production.wood", "production.wind", "production.thunder",
                "production.wood_fire_convert", "production.wood_earth_bonus", "production.wood_water_bonus", "production.wood_wood_bonus"
            }, catalog.GetProductionRules().Select(rule => rule.Id));
            var woodPair = catalog.GetProductionRules().Single(rule => rule.Id == "production.wood_wood_bonus");
            Assert.IsTrue(woodPair.Enabled);
            Assert.AreEqual(ProductionMatch.AdjacentPair, woodPair.Match);
            Assert.IsFalse(woodPair.ConsumeA || woodPair.ConsumeB);
            foreach (string id in new[] { "status.contact.burn", "status.contact.water_slow", "status.reaction.cold_wave", "status.boss.curse" })
            { StatusDefinition status; Assert.IsTrue(catalog.TryGetStatus(id, out status), id); }
            foreach (string id in new[] { "enemy.slime.water", "enemy.slime.fire", "enemy.slime.thunder", "enemy.sandworm", "enemy.gargoyle", "enemy.falcon" })
            {
                EnemyDefinition enemy; Assert.IsTrue(catalog.TryGetEnemy(id, out enemy), id);
                Assert.AreEqual(1, asset.PrefabEntries.Count(e => e.Key == enemy.PrefabKey));
                GameObject prefab = asset.PrefabEntries.Single(e => e.Key == enemy.PrefabKey).Prefab;
                Assert.NotNull(prefab);
                Assert.IsNull(prefab.transform.parent, "Prefab mapping must reference the enemy root, not a child visual.");
                DropTableDefinition drop; Assert.IsTrue(catalog.TryGetDropTable(enemy.DropTableId, out drop));
            }
        }
        [Test] public void EveryFormalWaveGroupUsesAnExistingPrefabAndCompatibleRoute()
        {
            LevelDefinition level; Assert.IsTrue(catalog.TryGetLevel("level.prototype", out level));
            Assert.AreEqual(MovementKind.Ground, level.Paths.Single(p => p.Id == "path.ground").Movement);
            Assert.AreEqual(MovementKind.Flying, level.Paths.Single(p => p.Id == "path.air").Movement);
            foreach (var wave in asset.StoryWaves.Concat(asset.WaveTemplates))
            foreach (var group in wave.ToDefinition().Groups)
            {
                EnemyDefinition enemy; Assert.IsTrue(catalog.TryGetEnemy(group.EnemyId, out enemy), group.EnemyId);
                Assert.NotNull(asset.PrefabEntries.Single(e => e.Key == enemy.PrefabKey).Prefab);
                PathDefinition path = level.Paths.Single(p => p.Id == group.PathId);
                Assert.AreEqual(enemy.Movement, path.Movement);
                Assert.AreEqual(group.SpawnPointId, path.SpawnPointId);
                Assert.AreEqual(path.Cells[0], level.SpawnPoints.Single(p => p.Id == group.SpawnPointId).Cell);
                Assert.AreEqual(level.SpringCell, path.Cells[path.Cells.Count - 1]);
            }
        }
        [TestCase("elm.water", "production.wood_water_bonus", "elm.wood", false)]
        [TestCase("elm.earth", "production.wood_earth_bonus", "elm.wood", false)]
        [TestCase("elm.fire", "production.wood_fire_convert", "elm.fire", true)]
        public void FormalWoodRulesRequireCardinalAdjacencyAndOnlyConversionConsumesWood(string neighbor, string ruleId, string output, bool consumes)
        {
            var service = new ElementRuleService(catalog, new RandomService(new SessionStateStore(catalog)));
            foreach (CellCoord offset in new[] { new CellCoord(1, 0), new CellCoord(-1, 0), new CellCoord(0, 1), new CellCoord(0, -1), new CellCoord(1, 1), new CellCoord(2, 0) })
            {
                var cells = new[] {
                    new CellSnapshot { Cell = new CellCoord(3, 3), OccupantId = new EntityId(2), OccupantKind = OccupantKind.ElementBlock, DefinitionId = "elm.wood" },
                    new CellSnapshot { Cell = new CellCoord(3 + offset.X, 3 + offset.Y), OccupantId = new EntityId(1), OccupantKind = OccupantKind.ElementBlock, DefinitionId = neighbor }
                };
                bool adjacent = Math.Abs(offset.X) + Math.Abs(offset.Y) == 1;
                ProductionResult result = service.ResolveProduction(new ProductionInput { Board = new BoardSnapshot { Revision = 7, Cells = cells }, CompletedNight = 1, Random = new RandomState(RandomStream.Production, 123) });
                Assert.AreEqual(adjacent ? 1 : 0, result.AppliedRuleIds.Count(id => id == ruleId));
                Assert.AreEqual(1 + (adjacent ? 1 : 0), result.HandDeltas.Where(d => d.DefinitionId == output).Sum(d => d.Amount));
                Assert.AreEqual(adjacent && consumes ? 1 : 0, result.Board.Count);
                if (result.Board.Count > 0) Assert.AreEqual(new EntityId(2), result.Board[0].EntityId);
                Assert.AreEqual(123u, result.NextRandom.State);
                Assert.AreEqual(7, result.ExpectedRevision);
                Assert.AreEqual(OccupantKind.ElementBlock, cells[0].OccupantKind);
            }
        }
        [Test] public void ExistingPrototypeOneCheckpointRestoresAndDifferentConfigStillRejects()
        {
            Assert.AreEqual("prototype.1", catalog.GetSettings().ConfigVersion);
            var store = new SessionStateStore(catalog);
            Assert.AreEqual(ErrorCode.None, store.Initialize(new NewRunRequest { LevelId = "level.prototype", Mode = GameMode.Story, Seed = 1 }).Error);
            CheckpointData checkpoint = store.CaptureCheckpoint(catalog);
            checkpoint.ConfigVersion = "prototype.1";
            string directory = Path.Combine(Path.GetTempPath(), "Spotlight_R3BCatalog_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var saves = new SaveService(store, catalog, null, directory);
                string path = Path.Combine(directory, "checkpoint.json");
                File.WriteAllText(path, JsonUtility.ToJson(CheckpointFileModel.FromCheckpoint(checkpoint)));
                var read = saves.ReadCheckpoint(); Assert.AreEqual(ErrorCode.None, read.Error);
                Assert.AreEqual(ErrorCode.None, new SessionStateStore(catalog).Restore(read.Data).Error);
                checkpoint.ConfigVersion = "other.configuration";
                File.WriteAllText(path, JsonUtility.ToJson(CheckpointFileModel.FromCheckpoint(checkpoint)));
                Assert.AreEqual(ErrorCode.ConfigMismatch, saves.ReadCheckpoint().Error);
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestCase("water-earth", 3, 1, 1, 0, 0)]
        [TestCase("water-fire", 2, 1, 0, 2, 1)]
        [TestCase("one-wood-two-fire", 1, 0, 0, 3, 1)]
        [TestCase("two-wood-one-fire", 2, 0, 0, 3, 2)]
        public void MixedFormalProductionAwardsBonusesBeforeConversionAndReusesFire(string layout, int wood, int water, int earth, int fire, int removals)
        {
            var core = new CoreServices(catalog, new GameEventBus());
            Assert.AreEqual(ErrorCode.None, core.Store.Initialize(new NewRunRequest { LevelId = "level.prototype", Mode = GameMode.Story, Seed = 42 }).Error);
            var cells = new List<CellSnapshot>();
            Action<string, int, int> add = (id, x, y) => cells.Add(new CellSnapshot { Cell = new CellCoord(x, y), DefinitionId = id, OccupantKind = OccupantKind.ElementBlock, OccupantId = new EntityId(cells.Count + 1) });
            if (layout == "two-wood-one-fire") { add("elm.fire", 3, 3); add("elm.wood", 2, 3); add("elm.wood", 4, 3); }
            else
            {
                add("elm.wood", 3, 3);
                add(layout == "one-wood-two-fire" ? "elm.fire" : "elm.water", 2, 3);
                add(layout == "water-earth" ? "elm.earth" : "elm.fire", 4, 3);
            }
            Dictionary<string, long> resources = core.Store.CaptureCheckpoint(catalog).Resources.ToDictionary(r => r.Bucket + ":" + r.DefinitionId, r => r.Amount);
            BoardSnapshot liveBefore = core.Board.GetSnapshot();
            var service = new ElementRuleService(catalog, core.Random);
            ProductionResult result = service.ResolveProduction(new ProductionInput { Board = new BoardSnapshot { Revision = liveBefore.Revision, Cells = cells }, CompletedNight = 1, Random = new RandomState(RandomStream.Production, 123) });
            foreach (var expectation in new[] { new KeyValuePair<string, long>("elm.wood", wood), new KeyValuePair<string, long>("elm.water", water), new KeyValuePair<string, long>("elm.earth", earth), new KeyValuePair<string, long>("elm.fire", fire) })
                Assert.AreEqual(expectation.Value, result.HandDeltas.Where(d => d.DefinitionId == expectation.Key).Sum(d => d.Amount), layout + ":" + expectation.Key);
            Assert.AreEqual(removals, result.Board.Count);
            Assert.AreEqual(removals, result.Board.Select(b => b.EntityId).Distinct().Count(), "Each wood is consumed only once.");
            foreach (BoardMutation mutation in result.Board)
                Assert.AreEqual("elm.wood", cells.Single(c => c.OccupantId.Equals(mutation.EntityId)).DefinitionId, "Fire is reusable and never consumed.");
            int conversion = result.AppliedRuleIds.ToList().IndexOf("production.wood_fire_convert");
            if (conversion >= 0)
                foreach (string bonus in new[] { "production.wood_water_bonus", "production.wood_earth_bonus" })
                    if (result.AppliedRuleIds.Contains(bonus)) Assert.Less(result.AppliedRuleIds.ToList().IndexOf(bonus), conversion);
            Assert.AreEqual(123u, result.NextRandom.State);
            Assert.IsTrue(cells.All(c => c.OccupantKind == OccupantKind.ElementBlock), "Detached input is unchanged.");
            CollectionAssert.AreEquivalent(resources, core.Store.CaptureCheckpoint(catalog).Resources.ToDictionary(r => r.Bucket + ":" + r.DefinitionId, r => r.Amount));
            Assert.AreEqual(liveBefore.Revision, core.Board.GetSnapshot().Revision);
            Assert.IsTrue(core.Board.GetSnapshot().Cells.All(c => c.OccupantKind == OccupantKind.None), "Planner must not place or remove live occupants.");
        }

        [Test] public void FormalFalconCrossesLegallyDeployedWoodWhileProjectedGroundObstacleStopsGroundEnemy()
        {
            CollectionAssert.IsEmpty(catalog.ValidateAll());
            var events = new GameEventBus();
            var core = new CoreServices(catalog, events);
            Assert.AreEqual(ErrorCode.None, core.Store.Initialize(new NewRunRequest { LevelId = "level.prototype", Mode = GameMode.Story, Seed = 42 }).Error);
            LevelDefinition level; Assert.IsTrue(catalog.TryGetLevel("level.prototype", out level));
            PathDefinition groundPath = level.Paths.Single(p => p.Id == "path.ground");
            PathDefinition airPath = level.Paths.Single(p => p.Id == "path.air");
            Assert.AreEqual(new CellCoord(0, 3), airPath.Cells[1]);
            Assert.AreEqual(OperationState.Committed, core.Deployment.TryPlace(core.Commands.Create(core.Transactions.Revision), new PlacementRequest { Kind = OccupantKind.ElementBlock, DefinitionId = "elm.wood", Cell = airPath.Cells[1] }).State);
            CellSnapshot deployed; Assert.IsTrue(core.Board.TryGetCell(airPath.Cells[1], out deployed));
            Assert.IsTrue(deployed.BlocksGround); Assert.IsFalse(deployed.OnGroundPath);
            Assert.AreEqual("elm.wood", deployed.DefinitionId);
            long woodAfterDeployment = core.Resources.GetQuantity(ResourceBucket.Hand, "elm.wood");
            // Deployment forbids ground-blocking placement on paths. Inject only a board-query obstruction,
            // retaining formal paths/enemies and the real committed world-motion writer.
            var board = new ObstructionProjection(core.Board, groundPath.Cells[1]);
            var clock = new GameClock(core.Store.GetFlowSnapshot, events);
            var combat = new CombatServices(catalog, core.Board, core.World, core.Transactions, core.Commands, core.Ids, events, new ElementRuleService(catalog, core.Random), clock);
            var enemies = new EnemyServices(catalog, board, core.World, core.Motion, core.Transactions, core.Commands, core.Ids, core.Random, combat.Status, combat.Damage, combat.Projectiles, combat.Effects, events, clock);
            var context = new SessionStartContext { RunId = core.Store.GetFlowSnapshot().RunId, LevelId = level.Id, Mode = GameMode.Story, DayIndex = 1, SpringId = core.World.SpringId };
            combat.BeginSession(context); enemies.BeginSession(context);
            try
            {
                Assert.AreEqual(OperationState.Committed, enemies.StartNight(1, GameMode.Story).State);
                EntityId ground, falcon;
                Assert.AreEqual(OperationState.Committed, enemies.TrySpawn(new EnemySpawnRequest { DefinitionId = "enemy.goblin.melee", SpawnPointId = groundPath.SpawnPointId, PathId = groundPath.Id }, out ground).State);
                Assert.AreEqual(OperationState.Committed, enemies.TrySpawn(new EnemySpawnRequest { DefinitionId = "enemy.falcon", SpawnPointId = airPath.SpawnPointId, PathId = airPath.Id }, out falcon).State);
                ActorSnapshot groundBefore, airBefore; Assert.IsTrue(core.World.TryGetActor(ground, out groundBefore)); Assert.IsTrue(core.World.TryGetActor(falcon, out airBefore));
                for (int i = 0; i < 4; i++) enemies.Tick(new TickContext(i, (i + 1) * .25f, .25f, TickStage.Movement));
                ActorSnapshot groundAfter, airAfter; Assert.IsTrue(core.World.TryGetActor(ground, out groundAfter)); Assert.IsTrue(core.World.TryGetActor(falcon, out airAfter));
                Assert.AreEqual(groundBefore.Position.X, groundAfter.Position.X); Assert.AreEqual(groundBefore.Position.Y, groundAfter.Position.Y);
                EnemyDefinition definition; Assert.IsTrue(catalog.TryGetEnemy("enemy.falcon", out definition));
                float travel = definition.SpeedInCellsPerSecond * level.CellSize;
                Assert.Greater(travel, level.CellSize, "One second must pass the wood on the first segment.");
                Assert.AreEqual(airBefore.Position.X + travel - level.CellSize, airAfter.Position.X, .0001f);
                Assert.AreEqual(airBefore.Position.Y + level.CellSize, airAfter.Position.Y, .0001f);
                Assert.Greater(enemies.GetSnapshot().Single(e => e.ActorId.Equals(falcon)).PathSegment, 0, "Falcon crosses the obstructed next cell.");
                Assert.AreEqual(0, enemies.GetSnapshot().Single(e => e.ActorId.Equals(ground)).PathSegment);
                board.Blocked = false;
                enemies.Tick(new TickContext(5, 1.25f, .25f, TickStage.Movement));
                Assert.IsTrue(core.World.TryGetActor(ground, out groundAfter)); Assert.Greater(groundAfter.Position.X, groundBefore.Position.X);
                CellSnapshot liveCell; Assert.IsTrue(core.Board.TryGetCell(groundPath.Cells[1], out liveCell)); Assert.IsFalse(liveCell.BlocksGround, "Projection does not mutate the live board.");
                Assert.IsTrue(core.Board.TryGetCell(airPath.Cells[1], out deployed));
                Assert.AreEqual("elm.wood", deployed.DefinitionId); Assert.IsTrue(deployed.BlocksGround);
                Assert.AreEqual(woodAfterDeployment, core.Resources.GetQuantity(ResourceBucket.Hand, "elm.wood"), "Movement must not consume deployed wood or hand resources.");
            }
            finally { enemies.EndSession(); combat.EndSession(); }
        }

        private sealed class ObstructionProjection : IBoardQuery
        {
            private readonly IBoardQuery inner; private readonly CellCoord obstruction;
            public bool Blocked = true;
            public ObstructionProjection(IBoardQuery inner, CellCoord obstruction) { this.inner = inner; this.obstruction = obstruction; }
            public BoardSnapshot GetSnapshot() { return inner.GetSnapshot(); }
            public bool TryGetCell(CellCoord cell, out CellSnapshot snapshot)
            {
                CellSnapshot source; bool found = inner.TryGetCell(cell, out source);
                snapshot = !found ? null : new CellSnapshot { Cell = source.Cell, TerrainId = source.TerrainId, Reserved = source.Reserved, OnGroundPath = source.OnGroundPath, OccupantId = source.OccupantId, OccupantKind = source.OccupantKind, DefinitionId = source.DefinitionId, BlocksGround = source.BlocksGround || (Blocked && cell.Equals(obstruction)) };
                return found;
            }
            public IReadOnlyList<CellCoord> GetNeighbors4(CellCoord cell) { return inner.GetNeighbors4(cell); }
            public WorldPoint CellToWorld(CellCoord cell) { return inner.CellToWorld(cell); }
            public bool TryWorldToCell(WorldPoint point, out CellCoord cell) { return inner.TryWorldToCell(point, out cell); }
            public IReadOnlyList<CellSnapshot> TraceSegment(WorldPoint from, WorldPoint to) { return inner.TraceSegment(from, to); }
        }
    }
}
#endif
