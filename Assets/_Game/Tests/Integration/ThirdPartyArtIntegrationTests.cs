#if UNITY_EDITOR
using System;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spotlight.Bootstrap;
using Spotlight.Contracts;
using Spotlight.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


namespace Spotlight.Tests.Integration
{
    public sealed class ThirdPartyArtIntegrationTests
    {
        const string Art="Assets/_Game/Art/ThirdParty/";
        const string Output="QA/R3B/ThirdPartyArt";
        static readonly string[] Names={"GoblinMelee","GoblinRanged","SlimeFire","SlimeWater","SlimeThunder","Sandworm","Gargoyle","Falcon","Wraith","SoulKing"};
        static GameObject Enemy(string name) { return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Enemies/ENM_"+name+".prefab"); }
        static void Source(Sprite sprite)
        {
            Assert.NotNull(sprite);Assert.That(AssetDatabase.GetAssetPath(sprite),Does.StartWith(Art));
            Assert.Greater(sprite.rect.width,20);Assert.Greater(sprite.rect.height,20);
            Assert.Less(sprite.rect.height,sprite.texture.height*.9f,"Transparent canvas was imported whole");
        }
        [Test] public void TenFormalEnemiesUseCroppedArtAndNoActivePlaceholder()
        {
            foreach(string name in Names)
            {
                GameObject prefab=Enemy(name);Assert.NotNull(prefab,name);
                var animation=prefab.GetComponent<EnemySpriteAnimator2D>();Assert.NotNull(animation,name);
                var facing=prefab.GetComponent<EnemyFacingVisual2D>();Assert.NotNull(facing,name);
                Assert.AreSame(animation.BodyRenderer,facing.BodyRenderer,name);Assert.IsFalse(facing.DefaultFacingRight,name);
                Assert.IsTrue(animation.BodyRenderer.enabled,name);Source(animation.BodyRenderer.sprite);
                Assert.Greater(animation.IdleFrames.Length,0,name);
                foreach(var group in new[]{animation.IdleFrames,animation.MoveFrames,animation.AttackFrames,animation.DeathFrames})
                    foreach(var frame in group)Source(frame);
                foreach(var old in prefab.GetComponentsInChildren<SpriteRenderer>(true))
                    if(old.name.StartsWith("Body_Placeholder",StringComparison.Ordinal))Assert.IsFalse(old.enabled,name);
                var body=animation.BodyRenderer;float worldHeight=body.sprite.bounds.size.y*Mathf.Abs(body.transform.lossyScale.y);
                Assert.That(worldHeight,Is.InRange(.35f,1.5f),name);
                Assert.That(body.sharedMaterial.shader.name,Does.Contain("Unlit").Or.EqualTo("Sprites/Default"),name);
                var bar=prefab.GetComponent<EnemyHealthBar2D>();Assert.NotNull(bar,name);Assert.IsFalse(bar.BarRoot.IsChildOf(body.transform),name);
            }
            var projectilePrefabs=new Dictionary<string,GameObject>();
            foreach(string key in new[]{"PRJ_Basic","PRJ_Enemy"})
                projectilePrefabs.Add(key,AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Projectiles/"+key+".prefab"));
            var projectilePool=new ViewPool(projectilePrefabs);
            try
            {
                foreach(string key in projectilePrefabs.Keys)
                {
                    var lease=projectilePool.Rent(key);var instance=(GameObject)lease.View;
                    var visual=instance.GetComponent<ProjectileVisual2D>();Assert.NotNull(visual,key);Assert.NotNull(visual.VisualRoot,key);
                    var body=visual.VisualRoot.GetComponentsInChildren<SpriteRenderer>(true).First(r=>r.name=="Direction" && r!=visual.ElementOverlay);
                    Assert.IsFalse(instance.GetComponent<SpriteRenderer>().enabled,key);
                    Assert.IsTrue(body.enabled,key);Assert.AreEqual(Art+"子弹.png",AssetDatabase.GetAssetPath(body.sprite),key);
                    Assert.IsTrue(body.transform.IsChildOf(visual.VisualRoot),key);
                    Vector3 position=instance.transform.position,rootScale=instance.transform.localScale;
                    Quaternion rotation=instance.transform.rotation;
                    visual.SetDirection(new WorldPoint(1,0));Vector3 horizontalBounds=body.bounds.size;
                    float expectedWidth=key=="PRJ_Basic"?.45f:.4f;
                    Assert.That(horizontalBounds.x,Is.EqualTo(expectedWidth).Within(.001f),key);
                    visual.SetDirection(new WorldPoint(0,1));
                    Assert.That(Vector3.Dot(body.transform.right,Vector3.up),Is.GreaterThan(.999f),key+" new art must follow the existing direction pivot");
                    Assert.That(body.bounds.size.y,Is.EqualTo(horizontalBounds.x).Within(.001f),key);
                    Assert.That(body.bounds.size.x,Is.EqualTo(horizontalBounds.y).Within(.001f),key);
                    Assert.AreEqual(position,instance.transform.position,key);Assert.AreEqual(rotation,instance.transform.rotation,key);Assert.AreEqual(rootScale,instance.transform.localScale,key);
                    Assert.AreEqual(OperationState.Committed,projectilePool.Return(lease.LeaseId).State);
                }
            }finally{projectilePool.Clear();}
            var catalog=AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(PrototypeCatalogFactory.CatalogPath);
            Assert.AreSame(Enemy("Wraith"),catalog.PrefabEntries.First(e=>e.Key=="EnemyWraith").Prefab);
            Assert.AreNotSame(Enemy("SoulKing"),Enemy("Wraith"));
        }
        [Test] public void FormalUiThemeHasFivePortraitsTwoBackgroundsAndRealButtonStates()
        {
            var ui=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/UI/UI_Game.prefab").GetComponent<GameUiReferences>();
            var theme=ui.ArtTheme;Assert.NotNull(theme);
            foreach(var portrait in new[]{theme.PortraitNeutral,theme.PortraitCalm,theme.PortraitSmile,theme.PortraitAngry,theme.PortraitCrying}){Assert.NotNull(portrait);Assert.That(AssetDatabase.GetAssetPath(portrait),Does.StartWith(Art));}
            Assert.NotNull(theme.DayBackground);Assert.NotNull(theme.NightBackground);Assert.AreNotSame(theme.DayBackground,theme.NightBackground);
            for(int i=0;i<4;i++){Assert.NotNull(theme.ButtonNormal[i]);Assert.NotNull(theme.ButtonPressed[i]);Assert.AreNotSame(theme.ButtonNormal[i],theme.ButtonPressed[i]);}
            var host=new GameObject("Theme button contract",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(Button));
            try
            {
                var label=new GameObject("Label",typeof(RectTransform),typeof(CanvasRenderer),typeof(Text)).GetComponent<Text>();
                label.transform.SetParent(host.transform,false);
                var button=host.GetComponent<Button>();
                theme.ApplyButton(button,3);Assert.Less(label.color.r,.3f,"Light button skin needs dark text");
                theme.ApplyButton(button,0);Assert.Greater(label.color.r,.8f,"Dark button skin needs light text");
                theme.ApplyButton(button,2);
                Assert.AreEqual(Selectable.Transition.SpriteSwap,button.transition);
                Assert.AreSame(theme.ButtonNormal[2],host.GetComponent<Image>().sprite);Assert.AreSame(theme.ButtonPressed[2],button.spriteState.pressedSprite);
                theme.ApplyState(button,true);Color selected=button.targetGraphic.color;Assert.AreNotEqual(Color.white,selected);
                button.interactable=false;theme.ApplyState(button);Assert.Less(button.targetGraphic.color.a,selected.a);Assert.Less(button.targetGraphic.color.r,selected.r);
            }finally{UnityEngine.Object.DestroyImmediate(host);}
        }
        [Test] public void EditModePreviewResetAndPoolReuseFollowActualClock()
        {

            using(var fixture=new Fixture(false))
            {
                var lease=fixture.Pool.Rent("EnemyGoblinMelee");var instance=(GameObject)lease.View;var animation=instance.GetComponent<EnemySpriteAnimator2D>();
                animation.ResetVisual();Vector3 original=animation.BodyRenderer.transform.localScale;
                animation.ApplyVisualTime(0,true);animation.ApplyVisualTime(.21,true);Sprite frame=animation.BodyRenderer.sprite;Vector3 scale=animation.BodyRenderer.transform.localScale;
                for(int i=0;i<100;i++)animation.ApplyVisualTime(.21,true);
                Assert.AreSame(frame,animation.BodyRenderer.sprite);Assert.AreEqual(scale,animation.BodyRenderer.transform.localScale);
                animation.PlayAttack(.21);animation.PlayDeath(.22);Assert.IsTrue(animation.IsDeathPlaying);
                // EditMode does not dispatch the PlayMode OnDisable lifecycle. Exercise the explicit preview reset before return.
                animation.ResetVisual();
                Assert.AreEqual(OperationState.Committed,fixture.Pool.Return(lease.LeaseId).State);
                lease=fixture.Pool.Rent("EnemyGoblinMelee");Assert.AreSame(instance,lease.View);Assert.IsFalse(animation.IsDeathPlaying);
                Assert.AreSame(animation.IdleFrames[0],animation.BodyRenderer.sprite);Assert.AreEqual(original,animation.BodyRenderer.transform.localScale);
                fixture.StartBuild();fixture.DismissTutorial();fixture.StartNight();
                double before=fixture.Runtime.Clock.GetSnapshot().GameTime;
                animation.ApplyVisualTime(before,true);Guid pause=fixture.Runtime.Clock.AcquirePause(PauseReason.Player);
                fixture.Runtime.Advance(.3f);Assert.AreEqual(before,fixture.Runtime.Clock.GetSnapshot().GameTime);
                animation.ApplyVisualTime(before,true);frame=animation.BodyRenderer.sprite;scale=animation.BodyRenderer.transform.localScale;
                fixture.Runtime.Advance(.3f);animation.ApplyVisualTime(fixture.Runtime.Clock.GetSnapshot().GameTime,true);
                Assert.AreSame(frame,animation.BodyRenderer.sprite);Assert.AreEqual(scale,animation.BodyRenderer.transform.localScale);
                fixture.Runtime.Clock.ReleasePause(pause);Assert.AreEqual(OperationState.Committed,fixture.Runtime.Clock.TrySetSpeed(fixture.Command(),GameSpeed.Double).State);
                fixture.Runtime.Advance(.1f);double after=fixture.Runtime.Clock.GetSnapshot().GameTime;
                double fixedStep=1d/fixture.Runtime.Catalog.GetSettings().LogicTicksPerGameSecond;
                Assert.That(after-before,Is.EqualTo(.2).Within(fixedStep+.00001),"Double speed advances two tenths, quantized to the configured logical tick");animation.ApplyVisualTime(after,true);
                animation.PlayAttack(after);animation.ResetVisual();fixture.Pool.Return(lease.LeaseId);lease=fixture.Pool.Rent("EnemyGoblinMelee");
                animation.ApplyVisualTime(after+.01,false);Assert.AreSame(animation.IdleFrames[0],animation.BodyRenderer.sprite,"Attack survived pool return");
                fixture.Pool.Return(lease.LeaseId);
            }

        }
        [Test] public void ActualRendererSwitchesDayNightAndCleansBackgroundOnRebind()
        {
            using(var fixture=new Fixture(false))
            {
                fixture.Draw();Assert.AreSame(fixture.Theme.DayBackground,fixture.Background.sprite);
                fixture.StartBuild();fixture.DismissTutorial();fixture.StartNight();fixture.Draw();
                Assert.AreSame(fixture.Theme.NightBackground,fixture.Background.sprite);
                fixture.Renderer.Unbind();Assert.IsNull(fixture.Host.transform.Find("ThirdPartyWorldBackground"));
                fixture.Renderer.Bind(fixture.Runtime,fixture.Pool);fixture.Draw();
                Assert.AreSame(fixture.Theme.NightBackground,fixture.Background.sprite);
                Assert.AreEqual(1,fixture.Host.GetComponentsInChildren<SpriteRenderer>(true).Count(s=>s.name=="ThirdPartyWorldBackground"));
            }
        }
        public static void RenderBatch()
        {
            Directory.CreateDirectory(Output);
            using(var fixture=new Fixture(true))
            {
                fixture.Capture("01-menu");
                Assert.AreEqual(OperationState.Committed,fixture.Runtime.Flow.TryNewRun(fixture.Command(),new NewRunRequest{LevelId="level.prototype",Mode=GameMode.Story,Seed=123}).State);
                fixture.Pump();fixture.Capture("02-day-event");
                fixture.ChooseEvent();fixture.Capture("03-tutorial");
                fixture.DismissTutorial();
                foreach(int x in new[]{1,3,5})
                    Assert.AreEqual(OperationState.Committed,fixture.Runtime.Core.Deployment.TryPlace(fixture.Command(),new PlacementRequest{DefinitionId="tower.basic",Kind=OccupantKind.Tower,Cell=new CellCoord(x,1)}).State);
                Assert.AreEqual(OperationState.Committed,fixture.Runtime.Core.Deployment.TryPlace(fixture.Command(),new PlacementRequest{DefinitionId="elm.water",Kind=OccupantKind.ElementBlock,Cell=new CellCoord(1,2)}).State);
                fixture.Capture("04-day-deployment");
                fixture.StartNight();
                for(int i=0;i<120 && fixture.Runtime.Core.World.GetActors(ActorKind.Enemy).Count==0;i++)fixture.Runtime.Advance(1f/30);
                Assert.Greater(fixture.Runtime.Core.World.GetActors(ActorKind.Enemy).Count,0,"Night capture requires real spawned enemies");
                // Give the actual tower/projectile service up to eight seconds to expose a live shot.
                for(int i=0;i<240 && fixture.Runtime.Combat.Projectiles.GetSnapshot().Count==0 && fixture.Runtime.Flow.GetSnapshot().Phase==GamePhase.Night;i++)fixture.Runtime.Advance(1f/30);
                Assert.Greater(fixture.Runtime.Core.World.GetActors(ActorKind.Tower).Count,0);
                Assert.Greater(fixture.Runtime.Core.World.GetActors(ActorKind.Enemy).Count,0);
                fixture.Capture("05-night-enemies");
            }
            using(var fixture=new Fixture(false))
            {
                fixture.Camera.transform.position=new Vector3(0,0,-10);fixture.Camera.orthographicSize=2.8f;
                for(int i=0;i<Names.Length;i++)
                {
                    var instance=UnityEngine.Object.Instantiate(Enemy(Names[i]));instance.transform.position=new Vector3((i%5-2)*2.0f,i<5?.85f:-.95f,0);
                    var facing=instance.GetComponent<EnemyFacingVisual2D>();facing.SetDirection(new WorldPoint(i%2==0?-1:1,0));
                    instance.GetComponent<EnemySpriteAnimator2D>().ApplyVisualTime(0,false);
                    instance.GetComponent<EnemyHealthBar2D>().ApplyHealth(35+i*5,100);
                }
                fixture.Capture("06-ten-enemy-gallery",false);
            }
            File.WriteAllText(Path.Combine(Output,"render-source.txt"),"1280x720; formal UI_Game prefab and ThirdPartyUiTheme; ModuleComposition with unique temporary save directory; real Flow/DayEvents/Wave services; one camera for world and ScreenSpaceCamera canvas. Gallery order: "+string.Join(", ",Names));
            Debug.Log("ThirdPartyArt real UI and actor gallery capture completed: "+Path.GetFullPath(Output));
        }
        sealed class Fixture:IDisposable
        {
            public readonly ModuleComposition Runtime;public readonly ViewPool Pool;public readonly GameObject Host;public readonly RuntimeWorldRenderer2D Renderer;public readonly Camera Camera;public readonly ThirdPartyUiThemeSO Theme;
            readonly GameView view;readonly string temp;readonly Scene scene,previous;
            public CommandContext Command(){return Runtime.Core.Commands.Create(Runtime.Core.Transactions.Revision);}
            public SpriteRenderer Background {get{return Host.transform.Find("ThirdPartyWorldBackground").GetComponent<SpriteRenderer>();}}
            public Fixture(bool ui)
            {
                // Batch Unity starts with a transient untitled scene. Give additive previews a saved anchor.
                // Interactive unsaved scenes belong to the user and must never be closed or saved here.
                if(!Application.isPlaying)
                {
                    var untitled=new List<Scene>();
                    for(int i=0;i<SceneManager.sceneCount;i++)
                    {
                        var loaded=SceneManager.GetSceneAt(i);
                        if(loaded.isLoaded && string.IsNullOrEmpty(loaded.path))untitled.Add(loaded);
                    }
                    if(untitled.Count>0)
                    {
                        if(!Application.isBatchMode)throw new InvalidOperationException("ThirdPartyArt preview requires a saved scene as additive anchor. Save the current untitled scene or run RenderBatch in batch mode; no user scene was changed.");
                        const string anchorPath="Assets/_Game/Scenes/00_Bootstrap.unity";
                        Scene anchor=SceneManager.GetSceneByPath(anchorPath);
                        if(!anchor.IsValid() || !anchor.isLoaded)anchor=EditorSceneManager.OpenScene(anchorPath,OpenSceneMode.Additive);
                        SceneManager.SetActiveScene(anchor);
                        foreach(var transient in untitled)EditorSceneManager.CloseScene(transient,true);
                    }
                }
                previous=SceneManager.GetActiveScene();scene=Application.isPlaying?SceneManager.CreateScene("ThirdPartyArt isolated tests"):EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
                temp=Path.Combine(Path.GetTempPath(),"Spotlight_ThirdPartyArt_"+Guid.NewGuid().ToString("N"));
            var catalog=AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(PrototypeCatalogFactory.CatalogPath);Runtime=new ModuleComposition(catalog.BuildCatalogData(),temp);
                var prefabs=new Dictionary<string,GameObject>();foreach(var entry in catalog.PrefabEntries)prefabs.Add(entry.Key,entry.Prefab);Pool=new ViewPool(prefabs);
                Host=new GameObject("ThirdPartyArt isolated preview");var cameraObject=new GameObject("ThirdPartyArt shared world and UI camera");Camera=cameraObject.AddComponent<Camera>();Camera.enabled=false;
                Camera.orthographic=true;Camera.orthographicSize=5.4f;Camera.aspect=1280f/720;Camera.transform.position=new Vector3(0,0,-10);Camera.clearFlags=CameraClearFlags.SolidColor;Camera.backgroundColor=new Color(.035f,.05f,.075f);Camera.cullingMask=1<<30;
                var formal=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/UI/UI_Game.prefab").GetComponent<GameUiReferences>();Theme=formal.ArtTheme;Assert.NotNull(Theme);
                Renderer=Host.AddComponent<RuntimeWorldRenderer2D>();Renderer.enabled=false;Renderer.ArtTheme=Theme;Renderer.WorldCamera=Camera;Renderer.Bind(Runtime,Pool);
                if(ui){view=Host.AddComponent<GameView>();view.UiPrefab=formal;view.Bind(Runtime.ViewContext);}
            }
            public void Draw(){typeof(RuntimeWorldRenderer2D).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Renderer,null);}
            public void Pump(){Runtime.Advance(0);if(view!=null)view.RefreshAll();Draw();}
            public void StartBuild(){Assert.AreEqual(OperationState.Committed,Runtime.Flow.TryNewRun(Command(),new NewRunRequest{LevelId="level.prototype",Mode=GameMode.Story,Seed=123}).State);Pump();ChooseEvent();}
            public void ChooseEvent(){var day=Runtime.Gameplay.DayEvents.GetSnapshot();Assert.AreEqual(OperationState.Committed,Runtime.Gameplay.DayEvents.TryChoose(Command(),day.State.InstanceId,day.Options[0].Id).State);Pump();}
            public void DismissTutorial(){if(!Runtime.Flow.GetSnapshot().TutorialShown)Assert.AreEqual(OperationState.Committed,Runtime.Flow.TryDismissTutorial(Command()).State);Pump();}
            public void StartNight(){Assert.AreEqual(OperationState.Committed,Runtime.Flow.TryStartNight(Command()).State);Pump();}
            public void Capture(string name,bool draw=true)
            {
                if(draw)Pump();
                foreach(GameObject root in scene.GetRootGameObjects())foreach(Transform child in root.GetComponentsInChildren<Transform>(true))child.gameObject.layer=30;
                foreach(Canvas canvas in Host.GetComponentsInChildren<Canvas>(true)){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=Camera;canvas.planeDistance=1;}
                var target=new RenderTexture(1280,720,24);var capture=new Texture2D(1280,720,TextureFormat.RGB24,false);var active=RenderTexture.active;
                try{Camera.targetTexture=target;Canvas.ForceUpdateCanvases();Camera.Render();RenderTexture.active=target;capture.ReadPixels(new Rect(0,0,1280,720),0,0);capture.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),capture.EncodeToPNG());}
                finally{RenderTexture.active=active;Camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(capture);}
            }
            public void Dispose()
            {
                if(view!=null)view.Unbind();if(Renderer!=null)Renderer.Unbind();Pool.Clear();Runtime.Dispose();SceneManager.SetActiveScene(previous);if(Application.isPlaying)SceneManager.UnloadSceneAsync(scene);else EditorSceneManager.CloseScene(scene,true);
                string resolved=Path.GetFullPath(temp),prefix=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(Directory.Exists(resolved)&&resolved.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(resolved).StartsWith("Spotlight_ThirdPartyArt_",StringComparison.Ordinal))Directory.Delete(resolved,true);
            }
        }
    }
}
#endif
