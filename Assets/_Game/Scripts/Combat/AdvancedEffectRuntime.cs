using System;
using System.Collections.Generic;
using Spotlight.Contracts;

namespace Spotlight.Combat
{
    // Dispatch runs only after the parent effect transaction commits.
    internal sealed class AdvancedEffectRuntime
    {
        internal sealed class Plan { internal EffectSpec Effect; internal EffectBatchRequest Request; internal ActorSnapshot Target; internal string Key; }
        private sealed class Area { internal long Id; internal Plan Plan; internal WorldPoint Position; internal double End, Next; internal int Pulse; internal HashSet<long> Inside = new HashSet<long>(); }
        private readonly CombatServices owner; private readonly DamageRuntime damage; private readonly StatusRuntime status;
        private readonly List<Area> areas = new List<Area>(); private long sequence;
        internal AdvancedEffectRuntime(CombatServices owner, DamageRuntime damage, StatusRuntime status) { this.owner = owner; this.damage = damage; this.status = status; }
        internal void ResetSession() { Clear(); sequence = 0; }
        internal void Clear() { foreach (Area a in areas) foreach (long id in a.Inside) status.ReleaseScope(new EntityId(id), Scope(a), owner.Time); areas.Clear(); }
        private static string Scope(Area a) { return "area:" + a.Id; }
        private float CellSize() { LevelDefinition d; return owner.Catalog.TryGetLevel(owner.Board.GetSnapshot().LevelId, out d) ? d.CellSize : 1; }
        internal IReadOnlyList<AreaEffectSnapshot> GetSnapshot()
        {
            var result = new List<AreaEffectSnapshot>(); foreach (Area a in areas) result.Add(new AreaEffectSnapshot { AreaId = a.Id, Source = a.Plan.Request.Source, Position = a.Position, RadiusInCells = a.Plan.Effect.RadiusInCells, RemainingSeconds = (float)Math.Max(0, a.End-owner.Time), ReactionId = a.Plan.Request.ReactionId, EffectId = a.Plan.Effect.Id }); return result.AsReadOnly();
        }
        internal static EffectSpec CopyEffect(EffectSpec e)
        {
            var children=new List<EffectSpec>(); if(e.AreaEffects!=null)foreach(EffectSpec child in e.AreaEffects)children.Add(CopyEffect(child));
            return new EffectSpec { Id=e.Id, Kind=e.Kind, TargetPolicy=e.TargetPolicy, PhysicalDamage=e.PhysicalDamage, ElementDamage=e.ElementDamage, Gauge=e.Gauge, Amount=e.Amount,Probability=e.Probability,UseAreaLease=e.UseAreaLease, ReferenceId=e.ReferenceId, Stacks=e.Stacks, DurationOverride=e.DurationOverride, ResourceBucket=e.ResourceBucket, ResourceAmount=e.ResourceAmount, DamageMultiplier=e.DamageMultiplier, SpeedMultiplier=e.SpeedMultiplier, RadiusInCells=e.RadiusInCells, MaxHits=e.MaxHits, RetainedDamageMultiplier=e.RetainedDamageMultiplier, DurationSeconds=e.DurationSeconds, TickIntervalSeconds=e.TickIntervalSeconds, LingeringSeconds=e.LingeringSeconds, RequiredStatusId=e.RequiredStatusId, AreaEffects=children.AsReadOnly() };
        }
        internal static EffectBatchRequest CopyRequest(EffectBatchRequest r)
        { return new EffectBatchRequest { Context=r.Context, Source=r.Source, SelectedTarget=r.SelectedTarget, Origin=r.Origin, ReactionId=r.ReactionId, ReactionDamageMultiplier=r.ReactionDamageMultiplier, HasImpactPosition=r.HasImpactPosition, ImpactPosition=r.ImpactPosition }; }
        private float StatusMultiplier(Plan p, string id)
        { StatusDefinition d; return owner.Catalog.TryGetStatus(id,out d)&&d.IsReactionDamage&&p.Request.Origin==DamageOrigin.Reaction?p.Request.ReactionDamageMultiplier:1; }
        internal void Dispatch(Plan p)
        {
            EffectSpec e = p.Effect; WorldPoint center = p.Request.HasImpactPosition ? p.Request.ImpactPosition : p.Target.Position;
            if (e.Kind == EffectKind.CreateArea)
            {
                Area a = new Area { Id = ++sequence, Plan = p, Position = p.Target.Position, End = owner.Time + e.DurationSeconds, Next = owner.Time + e.TickIntervalSeconds };
                areas.Add(a); Event(p, e.Kind, a.Position, a.Position); Pulse(a); return;
            }
            if (e.Kind == EffectKind.AreaDamage)
            {
                foreach (ActorSnapshot target in Enemies(center, e.RadiusInCells)) Hit(p, target, 1, "aoe:" + target.Id.Value);
                Event(p, e.Kind, center, center); return;
            }
            var visited = new HashSet<long> { p.Target.Id.Value }; ActorSnapshot current = p.Target;
            ApplyChainStatus(p, current); Event(p, e.Kind, current.Position, current.Position);
            float multiplier = 1;
            for (int hit = 1; hit < e.MaxHits; hit++)
            {
                ActorSnapshot closest = null; float best = Single.PositiveInfinity;
                foreach (ActorSnapshot candidate in Enemies(current.Position, e.RadiusInCells))
                {
                    if (visited.Contains(candidate.Id.Value) || !HasStatus(candidate.Id, e.RequiredStatusId)) continue;
                    float ds = CombatMath.DistanceSquared(current.Position,candidate.Position);
                    if (closest == null || ds < best || (ds == best && (candidate.SpawnSequence < closest.SpawnSequence || (candidate.SpawnSequence == closest.SpawnSequence && candidate.Id.Value < closest.Id.Value)))) { closest = candidate; best = ds; }
                }
                if (closest == null) break;
                Hit(p, closest, multiplier, "chain:" + hit); ApplyChainStatus(p, closest); Event(p, e.Kind, current.Position, closest.Position);
                visited.Add(closest.Id.Value); current = closest; multiplier *= e.RetainedDamageMultiplier;
            }
        }
        private bool HasStatus(EntityId id, string required)
        { if (String.IsNullOrEmpty(required)) return true; foreach (StatusSnapshot s in status.GetStatuses(id)) if (s.DefinitionId == required) foreach (StatusLayerSnapshot layer in s.Layers) if(layer.RemainingSeconds > 0) return true; return false; }
        private List<ActorSnapshot> Enemies(WorldPoint center, float cells)
        {
            var result = new List<ActorSnapshot>(); float radius = cells * CellSize();
            foreach (ActorSnapshot actor in owner.World.GetActors(ActorKind.Enemy)) if (actor.CurrentHp > 0 && actor.Targetable && CombatMath.DistanceSquared(center,actor.Position) <= radius*radius) result.Add(actor);
            result.Sort(delegate(ActorSnapshot a, ActorSnapshot b) { return a.Id.Value.CompareTo(b.Id.Value); }); return result;
        }
        private void Hit(Plan p, ActorSnapshot target, float multiplier, string suffix, EffectSpec effect = null)
        {
            EffectSpec e = effect ?? p.Effect; float snapshot = p.Request.Origin == DamageOrigin.Reaction ? p.Request.ReactionDamageMultiplier : 1;
            if (multiplier <= 0) return;
            damage.Enqueue(new DamagePacket { PacketId = p.Key + ":" + suffix, Source = p.Request.Source, Target = target.Id, Origin = p.Request.Origin == DamageOrigin.Tower || p.Request.Origin == DamageOrigin.Enemy ? DamageOrigin.Event : p.Request.Origin, ReactionId = p.Request.ReactionId, PhysicalDamage = e.PhysicalDamage, ElementDamage = e.ElementDamage, DamageMultiplier = snapshot*multiplier });
        }
        private void ApplyChainStatus(Plan p, ActorSnapshot target)
        { if (!String.IsNullOrEmpty(p.Effect.ReferenceId)) status.ApplyStatus(new StatusApplyRequest { Source=p.Request.Source, Target=target.Id, StatusId=p.Effect.ReferenceId, ReactionId=p.Request.ReactionId, Stacks=p.Effect.Stacks, DurationOverride=p.Effect.DurationOverride, SnapshotDamageMultiplier=StatusMultiplier(p,p.Effect.ReferenceId) }); }
        private void Event(Plan p, EffectKind kind, WorldPoint from, WorldPoint to)
        { owner.Events.Publish(new CombatEffectEvent { Kind=kind, Position=from, TargetPosition=to, RadiusInCells=p.Effect.RadiusInCells }); }
        internal void Advance()
        {
            var remove = new List<Area>();
            foreach (Area a in areas)
            {
                UpdateMembership(a);
                while (a.Next <= owner.Time + 0.000001 && a.Next < a.End - 0.000001) { Pulse(a); a.Next += a.Plan.Effect.TickIntervalSeconds; }
                if (owner.Time >= a.End) { foreach (long id in a.Inside) status.ReleaseScope(new EntityId(id), Scope(a), a.End+a.Plan.Effect.LingeringSeconds); remove.Add(a); }
            }
            foreach (Area a in remove) areas.Remove(a);
        }
        private void UpdateMembership(Area a)
        {
            var inside = new HashSet<long>();
            if (owner.Time < a.End) foreach (ActorSnapshot actor in Enemies(a.Position,a.Plan.Effect.RadiusInCells))
            {
                inside.Add(actor.Id.Value);
                if (!a.Inside.Contains(actor.Id.Value)) foreach (EffectSpec e in a.Plan.Effect.AreaEffects) if (e.Kind == EffectKind.ApplyStatus && owner.Roll(e.Probability, "area-status:" + a.Id + ":" + a.Pulse + ":" + actor.Id.Value + ":" + e.Id + ":" + e.ReferenceId))
                    status.ApplyStatus(new StatusApplyRequest { Source=a.Plan.Request.Source, Target=actor.Id, StatusId=e.ReferenceId, ReactionId=a.Plan.Request.ReactionId, ScopeId=e.UseAreaLease ? Scope(a) : String.Empty, Stacks=e.Stacks, DurationOverride=e.UseAreaLease ? (float)Math.Max(0.001,a.End-owner.Time+a.Plan.Effect.LingeringSeconds) : e.DurationOverride, SnapshotDamageMultiplier=StatusMultiplier(a.Plan,e.ReferenceId) });
            }
            foreach (long id in a.Inside) if (!inside.Contains(id)) status.ReleaseScope(new EntityId(id),Scope(a),Math.Min(owner.Time,a.End)+a.Plan.Effect.LingeringSeconds);
            a.Inside = inside;
        }
        private void Pulse(Area a)
        {
            UpdateMembership(a); int pulse = a.Pulse++;
            for (int i = 0; i < a.Plan.Effect.AreaEffects.Count; i++)
            {
                EffectSpec child = a.Plan.Effect.AreaEffects[i];
                if (child.Kind != EffectKind.Heal) continue;
                var friends = new List<ActorSnapshot>();
                if (child.TargetPolicy == EffectTargetPolicy.AllTowers) friends.AddRange(owner.World.GetActors(ActorKind.Tower));
                else { ActorSnapshot spring; if (owner.World.TryGetActor(owner.World.SpringId, out spring)) friends.Add(spring); }
                float radius = a.Plan.Effect.RadiusInCells * CellSize();
                foreach (ActorSnapshot friend in friends)
                {
                    if (friend.CurrentHp <= 0 || CombatMath.DistanceSquared(a.Position, friend.Position) > radius * radius) continue;
                    owner.Effects.TryExecute(new EffectBatchRequest { Context = owner.Commands.Create(owner.Transactions.Revision), Source = a.Plan.Request.Source, SelectedTarget = friend.Id, Origin = DamageOrigin.Event,
                        Effects = new EffectSpec[] { new EffectSpec { Kind = EffectKind.Heal, TargetPolicy = EffectTargetPolicy.SelectedActor, Amount = child.Amount } } });
                }
            }
            foreach (ActorSnapshot actor in Enemies(a.Position,a.Plan.Effect.RadiusInCells))
                for (int i=0;i<a.Plan.Effect.AreaEffects.Count;i++) if (a.Plan.Effect.AreaEffects[i].Kind == EffectKind.Damage) Hit(a.Plan,actor,1,"area:"+a.Id+":"+pulse+":"+i+":"+actor.Id.Value,a.Plan.Effect.AreaEffects[i]);
        }
    }
}
