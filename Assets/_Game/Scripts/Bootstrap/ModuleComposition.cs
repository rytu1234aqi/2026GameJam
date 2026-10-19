using System;
using System.Collections.Generic;
using Spotlight.Contracts;
using Spotlight.Core;
using Spotlight.Core.Clock;
using Spotlight.Core.Data;
using Spotlight.Core.Events;
using Spotlight.Core.Flow;
using Spotlight.Core.Save;
using Spotlight.Elements;
using Spotlight.Combat;
using Spotlight.Enemies;
using Spotlight.Gameplay;

namespace Spotlight.Bootstrap
{
    /// <summary>唯一的服务组装点。各模块只通过冻结的 Contracts 互相调用。</summary>
    public sealed class ModuleComposition : IDisposable
    {
        public GameCatalog Catalog { get; private set; }
        public GameEventBus Events { get; private set; }
        public CoreServices Core { get; private set; }
        public GameClock Clock { get; private set; }
        public SimulationDriver Driver { get; private set; }
        public ElementRuleService Elements { get; private set; }
        public CombatServices Combat { get; private set; }
        public EnemyServices Enemies { get; private set; }
        public GameplayServices Gameplay { get; private set; }
        public SaveService Saves { get; private set; }
        public GameFlowService Flow { get; private set; }
        public GameViewContext ViewContext { get; private set; }
        private bool disposed;
        private readonly IDisposable checkpointSubscription;
        private string checkpointRunId;
        private long lastCheckpointRevision=-1;
        private float checkpointRetryRemaining;

        public ModuleComposition(CatalogData data, string saveDirectory)
        {
            Catalog = new GameCatalog(data);
            EndlessWaveProvider endless = new EndlessWaveProvider();
            Catalog.SetEndlessWaveResolver(delegate(int day, GameMode mode)
            {
                return endless.Resolve(day, Catalog.GetEndlessDefinition(), delegate(string id)
                {
                    WaveDefinition wave;
                    return Catalog.TryGetWaveTemplate(id, out wave) ? wave : null;
                });
            });
            IReadOnlyList<string> errors = Catalog.ValidateAll();
            if (errors.Count > 0) throw new InvalidOperationException("配置检查失败：\n" + string.Join("\n", new List<string>(errors).ToArray()));

            Events = new GameEventBus();
            Core = new CoreServices(Catalog, Events);
            Clock = new GameClock(Core.Store.GetFlowSnapshot, Events);
            Driver = new SimulationDriver(Clock, Core.Store.GetFlowSnapshot, Catalog.GetSettings().LogicTicksPerGameSecond);
            Elements = new ElementRuleService(Catalog, Core.Random);
            Combat = new CombatServices(Catalog, Core.Board, Core.World, Core.Transactions, Core.Commands, Core.Ids, Events, Elements, Clock, Core.Random);
            Enemies = new EnemyServices(Catalog, Core.Board, Core.World, Core.Motion, Core.Transactions, Core.Commands, Core.Ids, Core.Random, Combat.Status, Combat.Damage, Combat.Projectiles, Combat.Effects, Events, Clock);
            Gameplay = new GameplayServices(Catalog, Core.World, Core.Transactions, Core.Commands, Core.Random, Combat.Effects, Clock, Events, Core.Store.GetFlowSnapshot, Core.Resources, Core.Store.GetDayEventState);
            Saves = new SaveService(Core.Store, Catalog, Events, saveDirectory);
            checkpointSubscription=Events.Subscribe<CheckpointSavedEvent>(delegate(CheckpointSavedEvent saved)
            {
                FlowSnapshot current=Core.Store.GetFlowSnapshot();
                if(current.RunId==checkpointRunId&&saved.Revision==current.Revision)
                {
                    lastCheckpointRevision=saved.Revision;
                    checkpointRetryRemaining=0;
                }
            });
            Flow = new GameFlowService(Core, Catalog, Elements, Saves, Enemies.Waves, Combat.Damage, Combat.Skill, Combat.Towers, Combat.Projectiles, Gameplay.Items, Gameplay.DayEvents, Clock, Driver, Events,
                new ISessionModule[] { Combat, Enemies, Gameplay });

            Driver.Register(new PreviousPositionSystem(Core.Store));
            Register(Combat.TickSystems);
            Register(Enemies.TickSystems);
            Register(Gameplay.TickSystems);
            Driver.Register(Flow);
            Driver.Register(Events);

            ViewContext = new GameViewContext
            {
                Commands = Core.Commands, Flow = Flow, Clock = Clock,
                Board = Core.Board, Deployment = Core.Deployment, Resources = Core.Resources,
                World = Core.World, Status = Combat.Status, Skill = Combat.Skill, Waves = Enemies.Waves,
                Items = Gameplay.Items, DayEvents = Gameplay.DayEvents, Saves = Saves,
                Events = Events, Catalog = Catalog
            };
        }

        private void Register(IReadOnlyList<ITickSystem> systems)
        {
            for (int i = 0; i < systems.Count; i++) Driver.Register(systems[i]);
        }

        public void Advance(float realDeltaSeconds)
        {
            if (disposed) return;
            TrackCheckpointRun();
            if(!float.IsNaN(realDeltaSeconds)&&!float.IsInfinity(realDeltaSeconds)&&realDeltaSeconds>0)
                checkpointRetryRemaining=Math.Max(0,checkpointRetryRemaining-realDeltaSeconds);
            // 白天没有逻辑 tick，也必须派发按钮产生的事件；入夜事件先于第一帧战斗。
            Events.Flush();
            Flow.SynchronizeDayEvent();
            SaveStableChanges();
            Combat.Towers.SynchronizeBoard();
            Driver.Advance(realDeltaSeconds);
            Flow.SynchronizeDayEvent();
            Events.Flush();
        }

        private void TrackCheckpointRun()
        {
            string runId=Core.Store.GetFlowSnapshot().RunId;
            if(runId==checkpointRunId)return;
            checkpointRunId=runId;lastCheckpointRevision=-1;checkpointRetryRemaining=0;
        }

        private void SaveStableChanges()
        {
            TrackCheckpointRun();FlowSnapshot current=Core.Store.GetFlowSnapshot();
            if(current.Phase!=GamePhase.Build||!current.CanStartNight||current.Revision==lastCheckpointRevision||checkpointRetryRemaining>0)return;
            OperationResult result=Saves.SaveCheckpoint(Core.Commands.Create(current.Revision),SaveReason.DeploymentChanged);
            if(result.State==OperationState.Committed)
            {
                // 保存命令history不递增Revision，因此不会自触发下一轮保存。
                lastCheckpointRevision=current.Revision;checkpointRetryRemaining=0;
            }
            else checkpointRetryRemaining=.5f;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            checkpointSubscription.Dispose();
            Gameplay.EndSession();
            Enemies.EndSession();
            Combat.EndSession();
            Driver.Reset();
            Events.Clear();
        }

        private sealed class PreviousPositionSystem : ITickSystem
        {
            private readonly SessionStateStore store;
            public PreviousPositionSystem(SessionStateStore store) { this.store = store; }
            public ModuleId Module { get { return ModuleId.Core; } }
            public IReadOnlyList<TickStage> Stages { get { return new[] { TickStage.Movement }; } }
            public void Tick(TickContext context) { store.AdvancePreviousPositions(); }
        }
    }
}
