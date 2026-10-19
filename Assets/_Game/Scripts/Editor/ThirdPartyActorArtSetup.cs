using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Spotlight.Bootstrap;
using Spotlight.Presentation;

namespace Spotlight.Editor
{
    /// <summary>Imports authored irregular regions without rewriting source pixels or gameplay data.</summary>
    public static class ThirdPartyActorArtSetup
    {
        const string Art = "Assets/_Game/Art/ThirdParty/";
        const string Prefabs = "Assets/_Game/Prefabs/";
        const float Ppu = 400;
        static readonly List<string> Manifest = new List<string>();
        sealed class Frame
        {
            public string Name; public RectInt Region; public float Scale;
            public Frame(string name, int x, int y, int w, int h, float scale = 1) { Name=name; Region=new RectInt(x,y,w,h); Scale=scale; }
        }
        static Frame F(string n,int x,int y,int w,int h,float s=1) { return new Frame(n,x,y,w,h,s); }
        [MenuItem("Spotlight/Art/Apply Third Party Actors")]
        public static void Apply()
        {
            Manifest.Clear(); Manifest.Add("file\tframe\tx\ty_from_top\twidth\theight\tscale_multiplier");
            Enemy("GoblinMelee","近战哥布林",.78f,360, new[] { F("idle0",120,150,380,400), F("move0",80,625,285,345,1.13f),F("move1",365,625,300,345,1.13f), F("attack0",105,995,285,345,1.13f),F("attack1",370,995,295,345,1.13f),F("death0",200,1345,350,260,1.13f) });
            Enemy("GoblinRanged","远程哥布林",.8f,380,new[] { F("attack0",265,270,350,410),F("idle0",645,270,375,390),F("move0",70,770,325,350,1.15f),F("move1",485,765,330,355,1.15f),F("attack1",315,1170,285,270,1.4f),F("death0",285,1470,380,210,1.1f) });
            Frame[] slime={F("idle0",125,840,355,270),F("move0",125,840,355,270),F("move1",610,870,425,250)};
            Enemy("SlimeFire","史莱姆",.53f,245,slime,new Color(1,.92f,.88f));
            Enemy("SlimeWater","史莱姆",.53f,245,slime,new Color(.9f,1,1));
            Enemy("SlimeThunder","史莱姆",.53f,245,slime,new Color(1,1,.87f));
            Enemy("Sandworm","沙虫",.84f,450,new[] { F("idle0",555,290,430,480),F("move0",85,940,200,230,2.1f),F("move1",410,950,280,215,2.1f),F("move2",725,960,290,205,2.1f),F("death0",145,1300,355,270,1.7f),F("death1",535,1330,385,230,1.7f) });
            Enemy("Gargoyle","石像鬼",.86f,355,new[] { F("move0",190,435,305,355),F("move1",530,415,325,380),F("idle0",70,1180,325,380),F("attack0",395,1180,310,385),F("attack1",695,1180,305,385) });
            Enemy("Falcon","风隼",.73f,265,new[] { F("idle0",75,130,320,285),F("move0",55,590,330,280),F("move1",400,555,295,320),F("attack0",75,920,290,345,.9f),F("attack1",350,925,300,340,.9f),F("death0",75,1305,295,305),F("death1",425,1370,470,225) });
            string wraithPath=Prefabs+"Enemies/ENM_Wraith.prefab";
            if(AssetDatabase.LoadAssetAtPath<GameObject>(wraithPath)==null) AssetDatabase.CopyAsset(Prefabs+"Enemies/ENM_GoblinRanged.prefab",wraithPath);
            Enemy("Wraith","怨灵",.83f,490,new[] { F("idle0",495,455,405,515),F("attack0",110,1060,360,345,1.5f),F("death0",560,1105,475,275,1.2f) });
            Enemy("SoulKing","蚀魂王",1.25f,665,null);
            StaticPrefab("Towers/TWR_Basic.prefab","防御塔",.95f,false,Color.white);
            StaticPrefab("Projectiles/PRJ_Basic.prefab","子弹",.45f,true,Color.white);
            StaticPrefab("Projectiles/PRJ_Enemy.prefab","子弹",.4f,true,new Color(1,.8f,1));
            var wraith=AssetDatabase.LoadAssetAtPath<GameObject>(wraithPath);
            foreach(string guid in AssetDatabase.FindAssets("t:PrototypeCatalogSO",new[]{"Assets/_Game/Data"}))
            {
                var catalog=AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(AssetDatabase.GUIDToAssetPath(guid));
                var list=new List<PrefabEntry>(catalog.PrefabEntries ?? new PrefabEntry[0]);
                var entry=list.Find(e=>e!=null && e.Key=="EnemyWraith");
                if(entry==null) list.Add(new PrefabEntry{Key="EnemyWraith",Prefab=wraith}); else entry.Prefab=wraith;
                catalog.PrefabEntries=list.ToArray(); EditorUtility.SetDirty(catalog);
            }
            Directory.CreateDirectory("QA/R3B/ThirdPartyArt"); File.WriteAllLines("QA/R3B/ThirdPartyArt/actor-slice-manifest.tsv",Manifest);
            AssetDatabase.SaveAssets();
        }
        static Sprite[] Import(string file, Frame[] frames, bool centered = false)
        {
            string path=Art+file+".png";
            var raw=new Texture2D(2,2,TextureFormat.RGBA32,false); raw.LoadImage(File.ReadAllBytes(path));
            var pixels=raw.GetPixels32(); int width=raw.width,height=raw.height;
            if(frames==null) frames=new[]{file=="石像鬼" ? F("idle0",70,600,950,860) : F("idle0",0,0,width,height)};
            var metadata=new List<SpriteMetaData>();
            foreach(var frame in frames)
            {
                RectInt r=Tighten(pixels,width,height,frame.Region);
                metadata.Add(new SpriteMetaData {name=frame.Name,rect=new Rect(r.x,height-r.y-r.height,r.width,r.height),alignment=(int)SpriteAlignment.Custom,pivot=new Vector2(.5f,centered ? .5f : 0)});
                Manifest.Add(file+".png\t"+frame.Name+"\t"+r.x+"\t"+r.y+"\t"+r.width+"\t"+r.height+"\t"+frame.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            UnityEngine.Object.DestroyImmediate(raw);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit=Ppu; importer.maxTextureSize=2048; importer.mipmapEnabled=false; importer.sRGBTexture=true; importer.alphaIsTransparency=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed; importer.filterMode=FilterMode.Bilinear;
#pragma warning disable 618
            importer.spritesheet=metadata.ToArray();
#pragma warning restore 618
            importer.SaveAndReimport();
            var sprites=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
            return frames.Select(f=>sprites.First(s=>s.name==f.Name)).ToArray();
        }
        // A pixel must have opaque neighbours; isolated export speckles cannot enlarge a frame.
        static RectInt Tighten(Color32[] p,int w,int h,RectInt area)
        {
            int x0=w,y0=h,x1=-1,y1=-1;
            for(int y=Math.Max(1,area.y);y<Math.Min(h-1,area.yMax);y++) for(int x=Math.Max(1,area.x);x<Math.Min(w-1,area.xMax);x++)
            {
                int index=(h-1-y)*w+x; if(p[index].a<40)continue; int neighbours=0;
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)if(p[index+dy*w+dx].a>=40)neighbours++;
                if(neighbours<7)continue;
                x0=Math.Min(x0,x);x1=Math.Max(x1,x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);
            }
            if(x1<x0)throw new InvalidOperationException("Empty art region "+area);
            x0=Math.Max(area.x,x0-2);y0=Math.Max(area.y,y0-2);x1=Math.Min(area.xMax-1,x1+2);y1=Math.Min(area.yMax-1,y1+2);
            return new RectInt(x0,y0,x1-x0+1,y1-y0+1);
        }
        static Material Unlit()
        {
            const string path="Assets/_Game/Art/ThirdParty/ActorSpriteUnlit.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){var shader=Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");if(shader==null)shader=Shader.Find("Sprites/Default");material=new Material(shader);AssetDatabase.CreateAsset(material,path);}return material;
        }
        static void Enemy(string key,string file,float worldHeight,float referenceHeight,Frame[] frames,Color? tint=null)
        {
            Sprite[] fallback=Import(file,null); Sprite[] sprites=frames==null?fallback:Import(file+"动作",frames);
            string path=Prefabs+"Enemies/ENM_"+key+".prefab"; var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var legacy=root.GetComponent<SpriteRenderer>();
                var bodyTransform=root.transform.Find("ThirdPartyBody");
                if(bodyTransform==null){bodyTransform=new GameObject("ThirdPartyBody").transform;bodyTransform.SetParent(root.transform,false);}
                var body=bodyTransform.GetComponent<SpriteRenderer>();if(body==null)body=bodyTransform.gameObject.AddComponent<SpriteRenderer>();
                body.sprite=sprites[Array.FindIndex(frames ?? new[]{F("idle0",0,0,1,1)},f=>f.Name.StartsWith("idle"))];
                body.sharedMaterial=Unlit();body.color=tint ?? Color.white;body.sortingOrder=10;
                float scale=worldHeight*Ppu/(referenceHeight*Mathf.Abs(root.transform.localScale.y));
                bodyTransform.localScale=Vector3.one*scale;
                bodyTransform.localPosition=new Vector3(0,-worldHeight*.45f/Mathf.Abs(root.transform.localScale.y),0);
                if(legacy!=null){legacy.sprite=body.sprite;legacy.color=Color.clear;legacy.enabled=false;legacy.sharedMaterial=Unlit();}
                var facing=root.GetComponent<EnemyFacingVisual2D>();
                if(facing!=null){var oldBody=facing.BodyRenderer;if(oldBody!=null && oldBody!=body)oldBody.enabled=false;facing.BodyRenderer=body;facing.DefaultFacingRight=false;}
                foreach(var oldRenderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                    if(oldRenderer!=body && oldRenderer.name.StartsWith("Body_Placeholder",StringComparison.Ordinal))oldRenderer.enabled=false;
                var flash=root.GetComponent<HitFlashVisual2D>();if(flash!=null)flash.BodyRenderer=body;
                var bar=root.GetComponent<EnemyHealthBar2D>();
                if(bar!=null && bar.BarRoot!=null)
                {
                    float maxFrameTop=0;
                    for(int i=0;i<sprites.Length;i++)maxFrameTop=Mathf.Max(maxFrameTop,sprites[i].bounds.max.y*scale*(frames==null?1:frames[i].Scale));
                    float top=bodyTransform.localPosition.y+maxFrameTop;
                    bar.BarRoot.localPosition=new Vector3(0,top+.13f/Mathf.Abs(root.transform.localScale.y),0);
                }
                var animation=root.GetComponent<EnemySpriteAnimator2D>();if(animation==null)animation=root.AddComponent<EnemySpriteAnimator2D>(); animation.BodyRenderer=body;
                animation.IdleFrames=Group(frames,sprites,"idle");animation.MoveFrames=Group(frames,sprites,"move");animation.AttackFrames=Group(frames,sprites,"attack");animation.DeathFrames=Group(frames,sprites,"death");
                animation.FramesPerSecond=5;animation.AttackSeconds=.4f;animation.DeathSeconds=.65f;
                var serialized=new SerializedObject(animation);foreach(string state in new[]{"Idle","Move","Attack","Death"})
                {
                    var field=serialized.FindProperty(state+"Scales");if(field==null)continue;
                    var scales=frames==null?new[]{1f}:frames.Where(f=>f.Name.StartsWith(state.ToLowerInvariant())).Select(f=>f.Scale).ToArray();
                    field.arraySize=scales.Length;for(int i=0;i<scales.Length;i++)field.GetArrayElementAtIndex(i).floatValue=scales[i];
                }serialized.ApplyModifiedPropertiesWithoutUndo();
                if(key.StartsWith("Slime",StringComparison.Ordinal))ApplyElementMark(root,key.Substring(5),worldHeight);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        static Sprite[] Group(Frame[] f,Sprite[] sprites,string prefix){return f==null?(prefix=="idle"?sprites:new Sprite[0]):f.Select((v,i)=>new{v,i}).Where(x=>x.v.Name.StartsWith(prefix)).Select(x=>sprites[x.i]).ToArray();}
        static void ApplyElementMark(GameObject root,string element,float worldHeight)
        {
            var sourcePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+"Elements/Element"+element+".prefab");
            if(sourcePrefab==null)throw new InvalidOperationException("Missing slime element marker source: "+element);
            var source=sourcePrefab.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(r=>r.sprite!=null && r.enabled);
            if(source==null)throw new InvalidOperationException("Missing slime element sprite: "+element);
            Transform marker=root.transform.Find("ElementMark");
            if(marker==null){marker=new GameObject("ElementMark").transform;marker.SetParent(root.transform,false);}
            var renderer=marker.GetComponent<SpriteRenderer>();if(renderer==null)renderer=marker.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite=source.sprite;renderer.color=source.color;renderer.sharedMaterial=Unlit();renderer.sortingOrder=21;renderer.enabled=true;
            float rootScale=Mathf.Abs(root.transform.localScale.x);
            float size=.18f/(Mathf.Max(source.sprite.bounds.size.x,source.sprite.bounds.size.y)*rootScale);
            marker.localScale=Vector3.one*size;
            marker.localPosition=new Vector3(worldHeight*.48f/rootScale,-worldHeight*.25f/Mathf.Abs(root.transform.localScale.y),0);
        }
        static void StaticPrefab(string relative,string file,float size,bool width,Color tint)
        {
            var sprite=Import(file,null,true)[0];string path=Prefabs+relative;var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var renderer=root.GetComponent<SpriteRenderer>();if(renderer==null)renderer=root.AddComponent<SpriteRenderer>();
                renderer.sprite=sprite;renderer.sharedMaterial=Unlit();
                if(width)
                {
                    var visual=root.GetComponent<ProjectileVisual2D>();
                    if(visual==null || visual.VisualRoot==null)throw new InvalidOperationException("Missing projectile VisualRoot: "+relative);
                    var direction=visual.VisualRoot.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(r=>r!=visual.ElementOverlay && r.name=="Direction");
                    if(direction==null)throw new InvalidOperationException("Missing projectile Direction sprite: "+relative);
                    direction.sprite=sprite;direction.sharedMaterial=Unlit();direction.color=tint;direction.enabled=true;
                    // Direction remains inside the existing rotating VisualRoot. Parent scale is measured independently
                    // of the previous body's scale, so repeated imports cannot accumulate resizing.
                    Vector3 parentScale=direction.transform.parent.lossyScale;
                    float factor=size/(sprite.bounds.size.x*Mathf.Abs(parentScale.x));
                    direction.transform.localScale=new Vector3(factor,factor,1);
                    renderer.enabled=false;renderer.color=Color.clear;
                }
                else
                {
                    renderer.enabled=true;renderer.color=tint;renderer.sortingOrder=5;
                    root.transform.localScale=Vector3.one*(size/sprite.bounds.size.y);
                }
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }finally{PrefabUtility.UnloadPrefabContents(root);}
        }
    }
}
