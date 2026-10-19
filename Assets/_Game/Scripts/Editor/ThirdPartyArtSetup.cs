using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Spotlight.Editor
{
    /// <summary>Refresh named source art without touching scenes, catalog data, or existing asset identities.</summary>
    public static class ThirdPartyArtSetup
    {
        [MenuItem("聚光灯/美术/接入ThirdParty命名素材")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play 模式，再接入 ThirdParty 素材。");
            string project=Directory.GetParent(Application.dataPath).FullName;
            string source=Path.Combine(project,"ThirdParty");
            string target=Path.Combine(Application.dataPath,"_Game/Art/ThirdParty");
            if(!Directory.Exists(source))throw new DirectoryNotFoundException("项目根目录缺少 ThirdParty 素材目录："+source);
            Directory.CreateDirectory(target);
            int copied=0;
            foreach(string file in Directory.GetFiles(source,"*",SearchOption.TopDirectoryOnly))
            {
                string name=Path.GetFileName(file);
                string extension=Path.GetExtension(file);
                if(name.StartsWith("游戏UI概念图",StringComparison.Ordinal))continue;
                if(!extension.Equals(".png",StringComparison.OrdinalIgnoreCase)&&!extension.Equals(".jpg",StringComparison.OrdinalIgnoreCase))continue;
                string destination=Path.Combine(target,name);
                if(File.Exists(destination)&&SameContent(file,destination))continue;
                File.Copy(file,destination,true);copied++;
            }
            AssetDatabase.Refresh();
            ThirdPartyUiArtSetup.Apply();
            ThirdPartyActorArtSetup.Apply();
            AssetDatabase.SaveAssets();
            Debug.Log("ThirdParty 命名素材接入完成；更新原图 "+copied+" 张，保留现有资产 GUID。");
        }
        private static bool SameContent(string left,string right)
        {
            if(new FileInfo(left).Length!=new FileInfo(right).Length)return false;
            using(SHA256 hash=SHA256.Create())
            using(FileStream a=File.OpenRead(left))
            using(FileStream b=File.OpenRead(right))
            {
                byte[] x=hash.ComputeHash(a),y=hash.ComputeHash(b);
                for(int i=0;i<x.Length;i++)if(x[i]!=y[i])return false;
                return true;
            }
        }
    }
}
