using System;
using System.Collections.Generic;
using NUnit.Framework;
using Spotlight.Contracts;
using Spotlight.Combat;
using Spotlight.Elements;
namespace Spotlight.Tests.ProjectileCombinations
{
    public sealed class ProjectileCombinationTests
    {
        ProjectileCombinationFixture f; CombatServices c; long tick;
        [SetUp] public void Setup()
        {
            f=new ProjectileCombinationFixture(); f.Actors[2].Position=new WorldPoint(12,.5f);
            f.Actors[10]=new ActorSnapshot {Id=new EntityId(10),Kind=ActorKind.Tower,CurrentHp=100,MaxHp=100,Targetable=true};
            f.Projectile.SpeedInCellsPerSecond=10; f.Projectile.RetainedDamageMultiplier=1;
            f.Elements["wind"]=new ElementDefinition {Id="wind",Element=ElementType.Wind,ProjectileModifier=new ProjectileModifierDefinition {Element=ElementType.Wind,PhysicalAdd=2}};
            f.Elements["fire"]=new ElementDefinition {Id="fire",Element=ElementType.Fire,ProjectileModifier=new ProjectileModifierDefinition {Element=ElementType.Fire,PhysicalAdd=3}};
            c=new CombatServices(f,f,f,f,f,f,f,new ElementRuleService(f,f),f);
            c.BeginSession(new SessionStartContext {RunId="run"}); f.Publish(new PhaseChangedEvent {NewPhase=GamePhase.Night});
        }
        [TearDown] public void Cleanup(){c.EndSession();}
        void Stage(double time,TickStage stage,float delta=.1f){c.Tick(new TickContext(++tick,time,delta,stage));}
        void Fly(float delta=.1f){Stage(tick*.1,TickStage.Projectiles,delta);}
        void Spawn(){Assert.AreEqual(OperationState.Committed,c.Projectiles.Spawn(new ProjectileSpawnRequest {Source=new EntityId(10),DefinitionId="test",Origin=new WorldPoint(0,.5f),AimPoint=new WorldPoint(30,.5f),PhysicalDamage=10}).State);}
        CellSnapshot Block(int x,long id,string element="wind"){return new CellSnapshot {Cell=new CellCoord(x,0),OccupantId=new EntityId(id),OccupantKind=OccupantKind.ElementBlock,DefinitionId=element};}
        void Rule(string id,ElementType a,ElementType b,int priority=0,params EffectSpec[] effects)
        {f.Reactions.Add(new ReactionDefinition {Id=id,Enabled=true,Context=ReactionContext.Projectile,A=a,B=b,ConsumeA=1,ConsumeB=1,Priority=priority,Effects=effects});}
        void WindRule(){Rule("wind.wind",ElementType.Wind,ElementType.Wind,0,new EffectSpec {Kind=EffectKind.ConfigurePiercing,MaxHits=3,RetainedDamageMultiplier=1},new EffectSpec {Kind=EffectKind.ApplyStatus,TargetPolicy=EffectTargetPolicy.SourceActor,ReferenceId="haste",Stacks=1});}
        [Test] public void DifferentWindBlocksActivateOnceAndSourceBuffExpires()
        {
            WindRule(); f.Cells=new[]{Block(0,20),Block(1,21),Block(2,22),Block(3,23)}; Spawn(); Fly();
            Assert.AreEqual(1,c.Projectiles.GetSnapshot()[0].AppliedReactionIds.Count); Assert.AreEqual(3,c.Projectiles.GetSnapshot()[0].MaxHits);
            Assert.AreEqual(1.3f,c.Status.GetModifiers(new EntityId(10)).AttackRateMultiplier,.0001);
            Fly(.3f); Assert.AreEqual(1,c.Projectiles.GetSnapshot()[0].AppliedReactionIds.Count);
            Stage(3,TickStage.StatusAndGauge); Assert.AreEqual(1,c.Status.GetModifiers(new EntityId(10)).AttackRateMultiplier,.0001);
            Spawn(); Fly(.4f); Assert.AreEqual(1,c.Status.GetStatuses(new EntityId(10)).Count);
        }
        [Test] public void SingleBlockAcrossFramesCannotSupplyTwoWindTokens()
        {WindRule();f.Projectile.SpeedInCellsPerSecond=1;f.Cells=new[]{Block(0,20)};Spawn();for(int i=0;i<8;i++)Fly();Assert.AreEqual(0,c.Projectiles.GetSnapshot()[0].AppliedReactionIds.Count);}
        [Test] public void BlockIdWinsOverChangedCellAndLegacyCellDeduplicates()
        {WindRule();f.Cells=new[]{Block(0,20)};Spawn();Fly(.05f);f.Cells=new[]{Block(1,20)};Fly(.1f);Assert.AreEqual(0,c.Projectiles.GetSnapshot()[0].AppliedReactionIds.Count);
         c.Projectiles.Clear();f.Cells=new[]{Block(0,0),Block(0,0)};Spawn();Fly(.05f);Assert.AreEqual(0,c.Projectiles.GetSnapshot()[0].AppliedReactionIds.Count);f.Cells=new[]{Block(1,0)};Fly();Assert.AreEqual(1,c.Projectiles.GetSnapshot()[0].AppliedReactionIds.Count);}
        [Test] public void OrdinaryFireAndWindBonusesApplyOncePerType()
        {f.Cells=new[]{Block(0,20),Block(1,21),Block(2,22,"fire"),Block(3,23,"fire")};f.Actors[2].Position=new WorldPoint(5,.5f);Spawn();Fly(.6f);Stage(1,TickStage.DamageAndReaction);Assert.AreEqual(85,f.Actors[2].CurrentHp,.0001);}
        [Test] public void NullModifierStillSuppliesValidCombinationInput()
        {f.Elements["wind"].ProjectileModifier=null;WindRule();f.Cells=new[]{Block(0,20),Block(1,21)};Spawn();Fly(.2f);Assert.AreEqual(1,c.Projectiles.GetSnapshot()[0].AppliedReactionIds.Count);}
        [Test] public void FailedSourceBuffRetainsTokensAndAvoidsPartialProjectileChange()
        {WindRule();f.Projectile.MaxHits=1;f.Cells=new[]{Block(0,20),Block(1,21)};Spawn();f.FailCommit=true;Fly(.2f);
         Assert.AreEqual(0,c.Projectiles.GetSnapshot()[0].AppliedReactionIds.Count);Assert.AreEqual(1,c.Projectiles.GetSnapshot()[0].MaxHits);Assert.AreEqual(0,c.Status.GetStatuses(new EntityId(10)).Count);
         f.FailCommit=false;f.Cells=new[]{Block(2,22)};Fly();Assert.AreEqual(1,c.Projectiles.GetSnapshot()[0].AppliedReactionIds.Count);Assert.AreEqual(3,c.Projectiles.GetSnapshot()[0].MaxHits);}
        [Test] public void PriorityConsumesTokensBeforeOtherMatchingRules()
        {Rule("high",ElementType.Fire,ElementType.Wind,10);Rule("low",ElementType.Fire,ElementType.Wind,0);Rule("later",ElementType.Wind,ElementType.Wind,0);
         f.Cells=new[]{Block(0,20,"fire"),Block(1,21),Block(2,22)};Spawn();Fly(.3f);CollectionAssert.AreEqual(new[]{"high"},c.Projectiles.GetSnapshot()[0].AppliedReactionIds);
         f.Cells=new[]{Block(3,23)};Fly();CollectionAssert.AreEqual(new[]{"high","later"},c.Projectiles.GetSnapshot()[0].AppliedReactionIds);}
        [Test] public void ExhaustedPiercingCannotBeReactivatedByLaterBlocks()
        {WindRule();f.Projectile.MaxHits=1;f.Actors[2].Position=new WorldPoint(.5f,.5f);f.Cells=new[]{Block(1,20),Block(2,21)};Spawn();Fly(.3f);Assert.AreEqual(1,c.Projectiles.GetSnapshot()[0].HitCount);Assert.AreEqual(0,c.Projectiles.GetSnapshot()[0].AppliedReactionIds.Count);Assert.AreEqual(0,c.Projectiles.GetSnapshot()[0].AppliedElementMask);Assert.Greater(c.Projectiles.GetSnapshot()[0].Position.X,2);}
        [Test] public void SelectedActorStatusWaitsUntilImpact()
        {Rule("slow",ElementType.Fire,ElementType.Wind,0,new EffectSpec {Kind=EffectKind.ApplyStatus,TargetPolicy=EffectTargetPolicy.SelectedActor,ReferenceId="base.water",Stacks=1});
         f.Cells=new[]{Block(0,20,"fire"),Block(1,21)};f.Actors[2].Position=new WorldPoint(4,.5f);Spawn();Fly(.2f);Assert.AreEqual(0,c.Status.GetStatuses(new EntityId(2)).Count);Fly(.3f);Assert.AreEqual(1,c.Status.GetStatuses(new EntityId(2)).Count);Assert.AreEqual(.8f,c.Status.GetModifiers(new EntityId(2)).MoveMultiplier,.0001);}
    }
    public sealed class ProjectileCombinationFixture : IGameCatalog, IBoardQuery, IWorldQuery, IStateTransactionService, ICommandContextFactory, IEntityIdService, IEventBus, IGameClock, IRandomService
    {
        public CellSnapshot[] Cells=new CellSnapshot[0];
        public readonly Dictionary<long, ActorSnapshot> Actors = new Dictionary<long, ActorSnapshot>();
        public readonly List<ReactionDefinition> Reactions = new List<ReactionDefinition>();
        public readonly List<ProductionRuleDefinition> Production = new List<ProductionRuleDefinition>();
        public readonly Dictionary<string, ElementDefinition> Elements = new Dictionary<string, ElementDefinition>();
        public ProjectileDefinition Projectile = new ProjectileDefinition { Id="test", SpeedInCellsPerSecond=1,LifetimeSeconds=20,CollisionRadiusInCells=.1f,PiercingEnabled=true,MaxHits=3,RetainedDamageMultiplier=.5f };
        public bool FailCommit; public long Revision { get; private set; }
        private long sequence = 10;
        public ProjectileCombinationFixture()
        {
            Actors.Add(1, new ActorSnapshot { Id = new EntityId(1), Kind = ActorKind.Spring, CurrentHp = 100, MaxHp = 100, Targetable = true });
            Actors.Add(2, new ActorSnapshot { Id = new EntityId(2), Kind = ActorKind.Enemy, CurrentHp = 100, MaxHp = 100, Targetable = true, PhysicalReduction = 0f, ElementResistance = new ElementAmounts() });
            Elements.Add("elm.water", new ElementDefinition { Id = "elm.water", Element = ElementType.Water, BaseStatusId = "base.water", ProjectileModifier = new ProjectileModifierDefinition { Element = ElementType.Water } });
        }
        public GameSettings GetSettings() { return new GameSettings { MaxReactionActionsPerTick = 1, SpecialSkillId = "skill", LogicTicksPerGameSecond = 10 }; }
        public bool TryGetElement(string id, out ElementDefinition value) { return Elements.TryGetValue(id, out value); }
        public bool TryGetStatus(string id, out StatusDefinition value)
        { value = null; if (id == "haste") value = new StatusDefinition { Id=id, DurationSeconds=1, MaxStacks=1, AttackHastePerStack=.3f, RefreshPolicy=StatusRefreshPolicy.RefreshAllLayers }; if (id == "base.water") value = new StatusDefinition { Id = id, ElementTag = ElementType.Water, MoveSlowPerStack=.2f, IsReactionInput = false, MaxStacks = 5, DurationSeconds = 3, RefreshPolicy = StatusRefreshPolicy.IndependentLayers }; if (id == "dot") value = new StatusDefinition { Id = id, IsReactionDamage = true, MaxStacks = 5, DurationSeconds = 1, TickIntervalSeconds = 1, DotElementPerStack = new ElementAmounts(0, 0, 0, 0, 0, 2) }; return value != null; }
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



