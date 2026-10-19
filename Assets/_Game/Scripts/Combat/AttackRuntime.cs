using System;
using System.Collections.Generic;
using Spotlight.Contracts;

namespace Spotlight.Combat
{
    internal sealed class SkillRuntime : ISpecialSkillService
    {
        private readonly CombatServices owner; private int uses; private double expires; private CommandContext pendingContext;
        private sealed class Receipt {internal CommandContext Context;internal OperationResult Result;}
        private readonly Dictionary<Guid,Receipt> receipts=new Dictionary<Guid,Receipt>();
        internal bool Pending;
        internal SkillRuntime(CombatServices owner) { this.owner = owner; }
        internal float Multiplier { get { SpecialSkillDefinition d; return owner.Time < expires && owner.Catalog.TryGetSkill(owner.Catalog.GetSettings().SpecialSkillId, out d) ? d.ReactionDamageMultiplier : 1f; } }
        public SpecialSkillSnapshot GetSnapshot() { return new SpecialSkillSnapshot { UsesRemaining = uses, RemainingSeconds = (float)Math.Max(0, expires - owner.Time), ReactionDamageMultiplier = Multiplier }; }
        public void ResetForNight() { SpecialSkillDefinition d; uses = owner.Catalog.TryGetSkill(owner.Catalog.GetSettings().SpecialSkillId, out d) ? d.UsesPerNight : 0; expires = 0; Pending = false; Publish(); }
        internal void Clear() { uses = 0; expires = 0; Pending = false; receipts.Clear(); }
        internal void EndNightTransient() { expires=0;Pending=false;Publish(); }
        public OperationResult TryUse(CommandContext context)
        {
            Receipt receipt;
            if(receipts.TryGetValue(context.CommandId,out receipt))return receipt.Context.RunId==context.RunId&&receipt.Context.ExpectedRevision==context.ExpectedRevision?receipt.Result:new OperationResult(OperationState.Rejected,ErrorCode.DuplicateCommand,context.CommandId,owner.Transactions.Revision,"skill.duplicate_body");
            if (owner.Phase != GamePhase.Night) return new OperationResult(OperationState.Rejected, ErrorCode.WrongPhase, context.CommandId, owner.Transactions.Revision, "skill.night_only");
            if (uses <= 0 || Pending) return new OperationResult(OperationState.Rejected, ErrorCode.SkillAlreadyUsed, context.CommandId, owner.Transactions.Revision, "skill.used");
            SpecialSkillDefinition d; if (!owner.Catalog.TryGetSkill(owner.Catalog.GetSettings().SpecialSkillId, out d) || d.DurationSeconds <= 0 || d.ReactionDamageMultiplier <= 0) return new OperationResult(OperationState.Rejected, ErrorCode.InvalidDefinition, context.CommandId, owner.Transactions.Revision, "skill.definition");
            StateMutationBatch batch = new StateMutationBatch { Context = context, Reason = "combat.skill" }; ValidationResult v = owner.Transactions.Validate(batch);
            if (v.Error != ErrorCode.None) return new OperationResult(OperationState.Rejected, v.Error, context.CommandId, owner.Transactions.Revision, v.MessageKey);
            pendingContext = context; Pending = true;OperationResult accepted=new OperationResult(OperationState.Queued, ErrorCode.None, context.CommandId, owner.Transactions.Revision, "skill.queued");receipts.Add(context.CommandId,new Receipt {Context=context,Result=accepted});return accepted;
        }
        internal void ExecutePending()
        {
            if (!Pending) return; Pending = false; SpecialSkillDefinition d;
            if (!owner.Catalog.TryGetSkill(owner.Catalog.GetSettings().SpecialSkillId, out d)) return;
            // Queue acceptance binds the caller's run/command. Expected revision is rebased at execution.
            StateMutationBatch batch = new StateMutationBatch { Context = new CommandContext(pendingContext.CommandId, pendingContext.RunId, owner.Transactions.Revision), Reason = "combat.skill" };
            OperationResult r = owner.Transactions.TryCommit(batch);
            if (r.State == OperationState.Committed) { uses--; expires = owner.Time + d.DurationSeconds; Publish(); }
            receipts[pendingContext.CommandId].Result=r;
            owner.Events.Publish(new CommandFinishedEvent { Result = r });
        }
        internal void Advance() { if (expires > 0 && owner.Time >= expires) { expires = 0; Publish(); } }
        private void Publish() { owner.Events.Publish(new SkillChangedEvent { Snapshot = GetSnapshot() }); }
    }

