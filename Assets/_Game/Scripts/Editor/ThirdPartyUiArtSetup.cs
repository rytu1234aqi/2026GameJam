using System;
using System.IO;
using Spotlight.Presentation;
using UnityEditor;
using UnityEngine;

namespace Spotlight.Editor
{
    /// <summary>Repeatable art import and prefab skin pass; preserves prefab identities and gameplay listeners.</summary>
    public static class ThirdPartyUiArtSetup
    {
        private const string Art="Assets/_Game/Art/ThirdParty/";
        private const string ThemePath="Assets/_Game/Data/Art/ThirdPartyUiTheme.asset";
        private const string GamePath="Assets/_Game/Prefabs/UI/UI_Game.prefab";
        [MenuItem("聚光灯/美术/接入命名 UI 素材")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("请先退出 Play。");
            Directory.CreateDirectory("Assets/_Game/Data/Art");AssetDatabase.Refresh();
            ThirdPartyUiThemeSO theme=AssetDatabase.LoadAssetAtPath<ThirdPartyUiThemeSO>(ThemePath);
            if(theme==null){theme=ScriptableObject.CreateInstance<ThirdPartyUiThemeSO>();AssetDatabase.CreateAsset(theme,ThemePath);}
            theme.WorldSpriteMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Prototype/SpriteUnlit.mat");
            theme.DayBackground=Import("白天背景图.jpg",true);theme.NightBackground=Import("夜晚背景图.png",true);
            theme.PortraitNeutral=Import("女主立绘平淡.png");theme.PortraitCalm=Import("女主立绘冷静.png");
            theme.PortraitSmile=Import("女主立绘微笑.png");theme.PortraitAngry=Import("女主立绘生气.png");theme.PortraitCrying=Import("女主立绘哭泣.png");
            theme.ButtonNormal=new Sprite[4];theme.ButtonPressed=new Sprite[4];
            for(int i=0;i<4;i++){theme.ButtonNormal[i]=Import("按钮交互"+(i+1)+".png",false,true);theme.ButtonPressed[i]=Import("按钮按下"+(i+1)+".png",false,true);}
            EditorUtility.SetDirty(theme);
            GameObject root=PrefabUtility.LoadPrefabContents(GamePath);
            try
            {
                GameUiReferences refs=root.GetComponent<GameUiReferences>();
                if(refs==null)throw new InvalidOperationException("UI_Game 缺少 GameUiReferences。");
                refs.ArtTheme=theme;theme.ApplyButtons(root.transform);
                PrefabUtility.SaveAsPrefabAsset(root,GamePath);
                if(refs.MainMenuPanelPrefab!=null)SkinPanel(AssetDatabase.GetAssetPath(refs.MainMenuPanelPrefab),theme);
                if(refs.TutorialPanelPrefab!=null)SkinPanel(AssetDatabase.GetAssetPath(refs.TutorialPanelPrefab),theme);
                if(refs.DayEventPanelPrefab!=null)SkinPanel(AssetDatabase.GetAssetPath(refs.DayEventPanelPrefab),theme);
                if(refs.NightResultPanelPrefab!=null)SkinPanel(AssetDatabase.GetAssetPath(refs.NightResultPanelPrefab),theme);
                if(refs.RunEndPanelPrefab!=null)SkinPanel(AssetDatabase.GetAssetPath(refs.RunEndPanelPrefab),theme);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
            Debug.Log("命名 UI 素材已导入，UI_Game.ArtTheme 与正式面板已绑定。");
        }
        private static void SkinPanel(string path,ThirdPartyUiThemeSO theme)
        {
            if(string.IsNullOrEmpty(path))return;
            GameObject root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                theme.ApplyButtons(root.transform);
                MainMenuPanelReferences menu=root.GetComponent<MainMenuPanelReferences>();if(menu!=null)theme.ApplyMenu(menu);
                TutorialPanelReferences tutorial=root.GetComponent<TutorialPanelReferences>();if(tutorial!=null)theme.ApplyDialogue(tutorial.Root,theme.PortraitCalm);
                DayEventPanelReferences day=root.GetComponent<DayEventPanelReferences>();if(day!=null)theme.ApplyDialogue(day.Root,theme.PortraitNeutral);
                NightResultPanelReferences night=root.GetComponent<NightResultPanelReferences>();if(night!=null)theme.ApplyDialogue(night.Root,theme.PortraitSmile);
                RunEndPanelReferences end=root.GetComponent<RunEndPanelReferences>();if(end!=null)theme.ApplyDialogue(end.Root,theme.PortraitCrying);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        private static Sprite Import(string name,bool background=false,bool button=false)
        {
            string path=Art+name;
            TextureImporter importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null)throw new InvalidOperationException("缺少命名图片："+path);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=!background;
            importer.mipmapEnabled=false;importer.maxTextureSize=background?4096:2048;
            importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.spriteBorder=button?new Vector4(12,12,12,12):Vector4.zero;
            importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
