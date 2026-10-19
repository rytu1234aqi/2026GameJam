using System;
using System.Collections.Generic;
using NUnit.Framework;
using Spotlight.Contracts;
using Spotlight.Combat;
namespace Spotlight.Tests.ElementModifiers
{
    public sealed class ElementModifierTests
    {
        ModifierFixture f;
        CombatServices c;
        long tick;
        sealed class Rules : IElementRuleService
        {
            public ProductionResult ResolveProduction(ProductionInput input) { return new ProductionResult(); }
            public ReactionPlan ResolveReactions(ReactionInput input) { return new ReactionPlan { Matches = new ReactionMatch[0], RemainingTokens = input.Tokens }; }
            public ProjectileModifierDefinition GetProjectileModifier(ElementType element) { return null; }
        }
        [SetUp] public void Setup()
        {
            f = new ModifierFixture();
            f.Actors[10] = new ActorSnapshot { Id = new EntityId(10), Kind = ActorKind.Tower, CurrentHp = 80, MaxHp = 100 };
            f.Statuses["buff"] = new StatusDefinition { Id = "buff", DurationSeconds = 2, TickIntervalSeconds = 1, MaxStacks = 3,
                AttackHastePerStack = .5f, OutgoingElementDamageBonusPerStack = new ElementAmounts(0, .5f, 0, 0, 0, 0),
                OutgoingGaugeBonusPerStack = new ElementAmounts(.5f, 0, 0, 0, 0, 0) };
            f.Statuses["incoming"] = new StatusDefinition { Id = "incoming", DurationSeconds = 2, MaxStacks = 1, IncomingGaugeBonusPerStack = new ElementAmounts(1, 0, 0, 0, 0, 0) };
            f.Statuses["blind"] = new StatusDefinition { Id = "blind", DurationSeconds = 2, MaxStacks = 1, AccuracyReductionPerStack = 1 };
            f.Statuses["base.water"] = new StatusDefinition { Id = "base.water", DurationSeconds = 2, MaxStacks = 3 };
            c = new CombatServices(f, f, f, f, f, f, f, new Rules(), f, f);
            c.BeginSession(new SessionStartContext { RunId = "modifier-run" });
            f.Publish(new PhaseChangedEvent { NewPhase = GamePhase.Night });
        }
        [TearDown] public void Cleanup() { c.EndSession(); }
        void Stage(double time, TickStage stage) { c.Tick(new TickContext(++tick, time, .1f, stage)); }
        void Apply(string id, long target)
        {
            c.Status.EnqueueStatus(new StatusApplyRequest { Source = new EntityId(10), Target = new EntityId(target), StatusId = id, Stacks = 1 });
            Stage(0, TickStage.DamageAndReaction);
        }
        DamagePacket Packet(string id, DamageOrigin origin = DamageOrigin.Tower)
        {
            return new DamagePacket { PacketId = id, Source = new EntityId(10), Target = new EntityId(2), Origin = origin,
                PhysicalDamage = 10, ElementDamage = new ElementAmounts(10, 10, 0, 0, 0, 0), Gauge = new ElementAmounts(1, 0, 0, 0, 0, 0) };
        }
        EffectBatchRequest Request(EffectSpec effect)
        {
            return new EffectBatchRequest { Context = f.Create(f.Revision), Source = new EntityId(10), SelectedTarget = new EntityId(2), Effects = new[] { effect } };
        }
        [Test] public void PrimaryDamageScalesOnlyMatchingElementAndExpiryRestoresModifiers()
        {
            Apply("buff", 10);
            Assert.AreEqual(1.5f, c.Status.GetModifiers(new EntityId(10)).AttackRateMultiplier);
            Assert.AreEqual(35, c.Damage.Preview(Packet("main")).FinalDamage);
            Assert.AreEqual(30, c.Damage.Preview(Packet("reaction", DamageOrigin.Reaction)).FinalDamage);
            Assert.AreEqual(30, c.Damage.Preview(Packet("dot", DamageOrigin.StatusDot)).FinalDamage);
            Stage(2.01, TickStage.StatusAndGauge);
            Assert.AreEqual(1, c.Status.GetModifiers(new EntityId(10)).AttackRateMultiplier);
            Assert.AreEqual(30, c.Damage.Preview(Packet("expired")).FinalDamage);
        }
        [Test] public void IncomingGaugeAlwaysAppliesAndOutgoingGaugeAppliesOnlyToPrimary()
        {
            Apply("buff", 10); Apply("incoming", 2);
            c.Damage.Enqueue(Packet("gauge-main")); Stage(0, TickStage.DamageAndReaction);
            Assert.AreEqual(3, c.Status.GetGauges(new EntityId(2))[0].Value);
            c.Status.EnqueueGauge(new EntityId(10), new EntityId(2), new ElementAmounts(1, 0, 0, 0, 0, 0)); Stage(0, TickStage.DamageAndReaction);
            Assert.AreEqual(5, c.Status.GetGauges(new EntityId(2))[0].Value);
        }
        [Test] public void MissIsRepeatableDoesNotDamageOrSupplyGaugeAndReceiptIsUnique()
        {
            Apply("blind", 10); var packet = Packet("miss");
            Assert.IsTrue(c.Damage.Preview(packet).Missed); Assert.IsTrue(c.Damage.Preview(packet).Missed);
            Assert.AreEqual(100, f.Actors[2].CurrentHp);
            c.Damage.Enqueue(packet); c.Damage.Enqueue(packet); Stage(0, TickStage.DamageAndReaction);
            Assert.AreEqual(100, f.Actors[2].CurrentHp); Assert.AreEqual(0, c.Status.GetGauges(new EntityId(2))[0].Value);
        }
        [TestCase(0f, 0)] [TestCase(1f, 1)]
        public void SourceActorProbabilityValidationAndRejectedCommitHaveNoSideEffects(float probability, int count)
        {
            var request = Request(new EffectSpec { Kind = EffectKind.ApplyStatus, TargetPolicy = EffectTargetPolicy.SourceActor, ReferenceId = "buff", Stacks = 1, Probability = probability });
            Assert.AreEqual(ErrorCode.None, c.Effects.Validate(request).Error);
            Assert.AreEqual(0, c.Status.GetStatuses(new EntityId(10)).Count);
            f.FailCommit = true; Assert.AreEqual(OperationState.Rejected, c.Effects.TryExecute(request).State);
            Assert.AreEqual(0, c.Status.GetStatuses(new EntityId(10)).Count);
            f.FailCommit = false; c.Effects.TryExecute(request); c.Effects.TryExecute(request);
            Assert.AreEqual(count, c.Status.GetStatuses(new EntityId(10)).Count);
            Assert.AreEqual(0, c.Status.GetStatuses(new EntityId(2)).Count);
        }
        [Test] public void FractionalProbabilityReplaysFromWaveSeedWithoutAdvancingRandomState()
        {
            string first = ProbabilityRun();
            c.BeginSession(new SessionStartContext { RunId = "replay" });
            f.Publish(new PhaseChangedEvent { NewPhase = GamePhase.Night });
            Assert.AreEqual(first, ProbabilityRun());
            Assert.IsTrue(first.Contains("0") && first.Contains("1"));
            Assert.AreEqual(1u, f.GetState(RandomStream.Wave).State);
        }
        string ProbabilityRun()
        {
            string outcomes = String.Empty;
            for (int i = 0; i < 16; i++)
            {
                var request = Request(new EffectSpec { Kind = EffectKind.ApplyStatus, TargetPolicy = EffectTargetPolicy.SourceActor, ReferenceId = "buff", Stacks = 1, Probability = .5f });
                Assert.AreEqual(ErrorCode.None, c.Effects.Validate(request).Error);
                c.Effects.TryExecute(request);
                outcomes += c.Status.GetStatuses(new EntityId(10)).Count == 0 ? "0" : "1";
                c.Status.ClearTransient(new EntityId(10));
            }
            return outcomes;
        }
        [Test] public void FriendlyAreaHealsCircularCentersClampsAndPulsesWithoutHealingEnemies()
        {
            f.Actors[1].CurrentHp = 99;
            f.Actors[11] = new ActorSnapshot { Id = new EntityId(11), Kind = ActorKind.Tower, CurrentHp = 50, MaxHp = 100, Position = new WorldPoint(.9f, .9f) };
            var request = Request(new EffectSpec { Kind = EffectKind.CreateArea, RadiusInCells = 1, DurationSeconds = 3, TickIntervalSeconds = 1,
                AreaEffects = new[] { new EffectSpec { Kind = EffectKind.Heal, TargetPolicy = EffectTargetPolicy.AllTowers, Amount = 2 }, new EffectSpec { Kind = EffectKind.Heal, TargetPolicy = EffectTargetPolicy.Spring, Amount = 2 } } });
            Assert.AreEqual(OperationState.Committed, c.Effects.TryExecute(request).State);
            Assert.AreEqual(82, f.Actors[10].CurrentHp); Assert.AreEqual(100, f.Actors[1].CurrentHp);
            Assert.AreEqual(50, f.Actors[11].CurrentHp); Assert.AreEqual(100, f.Actors[2].CurrentHp);
            Stage(1, TickStage.StatusAndGauge); Assert.AreEqual(84, f.Actors[10].CurrentHp);
        }
        [Test] public void AreaUsesEnemyPositionWhileAreaDamageUsesExplicitImpactPoint()
        {
            f.Actors[2].Position = new WorldPoint(3, 0);
            var request = Request(new EffectSpec
            {
                Kind = EffectKind.CreateArea, RadiusInCells = 1, DurationSeconds = 3, TickIntervalSeconds = 1,
                AreaEffects = new[] { new EffectSpec { Kind = EffectKind.Heal, TargetPolicy = EffectTargetPolicy.AllTowers, Amount = 2 } }
            });
            request.HasImpactPosition = true; request.ImpactPosition = new WorldPoint(0, 0);
            Assert.AreEqual(OperationState.Committed, c.Effects.TryExecute(request).State);
            Assert.AreEqual(3, c.GetAreaEffects()[0].Position.X);
            Assert.AreEqual(80, f.Actors[10].CurrentHp, "The tower is outside the enemy-centered healing area.");
            request = Request(new EffectSpec { Kind = EffectKind.AreaDamage, RadiusInCells = 1, PhysicalDamage = 10 });
            request.HasImpactPosition = true; request.ImpactPosition = new WorldPoint(0, 0);
            c.Effects.TryExecute(request); Stage(0, TickStage.DamageAndReaction);
            Assert.AreEqual(100, f.Actors[2].CurrentHp, "The primary enemy center is outside the impact-centered damage circle.");
        }
        [TestCase(0f, false)] [TestCase(1f, true)]
        public void NaturalAreaFreezeExpiresBeforeAreaAndDoesNotRefreshOnPulses(float probability, bool frozen)
        {
            f.Statuses["freeze"] = new StatusDefinition { Id = "freeze", DurationSeconds = 1.5f, MaxStacks = 1, PreventMovement = true, PreventAttack = true };
            var request = Request(new EffectSpec { Kind = EffectKind.CreateArea, RadiusInCells = 1, DurationSeconds = 6, TickIntervalSeconds = 1, LingeringSeconds = 2,
                AreaEffects = new[] { new EffectSpec { Kind = EffectKind.ApplyStatus, ReferenceId = "freeze", Stacks = 1, DurationOverride = 1.5f, UseAreaLease = false, Probability = probability } } });
            c.Effects.TryExecute(request);
            Assert.AreEqual(frozen, c.Status.GetModifiers(new EntityId(2)).PreventMovement);
            Stage(1, TickStage.StatusAndGauge);
            Assert.AreEqual(frozen, c.Status.GetModifiers(new EntityId(2)).PreventAttack);
            Stage(1.51, TickStage.StatusAndGauge);
            Assert.IsFalse(c.Status.GetModifiers(new EntityId(2)).PreventMovement);
            Assert.AreEqual(1, c.GetAreaEffects().Count);
        }
        [Test] public void HealingStatusUsesTicksAndSessionRestartClearsAllModifiers()
        {
            f.Statuses["buff"].HealPerTick = 3; Apply("buff", 10);
            Stage(1, TickStage.StatusAndGauge); Assert.AreEqual(83, f.Actors[10].CurrentHp);
            c.BeginSession(new SessionStartContext { RunId = "new-run" });
            Assert.AreEqual(1, c.Status.GetModifiers(new EntityId(10)).AttackRateMultiplier);
            Assert.AreEqual(0, c.Status.GetStatuses(new EntityId(10)).Count);
        }
    }
    public sealed class ModifierFixture : IGameCatalog, IBoardQuery, IWorldQuery, IStateTransactionService, ICommandContextFactory, IEntityIdService, IEventBus, IGameClock, IRandomService
    {
        public CellSnapshot[] Cells=new CellSnapshot[0];
        public readonly Dictionary<long, ActorSnapshot> Actors = new Dictionary<long, ActorSnapshot>();
        public readonly List<ReactionDefinition> Reactions = new List<ReactionDefinition>();
        public readonly List<ProductionRuleDefinition> Production = new List<ProductionRuleDefinition>();
        public readonly Dictionary<string, ElementDefinition> Elements = new Dictionary<string, ElementDefinition>();
        public ProjectileDefinition Projectile = new ProjectileDefinition { Id="test", SpeedInCellsPerSecond=1,LifetimeSeconds=20,CollisionRadiusInCells=.1f,PiercingEnabled=true,MaxHits=3,RetainedDamageMultiplier=.5f };
        public bool FailCommit; public long Revision { get; private set; }
        private long sequence = 10;
        public ModifierFixture()
        {
            Actors.Add(1, new ActorSnapshot { Id = new EntityId(1), Kind = ActorKind.Spring, CurrentHp = 100, MaxHp = 100, Targetable = true });
            Actors.Add(2, new ActorSnapshot { Id = new EntityId(2), Kind = ActorKind.Enemy, CurrentHp = 100, MaxHp = 100, Targetable = true, PhysicalReduction = 0f, ElementResistance = new ElementAmounts() });
            Elements.Add("elm.water", new ElementDefinition { Id = "elm.water", Element = ElementType.Water, BaseStatusId = "base.water", ProjectileModifier = new ProjectileModifierDefinition { Element = ElementType.Water } });
        }
        public GameSettings GetSettings() { return new GameSettings { MaxReactionActionsPerTick = 1, SpecialSkillId = "skill", LogicTicksPerGameSecond = 10 }; }
        public bool TryGetElement(string id, out ElementDefinition value) { return Elements.TryGetValue(id, out value); }
        public readonly Dictionary<string, StatusDefinition> Statuses = new Dictionary<string, StatusDefinition>();
        public bool TryGetStatus(string id, out StatusDefinition value) { return Statuses.TryGetValue(id, out value); }
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
