using System;
using System.Collections.Generic;
using Spotlight.Contracts;

namespace Spotlight.Combat
{
    /// <summary>Pure managed combat runtime. Canonical state writes always go through a transaction.</summary>
    public sealed class CombatServices : ISessionModule, ITickSystem
    {
        internal readonly IGameCatalog Catalog;
        internal readonly IBoardQuery Board;
        internal readonly IWorldQuery World;
        internal readonly IStateTransactionService Transactions;
        internal readonly ICommandContextFactory Commands;
        internal readonly IEntityIdService Ids;
        internal readonly IEventBus Events;
        internal readonly IElementRuleService Rules;
        internal readonly IGameClock Clock;
        private readonly DamageRuntime damage;
        private readonly StatusRuntime status;
        private readonly EffectRuntime effects;
        internal readonly AdvancedEffectRuntime Advanced;
        private readonly TowerRuntime towers;
        private readonly ProjectileRuntime projectiles;
        private readonly SkillRuntime skill;
        private readonly Dictionary<string, double> contacts = new Dictionary<string, double>();
        private readonly HashSet<long> reactionDirty = new HashSet<long>();
        private sealed class ReactionWork { internal EntityId Actor, Source; internal ReactionMatch Match; internal float Multiplier; internal WorldPoint Position; }
        private readonly Queue<ReactionWork> reactionQueue = new Queue<ReactionWork>();
        public IDamageService Damage { get { return damage; } }
        public IStatusService Status { get { return status; } }
        public IEffectService Effects { get { return effects; } }
        public ITowerService Towers { get { return towers; } }
        public IProjectileService Projectiles { get { return projectiles; } }
        public ISpecialSkillService Skill { get { return skill; } }
        public IReadOnlyList<ITickSystem> TickSystems { get; private set; }
        public ModuleId Module { get { return ModuleId.Combat; } }
        public IReadOnlyList<TickStage> Stages { get; private set; }
        public int PendingCount { get { return damage.QueueCount + status.PendingCount + reactionDirty.Count + reactionQueue.Count + (skill.Pending ? 1 : 0); } }
        internal double Time;
        internal string RunId;
        internal GamePhase Phase;
        private IDisposable phaseSubscription;
        private readonly IRandomService random;
        private uint probabilitySeed;
        internal long EffectSequence;
        internal bool Roll(float probability, string key)
        {
            if (probability <= 0) return false;
            if (probability >= 1) return true;
            unchecked
            {
                uint value = 2166136261u ^ probabilitySeed;
                foreach (char c in key ?? String.Empty) value = (value ^ c) * 16777619u;
                value ^= value >> 16; value *= 0x7feb352du;
                value ^= value >> 15; value *= 0x846ca68bu; value ^= value >> 16;
                return (value >> 8) / 16777216f < probability;
            }
        }
        public CombatServices(IGameCatalog catalog, IBoardQuery board, IWorldQuery world, IStateTransactionService transactions, ICommandContextFactory commands, IEntityIdService ids, IEventBus events, IElementRuleService rules, IGameClock clock, IRandomService random = null)
        {
            if (catalog == null || board == null || world == null || transactions == null || commands == null || ids == null || events == null || rules == null || clock == null) throw new ArgumentNullException();
            // The composition is constructed before a run initializes its random streams.
            this.random = random; probabilitySeed = 0u;
            Catalog = catalog; Board = board; World = world; Transactions = transactions; Commands = commands; Ids = ids; Events = events; Rules = rules; Clock = clock;
            status = new StatusRuntime(this); damage = new DamageRuntime(this, status); Advanced = new AdvancedEffectRuntime(this, damage, status); effects = new EffectRuntime(this, damage, status);
            projectiles = new ProjectileRuntime(this, damage); towers = new TowerRuntime(this, projectiles, status); skill = new SkillRuntime(this);
            TickSystems = new List<ITickSystem> { this }.AsReadOnly();
            Stages = new List<TickStage> { TickStage.PlayerCommands, TickStage.StatusAndGauge, TickStage.Attacks, TickStage.Projectiles, TickStage.DamageAndReaction, TickStage.DeathAndLoot }.AsReadOnly();
        }
        public void BeginSession(SessionStartContext context)
        {
            EndSession(); probabilitySeed = random == null ? 0u : random.GetState(RandomStream.Wave).State; RunId = context.RunId; Time = Clock.GetSnapshot().GameTime; Phase = GamePhase.Build;
            phaseSubscription = Events.Subscribe<PhaseChangedEvent>(delegate(PhaseChangedEvent e) {
                Phase = e.NewPhase;
                if (e.NewPhase == GamePhase.Night) ResetNight();
                else if(e.NewPhase==GamePhase.NightResult||e.NewPhase==GamePhase.Ending||e.NewPhase==GamePhase.GameOver||e.NewPhase==GamePhase.Menu)EndNightTransient();
            });
            skill.ResetForNight(); towers.SynchronizeBoard();
        }
        public void EndSession()
        {
            if (phaseSubscription != null) phaseSubscription.Dispose(); phaseSubscription = null;
            Advanced.ResetSession(); damage.Clear(); status.Clear(); effects.Clear(); projectiles.Clear(); towers.Clear(); contacts.Clear(); reactionDirty.Clear(); reactionQueue.Clear(); skill.Clear(); reactionTickIndex = -1; EffectSequence = 0; RunId = null;
        }
        public IReadOnlyList<AreaEffectSnapshot> GetAreaEffects() { return Advanced.GetSnapshot(); }
        public void ResetNight() { probabilitySeed = random == null ? 0u : random.GetState(RandomStream.Wave).State; EffectSequence = 0; Advanced.Clear(); towers.ResetNightCooldowns(); skill.ResetForNight(); contacts.Clear(); }
        internal void EndNightTransient()
        {
            // Session receipts and monotonically allocated IDs survive normal night boundaries.
            Advanced.Clear();status.ClearNightTransient();contacts.Clear();reactionDirty.Clear();reactionQueue.Clear();skill.EndNightTransient();projectiles.Clear();
        }
        public string GetProjectileDefinitionId(long id) { return projectiles.GetDefinitionId(id); }
        internal OperationResult Result(OperationState state, ErrorCode error, string key)
        { return new OperationResult(state, error, Guid.Empty, Transactions.Revision, key); }
        internal StateMutationBatch Batch(string reason)
        { return new StateMutationBatch { Context = Commands.Create(Transactions.Revision), Reason = reason }; }
        internal void MarkReaction(EntityId actor) { reactionDirty.Add(actor.Value); }
        internal float ReactionMultiplier { get { return skill.Multiplier; } }
        public void Tick(TickContext context)
        {
            Time = context.GameTime;
            if (context.Stage == TickStage.PlayerCommands) skill.ExecutePending();
            else if (context.Stage == TickStage.StatusAndGauge) { skill.Advance(); if (Phase == GamePhase.Night) Advanced.Advance(); status.Advance(context.DeltaSeconds); }
            else if (context.Stage == TickStage.Attacks) { towers.Advance(context.DeltaSeconds); Contact(); }
            else if (context.Stage == TickStage.Projectiles) projectiles.Advance(context.DeltaSeconds);
            else if (context.Stage == TickStage.DamageAndReaction)
            {
                if (reactionTickIndex != context.TickIndex) { reactionTickIndex = context.TickIndex; reactionTickBudget = Math.Max(1, Catalog.GetSettings().MaxReactionActionsPerTick); }
                // Deferred actors retain their live layers until their prepared effect batch succeeds.
                ResolveReactionWork(); status.Drain(); damage.Drain(); ResolveReactionWork();
            }
            else if (context.Stage == TickStage.DeathAndLoot) towers.RemoveDead();
        }
        private int reactionTickBudget;
        private long reactionTickIndex = -1;
        private void ResolveReactionWork()
        {
            List<long> actors = new List<long>(reactionDirty); actors.Sort();
            foreach (long id in actors)
            {
                EntityId actor = new EntityId(id); ActorSnapshot target;
                if (!World.TryGetActor(actor, out target) || target.CurrentHp <= 0) { reactionDirty.Remove(id); continue; }
                ReactionPlan plan = Rules.ResolveReactions(new ReactionInput { Context = ReactionContext.EnemyStatus, Tokens = status.Tokens(actor), AppliedRuleIds = new string[0] });
                foreach (ReactionMatch match in plan.Matches)
                {
                    if (!status.HasLayers(actor, match.ConsumedTokenIds)) continue;
                    reactionQueue.Enqueue(new ReactionWork { Actor = actor, Source = status.SourceFor(actor, match.ConsumedTokenIds), Match = match, Multiplier = ReactionMultiplier, Position = target.Position });
                    status.Reserve(match.ConsumedTokenIds);
                }
                reactionDirty.Remove(id);
            }
            while (reactionTickBudget > 0 && reactionQueue.Count > 0)
            {
                ReactionWork work = reactionQueue.Peek(); ActorSnapshot target;
                if (!World.TryGetActor(work.Actor, out target) || target.CurrentHp <= 0 || !status.HasLayers(work.Actor, work.Match.ConsumedTokenIds))
                { reactionQueue.Dequeue(); status.Release(work.Match.ConsumedTokenIds); continue; }
                OperationResult result = effects.TryExecute(new EffectBatchRequest { Context = Commands.Create(Transactions.Revision), Source = work.Source, SelectedTarget = work.Actor,
                    Origin = DamageOrigin.Reaction, ReactionId = work.Match.RuleId, ReactionDamageMultiplier = work.Multiplier, Effects = work.Match.Effects });
                if (result.State != OperationState.Committed) break;
                reactionQueue.Dequeue(); status.Consume(work.Actor, work.Match.ConsumedTokenIds); status.Release(work.Match.ConsumedTokenIds); reactionTickBudget--;
                Events.Publish(new ReactionResolvedEvent { Target = work.Actor, RuleId = work.Match.RuleId, Context = ReactionContext.EnemyStatus, Position = work.Position });
            }
        }
        private void Contact()
        {
            HashSet<string> retained = new HashSet<string>();
            foreach (ActorSnapshot actor in World.GetActors(ActorKind.Enemy))
            {
                if (actor.CurrentHp <= 0 || !actor.Targetable) continue;
                CellCoord end; bool hasEnd = Board.TryWorldToCell(actor.Position, out end);
                var contactCells = new List<CellSnapshot>(); var visited = new HashSet<CellCoord>();
                foreach (CellSnapshot crossed in Board.TraceSegment(actor.PreviousPosition, actor.Position))
                    if (visited.Add(crossed.Cell)) contactCells.Add(crossed);
                // Segment queries can omit a stationary actor or an exact endpoint. Its current
                // cell still supplies ongoing contact, governed by the same contact cooldown.
                CellSnapshot current; if (hasEnd && visited.Add(end) && Board.TryGetCell(end, out current)) contactCells.Add(current);
                foreach (CellSnapshot cell in contactCells)
                {
                    if (cell.OccupantKind != OccupantKind.ElementBlock) continue;
                    ElementDefinition definition; if (!Catalog.TryGetElement(cell.DefinitionId, out definition) || definition.ContactEffects == null || definition.ContactEffects.Count == 0) continue;
                    string key = actor.Id.Value + ":" + cell.OccupantId.Value; double next;
                    bool staying = hasEnd && end.Equals(cell.Cell); if (staying) retained.Add(key);
                    if (!contacts.TryGetValue(key, out next) || Time >= next)
                    {
                        OperationResult r = effects.TryExecute(new EffectBatchRequest { Context = Commands.Create(Transactions.Revision), Source = cell.OccupantId, SelectedTarget = actor.Id, Origin = DamageOrigin.Event, Effects = definition.ContactEffects });
                        if (r.State == OperationState.Committed && staying) contacts[key] = Time + definition.ContactIntervalSeconds;
                    }
                }
            }
            List<string> remove = new List<string>(); foreach (string key in contacts.Keys) if (!retained.Contains(key)) remove.Add(key); foreach (string key in remove) contacts.Remove(key);
        }
    }

