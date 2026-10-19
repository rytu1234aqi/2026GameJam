using System;
using System.Collections.Generic;
using Spotlight.Contracts;
using Spotlight.Presentation.Cinematics;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace Spotlight.Presentation
{
    public sealed class GameView : MonoBehaviour, IGameView
    {
        public string defaultLevelId = "level.prototype", towerId = "tower.basic";
        public uint seed = 1;
        public bool PreviewOnly;
        public GameUiReferences UiPrefab;
        [Tooltip("故事模式开场；留空保留直接开局。")]
        public CutsceneSequenceSO OpeningStory;
        public CutscenePanelReferences OpeningCutscenePrefab;
        public CutscenePlayer OpeningPlayer { get; private set; }
        public bool IsOpeningPlaying { get { return openingActive; } }
        private bool openingActive;
        private Guid openingPause;
        private GameObject openingObject;
        private GameViewContext services;
        private Canvas canvas;
        private Transform panel, actions, overlay;
        private Font font;
        private Text title, health, wave, skill, gold, note, selection;
        private readonly Text[] hands = new Text[6];
        private readonly Text[] items = new Text[2];
        private readonly List<IDisposable> subscriptions = new List<IDisposable>();
        private readonly HashSet<Guid> pending = new HashSet<Guid>();
        private readonly string[] ids = { "elm.water", "elm.fire", "elm.earth", "elm.wood", "elm.wind", "elm.thunder" };
        private readonly string[] names = { "水", "火", "土", "木", "风", "雷" };
        private readonly string[] itemIds = { "item.heal", "item.tower_stack" };
        private Guid itemPause, playerPause;
        private GameObject ownedEvents;
        private string placing, selectedItem;
        private CellCoord cell;
        private bool selected, move, swap, blocked;
        private float statusRefreshTimer;
        private GamePhase renderedPhase = (GamePhase)(-1);
        private GameUiReferences ui;
        private GameObject generatedUi;
        private string summaryRunId, productionSummary;
        private sealed class ButtonBinding { internal Button Button; internal Button.ButtonClickedEvent Event; internal UnityAction Listener; }
        private readonly List<ButtonBinding> buttonBindings = new List<ButtonBinding>();
        private string modalKey;
        private string modalRunId;
        private Guid modalPending;
        private bool modalSubmitting;
        private readonly List<Button> modalButtons = new List<Button>();
        private Text modalFeedback;
        private Guid resultPending;
        private bool resultSubmitting;
        private Text resultFeedback;
        private readonly List<Button> resultButtons = new List<Button>();

        public void Bind(GameViewContext context)
        {
            if (context == null) throw new ArgumentNullException("context");
            Unbind(); services = context;
            try
            {
            BuildUi();
            subscriptions.Add(context.Events.Subscribe<CommandFinishedEvent>(delegate(CommandFinishedEvent e) {
                if(e.Result.CommandId==resultPending){resultPending=Guid.Empty;SetResultInteractable(true);}
                if(e.Result.CommandId==modalPending){modalPending=Guid.Empty;SetModalInteractable(true);}
                if (pending.Remove(e.Result.CommandId)) {Result(e.Result);RefreshAll();}
            }));
            subscriptions.Add(context.Events.Subscribe<PhaseChangedEvent>(delegate(PhaseChangedEvent e) { CancelSelection(); ReleasePlayerPause(); RefreshAll(); }));
            subscriptions.Add(context.Events.Subscribe<BoardChangedEvent>(delegate(BoardChangedEvent e) { RefreshAll(); }));
            subscriptions.Add(context.Events.Subscribe<ResourcesChangedEvent>(delegate(ResourcesChangedEvent e) { RefreshAll(); }));
            subscriptions.Add(context.Events.Subscribe<HealthChangedEvent>(delegate(HealthChangedEvent e) { RefreshStatus(); }));
            subscriptions.Add(context.Events.Subscribe<WaveChangedEvent>(delegate(WaveChangedEvent e) { RefreshStatus(); }));
            subscriptions.Add(context.Events.Subscribe<SkillChangedEvent>(delegate(SkillChangedEvent e) { RefreshStatus(); }));
            subscriptions.Add(context.Events.Subscribe<DayEventChangedEvent>(delegate(DayEventChangedEvent e) { RefreshAll(); }));
            subscriptions.Add(context.Events.Subscribe<ClockChangedEvent>(delegate(ClockChangedEvent e) { RefreshStatus(); }));
            subscriptions.Add(context.Events.Subscribe<DawnSettledEvent>(delegate(DawnSettledEvent e) {
                summaryRunId=services.Flow.GetSnapshot().RunId;
                productionSummary=ProductionSummaryFormatter.Format(e.Rewards);RefreshStatus();
            }));
            RefreshAll();
            }
            catch { Unbind(); throw; }
        }
        public void Unbind()
        {
            CancelOpening();
            CancelSelection(); ReleasePlayerPause();
            foreach (IDisposable s in subscriptions) s.Dispose(); subscriptions.Clear(); pending.Clear(); services = null;
            foreach(ButtonBinding binding in buttonBindings)binding.Event.RemoveListener(binding.Listener);
            buttonBindings.Clear();
            if(generatedUi!=null){generatedUi.SetActive(false);PresentationObjects.Release(generatedUi);}generatedUi=null;ui=null;
            if(ownedEvents!=null){ownedEvents.SetActive(false);PresentationObjects.Release(ownedEvents);}ownedEvents=null;
            canvas = null; overlay = panel = actions = null;
            title=health=wave=skill=gold=note=selection=null;
            Array.Clear(hands,0,hands.Length);Array.Clear(items,0,items.Length);
            selected=false;blocked=false;cell=new CellCoord();statusRefreshTimer=0;
            summaryRunId=null;productionSummary=null;
            renderedPhase = (GamePhase)(-1);
            modalKey=null;modalRunId=null;modalPending=Guid.Empty;modalSubmitting=false;modalButtons.Clear();modalFeedback=null;
            resultPending=Guid.Empty;resultSubmitting=false;resultFeedback=null;resultButtons.Clear();
        }
        private void Start() { if (PreviewOnly && services == null) TestWidgets(); }
        private void OnDestroy() { Unbind(); }
        private void OnDisable() { if(openingActive){CancelOpening();RefreshControlStates();} }
        private CommandContext Command() { return services.Commands.Create(services.Flow.GetSnapshot().Revision); }
        private void Result(OperationResult result)
        {
            if (result.State == OperationState.Queued) { pending.Add(result.CommandId); note.text = "正在处理…";if(modalFeedback!=null)modalFeedback.text=note.text;RefreshControlStates(); }
            else if (result.State == OperationState.Committed) { note.text = "完成"; RefreshAll(); }
            else {note.text = ErrorText(result.Error);if(modalFeedback!=null)modalFeedback.text=note.text;}
            if(resultFeedback!=null&&result.State!=OperationState.Committed)resultFeedback.text=note.text;
        }
        private static string ErrorText(ErrorCode e)
        {
            switch (e) {
                case ErrorCode.InsufficientCost: case ErrorCode.InsufficientQuantity: return "数量不足";
                case ErrorCode.Occupied: return "这个格子已被占用";
                case ErrorCode.ReservedCell: case ErrorCode.InvalidPathPlacement: return "这里需要留作通道";
                case ErrorCode.WrongPhase: return "现在不能进行此操作";
                case ErrorCode.InvalidTarget: case ErrorCode.TargetDead: return "请选择有效目标";
                case ErrorCode.SkillAlreadyUsed: return "本夜技能已使用";
                case ErrorCode.SaveUnavailable: return "没有可继续的记录";
                case ErrorCode.SaveCorrupt: return "存档损坏，无法继续";
                case ErrorCode.SaveVersionUnsupported: return "存档版本不受支持，无法继续";
                case ErrorCode.ConfigMismatch: return "存档与当前内容配置不匹配，无法继续";
                case ErrorCode.SaveIoFailure: return "无法读取存档，请检查存档文件";
                case ErrorCode.SaveBusy: return "存档正在读写，请稍后重试";
                case ErrorCode.InvalidDefinition: return "此内容尚未配置";
                case ErrorCode.EffectAlreadyActive: return "同类效果正在生效";
                case ErrorCode.VersionConflict: return "状态已变化，请重试";
                case ErrorCode.LimitReached: return "已达到数量上限";
                default: return "操作未完成，请重试";
            }
        }
        public void RefreshAll()
        {
            if (services == null || canvas == null) return; RefreshStatus();
            for (int i = 0; i < 6; i++) hands[i].text = names[i] + " × " + services.Resources.GetQuantity(ResourceBucket.Hand, ids[i]);
            items[0].text = "回复药剂 × " + services.Resources.GetQuantity(ResourceBucket.Inventory, itemIds[0]);
            items[1].text = "塔强化 × " + services.Resources.GetQuantity(ResourceBucket.Inventory, itemIds[1]);
            GamePhase phase = services.Flow.GetSnapshot().Phase;
            if (renderedPhase != phase) { renderedPhase = phase; DrawActions(phase); }
            DrawModal();
            RefreshControlStates();
        }
        private void RefreshControlStates()
        {
            if(ui==null||services==null)return;GamePhase phase=services.Flow.GetSnapshot().Phase;bool ready=pending.Count==0&&!openingActive;
            for(int i=0;i<ids.Length;i++){
                ui.ElementButtons[i].interactable=ready&&phase==GamePhase.Build&&services.Resources.GetQuantity(ResourceBucket.Hand,ids[i])>0;
                ColorBlock colors=ui.ElementButtons[i].colors;colors.normalColor=placing==ids[i]?new Color(0.65f,0.9f,1):Color.white;ui.ElementButtons[i].colors=colors;ApplyArtState(ui.ElementButtons[i],placing==ids[i]);
            }
            for(int i=0;i<itemIds.Length;i++)ui.ItemButtons[i].interactable=ready&&(phase==GamePhase.Build||phase==GamePhase.Night)&&services.Resources.GetQuantity(ResourceBucket.Inventory,itemIds[i])>0;
            ui.CancelSelectionButton.interactable=ready;ui.PauseButton.interactable=ready&&(phase==GamePhase.Build||phase==GamePhase.Night);ui.NormalSpeedButton.interactable=ready;ui.DoubleSpeedButton.interactable=ready;
            foreach(ButtonBinding binding in buttonBindings)if(binding.Button!=null&&actions!=null&&binding.Button.transform.IsChildOf(actions)){
                MainMenuPanelReferences menu=binding.Button.GetComponentInParent<MainMenuPanelReferences>();RunEndPanelReferences end=binding.Button.GetComponentInParent<RunEndPanelReferences>();
                bool available=true;if((menu!=null&&binding.Button==menu.ContinueButton)||(end!=null&&binding.Button==end.RetryButton)){SaveReadResult save=services.Saves.ReadCheckpoint();available=save.Error==ErrorCode.None&&save.Data!=null;}
                binding.Button.interactable=ready&&available;
            }
            foreach(ButtonBinding binding in buttonBindings)if(binding.Button!=null&&Array.IndexOf(ui.ElementButtons,binding.Button)<0)ApplyArtState(binding.Button);
        }
        private void RefreshStatus()
        {
            if (services == null || title == null) return; FlowSnapshot f = services.Flow.GetSnapshot();
            if(summaryRunId!=f.RunId||f.Phase==GamePhase.Menu){summaryRunId=f.RunId;productionSummary=null;}
            if(ui!=null&&ui.ProductionSummaryText!=null)
                ui.ProductionSummaryText.text=f.Phase==GamePhase.Menu?"":productionSummary??"上夜生产：暂无记录";
            title.text = ui!=null&&ui.DayText!=null&&ui.PhaseText!=null?"聚光灯":"聚光灯 · 第 " + f.DayIndex + " 天 · " + PhaseName(f.Phase);
            ActorSnapshot spring; health.text = services.World.TryGetActor(services.World.SpringId, out spring) ? "灵泉 " + Mathf.CeilToInt(spring.CurrentHp) + " / " + Mathf.CeilToInt(spring.MaxHp) : "守护灵泉";
            WaveSnapshot w = services.Waves.GetSnapshot(); wave.text = "波次 " + w.WaveIndex + " · 敌人 " + w.AliveCount;
            SpecialSkillSnapshot s = services.Skill.GetSnapshot(); skill.text = "技能 " + s.UsesRemaining + " 次" + (s.RemainingSeconds > 0 ? " · 强化 " + Mathf.CeilToInt(s.RemainingSeconds) + " 秒" : "");
            gold.text = "金币 " + services.Resources.GetQuantity(ResourceBucket.Inventory, "res.gold");
            if(ui!=null)
            {
                NightResultPanelReferences night=ui.ActionsRoot.GetComponentInChildren<NightResultPanelReferences>();if(night!=null)SetResultSummary(night.SummaryText);
                RunEndPanelReferences end=ui.ActionsRoot.GetComponentInChildren<RunEndPanelReferences>();if(end!=null)SetResultSummary(end.SummaryText);
                ClockSnapshot clock=services.Clock.GetSnapshot();
                if(ui.DayText!=null)ui.DayText.text="第 "+f.DayIndex+" 天";
                if(ui.PhaseText!=null)ui.PhaseText.text=PhaseName(f.Phase);
                if(ui.SpeedText!=null)ui.SpeedText.text=(int)clock.Speed+" 倍"+(clock.IsPaused?" · 已暂停":"");
                if(ui.PauseButtonText!=null)ui.PauseButtonText.text=playerPause!=Guid.Empty?"继续":"暂停";
                if(ui.SpringHpBar!=null)
                {
                    ui.SpringHpBar.minValue=0;ui.SpringHpBar.maxValue=spring==null?1:Mathf.Max(1,spring.MaxHp);
                    ui.SpringHpBar.SetValueWithoutNotify(spring==null?0:Mathf.Clamp(spring.CurrentHp,0,spring.MaxHp));
                }
            }
        }
        private static string PhaseName(GamePhase p) { switch(p) { case GamePhase.Menu:return "开始旅程"; case GamePhase.Build:return "白天部署"; case GamePhase.Night:return "夜晚守护"; case GamePhase.DayEvent:return "今日事件"; case GamePhase.NightResult:return "守护成功"; case GamePhase.Ending:return "旅程完成"; case GamePhase.GameOver:return "灵泉失守"; default:return "迎接黎明"; } }
        private void DrawActions(GamePhase phase)
        {
            resultButtons.Clear();resultFeedback=null;
            ReleaseContainer(actions); actions = Rect(ActionMount, "Actions", 0, 0, 1020, 720);
            if(ui!=null)ui.ActionsRoot.SetSiblingIndex(ui.ModalRoot.GetSiblingIndex());
            if(phase==GamePhase.Menu||phase==GamePhase.NightResult||phase==GamePhase.GameOver||phase==GamePhase.Ending)((RectTransform)actions).sizeDelta=new Vector2(1280,720);
            int index = 0;
            if (phase == GamePhase.Menu && ui!=null && ui.MainMenuPanelPrefab!=null)
            {
                string error;MainMenuPanelReferences prefab=ui.MainMenuPanelPrefab;
                if(!prefab.Validate(out error))throw new InvalidOperationException(error);
                MainMenuPanelReferences menu=Instantiate(prefab,actions,false);menu.gameObject.SetActive(true);if(ui.ArtTheme!=null)ui.ArtTheme.ApplyMenu(menu);
                SaveReadResult checkpoint=services.Saves.ReadCheckpoint();
                bool available=checkpoint.Error==ErrorCode.None&&checkpoint.Data!=null;
                menu.ContinueButton.interactable=available;ApplyArtState(menu.ContinueButton);
                menu.HintText.text=available?"选择故事或无尽模式，或继续上次旅程。":CheckpointHint(checkpoint);
                BindButton(menu.StoryButton,delegate {NewRun(GameMode.Story);});
                BindButton(menu.EndlessButton,delegate {NewRun(GameMode.Endless);});
                BindButton(menu.ContinueButton,delegate {if(menu.ContinueButton.interactable)Load();});
            }
            else if (phase == GamePhase.Menu) { ActionButton("故事模式", index++, delegate { NewRun(GameMode.Story); }); ActionButton("无尽模式", index++, delegate { NewRun(GameMode.Endless); }); ActionButton("继续游戏", index++, Load); }
            if (phase == GamePhase.Build) {
                ActionButton("开始这一夜", index++, delegate { Result(services.Flow.TryStartNight(Command())); });
                ActionButton("保存进度", index++, delegate { Result(services.Saves.SaveCheckpoint(Command(), SaveReason.Manual)); });
                ActionButton("建造攻击塔", index++, delegate { Place(towerId); });
                ActionButton("移动所选", index++, delegate { ChooseMove(false); }); ActionButton("交换所选", index++, delegate { ChooseMove(true); });
                ActionButton("回收所选", index++, delegate { if (selected) Result(services.Deployment.TryRecall(Command(), cell)); else note.text = "先点选元素或塔"; });
                ActionButton("返回菜单", index++, Menu);
            }
            if (phase == GamePhase.Night) { ActionButton("释放特殊技", index++, delegate { Result(services.Skill.TryUse(Command())); }); ActionButton("返回菜单", index++, Menu); }
            if(phase==GamePhase.NightResult&&ui!=null&&ui.NightResultPanelPrefab!=null){DrawNightResult();}
            else if (phase == GamePhase.NightResult) {
                EndlessDefinition endless=services.Catalog.GetEndlessDefinition();bool milestone=services.Flow.GetSnapshot().Mode==GameMode.Endless&&endless!=null&&services.Flow.GetSnapshot().DayIndex==endless.MilestoneNight;
                if(milestone){ActionButton("继续无尽挑战",index++,delegate {Result(services.Flow.TryContinueEndless(Command()));});note.text="已完成无尽里程碑！继续挑战可领取本次里程碑奖励。";}
                else ActionButton("迎接下一天",index++,delegate {Result(services.Flow.TryConfirmNightResult(Command()));});
            }
            if((phase==GamePhase.GameOver||phase==GamePhase.Ending)&&ui!=null&&ui.RunEndPanelPrefab!=null)DrawRunEnd(phase);
            else {
                if (phase == GamePhase.GameOver) { ActionButton("从记录重试", index++, Load); ActionButton("返回菜单", index++, Menu); }
                if (phase == GamePhase.Ending) { ActionButton("返回菜单", index++, Menu); if (services.Flow.GetSnapshot().Mode == GameMode.Endless) ActionButton("继续无尽挑战", index++, delegate { Result(services.Flow.TryContinueEndless(Command())); }); }
            }
        }
        private bool IsEndlessMilestone()
        {
            FlowSnapshot flow=services.Flow.GetSnapshot();EndlessDefinition endless=services.Catalog.GetEndlessDefinition();
            return flow.Mode==GameMode.Endless&&endless!=null&&flow.DayIndex==endless.MilestoneNight;
        }
        private string ResultSummary()
        {
            FlowSnapshot flow=services.Flow.GetSnapshot();string text="第 "+flow.DayIndex+" 夜 · "+PhaseName(flow.Phase)+"\n当前金币："+services.Resources.GetQuantity(ResourceBucket.Inventory,"res.gold");
            for(int i=0;i<ids.Length;i++)text+="\n"+names[i]+"手牌："+services.Resources.GetQuantity(ResourceBucket.Hand,ids[i]);
            return text+"\n回复药剂："+services.Resources.GetQuantity(ResourceBucket.Inventory,itemIds[0])+"\n塔强化："+services.Resources.GetQuantity(ResourceBucket.Inventory,itemIds[1]);
        }
        private void DrawNightResult()
        {
            string error;if(!ui.NightResultPanelPrefab.Validate(out error))throw new InvalidOperationException(error);
            NightResultPanelReferences instance=Instantiate(ui.NightResultPanelPrefab,actions,false);instance.gameObject.SetActive(true);
            if(ui.ArtTheme!=null)ui.ArtTheme.ApplyDialogue(instance.Root,ui.ArtTheme.PortraitSmile);
            instance.TitleText.text="守护成功";SetResultSummary(instance.SummaryText);resultFeedback=instance.HintText;
            bool milestone=IsEndlessMilestone();instance.HintText.text=milestone?"已完成无尽里程碑，继续挑战可领取里程碑奖励。":"确认后迎接下一天，黎明生产以实际结算为准。";
            instance.NextDayButton.GetComponentInChildren<Text>(true).text=milestone?"继续无尽挑战":"迎接下一天";
            instance.ReturnMenuButton.GetComponentInChildren<Text>(true).text="返回菜单";
            BindResultButton(instance.NextDayButton,delegate {return milestone?services.Flow.TryContinueEndless(Command()):services.Flow.TryConfirmNightResult(Command());});
            BindResultButton(instance.ReturnMenuButton,delegate {return services.Flow.TryReturnToMenu(Command());});
        }
        private void DrawRunEnd(GamePhase phase)
        {
            string error;if(!ui.RunEndPanelPrefab.Validate(out error))throw new InvalidOperationException(error);
            RunEndPanelReferences instance=Instantiate(ui.RunEndPanelPrefab,actions,false);instance.gameObject.SetActive(true);
            if(ui.ArtTheme!=null)ui.ArtTheme.ApplyDialogue(instance.Root,phase==GamePhase.GameOver?ui.ArtTheme.PortraitCrying:ui.ArtTheme.PortraitSmile);
            instance.TitleText.text=PhaseName(phase);SetResultSummary(instance.SummaryText);resultFeedback=instance.HintText;
            instance.RetryButton.GetComponentInChildren<Text>(true).text="从记录重试";instance.ReturnMenuButton.GetComponentInChildren<Text>(true).text="返回菜单";instance.ContinueEndlessButton.GetComponentInChildren<Text>(true).text="继续无尽挑战";
            instance.RetryButton.gameObject.SetActive(phase==GamePhase.GameOver);
            instance.ContinueEndlessButton.gameObject.SetActive(false); // Production flow continues endless only from the milestone NightResult.
            SaveReadResult save=services.Saves.ReadCheckpoint();bool retry=save.Error==ErrorCode.None&&save.Data!=null;
            instance.HintText.text=phase==GamePhase.GameOver?(retry?"可以从最近记录重新守护灵泉。":CheckpointHint(save)):"感谢你守护灵泉。";
            BindResultButton(instance.RetryButton,delegate {
                SaveReadResult checkpoint=services.Saves.ReadCheckpoint();
                if(checkpoint.Error!=ErrorCode.None||checkpoint.Data==null)return new OperationResult(OperationState.Rejected,checkpoint.Error==ErrorCode.None?ErrorCode.SaveUnavailable:checkpoint.Error,Guid.Empty,services.Flow.GetSnapshot().Revision,"ui.retry.checkpoint");
                OperationResult loaded=services.Flow.TryLoadRun(Command(),checkpoint.Data);if(loaded.State==OperationState.Committed)productionSummary=null;return loaded;
            });
            instance.RetryButton.interactable=retry;ApplyArtState(instance.RetryButton);
            BindResultButton(instance.ReturnMenuButton,delegate {return services.Flow.TryReturnToMenu(Command());});
            BindResultButton(instance.ContinueEndlessButton,delegate {return services.Flow.TryContinueEndless(Command());});
        }
        private void BindResultButton(Button button,Func<OperationResult> command)
        {resultButtons.Add(button);BindButton(button,delegate {SubmitResult(command);});}
        private void SetResultSummary(Text text)
        {
            text.text=ResultSummary();float height=Mathf.Max(310,text.preferredHeight+24);
            text.rectTransform.sizeDelta=new Vector2(700,height);
            ScrollRect scroll=text.GetComponentInParent<ScrollRect>();
            if(scroll!=null){scroll.content.sizeDelta=new Vector2(scroll.content.sizeDelta.x,height+24);scroll.verticalNormalizedPosition=1;}
        }
        private void SubmitResult(Func<OperationResult> command)
        {
            if(services==null||resultSubmitting||resultPending!=Guid.Empty)return;
            resultSubmitting=true;SetResultInteractable(false);
            try{OperationResult result=command();if(result.State==OperationState.Queued)resultPending=result.CommandId;Result(result);}
            finally{resultSubmitting=false;SetResultInteractable(resultPending==Guid.Empty);}
        }
        private void SetResultInteractable(bool enabled)
        {
            foreach(Button button in resultButtons)if(button!=null){button.interactable=enabled;
                RunEndPanelReferences end=button.GetComponentInParent<RunEndPanelReferences>();
                if(enabled&&end!=null&&button==end.RetryButton){SaveReadResult save=services.Saves.ReadCheckpoint();button.interactable=save.Error==ErrorCode.None&&save.Data!=null;}ApplyArtState(button);}
        }
        private void ActionButton(string text, int i, Action action) { Button(actions, text, 24 + i % 4 * 236, 622 + i / 4 * 44, 220, 38, action); }
        private void NewRun(GameMode mode)
        {
            if(services==null||openingActive)return;
            if(mode==GameMode.Story&&services.Flow.GetSnapshot().Phase==GamePhase.Menu&&OpeningStory!=null&&OpeningStory.Slides!=null&&OpeningStory.Slides.Length>0)
            { PlayOpening(); return; }
            StartRun(mode);
        }
        private void PlayOpening()
        {
            openingActive=true;
            try
            {
                CancelSelection();
                openingPause=services.Clock.AcquirePause(PauseReason.Modal);
                string error;
                CutscenePanelReferences references=OpeningCutscenePrefab!=null&&OpeningCutscenePrefab.Validate(out error)
                    ?Instantiate(OpeningCutscenePrefab,canvas.transform,false)
                    :CutscenePanelReferences.CreateDefaultPanel(canvas.transform,font);
                openingObject=references.gameObject;openingObject.SetActive(true);references.transform.SetAsLastSibling();
                OpeningPlayer=references.GetComponent<CutscenePlayer>();
                if(OpeningPlayer==null)OpeningPlayer=references.gameObject.AddComponent<CutscenePlayer>();
                OpeningPlayer.Panel=references;
                OpeningPlayer.Begin(OpeningStory,delegate {
                    if(!openingActive||services==null)return;
                    CancelOpening();
                    if(services.Flow.GetSnapshot().Phase==GamePhase.Menu)StartRun(GameMode.Story);
                    RefreshControlStates();
                });
                RefreshControlStates();
            }
            catch(Exception error)
            {
                CancelOpening();note.text="开场播放失败，请重试。";Debug.LogException(error,this);RefreshControlStates();
            }
        }
        private void CancelOpening()
        {
            openingActive=false;
            if(OpeningPlayer!=null)OpeningPlayer.Cancel();OpeningPlayer=null;
            if(services!=null&&openingPause!=Guid.Empty)services.Clock.ReleasePause(openingPause);openingPause=Guid.Empty;
            if(openingObject!=null){openingObject.SetActive(false);PresentationObjects.Release(openingObject);}openingObject=null;
        }
        private void StartRun(GameMode mode)
        {
            OperationResult result=services.Flow.TryNewRun(Command(),new NewRunRequest {LevelId=defaultLevelId,Mode=mode,Seed=seed});
            if(result.State==OperationState.Committed){CancelSelection();ReleasePlayerPause();}Result(result);
        }
        private void Menu()
        {
            if(services==null||openingActive)return;
            OperationResult result=services.Flow.TryReturnToMenu(Command());
            if(result.State==OperationState.Committed){CancelSelection();ReleasePlayerPause();}Result(result);
        }
        private static string CheckpointHint(SaveReadResult save)
        { return save.Error==ErrorCode.SaveUnavailable||save.Error==ErrorCode.None?"没有可继续的记录，请开始新的旅程。":"继续游戏不可用："+ErrorText(save.Error)+"。请开始新的旅程。"; }
        private void Load()
        {
            if(services==null||openingActive)return;
            SaveReadResult save=services.Saves.ReadCheckpoint();
            if(save.Error!=ErrorCode.None||save.Data==null)
            {
                note.text=CheckpointHint(save);
                MainMenuPanelReferences menu=ui==null?null:ui.ActionsRoot.GetComponentInChildren<MainMenuPanelReferences>();
                if(menu!=null){menu.HintText.text=note.text;menu.ContinueButton.interactable=false;ApplyArtState(menu.ContinueButton);}return;
            }
            OperationResult result=services.Flow.TryLoadRun(Command(),save.Data);
            if(result.State==OperationState.Committed)productionSummary=null;
            Result(result);
            MainMenuPanelReferences remaining=ui==null?null:ui.ActionsRoot.GetComponentInChildren<MainMenuPanelReferences>();
            if(remaining!=null&&result.State!=OperationState.Committed)remaining.HintText.text=note.text;
        }
        private void Place(string id) { if (services == null || openingActive || pending.Count!=0 || services.Flow.GetSnapshot().Phase != GamePhase.Build) return; placing = id; move = swap = false; note.text = "点击空格放置，或点取消";RefreshControlStates(); }
        private void ChooseMove(bool exchange) { if (!selected) { note.text = "先点选元素或塔"; return; } placing = null; move = !exchange; swap = exchange; note.text = exchange ? "点击要交换的格子" : "点击要移往的空格"; }
        private void Update()
        {
            if(services!=null){statusRefreshTimer+=Time.unscaledDeltaTime;if(statusRefreshTimer>=0.2f){statusRefreshTimer=0;RefreshStatus();}}
            if (services == null || openingActive || blocked || !Input.GetMouseButtonDown(0) || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) return;
            Camera camera = Camera.main; if (camera == null) return;
            Vector3 hit = camera.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, -camera.transform.position.z)); WorldPoint world = new WorldPoint(hit.x, hit.y);
            if (selectedItem != null) { SelectItemTarget(world); return; }
            if (services.Flow.GetSnapshot().Phase != GamePhase.Build) return;
            CellCoord clicked; if (!services.Board.TryWorldToCell(world, out clicked)) return;
            HandleBoardClick(clicked);
        }
        private void HandleBoardClick(CellCoord clicked)
        {
            if(services==null||openingActive||blocked||pending.Count!=0||services.Flow.GetSnapshot().Phase!=GamePhase.Build)return;
            if (move || swap) { OperationResult r = swap ? services.Deployment.TrySwap(Command(), cell, clicked) : services.Deployment.TryMove(Command(), cell, clicked); move = swap = false; Result(r); return; }
            if (placing != null) { Result(services.Deployment.TryPlace(Command(), new PlacementRequest { Cell = clicked, DefinitionId = placing, Kind = placing == towerId ? OccupantKind.Tower : OccupantKind.ElementBlock })); return; }
            CellSnapshot c; selected = services.Board.TryGetCell(clicked, out c) && c.OccupantKind != OccupantKind.None; cell = clicked;
            selection.text = selected ? "已选格子（" + (cell.X + 1) + "，" + (cell.Y + 1) + "）" : "请选择元素或塔";
        }
        private void BeginItem(int index)
        {
            if (services == null || openingActive) return; CancelSelection();
            if (services.Resources.GetQuantity(ResourceBucket.Inventory, itemIds[index]) <= 0) { note.text = "暂时没有这个道具"; return; }
            selectedItem = itemIds[index]; itemPause = services.Clock.AcquirePause(PauseReason.ItemSelection);
            if (index == 0) UseItem(services.World.SpringId); else note.text = "点击一座存活的塔强化，或点取消";
        }
        private void SelectItemTarget(WorldPoint point)
        {
            CellCoord coord; CellSnapshot c; if (!services.Board.TryWorldToCell(point, out coord) || !services.Board.TryGetCell(coord, out c) || c.OccupantKind != OccupantKind.Tower) { note.text = "请选择一座存活的塔"; return; }
            ActorSnapshot actor; if (!services.World.TryGetActor(c.OccupantId, out actor) || actor.CurrentHp <= 0) { note.text = "请选择一座存活的塔"; return; } UseItem(actor.Id);
        }
        private void UseItem(EntityId target) { OperationResult result = services.Items.TryUse(Command(), new ItemUseRequest { ItemId = selectedItem, Target = target }); ReleaseItem(); Result(result); }
        private void ReleaseItem() { selectedItem = null; if (services != null && itemPause != Guid.Empty) services.Clock.ReleasePause(itemPause); itemPause = Guid.Empty; }
        private void ReleasePlayerPause() { if(services!=null&&playerPause!=Guid.Empty)services.Clock.ReleasePause(playerPause);playerPause=Guid.Empty; }
        private void CancelSelection() { ReleaseItem(); placing = null; move = swap = false; }
        private void Pause() { if (services == null || openingActive) return; if (playerPause == Guid.Empty) playerPause = services.Clock.AcquirePause(PauseReason.Player); else { services.Clock.ReleasePause(playerPause); playerPause = Guid.Empty; } note.text = playerPause == Guid.Empty ? "继续守护" : "已暂停"; }
        private void DrawModal()
        {
            FlowSnapshot flow = services.Flow.GetSnapshot();
            if(modalRunId!=flow.RunId){modalRunId=flow.RunId;modalPending=Guid.Empty;}
            DayEventSnapshot day=flow.Phase==GamePhase.DayEvent?services.DayEvents.GetSnapshot():null;
            string tutorial=services.Catalog.GetSettings().TutorialText;
            bool eventOpen=day!=null&&day.State!=null&&!day.State.Resolved;
            bool tutorialOpen=flow.Phase==GamePhase.Build&&!flow.TutorialShown&&!String.IsNullOrEmpty(tutorial);
            string key=null;
            if(eventOpen){key=flow.RunId+"/event/"+day.State.InstanceId+"/"+day.Text;if(day.Options!=null)foreach(EventOptionDefinition option in day.Options)key+="/"+(option==null?"null":option.Id+":"+option.Text);}
            else if(tutorialOpen)key=flow.RunId+"/tutorial/"+tutorial;
            if(key==modalKey&&overlay!=null){blocked=true;return;}
            ReleaseContainer(overlay);overlay=null;blocked=false;modalButtons.Clear();modalFeedback=null;modalKey=key;
            if(key==null)return;
            if (flow.Phase == GamePhase.DayEvent) {
                DayEventPanelReferences prefab=ui!=null?ui.DayEventPanelPrefab:null;
                if(prefab!=null){string error;if(!prefab.Validate(out error)){InvalidModal(error);return;}
                    DayEventPanelReferences instance=Instantiate(prefab,ModalMount,false);overlay=instance.transform;ActivateModal();if(ui.ArtTheme!=null)ui.ArtTheme.ApplyDialogue(instance.Root,ui.ArtTheme.PortraitNeutral);
                    instance.TitleText.text="第 "+day.State.DayIndex+" 天 · 今日事件";instance.BodyText.text=day.Text;
                    if(day.Options!=null)foreach(EventOptionDefinition option in day.Options){if(option==null)continue;string id=option.Id,eventId=day.State.InstanceId;
                        Button button=Instantiate(instance.OptionButtonTemplate,instance.OptionsRoot,false);button.name="Option_"+id;
                        button.GetComponentInChildren<Text>(true).text=option.Text;button.gameObject.SetActive(true);modalButtons.Add(button);
                        BindButton(button,delegate { SubmitModal(delegate {return services.DayEvents.TryChoose(Command(),eventId,id);}); });}
                    LayoutRebuilder.ForceRebuildLayoutImmediate(instance.OptionsRoot);instance.BodyScroll.verticalNormalizedPosition=1;
                } else {
                    Modal("第 "+day.State.DayIndex+" 天 · 今日事件",day.Text);
                    RectTransform root=CreateModalScroll("Options",345,250);float y=0;
                    if(day.Options!=null)foreach(EventOptionDefinition option in day.Options){if(option==null)continue;string id=option.Id,eventId=day.State.InstanceId;
                        Button button=Button(root,option.Text,0,y,740,50,delegate {SubmitModal(delegate {return services.DayEvents.TryChoose(Command(),eventId,id);});});
                        Text label=button.GetComponentInChildren<Text>();float height=Mathf.Max(50,label.preferredHeight+12);
                        ((RectTransform)button.transform).sizeDelta=new Vector2(740,height);label.rectTransform.sizeDelta=new Vector2(730,height);
                        modalButtons.Add(button);y+=height+8;}
                    root.sizeDelta=new Vector2(760,Mathf.Max(250,y));
                }
            } else if(tutorialOpen) {
                TutorialPanelReferences prefab=ui!=null?ui.TutorialPanelPrefab:null;
                if(prefab!=null){string error;if(!prefab.Validate(out error)){InvalidModal(error);return;}
                    TutorialPanelReferences instance=Instantiate(prefab,ModalMount,false);overlay=instance.transform;ActivateModal();if(ui.ArtTheme!=null)ui.ArtTheme.ApplyDialogue(instance.Root,ui.ArtTheme.PortraitCalm);
                    instance.TitleText.text="守护指南";instance.BodyText.text=tutorial;modalButtons.Add(instance.ConfirmButton);
                    BindButton(instance.ConfirmButton,delegate {SubmitModal(delegate {return services.Flow.TryDismissTutorial(Command());});});
                }else{Modal("守护指南",tutorial);modalButtons.Add(Button(overlay,"开始部署",490,550,300,48,delegate {SubmitModal(delegate {return services.Flow.TryDismissTutorial(Command());});}));}
            }
            modalFeedback=Label(overlay,"",260,650,760,30,17);
            if(eventOpen&&modalButtons.Count==0){note.text="事件没有可用选项，请检查事件配置。";modalFeedback.text=note.text;Debug.LogError(note.text,this);}
            else if(modalPending!=Guid.Empty)modalFeedback.text="正在处理…";
            SetModalInteractable(modalPending==Guid.Empty&&!modalSubmitting);
        }
        private void SubmitModal(Func<OperationResult> command)
        {
            if(services==null||modalSubmitting||modalPending!=Guid.Empty)return;
            modalSubmitting=true;SetModalInteractable(false);
            try{OperationResult result=command();if(result.State==OperationState.Queued)modalPending=result.CommandId;Result(result);}
            finally{modalSubmitting=false;SetModalInteractable(modalPending==Guid.Empty);}
        }
        private void SetModalInteractable(bool enabled)
        {foreach(Button button in modalButtons)if(button!=null){button.interactable=enabled;ApplyArtState(button);}}
        private void ActivateModal()
        {overlay.gameObject.SetActive(true);overlay.SetAsLastSibling();if(ui!=null)ui.ModalRoot.SetAsLastSibling();blocked=true;}
        private void InvalidModal(string error)
        {
            Debug.LogError("正式弹窗引用无效："+error,this);Modal("弹窗配置错误",error);note.text="正式弹窗引用无效："+error;
        }
        private RectTransform CreateModalScroll(string name,float y,float height)
        {
            RectTransform viewport=Rect(overlay,name,260,y,760,height);Image(viewport,new Color(0,0,0,0.01f));viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content=Rect(viewport,"Content",0,0,760,height);ScrollRect scroll=viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;return content;
        }
        private void Modal(string caption, string body)
        {
            ReleaseContainer(overlay);
            overlay = Rect(ModalMount, "Modal", 0, 0, 1280, 720); Image(overlay, new Color(0.02f,0.04f,0.08f,0.88f)); overlay.SetAsLastSibling();if(ui!=null)ui.ModalRoot.SetAsLastSibling(); blocked = true;
            Image(Rect(overlay, "Card", 220, 135, 840, 480), new Color(0.13f,0.19f,0.28f)); Label(overlay, caption, 260, 165, 760, 48, 30);
            RectTransform content=CreateModalScroll("BodyScroll",225,106);
            Text text=Label(content,body,0,0,740,106,21);text.alignment=TextAnchor.UpperLeft;
            text.verticalOverflow=VerticalWrapMode.Overflow;float height=Mathf.Max(106,text.preferredHeight);text.rectTransform.sizeDelta=new Vector2(740,height);content.sizeDelta=new Vector2(760,height);
        }
        private void BuildWidgets()
        {
            font = CreateFont(); GameObject obj = new GameObject("Game UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); obj.transform.SetParent(transform,false);
            generatedUi=obj;
            canvas = obj.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50; CanvasScaler scaler = obj.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280,720); scaler.matchWidthOrHeight = 0.5f; panel = obj.transform;
            EnsureEventSystem();
            Image(Rect(panel,"Header",0,0,1280,90),new Color(0.08f,0.12f,0.19f,0.97f)); Image(Rect(panel,"Sidebar",1020,90,260,630),new Color(0.08f,0.12f,0.19f,0.97f));
            title = Label(panel,"聚光灯",24,12,520,32,25); health = Label(panel,"灵泉",550,14,230,30,22); wave = Label(panel,"波次",800,14,220,30,20); note = Label(panel,"白天部署，夜晚守护灵泉。",24,53,980,26,17);
            gold = Label(panel,"金币 0",1040,104,220,30,21); skill = Label(panel,"技能 1 次",1040,140,220,30,18); selection = Label(panel,"请选择元素或塔",1040,180,220,46,16); Label(panel,"手牌 · 点击后放置",1040,238,220,24,18);
            for (int i=0;i<6;i++) { int index=i; hands[i]=Button(panel,names[i]+" × 0",1040+i%3*74,272+i/3*42,68,36,delegate { Place(ids[index]); }).GetComponentInChildren<Text>(); }
            Label(panel,"背包",1040,360,220,24,18); items[0]=Button(panel,"回复药剂 × 0",1040,392,220,36,delegate { BeginItem(0); }).GetComponentInChildren<Text>(); items[1]=Button(panel,"塔强化 × 0",1040,434,220,36,delegate { BeginItem(1); }).GetComponentInChildren<Text>();
            Button(panel,"取消选择",1040,484,220,34,delegate { CancelSelection(); note.text="选择已取消"; }); Button(panel,"暂停 / 继续",1040,528,220,36,Pause);
            Button(panel,"1 倍",1040,574,106,34,delegate { if (services!=null) Result(services.Clock.TrySetSpeed(Command(),GameSpeed.Normal)); }); Button(panel,"2 倍",1154,574,106,34,delegate { if (services!=null) Result(services.Clock.TrySetSpeed(Command(),GameSpeed.Double)); });
            Label(panel,"左键选格 · 右侧选择行动\n先部署，再开始夜晚",1040,632,220,58,16);
        }
        public void TestWidgets()
        {
            if (services!=null) return; if (canvas==null){try{BuildUi();}catch{Unbind();throw;}} title.text="聚光灯 · 第 1 天 · 白天部署"; health.text="灵泉 100 / 100"; wave.text="波次 0 · 敌人 0"; gold.text="金币 20";
            for(int i=0;i<6;i++) hands[i].text=names[i]+" × 3"; items[0].text="回复药剂 × 2"; items[1].text="塔强化 × 2";
            ReleaseContainer(actions); actions=Rect(ActionMount,"Preview Actions",0,0,1020,720);
            ActionButton("开始这一夜（预览）",0,delegate { title.text="聚光灯 · 第 1 天 · 夜晚守护"; note.text="界面预览：游戏流程由正式场景运行。"; });
            ActionButton("查看事件（预览）",1,delegate { Modal("今日事件","旅人送来了守护灵泉的补给。\n选择一个选项继续。"); Button(overlay,"收下补给（预览）",420,420,440,50,delegate { PresentationObjects.Release(overlay.gameObject); overlay=null; blocked=false; note.text="预览：补给已收下"; }); });
            note.text="界面预览 · 按钮只展示交互，不改变游戏进度。";
        }
        public static Font CreateFont() { Font value=Font.CreateDynamicFontFromOSFont(new string[] {"Microsoft YaHei","SimHei","Arial"},20); return value!=null?value:Resources.GetBuiltinResource<Font>("Arial.ttf"); }
        private Transform ActionMount { get { return ui!=null?(Transform)ui.ActionsRoot:panel; } }
        private Transform ModalMount { get { return ui!=null?(Transform)ui.ModalRoot:panel; } }
        private void BuildUi()
        {
            if(UiPrefab==null){BuildWidgets();return;}
            string error;
            if(!UiPrefab.Validate(out error))throw new InvalidOperationException(error);
            ui=Instantiate(UiPrefab,transform,false);generatedUi=ui.gameObject;
            if(!ui.Validate(out error))throw new InvalidOperationException(error);
            if(ui.ArtTheme!=null)ui.ArtTheme.ApplyButtons(ui.transform);
            canvas=ui.Canvas;panel=canvas.transform;font=ui.TitleText.font!=null?ui.TitleText.font:CreateFont();
            title=ui.TitleText;health=ui.SpringHpText;wave=ui.WaveText;skill=ui.SkillText;gold=ui.GoldText;note=ui.HintText;selection=ui.SelectionText;
            for(int i=0;i<hands.Length;i++){int index=i;hands[i]=ui.HandTexts[i];BindButton(ui.ElementButtons[i],delegate {Place(ids[index]);});}
            for(int i=0;i<items.Length;i++){int index=i;items[i]=ui.ItemTexts[i];BindButton(ui.ItemButtons[i],delegate {BeginItem(index);});}
            BindButton(ui.CancelSelectionButton,delegate {CancelSelection();note.text="选择已取消";});
            BindButton(ui.PauseButton,Pause);
            BindButton(ui.NormalSpeedButton,delegate {if(services!=null)Result(services.Clock.TrySetSpeed(Command(),GameSpeed.Normal));});
            BindButton(ui.DoubleSpeedButton,delegate {if(services!=null)Result(services.Clock.TrySetSpeed(Command(),GameSpeed.Double));});
            EnsureEventSystem();
        }
        private void EnsureEventSystem()
        {
            if(EventSystem.current!=null)return;
            if(ownedEvents!=null)
            {
                EventSystem owned=ownedEvents.GetComponent<EventSystem>();
                if(owned!=null&&owned.enabled&&owned.gameObject.activeInHierarchy)return;
                ownedEvents.SetActive(false);PresentationObjects.Release(ownedEvents);ownedEvents=null;
            }
            // EditMode may contain an active EventSystem before its OnEnable sets current.
            foreach(EventSystem existing in FindObjectsOfType<EventSystem>())
                if(existing.enabled&&existing.gameObject.activeInHierarchy)return;
            ownedEvents=new GameObject("UI Event System",typeof(EventSystem),typeof(StandaloneInputModule));
            ownedEvents.transform.SetParent(transform,false);
        }
        private void ApplyArtState(Button button,bool selected=false)
        {if(ui!=null&&ui.ArtTheme!=null)ui.ArtTheme.ApplyState(button,selected);}
        private void BindButton(Button button,Action action)
        {
            UnityAction listener=delegate {if(!openingActive&&pending.Count==0&&action!=null)action();};
            if(ui!=null&&ui.ArtTheme!=null){ui.ArtTheme.ApplyButton(button);ui.ArtTheme.ApplyState(button);}
            buttonBindings.Add(new ButtonBinding {Button=button,Event=button.onClick,Listener=listener});button.onClick.AddListener(listener);
        }
        private void ReleaseContainer(Transform container)
        {
            if(container==null)return;
            for(int i=buttonBindings.Count-1;i>=0;i--)
            {
                ButtonBinding binding=buttonBindings[i];
                if(binding.Button==null||binding.Button.transform.IsChildOf(container)){binding.Event.RemoveListener(binding.Listener);buttonBindings.RemoveAt(i);}
            }
            container.gameObject.SetActive(false);PresentationObjects.Release(container.gameObject);
        }
        private static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h) { GameObject o=new GameObject(name,typeof(RectTransform)); o.transform.SetParent(parent,false); RectTransform r=o.GetComponent<RectTransform>(); r.anchorMin=r.anchorMax=new Vector2(0,1); r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(x,-y); r.sizeDelta=new Vector2(w,h); return r; }
        private static void Image(Transform t,Color color) { t.gameObject.AddComponent<Image>().color=color; }
        private Text Label(Transform parent,string text,float x,float y,float w,float h,int size) { Text t=Rect(parent,"Label",x,y,w,h).gameObject.AddComponent<Text>(); t.font=font;t.text=text;t.fontSize=size;t.color=new Color(0.94f,0.96f,1);t.alignment=TextAnchor.MiddleLeft;t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;return t; }
        private Button Button(Transform parent,string text,float x,float y,float w,float h,Action action) { RectTransform r=Rect(parent,text,x,y,w,h);Image(r,new Color(0.21f,0.34f,0.46f));Button b=r.gameObject.AddComponent<Button>();Text l=Label(r,text,5,0,w-10,h,17);l.alignment=TextAnchor.MiddleCenter;BindButton(b,action);return b; }
    }
}