    internal sealed class TowerRuntime : ITowerService
    {
        private readonly CombatServices owner; private readonly ProjectileRuntime projectiles; private readonly StatusRuntime status;
        private readonly Dictionary<long, float> cooldowns = new Dictionary<long, float>();
        internal TowerRuntime(CombatServices owner, ProjectileRuntime projectiles, StatusRuntime status) { this.owner = owner; this.projectiles = projectiles; this.status = status; }
        public IReadOnlyList<ActorSnapshot> GetTowers() { return owner.World.GetActors(ActorKind.Tower); }
        internal void Clear() { cooldowns.Clear(); }
        public void ResetNightCooldowns() { cooldowns.Clear(); }
        private float CellSize()
        { LevelDefinition level; BoardSnapshot board = owner.Board.GetSnapshot(); return owner.Catalog.TryGetLevel(board.LevelId, out level) ? level.CellSize : 1; }
        public void SynchronizeBoard()
        {
            HashSet<long> boardTowers = new HashSet<long>(); List<ActorMutation> mutations = new List<ActorMutation>();
            foreach (CellSnapshot c in owner.Board.GetSnapshot().Cells)
            {
                if (c.OccupantKind != OccupantKind.Tower) continue; boardTowers.Add(c.OccupantId.Value); ActorSnapshot existing; if (owner.World.TryGetActor(c.OccupantId, out existing)) continue;
                TowerDefinition d; if (!owner.Catalog.TryGetTower(c.DefinitionId, out d)) continue;
                int stacks = Math.Max(1, d.InitialAttackStacks); float hp = d.HpPerStack * stacks;
                mutations.Add(new ActorMutation { Kind = ActorMutationKind.Register, ActorId = c.OccupantId, Registration = new ActorSnapshot { Id = c.OccupantId, Kind = ActorKind.Tower, DefinitionId = d.Id,
                    Position = owner.Board.CellToWorld(c.Cell), PreviousPosition = owner.Board.CellToWorld(c.Cell), CollisionRadius = d.CollisionRadiusInCells * CellSize(), CurrentHp = hp, MaxHp = hp,
                    TowerAttackStacks = stacks, SpawnSequence = owner.Ids.AllocateSpawnSequence(), Targetable = true } });
            }
            foreach (ActorSnapshot actor in GetTowers()) if (!boardTowers.Contains(actor.Id.Value)) { mutations.Add(new ActorMutation { Kind = ActorMutationKind.Remove, ActorId = actor.Id }); cooldowns.Remove(actor.Id.Value); }
            if (mutations.Count > 0) { StateMutationBatch batch = owner.Batch("combat.towers_sync"); batch.Actors = mutations.AsReadOnly(); owner.Transactions.TryCommit(batch); }
        }
        internal void Advance(float delta)
        {
            SynchronizeBoard(); float cellSize = CellSize();
            foreach (ActorSnapshot tower in GetTowers())
            {
                if (tower.CurrentHp <= 0 || !tower.Targetable) continue; TowerDefinition d; if (!owner.Catalog.TryGetTower(tower.DefinitionId, out d)) continue;
                CombatModifiers mods = status.GetModifiers(tower.Id); if (mods.PreventAttack) continue;
                float cooldown; cooldowns.TryGetValue(tower.Id.Value, out cooldown); cooldown -= delta * mods.AttackRateMultiplier;
                if (cooldown > 0) { cooldowns[tower.Id.Value] = cooldown; continue; }
                ActorSnapshot closest = null; float distance = Single.PositiveInfinity;
                foreach (ActorSnapshot enemy in owner.World.QueryRadius(tower.Position, d.RangeInCells * cellSize, ActorKind.Enemy))
                {
                    if (enemy.CurrentHp <= 0 || !enemy.Targetable) continue; float ds = CombatMath.DistanceSquared(tower.Position, enemy.Position);
                    if (closest == null || ds < distance || (ds == distance && (enemy.SpawnSequence < closest.SpawnSequence || (enemy.SpawnSequence == closest.SpawnSequence && enemy.Id.Value < closest.Id.Value)))) { closest = enemy; distance = ds; }
                }
                if (closest == null) { cooldowns[tower.Id.Value] = 0; continue; }
                OperationResult result = projectiles.Spawn(new ProjectileSpawnRequest { Source = tower.Id, InitialTarget = closest.Id, DefinitionId = d.ProjectileId, Origin = tower.Position, AimPoint = closest.Position,
                    PhysicalDamage = d.BasePhysicalDamage + d.DamagePerExtraStack * (Math.Max(1, tower.TowerAttackStacks) - 1), DamageOrigin = DamageOrigin.Tower });
                cooldowns[tower.Id.Value] = result.State == OperationState.Committed ? cooldown + d.AttackIntervalSeconds : 0;
            }
        }
        internal void RemoveDead()
        {
            foreach (ActorSnapshot tower in GetTowers())
            {
                if (tower.CurrentHp > 0) continue; List<BoardMutation> board = new List<BoardMutation>();
                foreach (CellSnapshot c in owner.Board.GetSnapshot().Cells) if (c.OccupantId.Equals(tower.Id)) board.Add(new BoardMutation { Kind = BoardMutationKind.Remove, EntityId = tower.Id, OccupantKind = OccupantKind.Tower, DefinitionId = c.DefinitionId, From = c.Cell });
                StateMutationBatch batch = owner.Batch("combat.tower_death"); batch.Board = board.AsReadOnly(); batch.Actors = new ActorMutation[] { new ActorMutation { Kind = ActorMutationKind.Remove, ActorId = tower.Id } };
                if (owner.Transactions.TryCommit(batch).State == OperationState.Committed) { status.ClearTransient(tower.Id); cooldowns.Remove(tower.Id.Value); }
            }
        }
    }

