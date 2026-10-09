using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Spotlight.Presentation;

namespace Spotlight.Editor
{
    /// <summary>Reproducible geometric placeholders. These are NOT final artwork.</summary>
    [InitializeOnLoad]
    public static class R3BEnemyPrefabTool
    {
        public static readonly string[] Names = { "SlimeWater", "SlimeFire", "SlimeThunder", "Sandworm", "Falcon", "Gargoyle", "SoulKing" };
        private const string Art = "Assets/_Game/Art/Enemies/";
        private const string Prefabs = "Assets/_Game/Prefabs/Enemies/";
        private static TestRunnerApi runner;
        static R3BEnemyPrefabTool()
        {
            EditorApplication.update += CheckRequest;
            EditorApplication.delayCall += EnsureRunner;
        }

        private static void EnsureRunner()
        {
            if (runner != null) return;
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new Results());
        }

        private static void CheckRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            const string request = "QA/R3B/request-N02.txt";
            if (!File.Exists(request)) return;
            File.Delete(request);
            Generate();
            EnsureRunner();
            SessionState.SetBool("Spotlight.N02.Running", true);
            runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode,
                testNames = new[] { "Spotlight.Tests.Integration.R3BEnemyPrefabTests.SevenEnemiesRenderRealHealthAndResetOnReuse" } }));
        }

        private sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor tests) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                if (!SessionState.GetBool("Spotlight.N02.Running", false)) return;
                SessionState.SetBool("Spotlight.N02.Running", false);
                File.WriteAllText("QA/R3B/N02-tests.xml", result.ToXml().OuterXml);
                File.WriteAllText("QA/R3B/N02-result.txt", "Passed: " + result.PassCount + "\nFailed: " + result.FailCount + "\n" + result.Message);
            }
        }

        [MenuItem("聚光灯/R3B/生成 N02 敌人占位 Prefab")]
        public static void Generate()
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "ENM_GoblinMelee.prefab");
            if (template == null) throw new InvalidOperationException("Missing canonical enemy prefab");
            foreach (string name in Names)
            {
                string texturePath = Art + "PH_" + name + ".png";
                Texture2D texture = Draw(name);
                File.WriteAllBytes(texturePath, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(texturePath);
                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 128;
                importer.spritePivot = new Vector2(.5f, .5f);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                GameObject view = UnityEngine.Object.Instantiate(template);
                try
                {
                    view.name = "ENM_" + name;
                    view.transform.localPosition = Vector3.zero;
                    view.transform.localRotation = Quaternion.identity;
                    view.transform.localScale = Vector3.one;
                    SpriteRenderer original = view.GetComponent<SpriteRenderer>();
                    GameObject body = new GameObject("Body_Placeholder_ReplaceArt");
                    body.transform.SetParent(view.transform, false);
                    float size = name == "SoulKing" ? 1.35f : name == "Sandworm" ? 1.1f : .9f;
                    body.transform.localScale = new Vector3(size, size, 1);
                    SpriteRenderer sprite = body.AddComponent<SpriteRenderer>();
                    EditorUtility.CopySerialized(original, sprite);
                    sprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
                    sprite.color = Color.white;
                    sprite.sortingOrder = 20;
                    UnityEngine.Object.DestroyImmediate(original);
                    EnemyFacingVisual2D facing = view.GetComponent<EnemyFacingVisual2D>();
                    facing.BodyRenderer = sprite;
                    facing.DefaultFacingRight = true;
                    EnemyHealthBar2D health = view.GetComponent<EnemyHealthBar2D>();
                    health.BarRoot.localPosition = new Vector3(0, size * .5f + .16f, 0);
                    float width = name == "SoulKing" ? 1.4f : .85f;
                    health.BarRoot.localScale = new Vector3(width, 1, 1);
                    PrefabUtility.SaveAsPrefabAsset(view, Prefabs + view.name + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(view); }
            }
            AssetDatabase.SaveAssets();
        }

        // Entire silhouettes, faces and elemental emblems are baked into one body sprite,
        // so the existing single-renderer facing and hit tint cannot affect the health bar.
        private static Texture2D Draw(string name)
        {
            Texture2D t = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            t.SetPixels(new Color[128 * 128]);
            Color ink = new Color(.10f, .12f, .20f), white = new Color(.95f, .98f, 1);
            Action<int,int,int,int,Color> ellipse = (cx,cy,rx,ry,c) => {
                for(int y=Math.Max(0,cy-ry);y<Math.Min(128,cy+ry+1);y++)
                    for(int x=Math.Max(0,cx-rx);x<Math.Min(128,cx+rx+1);x++)
                        if((x-cx)*(x-cx)/(float)(rx*rx)+(y-cy)*(y-cy)/(float)(ry*ry)<=1) t.SetPixel(x,y,c);
            };
            Action<Color,int[]> poly = (c, p) => {
                for(int y=0;y<128;y++) for(int x=0;x<128;x++) {
                    bool inside=false;
                    for(int i=0,j=p.Length-2;i<p.Length;j=i,i+=2)
                        if((p[i+1]>y)!=(p[j+1]>y) && x<(p[j]-p[i])*(y-p[i+1])/(float)(p[j+1]-p[i+1])+p[i]) inside=!inside;
                    if(inside)t.SetPixel(x,y,c);
                }
            };
            if(name.StartsWith("Slime"))
            {
                Color c = name=="SlimeWater" ? new Color(.12f,.66f,.94f) : name=="SlimeFire" ? new Color(.97f,.30f,.14f) : new Color(.65f,.30f,.95f);
                ellipse(64,51,55,39,ink); ellipse(64,52,50,34,c);
                ellipse(40,65,15,8,new Color(1,1,1,.7f));
                if(name=="SlimeWater") { poly(white,new[]{64,116,47,94,48,83,56,76,70,76,79,86,77,96}); }
                else if(name=="SlimeFire") { poly(new Color(1,.83f,.20f),new[]{63,119,43,92,47,77,67,74,83,89,75,107,65,96}); }
                else { poly(new Color(1,.94f,.23f),new[]{65,120,43,94,58,94,51,75,81,103,66,103,78,120}); }
                ellipse(70,49,5,8,ink); ellipse(89,49,5,8,ink);
                poly(ink,new[]{75,32,91,32,84,27});
            }
            else if(name=="Sandworm")
            {
                for(int i=0;i<4;i++) { ellipse(24+i*21,48+i*3,21,25+i,ink); ellipse(24+i*21,50+i*3,16,20+i,new Color(.72f+i*.05f,.44f+i*.03f,.16f)); }
                ellipse(101,63,19,28,ink); ellipse(102,64,14,22,new Color(.96f,.72f,.31f));
                poly(ink,new[]{91,62,118,66,108,45}); poly(white,new[]{99,63,106,62,102,54}); ellipse(102,78,4,5,ink);
                poly(ink,new[]{17,76,24,91,30,76});
            }
            else if(name=="Falcon")
            {
                poly(ink,new[]{6,104,49,84,63,61,77,87,118,108,103,67,82,49,67,15,52,25,41,50,20,67});
                poly(new Color(.78f,.44f,.18f),new[]{12,98,48,78,62,54,78,80,111,101,98,70,78,53,65,23,54,31,45,54,24,71});
                ellipse(78,61,18,21,white); poly(new Color(1,.74f,.13f),new[]{91,64,121,56,90,49}); ellipse(83,67,4,5,ink);
            }
            else if(name=="Gargoyle")
            {
                poly(ink,new[]{8,102,46,79,51,112,64,96,79,113,83,79,119,102,111,44,84,58,85,26,104,12,73,13,64,29,56,13,25,12,43,26,43,58,16,44});
                poly(new Color(.43f,.52f,.56f),new[]{13,94,48,72,55,101,65,89,77,101,80,72,113,94,107,53,79,64,78,29,91,18,75,19,64,40,53,19,35,18,49,29,48,64,20,53});
                ellipse(65,57,18,25,new Color(.63f,.69f,.69f));
                poly(new Color(.35f,1,.77f),new[]{58,79,66,74,60,69}); poly(new Color(.35f,1,.77f),new[]{73,74,84,79,80,69});
            }
            else
            {
                poly(ink,new[]{13,15,30,75,45,88,39,118,57,107,66,124,76,106,98,118,91,84,104,72,116,15,83,25,64,12,42,25});
                poly(new Color(.43f,.20f,.66f),new[]{19,20,36,73,48,82,88,82,99,70,110,20,82,31,64,18,43,31});
                poly(new Color(1,.76f,.19f),new[]{45,112,58,100,66,117,76,100,91,112,86,89,49,89});
                ellipse(67,70,21,21,new Color(.74f,.94f,.95f));
                ellipse(60,72,6,8,ink); ellipse(78,72,6,8,ink); poly(ink,new[]{68,62,63,54,74,54});
                poly(new Color(.23f,.95f,.86f),new[]{64,45,55,36,64,26,73,36});
            }
            t.Apply(); return t;
        }
    }
}
