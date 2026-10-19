using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Spotlight.Contracts;
using Spotlight.Presentation;
using Spotlight.Presentation.Cinematics;

namespace Spotlight.Bootstrap
{
    /// <summary>场景只挂此入口。业务由 ModuleComposition 统一创建，不在场景里查找服务。</summary>
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public PrototypeCatalogSO CatalogAsset;
        public Camera WorldCamera;
        [Tooltip("正式UI预制体；留空继续使用原型界面。")]
        public GameUiReferences UiPrefab;
        public CutsceneSequenceSO OpeningStory;
        public CutscenePanelReferences OpeningCutscenePrefab;
        [Tooltip("留空使用玩家存档目录；自动测试可指定自己的临时目录。")]
        public string SaveDirectoryOverride;
        public ModuleComposition Services { get; private set; }
        private RuntimeWorldRenderer2D worldView;
        private GameView gameView;
        private FeedbackService feedback;
        private ViewPool pool;
        private Guid focusPause;
        private string startupError;

        private void Awake()
        {
            try
            {
                if (CatalogAsset == null) throw new InvalidOperationException("缺少 PrototypeCatalog。请执行菜单「聚光灯/生成2D主程工程」，然后打开 00_Bootstrap 场景。");
                string smokeDirectory = StandaloneSmokeRunner.PrepareIsolatedSaveDirectory();
                if (smokeDirectory != null) SaveDirectoryOverride = smokeDirectory;
                string saveDirectory = string.IsNullOrEmpty(SaveDirectoryOverride) ? Path.Combine(Application.persistentDataPath, "Spotlight") : SaveDirectoryOverride;
                Services = new ModuleComposition(CatalogAsset.BuildCatalogData(), saveDirectory);
                Services.Events.OnListenerError = Debug.LogException;
                if (RuntimeQaRecorder.Requested) gameObject.AddComponent<RuntimeQaRecorder>().Bind(Services);
                if (WorldCamera == null) WorldCamera = Camera.main;
                if (WorldCamera == null)
                {
                    GameObject cameraObject = new GameObject("WorldCamera");
                    cameraObject.tag = "MainCamera";
                    WorldCamera = cameraObject.AddComponent<Camera>();
                }
                WorldCamera.orthographic = true;
                WorldCamera.orthographicSize = 5.5f;
                WorldCamera.transform.position = new Vector3(0, 0, -10);
                WorldCamera.backgroundColor = new Color(.07f, .09f, .14f);
                EnsureAudioListener();

                Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
                if (CatalogAsset.PrefabEntries != null)
                    foreach (PrefabEntry entry in CatalogAsset.PrefabEntries)
                        if (entry != null && !string.IsNullOrEmpty(entry.Key) && entry.Prefab != null)
                            prefabs.Add(entry.Key, entry.Prefab);
                pool = new ViewPool(prefabs);
                worldView = gameObject.AddComponent<RuntimeWorldRenderer2D>();
                worldView.ArtTheme = UiPrefab == null ? null : UiPrefab.ArtTheme;
                worldView.WorldCamera = WorldCamera;
                worldView.Bind(Services, pool);
                gameView = gameObject.AddComponent<GameView>();
                gameView.UiPrefab=UiPrefab;
                gameView.OpeningStory=OpeningStory;
                gameView.OpeningCutscenePrefab=OpeningCutscenePrefab;
                gameView.Bind(Services.ViewContext);
                feedback = gameObject.AddComponent<FeedbackService>();
                feedback.Bind(Services.Events, Services.Core.World);
            }
            catch (Exception error)
            {
                startupError = error.Message;
                Debug.LogException(error, this);
                if (Services != null) Services.Dispose();
                Services = null;
            }
        }

        private void Update()
        {
            if (Services != null) Services.Advance(Time.unscaledDeltaTime);
        }

        private void EnsureAudioListener()
        {
            // 尊重已加载场景中的有效监听器，避免生成第二个音频监听入口。
            foreach(AudioListener existing in UnityEngine.Object.FindObjectsOfType<AudioListener>())
                if(existing.enabled&&existing.gameObject.activeInHierarchy)return;
            AudioListener listener=WorldCamera.GetComponent<AudioListener>();
            if(listener==null)listener=WorldCamera.gameObject.AddComponent<AudioListener>();
            listener.enabled=true;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (RuntimeQaRecorder.HasArgument("-spotlightSmoke")) return;
            if (Services == null) return;
            if (!focused && focusPause == Guid.Empty) focusPause = Services.Clock.AcquirePause(PauseReason.FocusLost);
            if (focused && focusPause != Guid.Empty)
            {
                Services.Clock.ReleasePause(focusPause);
                focusPause = Guid.Empty;
            }
        }

        private void OnApplicationQuit()
        {
            if (Services == null || !Services.Flow.GetSnapshot().CanStartNight) return;
            Services.Saves.SaveCheckpoint(Services.Core.Commands.Create(Services.Core.Transactions.Revision), SaveReason.Manual);
        }

        private void OnDestroy()
        {
            if (gameView != null) gameView.Unbind();
            if (feedback != null) feedback.Unbind();
            if (worldView != null) worldView.Unbind();
            if (pool != null) pool.Clear();
            if (Services != null) Services.Dispose();
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(startupError)) return;
            GUI.Box(new Rect(20, 20, Mathf.Max(200, Screen.width - 40), 170), "工程启动失败\n\n" + startupError);
        }
    }
}
