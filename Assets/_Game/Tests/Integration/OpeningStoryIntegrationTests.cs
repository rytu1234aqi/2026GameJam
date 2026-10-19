#if UNITY_EDITOR
using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Spotlight.Bootstrap;
using Spotlight.Contracts;
using Spotlight.Presentation;
using Spotlight.Presentation.Cinematics;

namespace Spotlight.Tests.Integration
{
    public sealed class OpeningStoryIntegrationTests
    {
        private GameObject host;
        private GameView view;
        private ModuleComposition runtime;
        private string directory;
        private CutsceneSequenceSO sequence;
        private MainMenuPanelReferences Menu { get { return host.GetComponentInChildren<MainMenuPanelReferences>(true); } }
        private CutscenePlayer Player { get { return host.GetComponentInChildren<CutscenePlayer>(true); } }
        private CommandContext Command() { return runtime.Core.Commands.Create(runtime.Core.Transactions.Revision); }
        private void Pump() { runtime.Advance(0); view.RefreshAll(); }
        [SetUp] public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "Spotlight_Opening_" + Guid.NewGuid().ToString("N"));
            runtime = new ModuleComposition(AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(PrototypeCatalogFactory.CatalogPath).BuildCatalogData(), directory);
            host = new GameObject("Opening integration"); view = host.AddComponent<GameView>();
            view.UiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/UI/UI_Game.prefab").GetComponent<GameUiReferences>();
            sequence = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<CutsceneSequenceSO>("Assets/_Game/Data/Story/OpeningStory.asset"));
            view.OpeningStory = sequence;
            view.OpeningCutscenePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/UI/Story/UI_OpeningStory.prefab").GetComponent<CutscenePanelReferences>();
            view.Bind(runtime.ViewContext);
        }
        [TearDown] public void Cleanup()
        {
            if (view != null) view.Unbind(); if (host != null) UnityEngine.Object.DestroyImmediate(host);
            if (sequence != null) UnityEngine.Object.DestroyImmediate(sequence); if (runtime != null) runtime.Dispose();
            if (directory != null && Directory.Exists(directory))
            {
                string target = Path.GetFullPath(directory), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                Assert.IsTrue(target.StartsWith(temp, StringComparison.OrdinalIgnoreCase));
                Assert.IsTrue(Path.GetFileName(target).StartsWith("Spotlight_Opening_", StringComparison.Ordinal)); Directory.Delete(target, true);
            }
        }
        private void Complete(CutscenePlayer player)
        {
            for (int i = 0; i < 40 && player != null && player.IsPlaying; i++) { player.Tick(60); player.Advance(); }
            Assert.IsTrue(player == null || !player.IsPlaying, "Opening did not finish after all page confirmations.");
        }
        [Test] public void RealStoryClickKeepsMenuAndCheckpointUntilFinalPage()
        {
            // Create a real prior checkpoint and compare every file byte during the opening.
            Menu.EndlessButton.onClick.Invoke(); Pump();
            DayEventSnapshot day = runtime.Gameplay.DayEvents.GetSnapshot(); runtime.Gameplay.DayEvents.TryChoose(Command(), day.State.InstanceId, day.Options[0].Id); Pump();
            Assert.AreEqual(OperationState.Committed, runtime.ViewContext.Saves.SaveCheckpoint(Command(), SaveReason.Manual).State);
            runtime.Flow.TryReturnToMenu(Command()); Pump();
            string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories); byte[][] saved = new byte[files.Length][];
            for (int i = 0; i < files.Length; i++) saved[i] = File.ReadAllBytes(files[i]);
            int eventSystemsBefore = host.GetComponentsInChildren<EventSystem>(true).Length;
            long before = runtime.Core.Transactions.Revision; Menu.StoryButton.onClick.Invoke(); Pump();
            CutscenePlayer player = Player; Assert.NotNull(player); Assert.IsTrue(player.IsPlaying);
            Assert.AreEqual(GamePhase.Menu, runtime.Flow.GetSnapshot().Phase); Assert.AreEqual(before, runtime.Core.Transactions.Revision);
            player.Skip(); Assert.IsTrue(player.IsPlaying); Assert.AreEqual(GamePhase.Menu, runtime.Flow.GetSnapshot().Phase);
            for (int i = 0; i < files.Length; i++) CollectionAssert.AreEqual(saved[i], File.ReadAllBytes(files[i]));
            Assert.AreEqual(1, host.GetComponentsInChildren<Canvas>(true).Length);
            Assert.AreEqual(eventSystemsBefore, host.GetComponentsInChildren<EventSystem>(true).Length, "Opening added an EventSystem");
            Assert.LessOrEqual(host.GetComponentsInChildren<EventSystem>(true).Length, 1);
            Complete(player); Pump(); Assert.AreEqual(GamePhase.DayEvent, runtime.Flow.GetSnapshot().Phase); Assert.AreEqual(GameMode.Story, runtime.Flow.GetSnapshot().Mode);
            long committed = runtime.Core.Transactions.Revision; if (player != null) { player.Advance(); player.Skip(); player.Tick(60); } Pump(); Assert.AreEqual(committed, runtime.Core.Transactions.Revision);
        }
        [Test] public void EndlessAndContinueBypassOpening()
        {
            Menu.EndlessButton.onClick.Invoke(); Pump(); Assert.IsTrue(Player == null || !Player.IsPlaying);
            DayEventSnapshot day = runtime.Gameplay.DayEvents.GetSnapshot(); runtime.Gameplay.DayEvents.TryChoose(Command(), day.State.InstanceId, day.Options[0].Id); Pump();
            runtime.ViewContext.Saves.SaveCheckpoint(Command(), SaveReason.Manual); runtime.Flow.TryReturnToMenu(Command()); Pump();
            Menu.ContinueButton.onClick.Invoke(); Pump(); Assert.AreEqual(GamePhase.Build, runtime.Flow.GetSnapshot().Phase); Assert.IsTrue(Player == null || !Player.IsPlaying);
        }
        [Test] public void RebindCancelsOpeningAndPreservesExternalPause()
        {
            Guid external = runtime.ViewContext.Clock.AcquirePause(PauseReason.FocusLost);
            Menu.StoryButton.onClick.Invoke(); Pump(); long revision = runtime.Core.Transactions.Revision;
            view.Bind(runtime.ViewContext); view.Bind(runtime.ViewContext); Assert.AreEqual(revision, runtime.Core.Transactions.Revision);
            Assert.IsTrue(Player == null || !Player.IsPlaying); Assert.AreEqual(1, host.GetComponentsInChildren<Canvas>(true).Length);
            Menu.StoryButton.onClick.Invoke(); Pump(); view.Unbind(); Assert.AreEqual(revision, runtime.Core.Transactions.Revision);
            Assert.IsTrue(runtime.ViewContext.Clock.GetSnapshot().IsPaused); CollectionAssert.Contains(runtime.ViewContext.Clock.GetSnapshot().Reasons, PauseReason.FocusLost);
            runtime.ViewContext.Clock.ReleasePause(external); Assert.IsFalse(runtime.ViewContext.Clock.GetSnapshot().IsPaused);
        }
        [Test] public void EmptySequenceKeepsLegacyStoryStartCompatible()
        {
            sequence.Slides = new CutsceneSlide[0]; Menu.StoryButton.onClick.Invoke(); Pump(); Assert.AreEqual(GamePhase.DayEvent, runtime.Flow.GetSnapshot().Phase);
        }
        [Test] public void PlayerFastForwardAndTransitionFinishCallbackExactlyOnce()
        {
            var player = host.AddComponent<CutscenePlayer>(); player.Panel = CutscenePanelReferences.CreateDefaultPanel(host.transform, view.UiPrefab.TitleText.font);
            int completed = 0; player.Begin(sequence, delegate { completed++; });
            player.Advance(); Assert.AreEqual(0, completed); Assert.AreEqual(0, player.SlideIndex);
            player.Tick(2); player.Advance(); Assert.AreEqual(sequence.Slides[0].Body, player.RevealedText);
            // Stop on the fully visible final page, then verify the ending remains opaque until completion.
            while (player.SlideIndex < sequence.Slides.Length - 1) { player.Tick(60); player.Tick(60); player.Advance(); }
            player.Tick(60); player.Tick(60); player.Advance();
            Assert.IsTrue(player.IsTransitioning); Assert.AreEqual(0, completed);
            float finalFade = sequence.Slides[sequence.Slides.Length - 1].FadeSeconds;
            player.Tick(finalFade * .5f); Assert.AreEqual(1, player.Panel.RootGroup.alpha); Assert.IsTrue(player.IsTransitioning); Assert.AreEqual(0, completed);
            var veil = player.Panel.transform.Find("Ending Black Veil").GetComponent<Image>();
            Assert.That(veil.color.a, Is.InRange(.01f, .99f));
            player.Tick(finalFade); Assert.IsFalse(player.IsPlaying); Assert.AreEqual(1, completed);
            player.Advance(); player.Skip(); player.Tick(100); Assert.AreEqual(1, completed);
            player.Begin(sequence, delegate { completed++; }); player.Tick(sequence.Slides[0].FadeSeconds);
            Assert.AreEqual(0, veil.color.a, "Reused player retained ending black veil");
            Assert.AreEqual(1, player.Panel.RootGroup.alpha);
            Assert.AreSame(veil, player.Panel.transform.Find("Ending Black Veil").GetComponent<Image>());
            int veilCount = 0; foreach (Transform child in player.Panel.transform) if (child.name == "Ending Black Veil") veilCount++; Assert.AreEqual(1, veilCount);
            player.Cancel(); player.Tick(100); player.Advance(); Assert.AreEqual(1, completed);
        }
        [Test] public void MultiParagraphChineseCopyRendersWithoutTruncation()
        {
            sequence.Slides = new[] { new CutsceneSlide {
                Title = "七夜之约 · 长文预览", Illustration = sequence.Slides[4].Illustration,
                Body = "公主带着项链，踏入沙漠深处。传说中的灵泉，是她救回哥哥的唯一希望。\n在灵泉下祈祷七个夜晚，项链便能重新聚起力量。\n白天筑起防线，夜晚守住灵泉——她的旅程，从这里开始。" } };
            Menu.StoryButton.onClick.Invoke(); Pump();
            Assert.IsTrue(Player.Panel.RootGroup.blocksRaycasts); Assert.IsTrue(Player.Panel.RootGroup.interactable);
            Capture(Player, 960, 540, 1, sequence);
        }
        [TestCase(960,540)] [TestCase(1280,720)] [TestCase(1920,1080)]
        public void RealCanvasRendersAllChinesePages(int width, int height)
        {
            Menu.StoryButton.onClick.Invoke(); Pump(); Capture(Player, width, height, sequence.Slides.Length, sequence);
        }
        public static void RenderPreviewBatch()
        {
            foreach (var size in new[] { new Vector2Int(960,540), new Vector2Int(1280,720), new Vector2Int(1920,1080) })
            {
                var test = new OpeningStoryIntegrationTests();
                try { test.Setup(); test.RealCanvasRendersAllChinesePages(size.x, size.y); }
                finally { test.Cleanup(); }
            }
            Debug.Log("OpeningStory render QA passed at 960x540, 1280x720, 1920x1080.");
        }
        [Test] public void SameTickInputRevealsOnceAndNextTickTurnsPageWithoutRootFade()
        {
            var player = host.AddComponent<CutscenePlayer>(); player.Panel = CutscenePanelReferences.CreateDefaultPanel(host.transform, view.UiPrefab.TitleText.font);
            player.Begin(sequence, null); player.Tick(1);
            player.Advance(); player.Advance();
            Assert.AreEqual(sequence.Slides[0].Body, player.RevealedText); Assert.AreEqual(0, player.SlideIndex);
            player.Tick(0); player.Advance(); Assert.AreEqual(1, player.SlideIndex);
            Assert.AreEqual(1, player.Panel.RootGroup.alpha);
            player.Tick(.1f); Assert.AreEqual(1, player.Panel.RootGroup.alpha); Assert.IsTrue(player.IsTransitioning);
        }
        [Test] public void UnicodeRevealNeverSplitsSurrogateOrCombiningCharacter()
        {
            sequence.Slides = new[] { new CutsceneSlide { Body = "中😀e\u0301文", CharactersPerSecond = 1, FadeSeconds = 0 } };
            var player = host.AddComponent<CutscenePlayer>(); player.Panel = CutscenePanelReferences.CreateDefaultPanel(host.transform, view.UiPrefab.TitleText.font);
            int completed = 0; player.Begin(sequence, delegate { completed++; }); player.Tick(0);
            foreach (string expected in new[] { "中", "中😀", "中😀e\u0301", "中😀e\u0301文" }) { player.Tick(1); Assert.AreEqual(expected, player.RevealedText); }
            player.Advance(); player.Tick(0); player.Advance(); Assert.AreEqual(1, completed);
            player.Begin(sequence, delegate { completed++; }); player.Cancel(); player.Cancel(); player.Tick(100); Assert.AreEqual(1, completed);
        }
        [Test] public void FormalOpeningAssetsAndBootstrapReferencesAreComplete()
        {
            var formal = AssetDatabase.LoadAssetAtPath<CutsceneSequenceSO>("Assets/_Game/Data/Story/OpeningStory.asset");
            Assert.NotNull(formal); Assert.AreEqual(5, formal.Slides.Length);
            foreach (var slide in formal.Slides) { Assert.NotNull(slide); Assert.IsTrue(slide.Illustration != null, "Missing illustration: " + slide.Title); Assert.IsNotEmpty(slide.Title); Assert.IsNotEmpty(slide.Body); }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/UI/Story/UI_OpeningStory.prefab").GetComponent<CutscenePanelReferences>();
            string error; Assert.IsTrue(prefab.Validate(out error), error);
            var scene = SceneManager.GetSceneByPath("Assets/_Game/Scenes/00_Bootstrap.unity"); bool loadedByTest = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (loadedByTest) scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/00_Bootstrap.unity", OpenSceneMode.Additive);
                GameBootstrap found = null; foreach (var root in scene.GetRootGameObjects()) { found = root.GetComponentInChildren<GameBootstrap>(true); if (found != null) break; }
                Assert.NotNull(found); Assert.AreSame(formal, found.OpeningStory); Assert.AreSame(prefab, found.OpeningCutscenePrefab);
            }
            finally { if (loadedByTest && scene.IsValid()) EditorSceneManager.CloseScene(scene, true); }
        }
        public static void RenderAnimationBatch()
        {
            var test = new OpeningStoryIntegrationTests(); GameObject cameraObject = null; RenderTexture target = null; Texture2D image = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                test.Setup(); test.Menu.StoryButton.onClick.Invoke(); test.Pump(); var player = test.Player;
                var canvas = player.Panel.GetComponentInParent<Canvas>(); cameraObject = new GameObject("Opening animation camera"); var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true; camera.transform.position = new Vector3(0,0,-10); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                target = new RenderTexture(1280,720,24); camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                image = new Texture2D(1280,720,TextureFormat.RGB24,false);
                string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../QA/R3B/OpeningStory/Frames")); Directory.CreateDirectory(output);
                int frame = 0;
                // 3.5 seconds/page: initial crossfade, natural typewriter, full text hold, then next page.
                for (int page = 0; page < 5; page++)
                {
                    for (int local = 0; local < 53; local++)
                    {
                        player.Tick(1f/15); if (local == 43 && player.VisibleCharacterCount < new System.Globalization.StringInfo(test.sequence.Slides[page].Body).LengthInTextElements) player.Advance();
                        WriteAnimationFrame(camera, target, image, output, frame++);
                    }
                    player.Tick(0); player.Advance();
                }
                for (int tail = 0; tail < 12; tail++) { if (player != null) player.Tick(1f/15); WriteAnimationFrame(camera, target, image, output, frame++); }
                File.WriteAllText(Path.Combine(output, "capture.txt"), "fps=15\nframes=" + frame + "\nresolution=1280x720\nsource=real CutscenePlayer.Tick and formal Canvas\n");
                Debug.Log("Opening animation frames: " + frame + " at 15 fps -> " + output);
            }
            finally
            {
                RenderTexture.active = previous; if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); } if (image != null) UnityEngine.Object.DestroyImmediate(image); test.Cleanup();
            }
        }
        private static void WriteAnimationFrame(Camera camera, RenderTexture target, Texture2D image, string output, int frame)
        {
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply(); File.WriteAllBytes(Path.Combine(output,"frame-" + frame.ToString("D4") + ".png"),image.EncodeToPNG());
        }
        private static string SpriteDiagnostic(Sprite sprite)
        { return sprite != null ? sprite.name + "#" + sprite.GetInstanceID() : ReferenceEquals(sprite, null) ? "CLR-null" : "Unity-destroyed-null"; }
        private static void Capture(CutscenePlayer player, int width, int height, int pages, CutsceneSequenceSO expected)
        {
            var cameraObject = new GameObject("Opening QA camera"); var camera = cameraObject.AddComponent<Camera>();
            var target = new RenderTexture(width, height, 24); Texture2D image = null; RenderTexture previous = RenderTexture.active;
            Canvas canvas = player.Panel.GetComponentInParent<Canvas>(); RenderMode mode = canvas.renderMode; Camera oldCamera = canvas.worldCamera;
            try
            {
                camera.orthographic = true; camera.transform.position = new Vector3(0,0,-10); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                for (int i = 0; i < pages; i++)
                {
                    string beforeTick = "A=" + SpriteDiagnostic(player.Panel.IllustrationA.sprite) + ", B=" + SpriteDiagnostic(player.Panel.IllustrationB.sprite);
                    player.Tick(60); player.Tick(60); // Finish fade, then reveal the whole page before capture.
                    string afterTick = "A=" + SpriteDiagnostic(player.Panel.IllustrationA.sprite) + ", B=" + SpriteDiagnostic(player.Panel.IllustrationB.sprite);
                    Assert.AreEqual(i, player.SlideIndex);
                    Canvas.ForceUpdateCanvases(); camera.Render();
                    foreach (Text text in new[] { player.Panel.TitleText, player.Panel.BodyText, player.Panel.CounterText })
                    {
                        Assert.Greater(text.cachedTextGenerator.vertexCount, 4, text.name + " glyphs missing");
                        Assert.GreaterOrEqual(text.rectTransform.rect.height + 1, text.preferredHeight, text.name + " text clipped");
                        Vector3[] corners = new Vector3[4]; text.rectTransform.GetWorldCorners(corners);
                        foreach (var corner in corners) { var point = camera.WorldToViewportPoint(corner); Assert.That(point.x, Is.InRange(-.001f,1.001f)); Assert.That(point.y, Is.InRange(-.001f,1.001f)); }
                    }
                    Sprite a = player.Panel.IllustrationA.sprite, b = player.Panel.IllustrationB.sprite;
                    var formal = AssetDatabase.LoadAssetAtPath<CutsceneSequenceSO>("Assets/_Game/Data/Story/OpeningStory.asset");
                    Sprite source = formal != null && i < formal.Slides.Length ? formal.Slides[i].Illustration : null;
                    Assert.IsTrue(a != null || b != null, "Page illustration missing: page=" + (i+1) + ", source=" + SpriteDiagnostic(source) + ", clone=" + SpriteDiagnostic(expected.Slides[i].Illustration) + ", beforeTick=[" + beforeTick + "], afterTick=[" + afterTick + "], afterCamera A=" + SpriteDiagnostic(a) + ", B=" + SpriteDiagnostic(b) + ", PlayerSlide=" + player.SlideIndex + ", title=" + player.Panel.TitleText.text);
                    RenderTexture.active = target; image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
                    string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../QA/R3B/OpeningStory/Opening-" + (i+1) + "-" + width + "x" + height + ".png"));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image); image = null;
                    if (i + 1 < pages) player.Advance();
                }
            }
            finally
            {
                RenderTexture.active = previous; canvas.renderMode = mode; canvas.worldCamera = oldCamera; if (image != null) UnityEngine.Object.DestroyImmediate(image);
                camera.targetTexture = null; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
#endif
