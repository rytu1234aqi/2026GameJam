using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Spotlight.Editor
{
    /// <summary>Run N05 loot acceptance and N04 regression in the open editor without changing scenes.</summary>
    [InitializeOnLoad]
    public static class R3BLootTool
    {
        const string Running = "Spotlight.N05.Running";
        static TestRunnerApi runner;
        static R3BLootTool()
        {
            EditorApplication.delayCall += EnsureRunner;
            EditorApplication.update += CheckRequest;
        }
        static void EnsureRunner()
        {
            if (runner != null) return;
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new Results());
        }
        static void CheckRequest()
        {
            if (SessionState.GetBool(Running, false) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            const string request = "QA/R3B/request-N05.txt";
            if (!File.Exists(request)) return;
            File.Delete(request); Run();
        }
        [MenuItem("聚光灯/R3B/验证 N05 元素掉落及 N04 回归")]
        public static void Run()
        {
            if (SessionState.GetBool(Running, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureRunner(); SessionState.SetBool(Running, true);
            runner.Execute(new ExecutionSettings(new Filter { testMode=TestMode.EditMode, testNames=new[] { "Spotlight.Tests.Integration.R3BLootTests", "Spotlight.Tests.Integration.R3BStoryWaveTests" } }));
        }
        sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor tests) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                if (!SessionState.GetBool(Running, false)) return;
                SessionState.SetBool(Running, false);
                Directory.CreateDirectory("QA/R3B");
                File.WriteAllText("QA/R3B/N05-tests.xml", result.ToXml().OuterXml);
            }
        }
    }
}