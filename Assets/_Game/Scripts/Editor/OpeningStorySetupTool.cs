#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Spotlight.Bootstrap;
using Spotlight.Presentation;
using Spotlight.Presentation.Cinematics;

namespace Spotlight.EditorTools
{
    public static class OpeningStorySetupTool
    {
        public const string StoryPath = "Assets/_Game/Data/Story/OpeningStory.asset";
        public const string PrefabPath = "Assets/_Game/Prefabs/UI/Story/UI_OpeningStory.prefab";
        public const string PreviewPath = "Assets/_Game/Scenes/Sandbox/P5_OpeningStory.unity";
        private static readonly string[] Art = { "Opening_Border", "Opening_Border", "Opening_Necklace", "Opening_Spring", "Opening_Spring" };
        private static readonly string[] Titles = { "边境告急", "蚀魂之咒", "最后的守护", "沙漠中的灵泉", "七夜之约" };
        private static readonly string[] Bodies = {
            "魔物越过边境。公主与哥哥并肩迎敌，守护身后的王国。",
            "一道蚀魂咒击中了哥哥。就在神魂即将消散时，公主胸前的项链亮起柔光。",
            "项链震退魔物，将哥哥的神魂收入嵌石。但这次守护，也耗尽了它的力量。",
            "公主带着项链，踏入沙漠深处。传说中的灵泉，是她救回哥哥的唯一希望。",
            "在灵泉下祈祷七个夜晚，项链便能重新聚起力量。白天筑起防线，夜晚守住灵泉——她的旅程，从这里开始。" };

        [MenuItem("聚光灯/剧情/创建或补齐开场过场")]
        public static void CreateOrComplete()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("请退出播放模式后补齐开场资产。"); return; }
            EnsureFolder(Path.GetDirectoryName(StoryPath)); EnsureFolder(Path.GetDirectoryName(PrefabPath));
            var sequence = AssetDatabase.LoadAssetAtPath<CutsceneSequenceSO>(StoryPath);
            bool created = sequence == null;
            if (created) { sequence = ScriptableObject.CreateInstance<CutsceneSequenceSO>(); sequence.Slides = new CutsceneSlide[5]; AssetDatabase.CreateAsset(sequence, StoryPath); }
            for (int i = 0; i < 5; i++)
            {
                string path = "Assets/_Game/Art/Story/Opening/" + Art[i] + ".png";
                if (File.Exists(path))
                {
                    AssetDatabase.ImportAsset(path);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null) { importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single; importer.mipmapEnabled = false; importer.sRGBTexture = true; importer.maxTextureSize = 2048; importer.filterMode = FilterMode.Bilinear; importer.SaveAndReimport(); }
                }
                else Debug.LogWarning("开场插画缺失，补图后再次执行本菜单：" + path);
                if (created) sequence.Slides[i] = new CutsceneSlide { Title = Titles[i], Body = Bodies[i] };
                if (sequence.Slides != null && i < sequence.Slides.Length && sequence.Slides[i] != null && sequence.Slides[i].Illustration == null)
                    sequence.Slides[i].Illustration = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            EditorUtility.SetDirty(sequence);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                var ui = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/UI/UI_Game.prefab");
                Font font = ui != null ? ui.GetComponent<GameUiReferences>().TitleText.font : GameView.CreateFont();
                var panel = CutscenePanelReferences.CreateDefaultPanel(null, font);
                try { prefab = PrefabUtility.SaveAsPrefabAsset(panel.gameObject, PrefabPath); }
                finally { Object.DestroyImmediate(panel.gameObject); }
            }
            var bootScene = SceneManager.GetSceneByPath("Assets/_Game/Scenes/00_Bootstrap.unity");
            bool loadedByTool = !bootScene.IsValid() || !bootScene.isLoaded;
            bool wasDirty = !loadedByTool && bootScene.isDirty;
            try
            {
                if (loadedByTool) bootScene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/00_Bootstrap.unity", OpenSceneMode.Additive);
                foreach (var root in bootScene.GetRootGameObjects()) foreach (var boot in root.GetComponentsInChildren<GameBootstrap>(true))
                {
                    var serialized = new SerializedObject(boot);
                    var storyProperty = serialized.FindProperty("OpeningStory"); var panelProperty = serialized.FindProperty("OpeningCutscenePrefab");
                    if (storyProperty == null || panelProperty == null) throw new System.InvalidOperationException("GameBootstrap 缺少开场剧情字段。");
                    if (storyProperty.objectReferenceValue == null) storyProperty.objectReferenceValue = sequence;
                    if (panelProperty.objectReferenceValue == null) panelProperty.objectReferenceValue = prefab.GetComponent<CutscenePanelReferences>();
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                if (!wasDirty) EditorSceneManager.SaveScene(bootScene);
                else Debug.LogWarning("00_Bootstrap 已有未保存编辑，开场引用已补齐并保留为未保存状态，请自行保存场景。");
                if (!File.Exists(PreviewPath))
                {
                    bool canCreatePreview = true;
                    for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                    {
                        var open = SceneManager.GetSceneAt(i);
                        if (!string.IsNullOrEmpty(open.path)) continue;
                        if (Application.isBatchMode) EditorSceneManager.CloseScene(open, true);
                        else canCreatePreview = false;
                    }
                    if (!canCreatePreview) Debug.LogWarning("开场已接入正式 Bootstrap；请先保存未命名场景，再运行本菜单生成独立预览。");
                    else
                    {
                        var preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                        try
                        {
                            var canvasObject = new GameObject("Opening preview canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                            SceneManager.MoveGameObjectToScene(canvasObject, preview);
                            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
                            var panel = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasObject.transform)).GetComponent<CutscenePanelReferences>();
                            var driver = canvasObject.AddComponent<OpeningStoryPreview>(); driver.Sequence = sequence; driver.Panel = panel;
                            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); SceneManager.MoveGameObjectToScene(events, preview);
                            var cameraObject = new GameObject("Preview camera", typeof(Camera), typeof(AudioListener)); SceneManager.MoveGameObjectToScene(cameraObject, preview);
                            var camera = cameraObject.GetComponent<Camera>(); camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; cameraObject.transform.position = new Vector3(0, 0, -10);
                            EditorSceneManager.SaveScene(preview, PreviewPath);
                        }
                        finally { EditorSceneManager.CloseScene(preview, true); }
                    }
                }
            }
            finally { if (loadedByTool && bootScene.IsValid() && bootScene.isLoaded) EditorSceneManager.CloseScene(bootScene, true); AssetDatabase.SaveAssets(); }
            Debug.Log("开场过场已补齐。独立预览：" + PreviewPath);
        }
        private static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/'); if (AssetDatabase.IsValidFolder(path)) return;
            EnsureFolder(Path.GetDirectoryName(path)); AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }
    }
}
#endif
