#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Spotlight.Bootstrap;
using Spotlight.Contracts;
using Spotlight.Presentation;

namespace Spotlight.Tests.Integration
{
    public sealed class PR16UiIntegrationTests
    {
        private ModuleComposition runtime;
        private GameObject host;
        private GameView view;
        private string directory;
        private GameUiReferences temporary;
        private GameUiReferences Ui {get{return host.GetComponentInChildren<GameUiReferences>(true);}}
        private CommandContext Command(){return runtime.Core.Commands.Create(runtime.Core.Transactions.Revision);}
        private void Pump(){runtime.Advance(0);view.RefreshAll();}
        [SetUp] public void Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"Spotlight_PR16Ui_"+Guid.NewGuid().ToString("N"));
            runtime=new ModuleComposition(R3BTestCatalog.Build(),directory);
            host=new GameObject("PR16 real UI");view=host.AddComponent<GameView>();
            view.UiPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/UI/UI_Game.prefab").GetComponent<GameUiReferences>();view.Bind(runtime.ViewContext);
        }
        [TearDown] public void Cleanup()
        {
            if(view!=null)view.Unbind();if(host!=null)UnityEngine.Object.DestroyImmediate(host);
            if(temporary!=null)UnityEngine.Object.DestroyImmediate(temporary.gameObject);if(runtime!=null)runtime.Dispose();
            if(Directory.Exists(directory)){Assert.That(Path.GetFullPath(directory),Does.StartWith(Path.GetFullPath(Path.GetTempPath())));Assert.That(Path.GetFileName(directory),Does.StartWith("Spotlight_PR16Ui_"));Directory.Delete(directory,true);}
        }
        // Outcome branch fixtures use a production transaction and EvaluateNight; they do not claim normal-balance seven/fifty-night playthroughs.
        private void Outcome(GameMode mode,GamePhase target)
        {
            Assert.AreEqual(OperationState.Committed,runtime.Flow.TryNewRun(Command(),new NewRunRequest{LevelId="level.prototype",Mode=mode,Seed=17}).State);Pump();
            DayEventSnapshot day=runtime.Gameplay.DayEvents.GetSnapshot();
            Assert.AreEqual(OperationState.Committed,runtime.Gameplay.DayEvents.TryChoose(Command(),day.State.InstanceId,day.Options.First(o=>o.Cost==null||o.Cost.Count==0).Id).State);Pump();
            Assert.AreEqual(OperationState.Committed,runtime.Flow.TryDismissTutorial(Command()).State);
            Assert.AreEqual(OperationState.Committed,runtime.Flow.TryStartNight(Command()).State);Pump();
            int index=target==GamePhase.Ending?runtime.Catalog.GetSettings().StoryDays:mode==GameMode.Endless?runtime.Catalog.GetEndlessDefinition().MilestoneNight:1;
            ActorSnapshot spring;Assert.IsTrue(runtime.Core.World.TryGetActor(runtime.Core.World.SpringId,out spring));
            OperationResult fixture=runtime.Core.Transactions.TryCommit(new StateMutationBatch{Context=Command(),Reason="pr16.ui.outcome.fixture",Progress=new ProgressPatch{Phase=GamePhase.Night,DayIndex=index,LastSettledNight=index-1,TutorialShown=true},Actors=target==GamePhase.GameOver?new[]{new ActorMutation{ActorId=spring.Id,Kind=ActorMutationKind.SetHealth,CurrentHp=0,MaxHp=spring.MaxHp}}:null});
            Assert.AreEqual(OperationState.Committed,fixture.State,"Outcome fixture: "+fixture.Error);
            runtime.Flow.EvaluateNight(new NightOutcome{DayIndex=index,WavesComplete=target==GamePhase.NightResult||target==GamePhase.Ending,FinalBossKilled=target==GamePhase.Ending,SpringDestroyed=target==GamePhase.GameOver});Pump();
            Assert.AreEqual(target,runtime.Flow.GetSnapshot().Phase);
        }
        [TestCase(GameMode.Story)] [TestCase(GameMode.Endless)]
        public void NightResultExecutesRealDawnOnceAndClosesOwnedListeners(GameMode mode)
        {
            Outcome(mode,GamePhase.NightResult);var panel=Ui.ActionsRoot.GetComponentInChildren<NightResultPanelReferences>();Assert.NotNull(panel);
            Assert.That(panel.SummaryText.text,Does.Contain("当前金币"));Assert.That(panel.SummaryText.text,Does.Not.Contain("已生产"));
            Assert.AreEqual(mode==GameMode.Endless?"继续无尽挑战":"迎接下一天",panel.NextDayButton.GetComponentInChildren<Text>().text);
            int id=panel.GetInstanceID();for(int i=0;i<3;i++)view.RefreshAll();Assert.AreEqual(id,Ui.ActionsRoot.GetComponentInChildren<NightResultPanelReferences>().GetInstanceID());
            var clicks=panel.NextDayButton.onClick;int external=0;clicks.AddListener(()=>external++);int day=runtime.Flow.GetSnapshot().DayIndex;clicks.Invoke();Pump();
            Assert.AreEqual(day+1,runtime.Flow.GetSnapshot().DayIndex);Assert.IsNull(Ui.ActionsRoot.GetComponentInChildren<NightResultPanelReferences>());
            long revision=runtime.Core.Transactions.Revision;clicks.Invoke();Assert.AreEqual(2,external);Assert.AreEqual(revision,runtime.Core.Transactions.Revision);
            Assert.AreEqual(mode==GameMode.Endless,runtime.Flow.GetSnapshot().Milestone50Claimed);
        }
        [Test] public void GameOverRetriesRealCheckpointAndMenuClosesPanel()
        {
            Outcome(GameMode.Story,GamePhase.GameOver);var end=Ui.ActionsRoot.GetComponentInChildren<RunEndPanelReferences>();
            Assert.IsTrue(end.RetryButton.interactable);Assert.IsFalse(end.ContinueEndlessButton.gameObject.activeSelf);
            end.RetryButton.onClick.Invoke();Pump();Assert.AreEqual(GamePhase.Build,runtime.Flow.GetSnapshot().Phase);Assert.IsNull(Ui.ActionsRoot.GetComponentInChildren<RunEndPanelReferences>());
            runtime.Flow.TryReturnToMenu(Command());Pump();Assert.NotNull(Ui.ActionsRoot.GetComponentInChildren<MainMenuPanelReferences>());
        }
        [Test] public void StoryEndingShowsOnlyReturnAndPreservesActualFlow()
        {
            Outcome(GameMode.Story,GamePhase.Ending);var end=Ui.ActionsRoot.GetComponentInChildren<RunEndPanelReferences>();
            Assert.IsFalse(end.RetryButton.gameObject.activeSelf);Assert.IsFalse(end.ContinueEndlessButton.gameObject.activeSelf);
            end.ReturnMenuButton.onClick.Invoke();Pump();Assert.AreEqual(GamePhase.Menu,runtime.Flow.GetSnapshot().Phase);
        }
        [Test] public void RemovedCheckpointDisablesRetryAndExplainsFailure()
        {
            Outcome(GameMode.Story,GamePhase.GameOver);foreach(string path in Directory.GetFiles(directory,"*",SearchOption.AllDirectories))File.Delete(path);
            view.Bind(runtime.ViewContext);var end=Ui.ActionsRoot.GetComponentInChildren<RunEndPanelReferences>();
            Assert.IsFalse(end.RetryButton.interactable);Assert.That(end.HintText.text,Does.Contain("没有可继续"));
        }
        [Test] public void CorruptCheckpointDisablesRetryWithoutStartingAnotherRun()
        {
            Outcome(GameMode.Story,GamePhase.GameOver);foreach(string path in Directory.GetFiles(directory,"*",SearchOption.AllDirectories))File.WriteAllText(path,"{invalid checkpoint");
            string run=runtime.Flow.GetSnapshot().RunId;view.Bind(runtime.ViewContext);var end=Ui.ActionsRoot.GetComponentInChildren<RunEndPanelReferences>();
            Assert.IsFalse(end.RetryButton.interactable);Assert.That(end.HintText.text,Does.Contain("损坏"));Assert.AreEqual(run,runtime.Flow.GetSnapshot().RunId);Assert.AreEqual(GamePhase.GameOver,runtime.Flow.GetSnapshot().Phase);
        }
        [Test] public void FormalPanelAssetsValidateBeforeAwakeAndRejectNestedCanvas()
        {
            string error;Assert.IsTrue(view.UiPrefab.NightResultPanelPrefab.Validate(out error),error);Assert.IsTrue(view.UiPrefab.RunEndPanelPrefab.Validate(out error),error);
            var panel=UnityEngine.Object.Instantiate(view.UiPrefab.NightResultPanelPrefab);
            try{new GameObject("Invalid nested canvas",typeof(RectTransform),typeof(Canvas)).transform.SetParent(panel.Root,false);Assert.IsFalse(panel.Validate(out error));Assert.That(error,Does.Contain("Canvas"));}
            finally{UnityEngine.Object.DestroyImmediate(panel.gameObject);}
        }
        [Test] public void HandIconsPreserveShapeAndDoNotOverlapQuantityText()
        {
            for(int i=0;i<6;i++){
                Button button=Ui.ElementButtons[i];Image icon=button.transform.Find("ElementIcon"+i).GetComponent<Image>();Assert.NotNull(icon.sprite);Assert.IsTrue(icon.preserveAspect);Assert.IsFalse(icon.raycastTarget);Assert.NotNull(Ui.ArtTheme);Assert.That(Ui.ArtTheme.ButtonNormal,Does.Contain(button.GetComponent<Image>().sprite));
                RectTransform label=Ui.HandTexts[i].rectTransform;Assert.GreaterOrEqual(label.anchoredPosition.x,icon.rectTransform.anchoredPosition.x+icon.rectTransform.sizeDelta.x/2+3);
                Assert.AreEqual(22,icon.rectTransform.sizeDelta.x);Assert.AreEqual(22,icon.rectTransform.sizeDelta.y);
            }
        }
        [Test] public void OptionalResultPrefabsUseLegacyActions()
        {
            view.Unbind();temporary=UnityEngine.Object.Instantiate(view.UiPrefab);temporary.NightResultPanelPrefab=null;temporary.RunEndPanelPrefab=null;view.UiPrefab=temporary;view.Bind(runtime.ViewContext);
            Outcome(GameMode.Story,GamePhase.NightResult);Assert.IsNull(Ui.ActionsRoot.GetComponentInChildren<NightResultPanelReferences>());
            Ui.ActionsRoot.GetComponentsInChildren<Button>().First(b=>b.GetComponentInChildren<Text>().text=="迎接下一天").onClick.Invoke();Pump();Assert.AreEqual(2,runtime.Flow.GetSnapshot().DayIndex);
        }
        [TestCase(GamePhase.NightResult,1280,720)] [TestCase(GamePhase.NightResult,960,540)] [TestCase(GamePhase.NightResult,1920,1080)]
        [TestCase(GamePhase.GameOver,1280,720)] [TestCase(GamePhase.GameOver,960,540)] [TestCase(GamePhase.GameOver,1920,1080)]
        [TestCase(GamePhase.Ending,1280,720)] [TestCase(GamePhase.Ending,960,540)] [TestCase(GamePhase.Ending,1920,1080)]
        public void FormalOutcomeCanvasRendersReadableControls(GamePhase phase,int width,int height)
        {
            Outcome(GameMode.Story,phase);var night=Ui.ActionsRoot.GetComponentInChildren<NightResultPanelReferences>();var end=Ui.ActionsRoot.GetComponentInChildren<RunEndPanelReferences>();
            RectTransform root=night!=null?night.Root:end.Root;Text summary=night!=null?night.SummaryText:end.SummaryText;
            Image background=root.GetComponent<Image>();Assert.NotNull(background);Assert.AreEqual(1,background.color.a);Assert.Less(background.color.grayscale,0.2f);
            Assert.Greater(Ui.ActionsRoot.GetSiblingIndex(),Ui.ProductionSummaryText.transform.GetSiblingIndex());
            foreach(Text label in new[]{night!=null?night.HintText:end.HintText,night!=null?night.TitleText:end.TitleText,summary}){Assert.Greater(label.color.a,0.95f);Assert.Greater(label.color.grayscale,0.8f);}
            ScrollRect scroll=summary.GetComponentInParent<ScrollRect>();Assert.NotNull(scroll);Assert.AreEqual(summary.transform.parent,scroll.content);
            GameObject cameraObject=new GameObject("PR16 capture");Camera camera=cameraObject.AddComponent<Camera>();RenderTexture target=new RenderTexture(width,height,24);Texture2D image=null;RenderTexture prior=RenderTexture.active;
            try{
                camera.orthographic=true;camera.transform.position=new Vector3(0,0,-10);camera.targetTexture=target;camera.backgroundColor=Color.black;
                Ui.Canvas.renderMode=RenderMode.ScreenSpaceCamera;Ui.Canvas.worldCamera=camera;Ui.Canvas.planeDistance=1;Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(root);Canvas.ForceUpdateCanvases();camera.Render();
                foreach(Text label in root.GetComponentsInChildren<Text>()){
                    Assert.Greater(label.cachedTextGenerator.vertexCount,4,label.name+" glyphs missing");if(label==summary)continue;
                    Assert.GreaterOrEqual(label.rectTransform.rect.height+1,label.preferredHeight,label.name+" text clipped");
                    Vector3[] corners=new Vector3[4];label.rectTransform.GetWorldCorners(corners);foreach(var corner in corners){Vector3 p=camera.WorldToViewportPoint(corner);Assert.That(p.x,Is.InRange(0f,1f),label.name);Assert.That(p.y,Is.InRange(0f,1f),label.name);}
                }
                RenderTexture.active=target;image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../QA/R3B/P5/Screenshots/U02-"+phase+"-"+width+"x"+height+".png"));Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());
            }finally{RenderTexture.active=prior;Ui.Canvas.renderMode=RenderMode.ScreenSpaceOverlay;Ui.Canvas.worldCamera=null;if(image!=null)UnityEngine.Object.DestroyImmediate(image);camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(cameraObject);}
        }
    }
}
#endif
