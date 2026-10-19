#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Spotlight.Contracts;
using Spotlight.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spotlight.Tests.Integration
{
    public sealed class ProjectileVisualClockTests
    {
        private GameObject template;
        private ViewPool pool;

        private static void SetPreviewLayer(GameObject root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 31;
        }

        [UnityTest]
        public IEnumerator RenderOfficialSpritesAndSixElementTrailsForVisualAcceptance()
        {
            yield return new EnterPlayMode();
            var clones = new List<GameObject>();
            GameObject cameraObject = null;
            RenderTexture target = null;
            Texture2D screenshot = null;
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                cameraObject = new GameObject("PR17 isolated asset preview camera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.orthographicSize = 2.1f;
                camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(20, 24, 32, 255);
                camera.allowHDR = false;
                target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                target.Create();
                camera.targetTexture = target;
                string[] spritePaths = { "Towers/TWR_Basic", "Prototype/view.spring" };
                for (int i = 0; i < spritePaths.Length; i++)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/_Game/Prefabs/" + spritePaths[i] + ".prefab");
                    Assert.NotNull(prefab, spritePaths[i]);
                    GameObject clone = Object.Instantiate(prefab);
                    clones.Add(clone);
                    clone.transform.position = new Vector3(i == 0 ? -1.2f : 1.2f, 1f, 0f);
                    SetPreviewLayer(clone);
                }
                GameObject projectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/_Game/Prefabs/Projectiles/PRJ_Basic.prefab");
                Assert.NotNull(projectilePrefab);
                for (int element = 0; element < 6; element++)
                {
                    GameObject clone = Object.Instantiate(projectilePrefab);
                    clones.Add(clone);
                    SetPreviewLayer(clone);
                    ProjectileVisual2D visual = clone.GetComponent<ProjectileVisual2D>();
                    float x = -3f + element * 1.2f;
                    clone.transform.position = new Vector3(x - .3f, -.9f, 0);
                    visual.SetDirection(new WorldPoint(1, 0));
                    visual.SetElements(1 << element);
                    visual.ApplyVisualTime(0);
                    for (int step = 1; step <= 6; step++)
                    {
                        clone.transform.position = new Vector3(x - .3f + step * .1f, -.9f, 0);
                        visual.ApplyVisualTime(step * .05);
                    }
                    int particleCount = 0;
                    foreach (ParticleSystem particle in visual.elementEffectObjects[element].GetComponentsInChildren<ParticleSystem>(true))
                        particleCount += particle.particleCount;
                    Assert.Greater(particleCount, 0, "Official element slot " + element + " must emit at logical time .3");
                }
                // Let the active render pipeline draw the isolated camera into its target texture.
                yield return null;
                yield return null;
                RenderTexture.active = target;
                screenshot = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                screenshot.Apply();
                Color32[] pixels = screenshot.GetPixels32();
                Color32 background = pixels[0];
                int visiblePixels = 0;
                foreach (Color32 pixel in pixels)
                    if (Mathf.Abs(pixel.r - background.r) + Mathf.Abs(pixel.g - background.g) + Mathf.Abs(pixel.b - background.b) > 30)
                        visiblePixels++;
                Assert.Greater(visiblePixels, 1000, "The GPU preview must contain visible assets");
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath,
                    "../QA/R3B/PR17/Screenshots"));
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "asset-preview.png"), screenshot.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                foreach (GameObject clone in clones) if (clone != null) Object.DestroyImmediate(clone);
                if (screenshot != null) Object.DestroyImmediate(screenshot);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }

        [Test]
        public void TowerAndSpringUseCurrentArtSpritesWithinOneGridCell()
        {
            string[] paths = { "Towers/TWR_Basic", "Prototype/view.spring" };
            const string springSpriteGuid = "6d2468f009d94154d8d54a20e43289ad";
            for (int i = 0; i < paths.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/_Game/Prefabs/" + paths[i] + ".prefab");
                Assert.NotNull(prefab, paths[i]);
                GameObject instance = Object.Instantiate(prefab);
                try
                {
                    SpriteRenderer renderer = instance.GetComponent<SpriteRenderer>();
                    Assert.NotNull(renderer, paths[i]);
                    Assert.NotNull(renderer.sprite, paths[i]);
                    if (i == 0) Assert.AreEqual("Assets/_Game/Art/ThirdParty/防御塔.png", AssetDatabase.GetAssetPath(renderer.sprite), paths[i]);
                    else Assert.AreEqual(springSpriteGuid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(renderer.sprite)), paths[i]);
                    Assert.AreEqual(Color.white, renderer.color, paths[i]);
                    Assert.Greater(renderer.bounds.size.x, 0f, paths[i]);
                    Assert.Greater(renderer.bounds.size.y, 0f, paths[i]);
                    Assert.LessOrEqual(renderer.bounds.size.x, 1f, paths[i]);
                    Assert.LessOrEqual(renderer.bounds.size.y, 1f, paths[i]);
                }
                finally { Object.DestroyImmediate(instance); }
            }
        }

        [Test]
        public void ProjectilePrefabsHaveSixUnlitEffectsBehindTheOverlay()
        {
            foreach (string name in new[] { "PRJ_Basic", "PRJ_Enemy" })
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/_Game/Prefabs/Projectiles/" + name + ".prefab");
                Assert.NotNull(prefab, name);
                ProjectileVisual2D visual = prefab.GetComponent<ProjectileVisual2D>();
                Assert.NotNull(visual, name);
                Assert.AreEqual(6, visual.elementEffectObjects.Length, name);
                foreach (GameObject effect in visual.elementEffectObjects)
                {
                    Assert.NotNull(effect, name);
                    ParticleSystem[] particles = effect.GetComponentsInChildren<ParticleSystem>(true);
                    Assert.IsNotEmpty(particles, name);
                    foreach (ParticleSystem particle in particles)
                    {
                        Assert.IsFalse(particle.main.playOnAwake, name);
                        Assert.IsFalse(particle.main.prewarm, name);
                        ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
                        Assert.NotNull(renderer.sharedMaterial, name);
                        Assert.NotNull(renderer.sharedMaterial.shader, name);
                        Assert.AreEqual("Universal Render Pipeline/Particles/Unlit", renderer.sharedMaterial.shader.name, name);
                        Assert.AreEqual(29, renderer.sortingOrder, name);
                    }
                }
            }
        }

        private ProjectileVisual2D CreateVisual()
        {
            template = new GameObject("Projectile clock test");
            template.SetActive(false);
            ProjectileVisual2D visual = template.AddComponent<ProjectileVisual2D>();
            visual.ElementOverlay = template.AddComponent<SpriteRenderer>();
            visual.ElementOverlay.color = Color.gray;
            visual.elementEffectObjects = new GameObject[6];
            for (int i = 0; i < 6; i++)
            {
                GameObject effect = new GameObject("Element " + i);
                effect.transform.SetParent(template.transform, false);
                ParticleSystem particles = effect.AddComponent<ParticleSystem>();
                var main = particles.main;
                main.playOnAwake = true;
                main.prewarm = true;
                main.loop = true;
                main.startLifetime = 10f;
                main.startSpeed = 0f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = particles.emission;
                emission.rateOverTime = 40f;
                visual.elementEffectObjects[i] = effect;
            }
            return visual;
        }

        [Test]
        public void OverlayRemainsAvailableWithoutParticleSlots()
        {
            ProjectileVisual2D visual = CreateVisual();
            visual.elementEffectObjects = null;
            template.SetActive(true);
            visual.SetElements(3);
            Assert.IsTrue(visual.ElementOverlay.enabled);
            Assert.AreEqual((Color)new Color32(64, 156, 255, 255), visual.ElementOverlay.color);
            visual.SetElements(64);
            Assert.IsFalse(visual.ElementOverlay.enabled);
            visual.SetElements(32);
            Assert.AreEqual((Color)new Color32(177, 100, 255, 255), visual.ElementOverlay.color);
            visual.ResetVisual();
            Assert.AreEqual(Color.gray, visual.ElementOverlay.color);
        }

        [UnityTest]
        public IEnumerator ParticlesFollowGameClockAndClearOnSwitchAndPoolReturn()
        {
            yield return new EnterPlayMode();
            CreateVisual();
            pool = new ViewPool(delegate(string key) { return Object.Instantiate(template); });
            PoolLease lease = pool.Rent("projectile");
            GameObject instance = (GameObject)lease.View;
            ProjectileVisual2D visual = instance.GetComponent<ProjectileVisual2D>();
            ParticleSystem water = visual.elementEffectObjects[0].GetComponent<ParticleSystem>();
            ParticleSystem fire = visual.elementEffectObjects[1].GetComponent<ParticleSystem>();
            visual.SetElements(3);
            visual.ApplyVisualTime(100);
            Assert.AreEqual(0, water.particleCount, "Selection must not prewarm");
            Assert.IsFalse(visual.elementEffectObjects[1].activeSelf);
            visual.ApplyVisualTime(100.25);
            Assert.Greater(water.particleCount, 0, "Logical advancement must emit particles");
            float elapsed = water.time;
            int count = water.particleCount;
            for (int frame = 0; frame < 4; frame++)
            {
                yield return null;
                visual.SetElements(3);
                visual.ApplyVisualTime(100.25);
            }
            Assert.AreEqual(count, water.particleCount, "Paused game clock freezes emission across real frames");
            Assert.That(water.time, Is.EqualTo(elapsed).Within(.0001f));
            visual.ApplyVisualTime(double.NaN);
            visual.ApplyVisualTime(double.PositiveInfinity);
            visual.ApplyVisualTime(-1);
            visual.ApplyVisualTime(99);
            Assert.That(water.time, Is.EqualTo(elapsed).Within(.0001f));
            visual.ApplyVisualTime(100.75);
            Assert.That(water.time - elapsed, Is.EqualTo(.5f).Within(.001f), "Double logical delta advances twice as far");
            visual.SetElements(2);
            Assert.AreEqual(0, water.particleCount);
            Assert.IsFalse(water.isEmitting);
            Assert.AreEqual(0, fire.particleCount);
            visual.ApplyVisualTime(100.75);
            visual.ApplyVisualTime(101);
            Assert.Greater(fire.particleCount, 0);
            visual.SetElements(64);
            foreach (GameObject effect in visual.elementEffectObjects) Assert.IsFalse(effect.activeSelf);
            Assert.AreEqual(0, fire.particleCount);
            visual.SetElements(1);
            visual.ApplyVisualTime(101);
            visual.ApplyVisualTime(101.25);
            Assert.AreEqual(OperationState.Committed, pool.Return(lease.LeaseId).State);
            Assert.AreEqual(0, water.particleCount);
            Assert.IsFalse(water.isEmitting);
            lease = pool.Rent("projectile");
            Assert.AreSame(instance, lease.View);
            visual.SetElements(1);
            visual.ApplyVisualTime(200);
            Assert.AreEqual(0, water.particleCount, "Reused visual starts from the new clock baseline");
            visual.ApplyVisualTime(200.25);
            Assert.That(water.time, Is.EqualTo(.25f).Within(.001f));
            Assert.Greater(water.particleCount, 0);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (pool != null) { pool.Clear(); pool = null; }
            if (template != null) Object.DestroyImmediate(template);
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
#endif
