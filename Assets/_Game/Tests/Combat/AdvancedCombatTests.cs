using System;
using System.Collections.Generic;
using NUnit.Framework;
using Spotlight.Contracts;
using Spotlight.Combat;
namespace Spotlight.Tests.AdvancedCombat
{
    public sealed class AdvancedCombatTests
    {
        AdvancedFixture f; CombatServices c; Rules rules; long tick;
        sealed class Rules : IElementRuleService
        {
            public ReactionMatch Match;
            public ProductionResult ResolveProduction(ProductionInput input) { return new ProductionResult(); }
            public ReactionPlan ResolveReactions(ReactionInput input) { return new ReactionPlan { Matches=Match==null?(IReadOnlyList<ReactionMatch>)new ReactionMatch[0]:new[]{Match},RemainingTokens=input.Tokens }; }
            public ProjectileModifierDefinition GetProjectileModifier(ElementType e){return null;}
        }
        [SetUp] public void Setup(){f=new AdvancedFixture();c=new CombatServices(f,f,f,f,f,f,f,(rules=new Rules()),f);c.BeginSession(new SessionStartContext {RunId="run"});f.Publish(new PhaseChangedEvent {NewPhase=GamePhase.Night});}
        [TearDown] public void Cleanup(){c.EndSession();}
        void Stage(double time,TickStage stage){c.Tick(new TickContext(++tick,time,.1f,stage));}
        void Drain(){Stage(0,TickStage.DamageAndReaction);}
        EntityId Add(long id,float x,float y=0){var key=new EntityId(id);f.Actors[id]=new ActorSnapshot {Id=key,Kind=ActorKind.Enemy,Position=new WorldPoint(x,y),CurrentHp=100,MaxHp=100,Targetable=true};return key;}
        EffectBatchRequest Request(params EffectSpec[] effects){return new EffectBatchRequest {Context=f.Create(f.Revision),Source=new EntityId(1),SelectedTarget=new EntityId(2),Effects=effects};}
        EffectSpec Area(){return new EffectSpec {Kind=EffectKind.CreateArea,RadiusInCells=1,DurationSeconds=3,TickIntervalSeconds=1,LingeringSeconds=1,AreaEffects=new EffectSpec[]{new EffectSpec {Kind=EffectKind.Damage,PhysicalDamage=10},new EffectSpec {Kind=EffectKind.ApplyStatus,ReferenceId="base.water",Stacks=1}}};}
        [Test] public void AdvancedValidationAndRejectedCommitHaveNoSideEffects()
        {
            var r=Request(Area());Assert.AreEqual(ErrorCode.None,c.Effects.Validate(r).Error);Assert.AreEqual(0,c.GetAreaEffects().Count);Assert.AreEqual(0,c.PendingCount);
            f.FailCommit=true;Assert.AreEqual(OperationState.Rejected,c.Effects.TryExecute(r).State);Assert.AreEqual(0,c.GetAreaEffects().Count);Assert.AreEqual(100,f.Actors[2].CurrentHp);
            r.Effects[0].TickIntervalSeconds=.001f;Assert.AreEqual(ErrorCode.InvalidArgument,c.Effects.Validate(r).Error);
        }
        [Test] public void AreaDamageUsesCircularCentersAndDuplicateReceipt()
        {
            Add(3,.5f);Add(4,.9f,.9f);var r=Request(new EffectSpec {Kind=EffectKind.AreaDamage,RadiusInCells=1,PhysicalDamage=10});r.HasImpactPosition=true;r.ImpactPosition=new WorldPoint(0,0);
            c.Effects.TryExecute(r);c.Effects.TryExecute(r);Drain();Assert.AreEqual(90,f.Actors[2].CurrentHp);Assert.AreEqual(90,f.Actors[3].CurrentHp);Assert.AreEqual(100,f.Actors[4].CurrentHp);
        }
        [Test] public void ChainSurvivesFirstDeathAndRequiresStatusInsteadOfGauge()
        {
            var a=Add(3,1);var b=Add(4,2);Add(5,.2f);
            c.Status.EnqueueStatus(new StatusApplyRequest {Target=a,StatusId="base.water",Stacks=1});c.Status.EnqueueStatus(new StatusApplyRequest {Target=b,StatusId="base.water",Stacks=1});c.Status.EnqueueGauge(new EntityId(1),new EntityId(5),new ElementAmounts(5,0,0,0,0,0));Drain();
            var r=Request(new EffectSpec {Kind=EffectKind.Damage,PhysicalDamage=100},new EffectSpec {Kind=EffectKind.ChainDamage,RadiusInCells=1.1f,MaxHits=3,RetainedDamageMultiplier=.5f,PhysicalDamage=20,RequiredStatusId="base.water"});
            Assert.AreEqual(OperationState.Committed,c.Effects.TryExecute(r).State);Drain();Assert.AreEqual(0,f.Actors[2].CurrentHp);Assert.AreEqual(80,f.Actors[3].CurrentHp);Assert.AreEqual(90,f.Actors[4].CurrentHp);Assert.AreEqual(100,f.Actors[5].CurrentHp);
        }
        [Test] public void IndependentAreaScopesLeaveAndExpireWithoutRefreshOrDuplication()
        {
            var r=Request(Area());c.Effects.TryExecute(r);c.Effects.TryExecute(r);c.Effects.TryExecute(Request(Area()));Drain();Assert.AreEqual(80,f.Actors[2].CurrentHp);Assert.AreEqual(2,c.Status.GetStatuses(new EntityId(2))[0].Layers.Count);
            Stage(.5,TickStage.StatusAndGauge);Assert.AreEqual(2,c.Status.GetStatuses(new EntityId(2))[0].Layers.Count);f.Actors[2].Position=new WorldPoint(5,0);Stage(.6,TickStage.StatusAndGauge);
            foreach(var layer in c.Status.GetStatuses(new EntityId(2))[0].Layers)Assert.AreEqual(1,layer.RemainingSeconds,.0001);
            Stage(1.59,TickStage.StatusAndGauge);Assert.AreEqual(2,c.Status.GetStatuses(new EntityId(2))[0].Layers.Count);Stage(1.61,TickStage.StatusAndGauge);Assert.AreEqual(0,c.Status.GetStatuses(new EntityId(2)).Count);
            Stage(3,TickStage.StatusAndGauge);Assert.AreEqual(0,c.GetAreaEffects().Count);
        }
        [Test] public void LeavingOneAreaPreservesOtherScopeLease()
        {
            Add(3,1.5f);c.Effects.TryExecute(Request(Area()));var other=Request(Area());other.SelectedTarget=new EntityId(3);c.Effects.TryExecute(other);
            f.Actors[2].Position=new WorldPoint(.75f,0);Stage(.1,TickStage.StatusAndGauge);Assert.AreEqual(2,c.Status.GetStatuses(new EntityId(2))[0].Layers.Count);
            f.Actors[2].Position=new WorldPoint(1.5f,0);Stage(.2,TickStage.StatusAndGauge);var layers=c.Status.GetStatuses(new EntityId(2))[0].Layers;
            Assert.AreEqual(1,layers[0].RemainingSeconds,.0001);Assert.AreEqual(3.8f,layers[1].RemainingSeconds,.0001);Assert.AreNotEqual(layers[0].ScopeId,layers[1].ScopeId);
            Stage(1.21,TickStage.StatusAndGauge);Assert.AreEqual(1,c.Status.GetStatuses(new EntityId(2))[0].Layers.Count);
        }
        [Test] public void AreaCopiesConfigurationAndLogicalTimeControlsPulses()
        {
            EffectSpec e=Area();var r=Request(e);c.Effects.TryExecute(r);Drain();e.TickIntervalSeconds=.1f;e.AreaEffects[0].PhysicalDamage=99;
            Stage(0,TickStage.StatusAndGauge);Drain();Assert.AreEqual(90,f.Actors[2].CurrentHp);
            Stage(2,TickStage.StatusAndGauge);Stage(2,TickStage.DamageAndReaction);Assert.AreEqual(70,f.Actors[2].CurrentHp);Assert.AreEqual(1,c.GetAreaEffects().Count);
            f.Publish(new PhaseChangedEvent {NewPhase=GamePhase.NightResult});Assert.AreEqual(0,c.GetAreaEffects().Count);Assert.AreEqual(0,c.Status.GetStatuses(new EntityId(2)).Count);
        }
        [Test] public void PiercingCountsReentryAndKeepsFlyingAfterBudget()
        {
            f.Actors[10]=new ActorSnapshot {Id=new EntityId(10),Kind=ActorKind.Tower,CurrentHp=100,MaxHp=100};f.Actors[2].Position=new WorldPoint(.2f,0);f.Actors[2].CollisionRadius=.5f;
            Assert.AreEqual(OperationState.Committed,c.Projectiles.Spawn(new ProjectileSpawnRequest {Source=new EntityId(10),DefinitionId="test",Origin=new WorldPoint(0,0),AimPoint=new WorldPoint(10,0),PhysicalDamage=20}).State);
            Stage(.1,TickStage.Projectiles);Stage(.1,TickStage.DamageAndReaction);Assert.AreEqual(80,f.Actors[2].CurrentHp);
            Stage(.2,TickStage.Projectiles);Stage(.2,TickStage.DamageAndReaction);Assert.AreEqual(80,f.Actors[2].CurrentHp);
            f.Actors[2].Position=new WorldPoint(10,0);Stage(.3,TickStage.Projectiles);f.Actors[2].Position=new WorldPoint(.4f,0);Stage(.4,TickStage.Projectiles);Stage(.4,TickStage.DamageAndReaction);Assert.AreEqual(70,f.Actors[2].CurrentHp);
            f.Actors[2].Position=new WorldPoint(10,0);Stage(.5,TickStage.Projectiles);f.Actors[2].Position=new WorldPoint(.6f,0);Stage(.6,TickStage.Projectiles);Stage(.6,TickStage.DamageAndReaction);Assert.AreEqual(65,f.Actors[2].CurrentHp);
            Assert.AreEqual(3,c.Projectiles.GetSnapshot()[0].HitCount);Stage(.7,TickStage.Projectiles);Stage(.7,TickStage.DamageAndReaction);Assert.AreEqual(65,f.Actors[2].CurrentHp);Assert.AreEqual(1,c.Projectiles.GetSnapshot().Count);
        }
        [Test] public void ProjectileStoresSecondaryEffectUntilDirectImpactWithoutRecursion()
        {
            f.Projectile.SpeedInCellsPerSecond=10;f.Projectile.PiercingEnabled=false;
            f.Actors[10]=new ActorSnapshot {Id=new EntityId(10),Kind=ActorKind.Tower,CurrentHp=100,MaxHp=100};f.Actors[2].Position=new WorldPoint(1,0);Add(3,1.5f);
            f.Cells=new[]{new CellSnapshot {Cell=new CellCoord(0,0),OccupantId=new EntityId(20),OccupantKind=OccupantKind.ElementBlock,DefinitionId="elm.water"}};
            rules.Match=new ReactionMatch {RuleId="test.aoe",Effects=new EffectSpec[]{new EffectSpec {Kind=EffectKind.AreaDamage,RadiusInCells=1,PhysicalDamage=10}}};
            c.Projectiles.Spawn(new ProjectileSpawnRequest {Source=new EntityId(10),DefinitionId="test",Origin=new WorldPoint(0,0),AimPoint=new WorldPoint(10,0),PhysicalDamage=20});Stage(.1,TickStage.Projectiles);
            Assert.AreEqual(100,f.Actors[2].CurrentHp);Stage(.1,TickStage.DamageAndReaction);Assert.AreEqual(70,f.Actors[2].CurrentHp);Assert.AreEqual(90,f.Actors[3].CurrentHp);Assert.AreEqual(0,c.Projectiles.GetSnapshot().Count);
        }
        [Test] public void EmptySecondaryTargetsAndMissingPrimaryAreHandled()
        {
            var r=Request(new EffectSpec {Kind=EffectKind.ChainDamage,RadiusInCells=1,MaxHits=4,PhysicalDamage=10});Assert.AreEqual(OperationState.Committed,c.Effects.TryExecute(r).State);Drain();Assert.AreEqual(100,f.Actors[2].CurrentHp);
            r=Request(Area());r.SelectedTarget=new EntityId(999);Assert.AreEqual(ErrorCode.InvalidTarget,c.Effects.Validate(r).Error);
        }
    }
    public sealed class AdvancedFixture : IGameCatalog, IBoardQuery, IWorldQuery, IStateTransactionService, ICommandContextFactory, IEntityIdService, IEventBus, IGameClock, IRandomService
    {
        public CellSnapshot[] Cells=new CellSnapshot[0];
        public readonly Dictionary<long, ActorSnapshot> Actors = new Dictionary<long, ActorSnapshot>();
        public readonly List<ReactionDefinition> Reactions = new List<ReactionDefinition>();
        public readonly List<ProductionRuleDefinition> Production = new List<ProductionRuleDefinition>();
        public readonly Dictionary<string, ElementDefinition> Elements = new Dictionary<string, ElementDefinition>();
        public ProjectileDefinition Projectile = new ProjectileDefinition { Id="test", SpeedInCellsPerSecond=1,LifetimeSeconds=20,CollisionRadiusInCells=.1f,PiercingEnabled=true,MaxHits=3,RetainedDamageMultiplier=.5f };
        public bool FailCommit; public long Revision { get; private set; }
        private long sequence = 10;
        public AdvancedFixture()
        {
            Actors.Add(1, new ActorSnapshot { Id = new EntityId(1), Kind = ActorKind.Spring, CurrentHp = 100, MaxHp = 100, Targetable = true });
            Actors.Add(2, new ActorSnapshot { Id = new EntityId(2), Kind = ActorKind.Enemy, CurrentHp = 100, MaxHp = 100, Targetable = true, PhysicalReduction = 0f, ElementResistance = new ElementAmounts() });
            Elements.Add("elm.water", new ElementDefinition { Id = "elm.water", Element = ElementType.Water, BaseStatusId = "base.water", ProjectileModifier = new ProjectileModifierDefinition { Element = ElementType.Water } });
        }
        public GameSettings GetSettings() { return new GameSettings { MaxReactionActionsPerTick = 1, SpecialSkillId = "skill", LogicTicksPerGameSecond = 10 }; }
        public bool TryGetElement(string id, out ElementDefinition value) { return Elements.TryGetValue(id, out value); }
        public bool TryGetStatus(string id, out StatusDefinition value)
        { value = null; if (id == "base.water") value = new StatusDefinition { Id = id, ElementTag = ElementType.Water, IsReactionInput = false, MaxStacks = 5, DurationSeconds = 3, RefreshPolicy = StatusRefreshPolicy.IndependentLayers }; if (id == "dot") value = new StatusDefinition { Id = id, IsReactionDamage = true, MaxStacks = 5, DurationSeconds = 1, TickIntervalSeconds = 1, DotElementPerStack = new ElementAmounts(0, 0, 0, 0, 0, 2) }; return value != null; }
        public IReadOnlyList<ProductionRuleDefinition> GetProductionRules() { return Production.AsReadOnly(); }
        public IReadOnlyList<ReactionDefinition> GetReactionRules(ReactionContext context) { return Reactions.FindAll(delegate(ReactionDefinition r) { return r.Context == context; }).AsReadOnly(); }
        public IReadOnlyList<GaugeDefinition> GetGaugeDefinitions() { return new GaugeDefinition[] { new GaugeDefinition { Element = ElementType.Water, Threshold = 10, MaxValue = 100, IncomingMultiplier = 1, DecayDelaySeconds = 1, DecayPerSecond = 1 } }; }
        public bool TryGetLevel(string id, out LevelDefinition value) { value = new LevelDefinition { Id = "level", CellSize = 1 }; return true; }
        public bool TryGetSkill(string id, out SpecialSkillDefinition value) { value = new SpecialSkillDefinition { Id = "skill", UsesPerNight = 1, DurationSeconds = 10, ReactionDamageMultiplier = 1.2f }; return true; }
        public bool TryGetTower(string id, out TowerDefinition value) { value = null; return false; }
        public bool TryGetProjectile(string id, out ProjectileDefinition value) { value = Projectile; return true; }
        public bool TryGetEnemy(string id, out EnemyDefinition value) { value = null; return false; }
        public bool TryGetItem(string id, out ItemDefinition value) { value = null; return false; }
        public bool TryGetResource(string id, out ResourceDefinition value) { value = null; return false; }
        public bool TryGetEvent(string id, out DayEventDefinition value) { value = null; return false; }
        public bool TryGetBossSkill(string id, out BossSkillDefinition value) { value = null; return false; }
        public bool TryGetDropTable(string id, out DropTableDefinition value) { value = null; return false; }
        public EndlessDefinition GetEndlessDefinition() { return null; }
        public IReadOnlyList<DayEventDefinition> GetDayEvents() { return new DayEventDefinition[0]; }
        public IReadOnlyList<WaveDefinition> GetWaves(int dayIndex, GameMode mode) { return new WaveDefinition[0]; }
        public IReadOnlyList<string> ValidateAll() { return new string[0]; }
        public BoardSnapshot GetSnapshot() { return new BoardSnapshot { LevelId = "level", Revision = Revision, Cells = new CellSnapshot[0] }; }
        public bool TryGetCell(CellCoord cell, out CellSnapshot value) { value = null; return false; }
        public IReadOnlyList<CellCoord> GetNeighbors4(CellCoord cell) { return new CellCoord[0]; }
        public WorldPoint CellToWorld(CellCoord cell) { return new WorldPoint(cell.X + 0.5f, cell.Y + 0.5f); }
        public bool TryWorldToCell(WorldPoint point, out CellCoord cell) { cell = new CellCoord((int)Math.Floor(point.X), (int)Math.Floor(point.Y)); return true; }
        public IReadOnlyList<CellSnapshot> TraceSegment(WorldPoint a, WorldPoint b) { return Cells; }
        public EntityId SpringId { get { return new EntityId(1); } }
        public bool TryGetActor(EntityId id, out ActorSnapshot value) { return Actors.TryGetValue(id.Value, out value); }
        public IReadOnlyList<ActorSnapshot> GetActors(ActorKind kind) { List<ActorSnapshot> a = new List<ActorSnapshot>(); foreach (ActorSnapshot actor in Actors.Values) if (actor.Kind == kind) a.Add(actor); return a.AsReadOnly(); }
        public IReadOnlyList<ActorSnapshot> QueryRadius(WorldPoint p, float r, ActorKind kind) { return GetActors(kind); }
        public IReadOnlyList<ActorSnapshot> TraceActors(WorldPoint a, WorldPoint b, float r, ActorKind kind) { return GetActors(kind); }
        public ValidationResult Validate(StateMutationBatch batch) { return new ValidationResult(ErrorCode.None, "valid"); }
        public OperationResult TryCommit(StateMutationBatch batch)
        {
            if (FailCommit) return new OperationResult(OperationState.Rejected, ErrorCode.VersionConflict, batch.Context.CommandId, Revision, "test.fail");
            if (batch.Actors != null) foreach (ActorMutation m in batch.Actors) if (m.Kind == ActorMutationKind.SetHealth) { Actors[m.ActorId.Value].CurrentHp = m.CurrentHp; Actors[m.ActorId.Value].MaxHp = m.MaxHp; }
            Revision++; return new OperationResult(OperationState.Committed, ErrorCode.None, batch.Context.CommandId, Revision, "done");
        }
        public CommandContext Create(long revision) { return new CommandContext(Guid.NewGuid(), "run", revision); }
        public EntityId Allocate() { return new EntityId(++sequence); }
        public long AllocateSpawnSequence() { return ++sequence; }
        private readonly Dictionary<Type,List<object>> listeners=new Dictionary<Type,List<object>>();
        private sealed class Disposable : IDisposable { internal Action Remove;public void Dispose() {if(Remove!=null)Remove();Remove=null;} }
        public IDisposable Subscribe<T>(Action<T> handler) where T : class
        {List<object> list;if(!listeners.TryGetValue(typeof(T),out list)){list=new List<object>();listeners.Add(typeof(T),list);}list.Add(handler);return new Disposable {Remove=delegate {list.Remove(handler);}};}
        public void Publish<T>(T value) where T : class
        {List<object> list;if(listeners.TryGetValue(typeof(T),out list))foreach(object handler in new List<object>(list))((Action<T>)handler)(value);}
        ClockSnapshot IGameClock.GetSnapshot() { return new ClockSnapshot(); }
        public OperationResult TrySetSpeed(CommandContext context, GameSpeed speed) { return new OperationResult(); }
        public Guid AcquirePause(PauseReason reason) { return Guid.NewGuid(); }
        public void ReleasePause(Guid handle) { }
        public RandomState GetState(RandomStream stream) { return new RandomState(stream, 1); }
        private sealed class Cursor : IRandomCursor { private RandomState state; internal Cursor(RandomState state) { this.state = state; } public int NextInt(int a, int b) { state = new RandomState(state.Stream, state.State + 1); return a; } public float NextUnitFloat() { return 0; } public RandomState Capture() { return state; } }
        public IRandomCursor CreateCursor(RandomState state) { return new Cursor(state); }
    }
}
