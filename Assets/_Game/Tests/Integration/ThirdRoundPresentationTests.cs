#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Spotlight.Bootstrap;
using Spotlight.Contracts;
using Spotlight.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Spotlight.Tests.Integration
{
    public sealed class ThirdRoundPresentationTests
    {
        private ViewPool pool;
        private ModuleComposition runtime;
        private GameObject host;
        private string directory;
        private Scene originalScene, scene;
        private readonly List<KeyValuePair<GameObject, bool>> previousRoots = new List<KeyValuePair<GameObject, bool>>();
        private static readonly string[] Projectiles = { "PRJ_Basic", "PRJ_Enemy" };
        private static readonly string[] Enemies = { "ENM_GoblinMelee", "ENM_GoblinRanged" };

        private static GameObject Prefab(string path)
        {
            GameObject result = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/" + path + ".prefab");
            Assert.NotNull(result, path);
            return result;
        }

        [Test]
        public void ProjectileDirectionsAndOverlaysAreUnlitAndHaveDistinctSorting()
        {
            foreach (string name in Projectiles)
            {
                GameObject prefab = Prefab("Projectiles/" + name);
                ProjectileVisual2D visual = prefab.GetComponent<ProjectileVisual2D>();
                Assert.NotNull(visual, name);
                SpriteRenderer direction = visual.VisualRoot.Find("Direction").GetComponent<SpriteRenderer>();
                foreach (SpriteRenderer sprite in new[] { direction, visual.ElementOverlay })
                {
                    Assert.NotNull(sprite.sharedMaterial, name);
                    Assert.AreEqual("7afc6ee283ef9ce46bf550a0b9de58a2",
                        AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sprite.sharedMaterial)), name);
                    StringAssert.Contains("Unlit", sprite.sharedMaterial.shader.name, name);
                }
                Assert.AreEqual(30, direction.sortingOrder, name);
                Assert.AreEqual(31, visual.ElementOverlay.sortingOrder, name);
            }
        }

        private void SetUpPlayScene()
        {
            originalScene = SceneManager.GetActiveScene();
            foreach (GameObject root in originalScene.GetRootGameObjects())
            {
                previousRoots.Add(new KeyValuePair<GameObject, bool>(root, root.activeSelf));
                root.SetActive(false);
            }
            scene = SceneManager.CreateScene("ThirdRoundPresentation_" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(scene);
        }

        [UnityTest]
        public IEnumerator PooledProjectilesAndEnemyBodiesResetBeforeAnyDisplayUpdate()
        {
            yield return new EnterPlayMode();
            SetUpPlayScene();
            Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
            foreach (string name in Projectiles) prefabs.Add(name, Prefab("Projectiles/" + name));
            foreach (string name in Enemies) prefabs.Add(name, Prefab("Enemies/" + name));
            pool = new ViewPool(prefabs);
            foreach (string name in Projectiles)
            {
                PoolLease lease = pool.Rent(name);
                GameObject instance = (GameObject)lease.View;
                ProjectileVisual2D visual = instance.GetComponent<ProjectileVisual2D>();
                Quaternion baseline = visual.VisualRoot.localRotation;
                Color color = visual.ElementOverlay.color;
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    visual.SetDirection(new WorldPoint(0, 1));
                    visual.SetElements(3);
                    Assert.IsTrue(visual.ElementOverlay.enabled, name);
                    Assert.That(Quaternion.Angle(Quaternion.Euler(0, 0, 90), visual.VisualRoot.localRotation), Is.LessThan(.001f));
                    visual.SetElements(64);
                    Assert.IsFalse(visual.ElementOverlay.enabled, "Unknown masks must not display an element");
                    visual.SetElements(32);
                    Assert.AreEqual(OperationState.Committed, pool.Return(lease.LeaseId).State);
                    Assert.That(Quaternion.Angle(baseline, visual.VisualRoot.localRotation), Is.LessThan(.001f), "OnDisable reset");
                    Assert.IsFalse(visual.ElementOverlay.enabled);
                    lease = pool.Rent(name);
                    Assert.AreSame(instance, lease.View, "Must reuse the actual pooled instance");
                    Assert.That(Quaternion.Angle(baseline, visual.VisualRoot.localRotation), Is.LessThan(.001f), "OnEnable reset before renderer");
                    Assert.AreEqual(color, visual.ElementOverlay.color);
                    Assert.IsFalse(visual.ElementOverlay.enabled);
                }
                pool.Return(lease.LeaseId);
            }
            for (int i = 0; i < Enemies.Length; i++)
            {
                PoolLease lease = pool.Rent(Enemies[i]);
                GameObject instance = (GameObject)lease.View;
                EnemyFacingVisual2D facing = instance.GetComponent<EnemyFacingVisual2D>();
                EnemyHealthBar2D health = instance.GetComponent<EnemyHealthBar2D>();
                Assert.AreSame(instance.GetComponent<EnemySpriteAnimator2D>().BodyRenderer, facing.BodyRenderer);
                Assert.AreSame(instance.transform, facing.BodyRenderer.transform.parent);
                Assert.IsFalse(facing.DefaultFacingRight, "Both current goblin sprites face left");
                bool baseline = facing.BodyRenderer.flipX;
                Vector3 scale = instance.transform.localScale, barPosition = health.BarRoot.localPosition;
                Quaternion rotation = instance.transform.localRotation;
                facing.SetDirection(new WorldPoint(1, 0));
                Assert.AreEqual(!facing.DefaultFacingRight, facing.BodyRenderer.flipX);
                facing.SetDirection(new WorldPoint(-1, 0));
                Assert.AreEqual(facing.DefaultFacingRight, facing.BodyRenderer.flipX);
                facing.SetDirection(new WorldPoint(0, 1));
                facing.SetDirection(new WorldPoint(float.NaN, 0));
                Assert.AreEqual(facing.DefaultFacingRight, facing.BodyRenderer.flipX, "Zero X / invalid inputs retain facing");
                Assert.AreEqual(scale, instance.transform.localScale);
                Assert.AreEqual(rotation, instance.transform.localRotation);
                Assert.AreEqual(barPosition, health.BarRoot.localPosition);
                foreach (SpriteRenderer sprite in health.BarRoot.GetComponentsInChildren<SpriteRenderer>()) Assert.IsFalse(sprite.flipX);
                pool.Return(lease.LeaseId);
                lease = pool.Rent(Enemies[i]);
                Assert.AreSame(instance, lease.View);
                Assert.AreEqual(baseline, facing.BodyRenderer.flipX, "Reused enemy restores original flip");
                pool.Return(lease.LeaseId);
            }
            yield return null;
        }

        private CommandContext Command() { return runtime.Core.Commands.Create(runtime.Core.Transactions.Revision); }
        private void StartBuild()
        {
            Assert.AreEqual(OperationState.Committed, runtime.Flow.TryNewRun(Command(),
                new NewRunRequest { LevelId = "level.prototype", Mode = GameMode.Story, Seed = 123 }).State);
            runtime.Advance(0);
            DayEventSnapshot day = runtime.Gameplay.DayEvents.GetSnapshot();
            Assert.NotNull(day.State);
            Assert.AreEqual(OperationState.Committed, runtime.Gameplay.DayEvents.TryChoose(Command(), day.State.InstanceId, day.Options[0].Id).State);
            runtime.Advance(0);
            if (!runtime.Flow.GetSnapshot().TutorialShown)
                Assert.AreEqual(OperationState.Committed, runtime.Flow.TryDismissTutorial(Command()).State);
            Assert.AreEqual(GamePhase.Build, runtime.Flow.GetSnapshot().Phase);
        }
        private void Place(string definition, OccupantKind kind, CellCoord cell)
        {
            Assert.AreEqual(OperationState.Committed, runtime.Core.Deployment.TryPlace(Command(),
                new PlacementRequest { DefinitionId = definition, Kind = kind, Cell = cell }).State);
            runtime.Advance(0);
        }
        // Deterministically exercise the real renderer using real snapshots; no GameBootstrap or second simulation loop.
        private static void Draw(RuntimeWorldRenderer2D renderer)
        {
            typeof(RuntimeWorldRenderer2D).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(renderer, null);
        }
        private static IDictionary Entries(RuntimeWorldRenderer2D renderer)
        {
            return (IDictionary)typeof(RuntimeWorldRenderer2D).GetField("views", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(renderer);
        }
        private static GameObject View(RuntimeWorldRenderer2D renderer, string key)
        {
            object entry = Entries(renderer)[key];
            Assert.NotNull(entry, key);
            return (GameObject)entry.GetType().GetField("View", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(entry);
        }

        [UnityTest]
        public IEnumerator RendererUsesRealSnapshotsClockMotionAndClearsLeasesAcrossRunsAndRebind()
        {
            yield return new EnterPlayMode();
            SetUpPlayScene();
            PrototypeCatalogSO catalog = AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(PrototypeCatalogFactory.CatalogPath);
            Assert.NotNull(catalog);
            directory = Path.Combine(Path.GetTempPath(), "Spotlight_ThirdRound_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            runtime = new ModuleComposition(catalog.BuildCatalogData(), directory);
            Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
            foreach (PrefabEntry entry in catalog.PrefabEntries) prefabs.Add(entry.Key, entry.Prefab);
            pool = new ViewPool(prefabs);
            host = new GameObject("Real world renderer regression");
            RuntimeWorldRenderer2D renderer = host.AddComponent<RuntimeWorldRenderer2D>();
            renderer.enabled = false;
            renderer.Bind(runtime, pool);
            StartBuild();
            Place("tower.basic", OccupantKind.Tower, new CellCoord(2, 1));
            Place("elm.water", OccupantKind.ElementBlock, new CellCoord(2, 2));
            Draw(renderer);
            ElementVisual2D element = null;
            foreach (DictionaryEntry entry in Entries(renderer))
                if (((string)entry.Key).StartsWith("block:")) element = View(renderer, (string)entry.Key).GetComponent<ElementVisual2D>();
            Assert.NotNull(element);
            Vector3 baseScale = element.VisualRoot.localScale;
            Assert.AreEqual(OperationState.Committed, runtime.Flow.TryStartNight(Command()).State);
            ActorSnapshot tower = runtime.Core.World.GetActors(ActorKind.Tower)[0];
            WorldPoint origin = runtime.Core.Board.CellToWorld(new CellCoord(2, 2));
            Assert.AreEqual(OperationState.Committed, runtime.Combat.Projectiles.Spawn(new ProjectileSpawnRequest
            {
                Source = tower.Id, DefinitionId = "projectile.basic", Origin = origin,
                AimPoint = new WorldPoint(origin.X, origin.Y + 5), PhysicalDamage = 1, DamageOrigin = DamageOrigin.Tower
            }).State);
            runtime.Advance(1f / 30);
            ProjectileSnapshot bullet = null;
            foreach (ProjectileSnapshot candidate in runtime.Combat.Projectiles.GetSnapshot())
                if (candidate.Source.Equals(tower.Id) && candidate.Direction.Y > .99f) bullet = candidate;
            Assert.NotNull(bullet, "Real vertical projectile must survive the first simulation tick");
            Assert.AreNotEqual(0, bullet.AppliedElementMask & 1, "Real trajectory crosses placed water block");
            Draw(renderer);
            ProjectileVisual2D projectile = View(renderer, "projectile:" + bullet.ProjectileId).GetComponent<ProjectileVisual2D>();
            Assert.IsTrue(projectile.ElementOverlay.enabled, "Renderer applies real element mask");
            Assert.That(Quaternion.Angle(Quaternion.Euler(0, 0, 90), projectile.VisualRoot.localRotation), Is.LessThan(.001f));
            ActorSnapshot enemy = runtime.Core.World.GetActors(ActorKind.Enemy)[0];
            GameObject enemyView = View(renderer, "actor:" + enemy.Id.Value);
            EnemyFacingVisual2D facing = enemyView.GetComponent<EnemyFacingVisual2D>();
            EnemyHealthBar2D health = enemyView.GetComponent<EnemyHealthBar2D>();
            Assert.IsFalse(facing.BodyRenderer.flipX, "First appearance uses zero direction and lifecycle default");
            float fullWidth = health.Fill.localScale.x;
            Assert.AreEqual(OperationState.Committed, runtime.Core.Transactions.TryCommit(new StateMutationBatch
            {
                Context = Command(), Actors = new[] { new ActorMutation { ActorId = enemy.Id,
                    Kind = ActorMutationKind.SetHealth, CurrentHp = enemy.MaxHp / 2, MaxHp = enemy.MaxHp } }
            }).State);
            Assert.AreEqual(OperationState.Committed, runtime.Core.Motion.TrySetPosition(enemy.Id,
                new WorldPoint(enemy.Position.X + .2f, enemy.Position.Y)).State);
            Draw(renderer);
            Assert.That(health.Fill.localScale.x, Is.EqualTo(fullWidth / 2).Within(.0001f));
            Assert.AreEqual(!facing.DefaultFacingRight, facing.BodyRenderer.flipX, "Facing follows same entity's real position delta");
            Assert.AreEqual(OperationState.Committed, runtime.Core.Motion.TrySetPosition(enemy.Id,
                new WorldPoint(enemy.Position.X - .2f, enemy.Position.Y)).State);
            Draw(renderer);
            Assert.AreEqual(facing.DefaultFacingRight, facing.BodyRenderer.flipX);
            Guid pause = runtime.Clock.AcquirePause(PauseReason.Player);
            Draw(renderer);
            Vector3 pausedScale = element.VisualRoot.localScale;
            double before = runtime.Clock.GetSnapshot().GameTime;
            runtime.Advance(.2f);
            Draw(renderer);
            Assert.AreEqual(before, runtime.Clock.GetSnapshot().GameTime);
            Assert.AreEqual(pausedScale, element.VisualRoot.localScale, "Paused game clock freezes pulse");
            Assert.AreEqual(facing.DefaultFacingRight, facing.BodyRenderer.flipX, "Paused position retains facing");
            runtime.Clock.ReleasePause(pause);
            Assert.AreEqual(OperationState.Committed, runtime.Clock.TrySetSpeed(Command(), GameSpeed.Double).State);
            runtime.Advance((1f / 30) * 3);
            Draw(renderer);
            Assert.That(runtime.Clock.GetSnapshot().GameTime - before, Is.EqualTo(.2).Within(.00001));
            double time = runtime.Clock.GetSnapshot().GameTime;
            float expectedFactor = 1 + element.PulseAmplitude * Mathf.Sin((float)(time / element.PulsePeriodSeconds * Math.PI * 2));
            Assert.That(element.VisualRoot.localScale.x, Is.EqualTo(baseScale.x * expectedFactor).Within(.0001f), "Pulse uses game seconds at 2x");
            runtime.Combat.Projectiles.Clear();
            Draw(renderer);
            Assert.IsFalse(Entries(renderer).Contains("projectile:" + bullet.ProjectileId));
            Assert.IsFalse(projectile.gameObject.activeSelf, "Removed snapshot returns actual lease");
            Assert.AreEqual(OperationState.Committed, runtime.Flow.TryReturnToMenu(Command()).State);
            Draw(renderer);
            Assert.AreEqual(0, Entries(renderer).Count);
            yield return null;
            Assert.AreEqual(0, host.transform.childCount, "Tile destruction completes after a frame");
            StartBuild();
            Draw(renderer);
            Assert.AreEqual(1, Entries(renderer).Count, "New run contains spring only; no old enemy or projectile entries");
            renderer.Bind(runtime, pool);
            Assert.AreEqual(0, Entries(renderer).Count, "Repeated Bind returns all old leases");
            Draw(renderer);
            Assert.AreEqual(1, Entries(renderer).Count);
            Assert.AreEqual(OperationState.Committed, runtime.Flow.TryStartNight(Command()).State);
            runtime.Advance(1f / 30);
            Draw(renderer);
            ActorSnapshot freshEnemy = runtime.Core.World.GetActors(ActorKind.Enemy)[0];
            GameObject freshView = View(renderer, "actor:" + freshEnemy.Id.Value);
            Assert.AreSame(enemyView, freshView, "New run must actually reuse the prior enemy lease");
            Assert.IsFalse(freshView.GetComponent<EnemyFacingVisual2D>().BodyRenderer.flipX,
                "New entity has no previous display position and must not inherit prior left/right movement");
            renderer.Unbind();
            renderer.Unbind();
            Assert.AreEqual(0, Entries(renderer).Count, "Unbind is idempotent");
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (host != null) host.GetComponent<RuntimeWorldRenderer2D>().Unbind();
            if (runtime != null) runtime.Dispose();
            if (pool != null) pool.Clear();
            if (host != null) UnityEngine.Object.Destroy(host);
            runtime = null; pool = null; host = null;
            yield return null;
            foreach (KeyValuePair<GameObject, bool> entry in previousRoots) if (entry.Key != null) entry.Key.SetActive(entry.Value);
            previousRoots.Clear();
            if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            if (directory != null && Directory.Exists(directory))
            {
                string absolute = Path.GetFullPath(directory), temp = Path.GetFullPath(Path.GetTempPath());
                Assert.IsTrue(absolute.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(absolute).StartsWith("Spotlight_ThirdRound_", StringComparison.Ordinal));
                Directory.Delete(absolute, true);
            }
            directory = null;
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
#endif
