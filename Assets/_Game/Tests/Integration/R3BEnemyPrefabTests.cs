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
    public sealed class R3BEnemyPrefabTests
    {
        [UnityTest]
        public IEnumerator SevenEnemiesRenderRealHealthAndResetOnReuse()
        {
            yield return new EnterPlayMode();
            Scene original = SceneManager.GetActiveScene();
            var roots = new List<KeyValuePair<GameObject,bool>>();
            foreach (GameObject root in original.GetRootGameObjects())
            {
                roots.Add(new KeyValuePair<GameObject,bool>(root, root.activeSelf));
                root.SetActive(false);
            }
            Scene scene = SceneManager.CreateScene("N02 isolated acceptance");
            SceneManager.SetActiveScene(scene);
            string[] names = { "SlimeWater", "SlimeFire", "SlimeThunder", "Sandworm", "Falcon", "Gargoyle", "SoulKing" };
            var prefabs = new Dictionary<string,GameObject>();
            ViewPool pool = null;
            GameObject host = null;
            try
            {
                foreach(string name in names)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Enemies/ENM_" + name + ".prefab");
                    Assert.NotNull(prefab, name);
                    Assert.AreEqual(Vector3.zero, prefab.transform.localPosition, name);
                    Assert.AreEqual(Vector3.one, prefab.transform.localScale, name);
                    foreach(Transform node in prefab.GetComponentsInChildren<Transform>(true))
                        foreach(Component component in node.GetComponents<Component>()) Assert.NotNull(component, name + " Missing script");
                    foreach(SpriteRenderer sprite in prefab.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        Assert.NotNull(sprite.sprite, name + " Missing sprite");
                        Assert.NotNull(sprite.sharedMaterial, name + " Missing material");
                        Assert.IsFalse(sprite.sharedMaterial.shader.name.Contains("Error"), name);
                    }
                    EnemyFacingVisual2D f = prefab.GetComponent<EnemyFacingVisual2D>();
                    EnemyHealthBar2D h = prefab.GetComponent<EnemyHealthBar2D>();
                    Assert.NotNull(f, name); Assert.NotNull(h, name);
                    Assert.AreEqual(prefab.transform, f.BodyRenderer.transform.parent, name);
                    Assert.AreEqual(prefab.transform, h.BarRoot.parent, name);
                    Assert.IsFalse(h.BarRoot.IsChildOf(f.BodyRenderer.transform), name);
                    prefabs.Add(name,prefab); // Test-only keys. Production mapping belongs to the lead programmer.
                }
                pool = new ViewPool(prefabs);
                foreach(string name in names)
                {
                    PoolLease lease = pool.Rent(name);
                    GameObject instance = (GameObject)lease.View;
                    EnemyFacingVisual2D facing = instance.GetComponent<EnemyFacingVisual2D>();
                    EnemyHealthBar2D health = instance.GetComponent<EnemyHealthBar2D>();
                    Color bodyColor = facing.BodyRenderer.color;
                    bool baseFlip = facing.BodyRenderer.flipX;
                    Vector3 fillScale = health.Fill.localScale, fillPosition = health.Fill.localPosition;
                    float leftEdge = fillPosition.x - fillScale.x * .5f;
                    var barColors = new List<Color>();
                    SpriteRenderer[] barSprites = health.BarRoot.GetComponentsInChildren<SpriteRenderer>();
                    foreach(SpriteRenderer sprite in barSprites) barColors.Add(sprite.color);
                    for(int cycle = 0; cycle < 3; cycle++)
                    {
                        health.ApplyHealth(25,100);
                        Assert.That(health.Fill.localScale.x, Is.EqualTo(fillScale.x * .25f).Within(.0001f), name);
                        Assert.That(health.Fill.localPosition.x - health.Fill.localScale.x*.5f, Is.EqualTo(leftEdge).Within(.0001f), name);
                        facing.SetDirection(new WorldPoint(-1,0));
                        Assert.IsTrue(facing.BodyRenderer.flipX, name);
                        facing.BodyRenderer.color = Color.red; // Same renderer a body-only damage flash must target.
                        for(int i=0;i<barSprites.Length;i++)
                        {
                            Assert.IsFalse(barSprites[i].flipX, name);
                            Assert.AreEqual(barColors[i],barSprites[i].color, name);
                        }
                        health.ApplyHealth(0,0);
                        Assert.IsFalse(health.BarRoot.gameObject.activeSelf, name);
                        Assert.AreEqual(OperationState.Committed,pool.Return(lease.LeaseId).State,name);
                        lease = pool.Rent(name);
                        Assert.AreSame(instance,lease.View,name);
                        Assert.AreEqual(fillScale,health.Fill.localScale,name);
                        Assert.AreEqual(fillPosition,health.Fill.localPosition,name);
                        Assert.IsTrue(health.BarRoot.gameObject.activeSelf,name);
                        Assert.AreEqual(bodyColor,facing.BodyRenderer.color,name);
                        Assert.AreEqual(baseFlip,facing.BodyRenderer.flipX,name);
                    }
                    pool.Return(lease.LeaseId);
                }

                // Exercise the actual runtime ActorSnapshot -> health widget path, all seven at once.
                host = new GameObject("N02 runtime renderer");
                RuntimeWorldRenderer2D renderer = host.AddComponent<RuntimeWorldRenderer2D>();
                renderer.Bind(null,pool);
                MethodInfo show = typeof(RuntimeWorldRenderer2D).GetMethod("Show",BindingFlags.Instance|BindingFlags.NonPublic);
                Assert.NotNull(show);
                for(int i=0;i<names.Length;i++)
                {
                    WorldPoint position = new WorldPoint((i-3)*1.65f,0);
                    ActorSnapshot actor = new ActorSnapshot { Kind = ActorKind.Enemy, CurrentHp = 20+i*10, MaxHp = 100 };
                    show.Invoke(renderer,new object[]{"n02:"+i,names[i],position,20,actor,null,0d});
                }
                EnemyHealthBar2D[] bars = UnityEngine.Object.FindObjectsOfType<EnemyHealthBar2D>();
                Assert.AreEqual(7,bars.Length,"Seven active enemies on screen");
                foreach(EnemyHealthBar2D health in bars)
                {
                    int i = Array.FindIndex(names,n=>health.name.StartsWith("ENM_"+n));
                    Assert.That(i,Is.GreaterThanOrEqualTo(0));
                    Assert.That(health.Fill.localScale.x, Is.EqualTo(.82f*(20+i*10)/100f).Within(.0001f),names[i]);
                    EnemyFacingVisual2D f=health.GetComponent<EnemyFacingVisual2D>();
                    Assert.That(health.BarRoot.position.y-.065f,Is.GreaterThan(f.BodyRenderer.bounds.max.y),names[i]+" bar clears body");
                    Assert.That((f.BodyRenderer.bounds.center-health.transform.position).magnitude,Is.LessThan(.07f),names[i]+" visible center stays near anchor");
                    // Update with a second snapshot to detect a stale or decorative health bar.
                    ActorSnapshot actor=new ActorSnapshot {Kind=ActorKind.Enemy,CurrentHp=10,MaxHp=100};
                    show.Invoke(renderer,new object[]{"n02:"+i,names[i],new WorldPoint((i-3)*1.65f,0),20,actor,null,1d});
                    Assert.That(health.Fill.localScale.x,Is.EqualTo(.082f).Within(.0001f));
                    show.Invoke(renderer,new object[]{"n02:"+i,names[i],new WorldPoint((i-3)*1.65f,0),20,new ActorSnapshot {Kind=ActorKind.Enemy,CurrentHp=20+i*10,MaxHp=100},null,2d});
                }
                GameObject cameraObject = new GameObject("N02 preview camera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic=true; camera.orthographicSize=1.8f;
                camera.transform.position=new Vector3(0,.15f,-10);
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.045f,.06f,.095f);
                RenderTexture target=new RenderTexture(1960,560,24);
                Texture2D capture=new Texture2D(1960,560,TextureFormat.RGB24,false);
                RenderTexture previous=RenderTexture.active;
                try
                {
                    camera.targetTexture=target; camera.Render(); RenderTexture.active=target;
                    capture.ReadPixels(new Rect(0,0,1960,560),0,0); capture.Apply();
                    Directory.CreateDirectory("QA/R3B");
                    File.WriteAllBytes("QA/R3B/N02-seven-enemies.png",capture.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active=previous; camera.targetTexture=null;
                    target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(capture);
                    UnityEngine.Object.DestroyImmediate(cameraObject);
                }
                renderer.Unbind();
            }
            finally
            {
                if(host!=null) UnityEngine.Object.DestroyImmediate(host);
                if(pool!=null) pool.Clear();
                SceneManager.SetActiveScene(original);
                foreach(var pair in roots) if(pair.Key!=null) pair.Key.SetActive(pair.Value);
                SceneManager.UnloadSceneAsync(scene);
            }
            yield return new ExitPlayMode();
        }
    }
}
#endif