    internal sealed class ProjectileRuntime : IProjectileService
    {
        private sealed class Bullet
        {
            internal long Id; internal string Definition; internal EntityId Source; internal ActorKind SourceKind; internal WorldPoint Position, Direction;
            internal float Speed, Life, Radius, Physical; internal ElementAmounts Damage, Gauge; internal DamageOrigin Origin; internal int Mask;
            internal bool Piercing, Exhausted; internal int Hits, MaxHits = 1; internal float Retention = 1; internal HashSet<long> Overlapping = new HashSet<long>();
            internal List<HitEffect> HitEffects = new List<HitEffect>();
            internal List<ReactionToken> Tokens = new List<ReactionToken>(); internal List<string> Rules = new List<string>();
            internal HashSet<long> VisitedBlocks = new HashSet<long>();
            internal HashSet<CellCoord> VisitedLegacyCells = new HashSet<CellCoord>();
        }
        private sealed class HitEffect { internal EffectSpec Effect; internal string Reaction; internal float Multiplier; }
        private sealed class Intersection { internal float T; internal ActorSnapshot Actor; internal CellSnapshot Cell; }
        private readonly CombatServices owner; private readonly DamageRuntime damage; private readonly List<Bullet> bullets = new List<Bullet>(); private long sequence, tokenSequence;
        internal ProjectileRuntime(CombatServices owner, DamageRuntime damage) { this.owner = owner; this.damage = damage; }
        internal string GetDefinitionId(long id) { Bullet b = bullets.Find(delegate(Bullet value) { return value.Id == id; }); return b == null ? null : b.Definition; }
        public void Clear() { bullets.Clear(); }
        public OperationResult Spawn(ProjectileSpawnRequest request)
        {
            if (request == null || request.PhysicalDamage < 0 || !CombatMath.Finite(request.PhysicalDamage) || !CombatMath.ValidAmounts(request.ElementDamage, false) || !CombatMath.ValidAmounts(request.Gauge, false)) return owner.Result(OperationState.Rejected, ErrorCode.InvalidArgument, "projectile.request");
            ProjectileDefinition d; ActorSnapshot source;
            if (!owner.Catalog.TryGetProjectile(request.DefinitionId, out d) || d.SpeedInCellsPerSecond <= 0 || d.LifetimeSeconds <= 0 || d.CollisionRadiusInCells < 0 || d.MaxHits < 1 || d.MaxHits > 64 || !CombatMath.Finite(d.RetainedDamageMultiplier) || d.RetainedDamageMultiplier < 0 || d.RetainedDamageMultiplier > 1) return owner.Result(OperationState.Rejected, ErrorCode.InvalidDefinition, "projectile.definition");
            if (!owner.World.TryGetActor(request.Source, out source) || source.CurrentHp <= 0 || (source.Kind != ActorKind.Tower && source.Kind != ActorKind.Enemy)) return owner.Result(OperationState.Rejected, ErrorCode.InvalidTarget, "projectile.source");
            float dx = request.AimPoint.X - request.Origin.X, dy = request.AimPoint.Y - request.Origin.Y, length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (!CombatMath.Finite(length) || length <= 0) return owner.Result(OperationState.Rejected, ErrorCode.InvalidArgument, "projectile.direction");
            LevelDefinition level; float size = owner.Catalog.TryGetLevel(owner.Board.GetSnapshot().LevelId, out level) ? level.CellSize : 1;
            bullets.Add(new Bullet { Id = ++sequence, Definition = request.DefinitionId, Source = request.Source, SourceKind = source.Kind, Position = request.Origin, Direction = new WorldPoint(dx / length, dy / length),
                Speed = d.SpeedInCellsPerSecond * size, Life = d.LifetimeSeconds, Radius = d.CollisionRadiusInCells * size, Piercing = d.PiercingEnabled, MaxHits = d.MaxHits, Retention = d.RetainedDamageMultiplier, Physical = request.PhysicalDamage, Damage = request.ElementDamage, Gauge = request.Gauge, Origin = request.DamageOrigin });
            return owner.Result(OperationState.Committed, ErrorCode.None, "projectile.spawned");
        }
        public IReadOnlyList<ProjectileSnapshot> GetSnapshot()
        { List<ProjectileSnapshot> result = new List<ProjectileSnapshot>(); foreach (Bullet b in bullets) result.Add(new ProjectileSnapshot { ProjectileId = b.Id, Source = b.Source, Position = b.Position, Direction = b.Direction, AppliedElementMask = b.Mask, HitCount = b.Hits, MaxHits = b.MaxHits, PiercingEnabled = b.Piercing, AppliedReactionIds = new List<string>(b.Rules).AsReadOnly() }); return result.AsReadOnly(); }
        internal void Advance(float delta)
        {
            List<Bullet> remove = new List<Bullet>();
            foreach (Bullet b in bullets)
            {
                float travel = Math.Min(delta, b.Life); WorldPoint next = new WorldPoint(b.Position.X + b.Direction.X * b.Speed * travel, b.Position.Y + b.Direction.Y * b.Speed * travel);
                List<Intersection> intersections = new List<Intersection>();
                ActorKind targets = b.SourceKind == ActorKind.Enemy ? ActorKind.Spring : ActorKind.Enemy;
                foreach (ActorSnapshot actor in owner.World.TraceActors(b.Position, next, b.Radius, targets))
                { if (!actor.Targetable || actor.CurrentHp <= 0) continue; float t = CombatMath.SegmentCircle(b.Position, next, actor.Position, actor.CollisionRadius + b.Radius); if (!Single.IsInfinity(t)) intersections.Add(new Intersection { T = t, Actor = actor }); }
                if (b.SourceKind == ActorKind.Tower) foreach (CellSnapshot cell in owner.Board.TraceSegment(b.Position, next))
                { if (cell.OccupantKind == OccupantKind.ElementBlock) intersections.Add(new Intersection { T = CellEntry(b.Position, next, cell.Cell), Cell = cell }); }
                intersections.Sort(delegate(Intersection a, Intersection c)
                {
                    int v = a.T.CompareTo(c.T); if (v != 0) return v; if ((a.Actor != null) != (c.Actor != null)) return a.Actor != null ? -1 : 1;
                    return (a.Actor != null ? a.Actor.Id.Value : a.Cell.OccupantId.Value).CompareTo(c.Actor != null ? c.Actor.Id.Value : c.Cell.OccupantId.Value);
                });
                bool hit = false;
                var overlap = new HashSet<long>();
                foreach (ActorSnapshot actor in owner.World.GetActors(targets)) { float radius = actor.CollisionRadius+b.Radius; if (CombatMath.DistanceSquared(next,actor.Position) <= radius*radius) overlap.Add(actor.Id.Value); }
                foreach (Intersection item in intersections)
                {
                    if (item.T < 0 || item.T > 1) continue;
                    if (item.Actor != null)
                    {
                        if (b.Overlapping.Contains(item.Actor.Id.Value) || b.Exhausted || b.Hits >= b.MaxHits) continue;
                        int hitIndex = b.Hits + 1;
                        DamagePacket packet = new DamagePacket { PacketId = "projectile:" + b.Id + ":" + hitIndex, Source = b.Source, Target = item.Actor.Id, Origin = b.Origin,
                            PhysicalDamage = b.Physical, ElementDamage = b.Damage, Gauge = b.Gauge };
                        bool missed = damage.Preview(packet).Missed;
                        OperationResult result = damage.Enqueue(packet);
                        if (result.State != OperationState.Rejected)
                        {
                            b.Hits++; if(b.Hits >= b.MaxHits)b.Exhausted=true; WorldPoint impact = new WorldPoint(b.Position.X+(next.X-b.Position.X)*item.T,b.Position.Y+(next.Y-b.Position.Y)*item.T);
                            if (!missed) foreach (HitEffect effect in b.HitEffects) owner.Effects.TryExecute(new EffectBatchRequest { Context=owner.Commands.Create(owner.Transactions.Revision), Source=b.Source, SelectedTarget=item.Actor.Id, Origin=DamageOrigin.Reaction, ReactionId=effect.Reaction, ReactionDamageMultiplier=effect.Multiplier, HasImpactPosition=true, ImpactPosition=impact, Effects=new EffectSpec[] { effect.Effect } });
                            if (!b.Piercing) { hit = true; break; }
                            b.Physical *= b.Retention; b.Damage = CombatMath.Scale(b.Damage,b.Retention);
                        }
                    }
                    else Modify(b, item.Cell);
                }
                b.Overlapping = overlap; b.Position = next; b.Life -= delta; if (hit || b.Life <= 0) remove.Add(b);
            }
            foreach (Bullet b in remove) bullets.Remove(b);
        }
        private float CellEntry(WorldPoint from, WorldPoint to, CellCoord cell)
        {
            WorldPoint center = owner.Board.CellToWorld(cell); LevelDefinition level;
            float half = owner.Catalog.TryGetLevel(owner.Board.GetSnapshot().LevelId, out level) ? level.CellSize * 0.5f : 0.5f;
            float lo = 0, hi = 1; if (!Slab(from.X, to.X - from.X, center.X - half, center.X + half, ref lo, ref hi) || !Slab(from.Y, to.Y - from.Y, center.Y - half, center.Y + half, ref lo, ref hi)) return Single.PositiveInfinity; return lo;
        }
        private static bool Slab(float origin, float delta, float min, float max, ref float lo, ref float hi)
        { if (Math.Abs(delta) < 0.000001f) return origin >= min && origin <= max; float a = (min - origin) / delta, b = (max - origin) / delta; if (a > b) { float t = a; a = b; b = t; } lo = Math.Max(lo, a); hi = Math.Min(hi, b); return lo <= hi; }
        private void Modify(Bullet b, CellSnapshot cell)
        {
            if(b.Exhausted)return;
            ElementDefinition element; if (!owner.Catalog.TryGetElement(cell.DefinitionId, out element) || (int)element.Element < 1 || (int)element.Element > 6) return;
            // Block identity and element identity have different lifetimes: every distinct
            // block supplies an input, while the ordinary modifier applies once per type.
            if (cell.OccupantId.Value != 0 ? !b.VisitedBlocks.Add(cell.OccupantId.Value) : !b.VisitedLegacyCells.Add(cell.Cell)) return;
            int mask = 1 << ((int)element.Element - 1);
            if ((b.Mask & mask) == 0)
            {
                b.Mask |= mask; ProjectileModifierDefinition m = element.ProjectileModifier;
                if (m != null) Apply(b, m.PhysicalAdd, m.DamageAdd, m.GaugeAdd, m.DamageMultiplier, m.SpeedMultiplier);
            }
            b.Tokens.Add(new ReactionToken { TokenId = ++tokenSequence, Element = element.Element, DefinitionId = element.Id });
            ReactionPlan plan = owner.Rules.ResolveReactions(new ReactionInput { Context = ReactionContext.Projectile, Tokens = b.Tokens.AsReadOnly(), AppliedRuleIds = b.Rules.AsReadOnly() });
            foreach (ReactionMatch match in plan.Matches)
            {
                if (b.Rules.Contains(match.RuleId)) continue;
                List<EffectSpec> sourceEffects = new List<EffectSpec>();
                foreach (EffectSpec e in match.Effects) if (e.Kind == EffectKind.ApplyStatus && e.TargetPolicy == EffectTargetPolicy.SourceActor) sourceEffects.Add(e);
                if (sourceEffects.Count > 0)
                {
                    OperationResult applied = owner.Effects.TryExecute(new EffectBatchRequest { Context=owner.Commands.Create(owner.Transactions.Revision), Source=b.Source, SelectedTarget=b.Source, Origin=DamageOrigin.Reaction, ReactionId=match.RuleId, ReactionDamageMultiplier=owner.ReactionMultiplier, Effects=sourceEffects.AsReadOnly() });
                    if (applied.State != OperationState.Committed) continue;
                }
                foreach (EffectSpec e in match.Effects)
                {
                    if(b.Exhausted)continue;
                    if(e.Kind == EffectKind.ApplyStatus && e.TargetPolicy == EffectTargetPolicy.SourceActor) continue;
                    if(e.Kind == EffectKind.ModifyProjectile) Apply(b,e.PhysicalDamage,e.ElementDamage,e.Gauge,e.DamageMultiplier,e.SpeedMultiplier);
                    else if(e.Kind == EffectKind.ConfigurePiercing) { b.Piercing=true; b.MaxHits=e.MaxHits; b.Retention=e.RetainedDamageMultiplier; }
                    else if(e.Kind == EffectKind.AreaDamage || e.Kind == EffectKind.ChainDamage || e.Kind == EffectKind.CreateArea || e.Kind == EffectKind.AddGauge || e.Kind == EffectKind.ApplyStatus) b.HitEffects.Add(new HitEffect { Effect=AdvancedEffectRuntime.CopyEffect(e), Reaction=match.RuleId, Multiplier=owner.ReactionMultiplier });
                }
                b.Rules.Add(match.RuleId); owner.Events.Publish(new ReactionResolvedEvent { Target = b.Source, RuleId = match.RuleId, Context = ReactionContext.Projectile, Position = owner.Board.CellToWorld(cell.Cell) });
                if (match.ConsumedTokenIds != null) b.Tokens.RemoveAll(delegate(ReactionToken token) { return ContainsToken(match.ConsumedTokenIds, token.TokenId); });
            }
        }
        private static bool ContainsToken(IReadOnlyList<long> ids, long id)
        { for (int i = 0; i < ids.Count; i++) if (ids[i] == id) return true; return false; }
        private static void Apply(Bullet b, float physical, ElementAmounts elemental, ElementAmounts gauge, float multiplier, float speed)
        { b.Physical = Math.Max(0, b.Physical + physical) * multiplier; b.Damage = CombatMath.Scale(CombatMath.Add(b.Damage, elemental, true), multiplier); b.Gauge = CombatMath.Add(b.Gauge, gauge, true); b.Speed *= speed; }
    }
}
