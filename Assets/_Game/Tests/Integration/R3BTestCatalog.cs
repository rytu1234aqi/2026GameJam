#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using Spotlight.Bootstrap;
using Spotlight.Contracts;
using Spotlight.Core.Data;
using Spotlight.Enemies.Definitions;
using UnityEditor;

namespace Spotlight.Tests.Integration
{
    // H02 catalog/enemy/flight registration is supplied only in memory.
    // N05 drops are loaded through the actual public Catalog DropTables references.
    internal static class R3BTestCatalog
    {
        internal static CatalogData Build()
        {
            var asset = AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(PrototypeCatalogFactory.CatalogPath);
            Assert.NotNull(asset);
            CatalogData data = asset.BuildCatalogData();
            // Register existing enemy SOs only inside the test fixture.
            var enemies = new List<EnemyDefinition>(data.Enemies);
            foreach (string name in new[] { "SlimeWater", "SlimeFire", "SlimeThunder", "Sandworm", "Gargoyle", "Falcon" })
            {
                var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinitionSO>("Assets/_Game/Data/Prototype/P4/" + name + ".asset");
                Assert.NotNull(enemy);
                EnemyDefinition definition = enemy.ToDefinition();
                enemies.RemoveAll(e => e.Id == definition.Id); enemies.Add(definition);
            }
            data.Enemies = enemies.ToArray();
            foreach (LevelDefinition level in data.Levels)
            {
                if (level.Id != "level.prototype") continue;
                PathDefinition ground = null;
                foreach (PathDefinition path in level.Paths) if (path.Id == "path.ground") ground = path;
                Assert.NotNull(ground);
                var paths = new List<PathDefinition>(level.Paths); paths.RemoveAll(p => p.Id == "path.air");
                paths.Add(new PathDefinition { Id="path.air", SpawnPointId="spawn.air", Movement=MovementKind.Flying, Cells=ground.Cells });
                level.Paths = paths.ToArray();
                var spawns = new List<SpawnPointDefinition>(level.SpawnPoints); spawns.RemoveAll(s => s.Id == "spawn.air");
                spawns.Add(new SpawnPointDefinition { Id="spawn.air", Cell=ground.Cells[0] }); level.SpawnPoints=spawns.ToArray();
            }
            CollectionAssert.IsEmpty(new GameCatalog(data).ValidateAll(), "N05 drops and all fixture references must resolve");
            return data;
        }
    }
}
#endif