    internal static class CombatMath
    {
        internal static float Get(ElementAmounts a, int i) { switch (i) { case 0: return a.Water; case 1: return a.Fire; case 2: return a.Earth; case 3: return a.Wood; case 4: return a.Wind; default: return a.Thunder; } }
        internal static ElementAmounts Amounts(float[] a) { return new ElementAmounts(a[0], a[1], a[2], a[3], a[4], a[5]); }
        internal static ElementAmounts Add(ElementAmounts a, ElementAmounts b, bool clamp)
        { float[] c = new float[6]; for (int i = 0; i < 6; i++) { c[i] = Get(a, i) + Get(b, i); if (clamp) c[i] = Math.Max(0, c[i]); } return Amounts(c); }
        internal static ElementAmounts Scale(ElementAmounts a, float m) { float[] c = new float[6]; for (int i = 0; i < 6; i++) c[i] = Math.Max(0, Get(a, i) * m); return Amounts(c); }
        internal static float Clamp01(float value) { return Math.Max(0, Math.Min(1, value)); }
        internal static bool Finite(float f) { return !Single.IsNaN(f) && !Single.IsInfinity(f); }
        internal static bool ValidAmounts(ElementAmounts a, bool negative) { for (int i = 0; i < 6; i++) if (!Finite(Get(a, i)) || (!negative && Get(a, i) < 0)) return false; return true; }
        internal static bool Zero(ElementAmounts a) { for (int i = 0; i < 6; i++) if (Get(a, i) != 0) return false; return true; }
        internal static float DistanceSquared(WorldPoint a, WorldPoint b) { float x = a.X - b.X, y = a.Y - b.Y; return x * x + y * y; }
        internal static float SegmentCircle(WorldPoint a, WorldPoint b, WorldPoint center, float radius)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, fx = a.X - center.X, fy = a.Y - center.Y;
            float c = fx * fx + fy * fy - radius * radius; if (c <= 0) return 0;
            float q = dx * dx + dy * dy; if (q == 0) return Single.PositiveInfinity;
            float z = 2 * (fx * dx + fy * dy), discriminant = z * z - 4 * q * c; if (discriminant < 0) return Single.PositiveInfinity;
            float t = (-z - (float)Math.Sqrt(discriminant)) / (2 * q); return t >= 0 && t <= 1 ? t : Single.PositiveInfinity;
        }
        internal static ActorMutation Health(ActorSnapshot actor, float hp, float max)
        { return new ActorMutation { Kind = ActorMutationKind.SetHealth, ActorId = actor.Id, CurrentHp = hp, MaxHp = max }; }
    }

    internal sealed class DamageRuntime : IDamageService
    {
        private readonly CombatServices owner; private readonly StatusRuntime statuses;
        private readonly Queue<DamagePacket> queue = new Queue<DamagePacket>();
        private readonly Dictionary<string, DamagePacket> accepted = new Dictionary<string, DamagePacket>(StringComparer.Ordinal);
        internal DamageRuntime(CombatServices owner, StatusRuntime statuses) { this.owner = owner; this.statuses = statuses; }
        internal int QueueCount { get { return queue.Count; } }
        public int PendingCount { get { return owner.PendingCount; } }
        internal void Clear() { queue.Clear(); accepted.Clear(); }
        internal static DamagePacket Copy(DamagePacket p) { return new DamagePacket { PacketId = p.PacketId, Source = p.Source, Target = p.Target, Origin = p.Origin, ReactionId = p.ReactionId, PhysicalDamage = p.PhysicalDamage, ElementDamage = p.ElementDamage, Gauge = p.Gauge, DamageMultiplier = p.DamageMultiplier }; }
        private static bool Same(DamagePacket a, DamagePacket b)
        { return a.Source.Equals(b.Source) && a.Target.Equals(b.Target) && a.Origin == b.Origin && a.ReactionId == b.ReactionId && a.PhysicalDamage == b.PhysicalDamage && a.ElementDamage.Equals(b.ElementDamage) && a.Gauge.Equals(b.Gauge) && a.DamageMultiplier == b.DamageMultiplier; }
        internal ErrorCode Check(DamagePacket p)
        {
            if (p == null || String.IsNullOrEmpty(p.PacketId) || !CombatMath.Finite(p.PhysicalDamage) || p.PhysicalDamage < 0 || !CombatMath.Finite(p.DamageMultiplier) || p.DamageMultiplier <= 0 || !CombatMath.ValidAmounts(p.ElementDamage, false) || !CombatMath.ValidAmounts(p.Gauge, false)) return ErrorCode.InvalidArgument;
            ActorSnapshot actor; if (!owner.World.TryGetActor(p.Target, out actor)) return ErrorCode.InvalidTarget;
            if (actor.CurrentHp <= 0) return ErrorCode.TargetDead;
            return ErrorCode.None;
        }
        public OperationResult Enqueue(DamagePacket packet)
        {
            DamagePacket previous; if (packet != null && packet.PacketId != null && accepted.TryGetValue(packet.PacketId, out previous)) return owner.Result(Same(previous, packet) ? OperationState.Committed : OperationState.Rejected, Same(previous, packet) ? ErrorCode.None : ErrorCode.DuplicateCommand, "damage.duplicate");
            ErrorCode error = Check(packet); if (error != ErrorCode.None) return owner.Result(OperationState.Rejected, error, "damage.invalid");
            DamagePacket copy = Copy(packet); accepted.Add(copy.PacketId, copy); queue.Enqueue(copy); return owner.Result(OperationState.Queued, ErrorCode.None, "damage.queued");
        }
        public DamageResult Preview(DamagePacket packet)
        {
            ErrorCode error = Check(packet); ActorSnapshot actor;
            if (error != ErrorCode.None || !owner.World.TryGetActor(packet.Target, out actor)) return new DamageResult { Error = error, PacketId = packet == null ? null : packet.PacketId };
            return Calculate(packet, actor);
        }
        internal DamageResult Calculate(DamagePacket p, ActorSnapshot actor)
        {
            CombatModifiers m = statuses.GetModifiers(actor.Id);
            bool primary = p.Origin == DamageOrigin.Tower || p.Origin == DamageOrigin.Enemy;
            CombatModifiers source = primary ? statuses.GetModifiers(p.Source) : new CombatModifiers();
            if (primary && !owner.Roll(source.AccuracyMultiplier, "accuracy:" + p.PacketId))
                return new DamageResult { Error = ErrorCode.None, PacketId = p.PacketId, Source = p.Source, Target = p.Target, RemainingHp = actor.CurrentHp, Missed = true };
            float final = Math.Max(0, p.PhysicalDamage * p.DamageMultiplier) * (1 - CombatMath.Clamp01(actor.PhysicalReduction - m.ArmorBreak));
            for (int i = 0; i < 6; i++) final += Math.Max(0, CombatMath.Get(p.ElementDamage, i) * p.DamageMultiplier * (1 + CombatMath.Get(source.OutgoingElementDamageBonus, i))) * (1 - CombatMath.Clamp01(CombatMath.Get(actor.ElementResistance, i) - CombatMath.Get(m.ResistanceBreak, i)));
            return new DamageResult { Error = ErrorCode.None, PacketId = p.PacketId, Source = p.Source, Target = p.Target, FinalDamage = final, RemainingHp = Math.Max(0, actor.CurrentHp - final), NewlyKilled = actor.CurrentHp > 0 && actor.CurrentHp - final <= 0 };
        }
        internal void Drain()
        {
            int count = queue.Count;
            for (int i = 0; i < count; i++)
            {
                DamagePacket p = queue.Peek(); DamageResult r = Preview(p);
                if (r.Error != ErrorCode.None) { queue.Dequeue(); continue; }
                ActorSnapshot actor; owner.World.TryGetActor(p.Target, out actor);
                StateMutationBatch batch = owner.Batch("combat.damage"); batch.Actors = new ActorMutation[] { CombatMath.Health(actor, r.RemainingHp, actor.MaxHp) };
                OperationResult commit = owner.Transactions.TryCommit(batch); if (commit.State != OperationState.Committed) break;
                queue.Dequeue(); if (!r.Missed) statuses.ApplyGauge(p.Source, p.Target, p.Gauge, p.Origin == DamageOrigin.Tower || p.Origin == DamageOrigin.Enemy); owner.Events.Publish(new DamageAppliedEvent { Result = r });
            }
        }
    }

    internal sealed class StatusRuntime : IStatusService
    {
        private sealed class Gauge { internal float Value; internal double LastHit; }
        private sealed class Layer { internal long Id; internal EntityId Source; internal string Reaction, Scope; internal double Expiry, NextTick; internal float Multiplier; }
        private sealed class Group { internal string Id; internal List<Layer> Layers = new List<Layer>(); }
        private readonly CombatServices owner;
        private readonly Dictionary<long, Gauge[]> gauges = new Dictionary<long, Gauge[]>();
        private readonly Dictionary<long, List<Group>> groups = new Dictionary<long, List<Group>>();
        private readonly Queue<StatusApplyRequest> statusQueue = new Queue<StatusApplyRequest>();
        private sealed class GaugeWork { internal EntityId Source, Target; internal ElementAmounts Amounts; }
        private readonly Queue<GaugeWork> gaugeQueue = new Queue<GaugeWork>();
        private long layerSequence;
        private readonly HashSet<long> reserved = new HashSet<long>();
        internal int PendingCount { get { return statusQueue.Count + gaugeQueue.Count; } }
        internal StatusRuntime(CombatServices owner) { this.owner = owner; }
        internal void Clear() { ClearNightTransient(); layerSequence = 0; }
        internal void ClearNightTransient() { gauges.Clear(); groups.Clear(); statusQueue.Clear(); gaugeQueue.Clear(); reserved.Clear(); }
        internal void Reserve(IReadOnlyList<long> ids) { foreach (long id in ids) reserved.Add(id); }
        internal void Release(IReadOnlyList<long> ids) { foreach (long id in ids) reserved.Remove(id); }
        public void ClearTransient(EntityId actor) {
            List<Group> list; if (groups.TryGetValue(actor.Value, out list)) foreach (Group g in list) foreach (Layer l in g.Layers) reserved.Remove(l.Id);
            gauges.Remove(actor.Value); groups.Remove(actor.Value);
        }
        private GaugeDefinition Definition(int index)
        { foreach (GaugeDefinition d in owner.Catalog.GetGaugeDefinitions()) if ((int)d.Element == index + 1) return d; return null; }
        private Gauge[] GetGaugeState(EntityId actor)
        { Gauge[] state; if (!gauges.TryGetValue(actor.Value, out state)) { state = new Gauge[6]; for (int i = 0; i < 6; i++) state[i] = new Gauge { LastHit = Double.NegativeInfinity }; gauges.Add(actor.Value, state); } return state; }
        public IReadOnlyList<GaugeSnapshot> GetGauges(EntityId actor)
        {
            List<GaugeSnapshot> result = new List<GaugeSnapshot>(); Gauge[] state; gauges.TryGetValue(actor.Value, out state);
            for (int i = 0; i < 6; i++) { GaugeDefinition d = Definition(i); if (d == null) continue; result.Add(new GaugeSnapshot { Element = d.Element, Value = state == null ? 0 : state[i].Value, Threshold = d.Threshold, DelayRemaining = state == null ? 0 : (float)Math.Max(0, d.DecayDelaySeconds - (owner.Time - state[i].LastHit)) }); }
            return result.AsReadOnly();
        }
        public IReadOnlyList<StatusSnapshot> GetStatuses(EntityId actor)
        {
            List<StatusSnapshot> result = new List<StatusSnapshot>(); List<Group> list;
            if (groups.TryGetValue(actor.Value, out list)) foreach (Group g in list)
            {
                List<StatusLayerSnapshot> layers = new List<StatusLayerSnapshot>();
                foreach (Layer l in g.Layers) layers.Add(new StatusLayerSnapshot { LayerId = l.Id, Source = l.Source, ReactionId = l.Reaction, ScopeId = l.Scope, RemainingSeconds = (float)Math.Max(0, l.Expiry - owner.Time), SnapshotDamageMultiplier = l.Multiplier });
                result.Add(new StatusSnapshot { DefinitionId = g.Id, Target = actor, Layers = layers.AsReadOnly() });
            }
            return result.AsReadOnly();
        }
        public CombatModifiers GetModifiers(EntityId actor)
        {
            ActorSnapshot living;
            if (!owner.World.TryGetActor(actor, out living) || living.CurrentHp <= 0) return new CombatModifiers();
            CombatModifiers result = new CombatModifiers(); float[] resistance = new float[6]; List<Group> list;
            if (groups.TryGetValue(actor.Value, out list)) foreach (Group g in list)
            {
                StatusDefinition d; if (!owner.Catalog.TryGetStatus(g.Id, out d)) continue; int count = g.Layers.FindAll(delegate(Layer l) { return l.Expiry > owner.Time; }).Count;
                result.MoveMultiplier -= d.MoveSlowPerStack * count; result.AttackRateMultiplier += (d.AttackHastePerStack - d.AttackSlowPerStack) * count; result.AccuracyMultiplier -= d.AccuracyReductionPerStack * count;
                result.OutgoingElementDamageBonus = CombatMath.Add(result.OutgoingElementDamageBonus, CombatMath.Scale(d.OutgoingElementDamageBonusPerStack, count), false);
                result.OutgoingGaugeBonus = CombatMath.Add(result.OutgoingGaugeBonus, CombatMath.Scale(d.OutgoingGaugeBonusPerStack, count), false);
                result.IncomingGaugeBonus = CombatMath.Add(result.IncomingGaugeBonus, CombatMath.Scale(d.IncomingGaugeBonusPerStack, count), false); result.ArmorBreak += d.ArmorBreakPerStack * count;
                result.PreventMovement |= d.PreventMovement && count > 0; result.PreventAttack |= d.PreventAttack && count > 0;
                for (int i = 0; i < 6; i++) resistance[i] += CombatMath.Get(d.ResistanceBreakPerStack, i) * count;
            }
            result.AccuracyMultiplier = CombatMath.Clamp01(result.AccuracyMultiplier);
            result.MoveMultiplier = Math.Max(0.1f, result.MoveMultiplier); result.AttackRateMultiplier = Math.Max(0.1f, result.AttackRateMultiplier); result.ResistanceBreak = CombatMath.Amounts(resistance); return result;
        }
        internal ErrorCode CheckGauge(EntityId target, ElementAmounts amounts)
        {
            ActorSnapshot a; if (!owner.World.TryGetActor(target, out a) || a.Kind != ActorKind.Enemy) return ErrorCode.InvalidTarget;
            if (a.CurrentHp <= 0) return ErrorCode.TargetDead;
            if (!CombatMath.ValidAmounts(amounts, true)) return ErrorCode.InvalidArgument;
            for (int i = 0; i < 6; i++) if (CombatMath.Get(amounts, i) > 0)
            { GaugeDefinition d = Definition(i); if (d == null || d.Threshold <= 0 || d.Threshold > d.MaxValue || d.IncomingMultiplier < 0) return ErrorCode.InvalidDefinition; if (FindBaseStatus((ElementType)(i + 1)) == null) return ErrorCode.InvalidDefinition; }
            return ErrorCode.None;
        }
        private string FindBaseStatus(ElementType element)
        {
            string[] ids = { "elm.water", "elm.fire", "elm.earth", "elm.wood", "elm.wind", "elm.thunder" }; ElementDefinition direct;
            if ((int)element >= 1 && (int)element <= 6 && owner.Catalog.TryGetElement(ids[(int)element - 1], out direct) && !String.IsNullOrEmpty(direct.BaseStatusId)) { StatusDefinition sd; if (owner.Catalog.TryGetStatus(direct.BaseStatusId, out sd)) return direct.BaseStatusId; }
            foreach (ProductionRuleDefinition r in owner.Catalog.GetProductionRules())
            { ElementDefinition d; if (owner.Catalog.TryGetElement(r.OutputElementId, out d) && d.Element == element && !String.IsNullOrEmpty(d.BaseStatusId)) { StatusDefinition sd; if (owner.Catalog.TryGetStatus(d.BaseStatusId, out sd)) return d.BaseStatusId; } }
            return null;
        }
        internal ErrorCode CheckStatus(StatusApplyRequest request)
        {
            if (request == null || request.Stacks <= 0 || !CombatMath.Finite(request.DurationOverride) || request.DurationOverride < 0 || !CombatMath.Finite(request.SnapshotDamageMultiplier) || request.SnapshotDamageMultiplier <= 0) return ErrorCode.InvalidArgument;
            ActorSnapshot actor; if (!owner.World.TryGetActor(request.Target, out actor) || (actor.Kind != ActorKind.Enemy && actor.Kind != ActorKind.Tower)) return ErrorCode.InvalidTarget;
            if (actor.CurrentHp <= 0) return ErrorCode.TargetDead;
            StatusDefinition d; if (!owner.Catalog.TryGetStatus(request.StatusId, out d) || d.MaxStacks <= 0 || d.DurationSeconds <= 0) return ErrorCode.InvalidDefinition;
            if (actor.Kind == ActorKind.Tower && d.IsReactionInput) return ErrorCode.InvalidTarget;
            return ErrorCode.None;
        }
        public OperationResult EnqueueGauge(EntityId source, EntityId target, ElementAmounts amounts)
        { ErrorCode e = CheckGauge(target, amounts); if (e != ErrorCode.None) return owner.Result(OperationState.Rejected, e, "gauge.invalid"); gaugeQueue.Enqueue(new GaugeWork { Source = source, Target = target, Amounts = amounts }); return owner.Result(OperationState.Queued, ErrorCode.None, "gauge.queued"); }
        public OperationResult EnqueueStatus(StatusApplyRequest request)
        {
            ErrorCode e = CheckStatus(request); if (e != ErrorCode.None) return owner.Result(OperationState.Rejected, e, "status.invalid");
            statusQueue.Enqueue(Copy(request)); return owner.Result(OperationState.Queued, ErrorCode.None, "status.queued");
        }
        internal static StatusApplyRequest Copy(StatusApplyRequest r) { return new StatusApplyRequest { Source = r.Source, Target = r.Target, StatusId = r.StatusId, ReactionId = r.ReactionId, ScopeId = r.ScopeId, Stacks = r.Stacks, DurationOverride = r.DurationOverride, SnapshotDamageMultiplier = r.SnapshotDamageMultiplier }; }
        internal void Drain()
        { while (gaugeQueue.Count > 0) { GaugeWork w = gaugeQueue.Dequeue(); ApplyGauge(w.Source, w.Target, w.Amounts); } while (statusQueue.Count > 0) ApplyStatus(statusQueue.Dequeue()); }
        internal void ApplyGauge(EntityId source, EntityId target, ElementAmounts amounts, bool outgoing = false)
        {
            if (CheckGauge(target, amounts) != ErrorCode.None) return;
            Gauge[] state = GetGaugeState(target);
            CombatModifiers incomingMods = GetModifiers(target);
            CombatModifiers outgoingMods = outgoing ? GetModifiers(source) : new CombatModifiers();
            for (int i = 0; i < 6; i++)
            {
                float incoming = CombatMath.Get(amounts, i); if (incoming <= 0) continue; GaugeDefinition d = Definition(i);
                state[i].Value = Math.Min(d.MaxValue, state[i].Value + incoming * d.IncomingMultiplier * (1 + CombatMath.Get(incomingMods.IncomingGaugeBonus, i)) * (1 + CombatMath.Get(outgoingMods.OutgoingGaugeBonus, i))); state[i].LastHit = owner.Time;
                if (state[i].Value >= d.Threshold) { state[i].Value = 0; ApplyStatus(new StatusApplyRequest { Source = source, Target = target, StatusId = FindBaseStatus(d.Element), Stacks = 1 }); }
            }
            owner.Events.Publish(new StatusChangedEvent { Actor = target });
        }
        internal void ApplyStatus(StatusApplyRequest request)
        {
            if (CheckStatus(request) != ErrorCode.None) return;
            StatusDefinition d; owner.Catalog.TryGetStatus(request.StatusId, out d);
            List<Group> list; if (!groups.TryGetValue(request.Target.Value, out list)) { list = new List<Group>(); groups.Add(request.Target.Value, list); }
            Group group = list.Find(delegate(Group g) { return g.Id == request.StatusId; }); if (group == null) { group = new Group { Id = request.StatusId }; list.Add(group); }
            float duration = request.DurationOverride > 0 ? request.DurationOverride : d.DurationSeconds;
            string scope = request.ScopeId ?? String.Empty;
            int count = 0;
            foreach (Layer l in group.Layers) if ((l.Scope ?? String.Empty) == scope) { count++; if (scope.Length > 0 || d.RefreshPolicy == StatusRefreshPolicy.RefreshAllLayers) l.Expiry = owner.Time + duration; }
            int wanted = scope.Length > 0 ? Math.Max(0, request.Stacks - count) : request.Stacks;
            for (int i = 0; i < wanted && count + i < d.MaxStacks; i++) group.Layers.Add(new Layer { Id = ++layerSequence, Source = request.Source, Reaction = request.ReactionId, Scope = scope, Expiry = owner.Time + duration,
                NextTick = d.TickIntervalSeconds > 0 ? owner.Time + d.TickIntervalSeconds : Double.PositiveInfinity, Multiplier = request.SnapshotDamageMultiplier });
            if (d.IsReactionInput) owner.MarkReaction(request.Target);
            owner.Events.Publish(new StatusChangedEvent { Actor = request.Target });
        }
        internal void ReleaseScope(EntityId actor, string scope, double expiry)
        {
            List<Group> list; if (!groups.TryGetValue(actor.Value, out list)) return;
            foreach (Group g in list) foreach (Layer l in g.Layers) if (l.Scope == scope) l.Expiry = expiry;
        }
        internal IReadOnlyList<ReactionToken> Tokens(EntityId actor)
        {
            List<ReactionToken> result = new List<ReactionToken>(); List<Group> list;
            if (groups.TryGetValue(actor.Value, out list)) foreach (Group g in list)
            { StatusDefinition d; if (owner.Catalog.TryGetStatus(g.Id, out d) && d.IsReactionInput) foreach (Layer l in g.Layers) if (!reserved.Contains(l.Id)) result.Add(new ReactionToken { TokenId = l.Id, Element = d.ElementTag, DefinitionId = g.Id }); }
            return result.AsReadOnly();
        }
        internal bool HasLayers(EntityId actor, IReadOnlyList<long> ids)
        { HashSet<long> available = new HashSet<long>(); List<Group> list; if (groups.TryGetValue(actor.Value, out list)) foreach (Group g in list) foreach (Layer l in g.Layers) available.Add(l.Id); foreach (long id in ids) if (!available.Contains(id)) return false; return true; }
        internal EntityId SourceFor(EntityId actor, IReadOnlyList<long> ids)
        { List<Group> list; if (groups.TryGetValue(actor.Value, out list)) foreach (Group g in list) foreach (Layer layer in g.Layers) foreach (long id in ids) if (layer.Id == id) return layer.Source; return actor; }
        internal void Consume(EntityId actor, IReadOnlyList<long> ids)
        {
            List<Group> list; if (!groups.TryGetValue(actor.Value, out list)) return; HashSet<long> consume = new HashSet<long>(ids);
            foreach (Group g in list) g.Layers.RemoveAll(delegate(Layer l) { return consume.Contains(l.Id); }); list.RemoveAll(delegate(Group g) { return g.Layers.Count == 0; }); owner.Events.Publish(new StatusChangedEvent { Actor = actor });
        }
        internal void Advance(float delta)
        {
            // Removed actors cannot retain status/gauge state across replacement or later sessions.
            var removed = new List<long>();
            foreach (long id in groups.Keys) { ActorSnapshot a; if (!owner.World.TryGetActor(new EntityId(id), out a) || a.CurrentHp <= 0) removed.Add(id); }
            foreach (long id in gauges.Keys) { ActorSnapshot a; if ((!owner.World.TryGetActor(new EntityId(id), out a) || a.CurrentHp <= 0) && !removed.Contains(id)) removed.Add(id); }
            foreach (long id in removed) ClearTransient(new EntityId(id));
            foreach (KeyValuePair<long, Gauge[]> pair in gauges)
                for (int i = 0; i < 6; i++) { GaugeDefinition d = Definition(i); if (d == null) continue; double begin = Math.Max(owner.Time - delta, pair.Value[i].LastHit + d.DecayDelaySeconds); float elapsed = (float)Math.Max(0, owner.Time - begin); pair.Value[i].Value = Math.Max(0, pair.Value[i].Value - d.DecayPerSecond * elapsed); }
            foreach (KeyValuePair<long, List<Group>> pair in groups)
            {
                EntityId actor = new EntityId(pair.Key); ActorSnapshot target; if (!owner.World.TryGetActor(actor, out target) || target.CurrentHp <= 0) { foreach (Group g in pair.Value) g.Layers.Clear(); continue; }
                bool changed = false;
                foreach (Group g in pair.Value)
                {
                    StatusDefinition d; if (!owner.Catalog.TryGetStatus(g.Id, out d)) continue;
                    foreach (Layer l in g.Layers)
                    {
                        while (l.NextTick <= owner.Time + 0.000001 && l.NextTick <= l.Expiry + 0.000001)
                        {
                            if (d.DotPhysicalPerStack > 0 || !CombatMath.Zero(d.DotElementPerStack)) owner.Damage.Enqueue(new DamagePacket { PacketId = "dot:" + actor.Value + ":" + l.Id + ":" + l.NextTick.ToString("R", System.Globalization.CultureInfo.InvariantCulture), Source = l.Source, Target = actor, Origin = DamageOrigin.StatusDot, ReactionId = l.Reaction,
                                PhysicalDamage = d.DotPhysicalPerStack, ElementDamage = d.DotElementPerStack, DamageMultiplier = l.Multiplier });
                            if (d.HealPerTick > 0 && target.Kind == ActorKind.Tower)
                            {
                                OperationResult healed = owner.Effects.TryExecute(new EffectBatchRequest { Context = owner.Commands.Create(owner.Transactions.Revision), Source = l.Source, SelectedTarget = actor, Origin = DamageOrigin.StatusDot,
                                    Effects = new EffectSpec[] { new EffectSpec { Kind = EffectKind.Heal, TargetPolicy = EffectTargetPolicy.SelectedActor, Amount = d.HealPerTick } } });
                                if (healed.State != OperationState.Committed) break;
                            }
                            l.NextTick += d.TickIntervalSeconds;
                            if (d.TickIntervalSeconds <= 0) break;
                        }
                    }
                    if (g.Layers.RemoveAll(delegate(Layer l) { return !reserved.Contains(l.Id) && l.Expiry <= owner.Time + 0.000001; }) > 0) changed = true;
                }
                pair.Value.RemoveAll(delegate(Group g) { return g.Layers.Count == 0; }); if (changed) owner.Events.Publish(new StatusChangedEvent { Actor = actor });
            }
        }
    }
}
