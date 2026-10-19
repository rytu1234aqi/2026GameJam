#if UNITY_EDITOR
using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Spotlight.Bootstrap;
using Spotlight.Contracts;
using Spotlight.Presentation;

namespace Spotlight.Tests.Integration
{
    public sealed class R3BMenuIntegrationTests
    {
        private GameObject host;
        private GameView view;
        private ModuleComposition runtime;
        private string directory;
        private GameUiReferences temporaryPrefab;
        private GameUiReferences Ui { get { return host.GetComponentInChildren<GameUiReferences>(true); } }
        private MainMenuPanelReferences Menu { get { return Ui.ActionsRoot.GetComponentInChildren<MainMenuPanelReferences>(); } }
        private CommandContext Command() { return runtime.Core.Commands.Create(runtime.Core.Transactions.Revision); }
        private void Pump() { runtime.Advance(0); view.RefreshAll(); }
        [SetUp] public void Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"Spotlight_R3BMenu_"+Guid.NewGuid().ToString("N"));
            runtime=new ModuleComposition(AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(PrototypeCatalogFactory.CatalogPath).BuildCatalogData(),directory);
            host=new GameObject("R3B menu integration");view=host.AddComponent<GameView>();
            view.UiPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/UI/UI_Game.prefab").GetComponent<GameUiReferences>();
            view.Bind(runtime.ViewContext);
        }
        [TearDown] public void Cleanup()
        {
            if(view!=null)view.Unbind();if(host!=null)UnityEngine.Object.DestroyImmediate(host);
            if(temporaryPrefab!=null)UnityEngine.Object.DestroyImmediate(temporaryPrefab.gameObject);
            if(runtime!=null)runtime.Dispose();
            if(directory!=null&&Directory.Exists(directory))
            {
                string target=Path.GetFullPath(directory),root=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                Assert.IsTrue(target.StartsWith(root,StringComparison.OrdinalIgnoreCase));
                Assert.IsTrue(Path.GetFileName(target).StartsWith("Spotlight_R3BMenu_"));Directory.Delete(target,true);
            }
        }
        [Test] public void FormalMenuHasReadableDistinctControlsAndDisablesContinueWithoutCheckpoint()
        {
            Assert.NotNull(Menu);string error;Assert.IsTrue(Menu.Validate(out error),error);
            Assert.AreEqual("聚光灯",Menu.TitleText.text);
            Assert.IsFalse(Menu.ContinueButton.interactable);Assert.That(Menu.HintText.text,Does.Contain("没有可继续"));
            Assert.AreEqual(Menu.TitleText.font,Ui.TitleText.font);
            Canvas.ForceUpdateCanvases();Assert.Greater(Menu.StoryButton.GetComponent<RectTransform>().rect.height,40);
            Assert.AreNotEqual(Menu.StoryButton.transform.localPosition,Menu.EndlessButton.transform.localPosition);
            Assert.AreEqual(1,host.GetComponentsInChildren<Canvas>(true).Length);
        }
        [TestCase(GameMode.Story)] [TestCase(GameMode.Endless)]
        public void MenuStartsRealModeAndClosedMenuDetachesOwnedListeners(GameMode mode)
        {
            MainMenuPanelReferences menu=Menu;Button button=mode==GameMode.Story?menu.StoryButton:menu.EndlessButton;
            Button.ButtonClickedEvent clicks=button.onClick;int external=0;clicks.AddListener(delegate {external++;});
            int identity=menu.GetInstanceID();for(int i=0;i<5;i++)view.RefreshAll();Assert.AreEqual(identity,Menu.GetInstanceID());
            clicks.Invoke();Pump();Assert.AreEqual(mode,runtime.Flow.GetSnapshot().Mode);Assert.AreEqual(GamePhase.DayEvent,runtime.Flow.GetSnapshot().Phase);
            Assert.IsNull(Menu);long revision=runtime.Core.Transactions.Revision;
            clicks.Invoke();Assert.AreEqual(2,external);Assert.AreEqual(revision,runtime.Core.Transactions.Revision);
            Assert.AreEqual(1,host.GetComponentsInChildren<DayEventPanelReferences>().Length);
        }
        [Test] public void RepeatBindingAndUnbindDetachMenuWithoutRemovingExternalListener()
        {
            Button.ButtonClickedEvent stale=Menu.StoryButton.onClick;int external=0;stale.AddListener(delegate {external++;});
            view.Bind(runtime.ViewContext);view.Bind(runtime.ViewContext);
            Assert.AreEqual(1,host.GetComponentsInChildren<MainMenuPanelReferences>(true).Length);
            long revision=runtime.Core.Transactions.Revision;stale.Invoke();Assert.AreEqual(1,external);Assert.AreEqual(revision,runtime.Core.Transactions.Revision);
            Button.ButtonClickedEvent current=Menu.StoryButton.onClick;view.Unbind();current.Invoke();Assert.AreEqual(revision,runtime.Core.Transactions.Revision);
            Assert.AreEqual(0,host.GetComponentsInChildren<Canvas>(true).Length);
        }
        [Test] public void SummaryUsesDawnRewardsThenClearsAtMenuAndNewRun()
        {
            Menu.StoryButton.onClick.Invoke();Pump();Assert.AreEqual("上夜生产：暂无记录",Ui.ProductionSummaryText.text);
            ResourceAmount[] rewards={new ResourceAmount(ResourceBucket.Hand,"elm.water",3)};
            runtime.Events.Publish(new DawnSettledEvent{Rewards=rewards});runtime.Events.Flush();
            Assert.AreEqual(ProductionSummaryFormatter.Format(rewards),Ui.ProductionSummaryText.text);
            runtime.Flow.TryReturnToMenu(Command());Pump();Assert.AreEqual("",Ui.ProductionSummaryText.text);
            Menu.EndlessButton.onClick.Invoke();Pump();Assert.AreEqual("上夜生产：暂无记录",Ui.ProductionSummaryText.text);
        }
        [Test] public void ContinueReadsRealCheckpointAndDoesNotInventSummaryHistory()
        {
            Menu.StoryButton.onClick.Invoke();Pump();
            DayEventSnapshot day=runtime.Gameplay.DayEvents.GetSnapshot();runtime.Gameplay.DayEvents.TryChoose(Command(),day.State.InstanceId,day.Options[0].Id);Pump();
            Assert.AreEqual(GamePhase.Build,runtime.Flow.GetSnapshot().Phase);
            Assert.AreEqual(OperationState.Committed,runtime.ViewContext.Saves.SaveCheckpoint(Command(),SaveReason.Manual).State);Pump();
            runtime.Flow.TryReturnToMenu(Command());Pump();Assert.IsTrue(Menu.ContinueButton.interactable);
            Menu.ContinueButton.onClick.Invoke();Pump();Assert.AreEqual(GamePhase.Build,runtime.Flow.GetSnapshot().Phase);
            Assert.AreEqual("上夜生产：暂无记录",Ui.ProductionSummaryText.text);
        }
        [Test] public void OptionalMenuAndSummaryRemainCompatibleWithLegacyPrefab()
        {
            view.Unbind();temporaryPrefab=UnityEngine.Object.Instantiate(view.UiPrefab);
            temporaryPrefab.MainMenuPanelPrefab=null;temporaryPrefab.ProductionSummaryText=null;
            view.UiPrefab=temporaryPrefab;view.Bind(runtime.ViewContext);Assert.IsNull(Menu);
            foreach(Button button in Ui.ActionsRoot.GetComponentsInChildren<Button>())
                if(button.GetComponentInChildren<Text>().text=="故事模式"){button.onClick.Invoke();Pump();Assert.AreEqual(GameMode.Story,runtime.Flow.GetSnapshot().Mode);return;}
            Assert.Fail("Legacy menu action missing");
        }
        [Test] public void BuildSummaryRendersApartFromActualHint()
        {
            Menu.StoryButton.onClick.Invoke();Pump();DayEventSnapshot day=runtime.Gameplay.DayEvents.GetSnapshot();
            runtime.Gameplay.DayEvents.TryChoose(Command(),day.State.InstanceId,day.Options[0].Id);Pump();
            if(!runtime.Flow.GetSnapshot().TutorialShown)runtime.Flow.TryDismissTutorial(Command());Pump();
            Assert.AreEqual(GamePhase.Build,runtime.Flow.GetSnapshot().Phase);
            runtime.Events.Publish(new DawnSettledEvent{Rewards=new[]{new ResourceAmount(ResourceBucket.Hand,"elm.water",3),new ResourceAmount(ResourceBucket.Hand,"elm.fire",2)}});runtime.Events.Flush();
            FormalCanvasRendersMenuAtReferenceAndSmallWindowSizes(1280,720);
            FormalCanvasRendersMenuAtReferenceAndSmallWindowSizes(960,540);
        }
        [TestCase(1280,720)] [TestCase(960,540)]
        public void FormalCanvasRendersMenuAtReferenceAndSmallWindowSizes(int width,int height)
        {
            GameObject cameraObject=new GameObject("R3B menu capture camera");Camera camera=cameraObject.AddComponent<Camera>();
            RenderTexture target=new RenderTexture(width,height,24);Texture2D image=null;
            RenderTexture previous=RenderTexture.active;
            try
            {
                camera.orthographic=true;camera.transform.position=new Vector3(0,0,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                camera.targetTexture=target;Ui.Canvas.renderMode=RenderMode.ScreenSpaceCamera;Ui.Canvas.worldCamera=camera;Ui.Canvas.planeDistance=1;
                Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(Menu!=null?Menu.Root:Ui.Canvas.transform as RectTransform);Canvas.ForceUpdateCanvases();camera.Render();
                Text[] labels=Menu!=null?new Text[]{Menu.TitleText,Menu.HintText,Menu.StoryButton.GetComponentInChildren<Text>(),Menu.EndlessButton.GetComponentInChildren<Text>(),Menu.ContinueButton.GetComponentInChildren<Text>()}:new Text[]{Ui.ProductionSummaryText,Ui.HintText};
                foreach(Text label in labels)
                {
                    Assert.Greater(label.cachedTextGenerator.vertexCount,4,label.name+" missing rendered glyphs");
                    Assert.GreaterOrEqual(label.rectTransform.rect.height+1,label.preferredHeight,label.name+" clipped text");
                    Vector3[] corners=new Vector3[4];label.rectTransform.GetWorldCorners(corners);
                    foreach(Vector3 corner in corners){Vector3 point=camera.WorldToViewportPoint(corner);Assert.That(point.x,Is.InRange(0f,1f));Assert.That(point.y,Is.InRange(0f,1f));}
                }
                for(int i=0;i<labels.Length;i++)for(int j=i+1;j<labels.Length;j++)
                {
                    Vector3[] a=new Vector3[4],b=new Vector3[4];labels[i].rectTransform.GetWorldCorners(a);labels[j].rectTransform.GetWorldCorners(b);
                    Assert.IsFalse(new Rect(a[0],a[2]-a[0]).Overlaps(new Rect(b[0],b[2]-b[0])),"Menu labels overlap");
                }
                RenderTexture.active=target;image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../QA/R3B/P5/Screenshots/"+(Menu!=null?"U01-runtime-":"U01-summary-")+width+"x"+height+".png"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;Ui.Canvas.renderMode=RenderMode.ScreenSpaceOverlay;Ui.Canvas.worldCamera=null;
                if(image!=null)UnityEngine.Object.DestroyImmediate(image);camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
        [Test] public void PanelValidationRejectsCanvasAndDuplicateReferences()
        {
            MainMenuPanelReferences menu=Menu;Button original=menu.EndlessButton;menu.EndlessButton=menu.StoryButton;
            string error;Assert.IsFalse(menu.Validate(out error));menu.EndlessButton=original;
            menu.gameObject.AddComponent<Canvas>();Assert.IsFalse(menu.Validate(out error));
        }
    }
}
#endif
