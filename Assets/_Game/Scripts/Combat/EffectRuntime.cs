using System;
using System.Collections.Generic;
using Spotlight.Contracts;

namespace Spotlight.Combat
{
    internal sealed class EffectRuntime : IEffectService
    {
        private readonly CombatServices owner; private readonly DamageRuntime damage; private readonly StatusRuntime status;
        private sealed class Receipt { internal string Fingerprint; internal OperationResult Result; }
        private readonly Dictionary<Guid,Receipt> committed = new Dictionary<Guid,Receipt>();
        private sealed class GaugeChange { internal EntityId Source, Target; internal ElementAmounts Amounts; internal bool Outgoing; }
        private sealed class Prepared
        {
            internal StateMutationBatch Batch;
            internal List<AdvancedEffectRuntime.Plan> Advanced = new List<AdvancedEffectRuntime.Plan>();
            internal List<StatusApplyRequest> Statuses = new List<StatusApplyRequest>();
            internal List<GaugeChange> Gauges = new List<GaugeChange>();
            internal List<DamageResult> Damage = new List<DamageResult>();
        }
        internal EffectRuntime(CombatServices owner, DamageRuntime damage, StatusRuntime status) { this.owner = owner; this.damage = damage; this.status = status; }
        public ValidationResult Validate(EffectBatchRequest request)
        {
            Receipt receipt;
            if(request!=null&&committed.TryGetValue(request.Context.CommandId,out receipt))return new ValidationResult(receipt.Fingerprint==Fingerprint(request)?ErrorCode.None:ErrorCode.DuplicateCommand,"effect.duplicate");
            Prepared p; return Prepare(request, out p);
        }
        public OperationResult TryExecute(EffectBatchRequest request)
        {
            Receipt receipt;
            if(request!=null&&committed.TryGetValue(request.Context.CommandId,out receipt))return receipt.Fingerprint==Fingerprint(request)?receipt.Result:new OperationResult(OperationState.Rejected,ErrorCode.DuplicateCommand,request.Context.CommandId,owner.Transactions.Revision,"effect.duplicate_body");
            Prepared prepared; ValidationResult validation = Prepare(request, out prepared);
            if (validation.Error != ErrorCode.None) return new OperationResult(OperationState.Rejected, validation.Error, request == null ? Guid.Empty : request.Context.CommandId, owner.Transactions.Revision, validation.MessageKey);
            OperationResult result = owner.Transactions.TryCommit(prepared.Batch);
            if (result.State != OperationState.Committed) return result;
            owner.EffectSequence++;
            committed.Add(request.Context.CommandId,new Receipt {Fingerprint=Fingerprint(request),Result=result});
            foreach (GaugeChange g in prepared.Gauges) status.ApplyGauge(g.Source, g.Target, g.Amounts, g.Outgoing);
            foreach (StatusApplyRequest s in prepared.Statuses) status.ApplyStatus(s);
            foreach (AdvancedEffectRuntime.Plan plan in prepared.Advanced) owner.Advanced.Dispatch(plan);
            foreach (DamageResult d in prepared.Damage) owner.Events.Publish(new DamageAppliedEvent { Result = d });
            return result;
        }
        internal void Clear() { committed.Clear(); }
        private static string Fingerprint(object value)
        {System.Text.StringBuilder text=new System.Text.StringBuilder();AppendFingerprint(text,value);return text.ToString();}
        private static void AppendFingerprint(System.Text.StringBuilder text,object value)
        {
            if(value==null){text.Append("null;");return;}Type type=value.GetType();
            if(type.IsPrimitive||type.IsEnum||value is string||value is Guid){string atom=Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture);text.Append(atom.Length).Append(':').Append(atom).Append(';');return;}
            System.Collections.IEnumerable sequence=value as System.Collections.IEnumerable;
            if(sequence!=null){text.Append('[');foreach(object item in sequence)AppendFingerprint(text,item);text.Append(']');return;}
            System.Reflection.FieldInfo[] fields=type.GetFields();Array.Sort(fields,delegate(System.Reflection.FieldInfo a,System.Reflection.FieldInfo b){return StringComparer.Ordinal.Compare(a.Name,b.Name);});
            text.Append('{');foreach(System.Reflection.FieldInfo field in fields){text.Append(field.Name).Append('=');AppendFingerprint(text,field.GetValue(value));}text.Append('}');
        }
        private static ValidationResult Invalid(ErrorCode error, string key) { return new ValidationResult(error, key); }
        private IReadOnlyList<ActorSnapshot> Targets(EffectSpec effect, EffectBatchRequest request)
        {
            if (effect.TargetPolicy == EffectTargetPolicy.AllEnemies) return owner.World.GetActors(ActorKind.Enemy);
            if (effect.TargetPolicy == EffectTargetPolicy.AllTowers) return owner.World.GetActors(ActorKind.Tower);
            ActorSnapshot actor;
            EntityId id = effect.TargetPolicy == EffectTargetPolicy.Spring ? owner.World.SpringId : effect.TargetPolicy == EffectTargetPolicy.SourceActor ? request.Source : request.SelectedTarget;
            if (owner.World.TryGetActor(id, out actor)) return new ActorSnapshot[] { actor }; return new ActorSnapshot[0];
        }
        private ValidationResult Prepare(EffectBatchRequest request, out Prepared prepared)
        {
            prepared = null;
            if (request == null || request.Effects == null || (request.HasImpactPosition && (!CombatMath.Finite(request.ImpactPosition.X) || !CombatMath.Finite(request.ImpactPosition.Y))) || !CombatMath.Finite(request.ReactionDamageMultiplier) || request.ReactionDamageMultiplier <= 0) return Invalid(ErrorCode.InvalidArgument, "effect.request");
            Prepared p = new Prepared();
            List<ActorMutation> mutations = new List<ActorMutation>(); List<ResourceAmount> resources = new List<ResourceAmount>();
            Dictionary<long, ActorSnapshot> actors = new Dictionary<long, ActorSnapshot>();
            StateMutationBatch extra = request.CommitExtras;
            if (extra != null && extra.Context.CommandId != Guid.Empty && extra.Context.CommandId != request.Context.CommandId) return Invalid(ErrorCode.InvalidArgument, "effect.extras_context");
            if (extra != null)
            {
                if (extra.Actors != null) foreach (ActorMutation m in extra.Actors)
                {
                    // HP/stack writes in extras would compete with interpreter writes. Reject ambiguous batches.
                    if (m.Kind == ActorMutationKind.SetHealth || m.Kind == ActorMutationKind.SetTowerStacks) return Invalid(ErrorCode.InvalidArgument, "effect.extras_actor_conflict");
                    mutations.Add(m);
                }
                if (extra.ResourceDeltas != null) resources.AddRange(extra.ResourceDeltas);
            }
            int effectIndex = 0;
            foreach (EffectSpec effect in request.Effects)
            {
                effectIndex++;
                ValidationResult shape = CheckShape(effect); if (shape.Error != ErrorCode.None) return shape;
                if (effect.Kind == EffectKind.AddResource)
                {
                    if (effect.TargetPolicy != EffectTargetPolicy.Resources || String.IsNullOrEmpty(effect.ReferenceId)) return Invalid(ErrorCode.InvalidArgument, "effect.resource_target");
                    ResourceDefinition rd; ElementDefinition ed; ItemDefinition item;
                    if (!owner.Catalog.TryGetResource(effect.ReferenceId, out rd) && !owner.Catalog.TryGetElement(effect.ReferenceId, out ed) && !owner.Catalog.TryGetItem(effect.ReferenceId, out item)) return Invalid(ErrorCode.InvalidDefinition, "effect.resource_definition");
                    resources.Add(new ResourceAmount(effect.ResourceBucket, effect.ReferenceId, effect.ResourceAmount)); continue;
                }
                if (effect.Kind == EffectKind.ModifyProjectile || effect.Kind == EffectKind.ConfigurePiercing || effect.TargetPolicy == EffectTargetPolicy.CurrentProjectile || effect.TargetPolicy == EffectTargetPolicy.Resources) return Invalid(ErrorCode.InvalidTarget, "effect.target_policy");
                IReadOnlyList<ActorSnapshot> targets = Targets(effect, request);
                if (targets.Count == 0 && effect.TargetPolicy != EffectTargetPolicy.AllEnemies && effect.TargetPolicy != EffectTargetPolicy.AllTowers) return Invalid(ErrorCode.InvalidTarget, "effect.target_missing");
                foreach (ActorSnapshot target in targets)
                {
                    if (effect.Kind == EffectKind.AreaDamage || effect.Kind == EffectKind.ChainDamage || effect.Kind == EffectKind.CreateArea)
                    {
                        if (target.Kind != ActorKind.Enemy) return Invalid(ErrorCode.InvalidTarget,"effect.advanced_actor");
                        p.Advanced.Add(new AdvancedEffectRuntime.Plan { Effect=AdvancedEffectRuntime.CopyEffect(effect), Request=AdvancedEffectRuntime.CopyRequest(request), Target=CopyActor(target), Key="effect:"+request.Context.CommandId.ToString("N")+":"+effectIndex+":"+target.Id.Value }); continue;
                    }
                    if (target.CurrentHp <= 0)
                    { if (effect.TargetPolicy == EffectTargetPolicy.AllEnemies || effect.TargetPolicy == EffectTargetPolicy.AllTowers) continue; return Invalid(ErrorCode.TargetDead, "effect.target_dead"); }
                    ActorSnapshot actor;
                    if (!actors.TryGetValue(target.Id.Value, out actor))
                    { actor = CopyActor(target); actors.Add(target.Id.Value, actor); }
                    if (actor.CurrentHp <= 0) return Invalid(ErrorCode.TargetDead, "effect.target_killed_in_batch");
                    switch (effect.Kind)
                    {
                        case EffectKind.Damage:
                            DamagePacket packet = new DamagePacket { PacketId = "effect:" + owner.EffectSequence + ":" + request.Source.Value + ":" + effectIndex + ":" + actor.Id.Value,
                                Source = request.Source, Target = actor.Id, Origin = request.Origin, ReactionId = request.ReactionId, PhysicalDamage = effect.PhysicalDamage,
                                ElementDamage = effect.ElementDamage, Gauge = effect.Gauge, DamageMultiplier = request.Origin == DamageOrigin.Reaction ? request.ReactionDamageMultiplier : 1f };
                            DamageResult dr = damage.Calculate(packet, actor); actor.CurrentHp = dr.RemainingHp; p.Damage.Add(dr);
                            if (!dr.Missed && !CombatMath.Zero(effect.Gauge)) { ErrorCode ge = status.CheckGauge(actor.Id, effect.Gauge); if (ge != ErrorCode.None) return Invalid(ge, "effect.gauge"); p.Gauges.Add(new GaugeChange { Source = request.Source, Target = actor.Id, Amounts = effect.Gauge, Outgoing = request.Origin == DamageOrigin.Tower || request.Origin == DamageOrigin.Enemy }); }
                            break;
                        case EffectKind.Heal: actor.CurrentHp = Math.Min(actor.MaxHp, actor.CurrentHp + effect.Amount); break;
                        case EffectKind.AddGauge:
                            ErrorCode gaugeError = status.CheckGauge(actor.Id, effect.Gauge); if (gaugeError != ErrorCode.None) return Invalid(gaugeError, "effect.gauge");
                            p.Gauges.Add(new GaugeChange { Source = request.Source, Target = actor.Id, Amounts = effect.Gauge }); break;
                        case EffectKind.ApplyStatus:
                            StatusDefinition definition; if (!owner.Catalog.TryGetStatus(effect.ReferenceId, out definition)) return Invalid(ErrorCode.InvalidDefinition, "effect.status_definition");
                            if (effect.TargetPolicy == EffectTargetPolicy.SourceActor && definition.IsReactionInput) return Invalid(ErrorCode.InvalidTarget, "effect.source_reaction_input");
                            StatusApplyRequest sr = new StatusApplyRequest { Source = request.Source, Target = actor.Id, StatusId = effect.ReferenceId, ReactionId = request.ReactionId, Stacks = effect.Stacks,
                                DurationOverride = effect.DurationOverride, SnapshotDamageMultiplier = definition.IsReactionDamage && request.Origin == DamageOrigin.Reaction ? request.ReactionDamageMultiplier : 1f };
                            ErrorCode se = status.CheckStatus(sr);
                            if (se != ErrorCode.None) return Invalid(se, "effect.status");
                            string probabilityKey = "status:" + owner.EffectSequence + ":" + request.ReactionId + ":" + request.Source.Value + ":" + actor.Id.Value + ":" + effectIndex + ":" + effect.ReferenceId;
                            if (owner.Roll(effect.Probability, probabilityKey)) p.Statuses.Add(sr);
                            break;
                        case EffectKind.AddTowerStacks:
                            TowerDefinition tower; if (actor.Kind != ActorKind.Tower || !owner.Catalog.TryGetTower(actor.DefinitionId, out tower)) return Invalid(ErrorCode.InvalidTarget, "effect.tower");
                            int stacks = Math.Max(1, Math.Min(tower.MaxAttackStacks, actor.TowerAttackStacks + effect.Stacks)); float lost = actor.MaxHp - actor.CurrentHp;
                            actor.TowerAttackStacks = stacks; actor.MaxHp = stacks * tower.HpPerStack; actor.CurrentHp = Math.Max(0, actor.MaxHp - lost); break;
                    }
                }
            }
            foreach (ActorSnapshot actor in actors.Values)
            {
                mutations.Add(CombatMath.Health(actor, actor.CurrentHp, actor.MaxHp));
                if (actor.Kind == ActorKind.Tower) mutations.Add(new ActorMutation { Kind = ActorMutationKind.SetTowerStacks, ActorId = actor.Id, TowerAttackStacks = actor.TowerAttackStacks });
            }
            p.Batch = new StateMutationBatch { Context = request.Context, Reason = "combat.effects", Actors = mutations.AsReadOnly(), ResourceDeltas = resources.AsReadOnly(),
                Board = extra == null ? null : extra.Board, Random = extra == null ? null : extra.Random, Progress = extra == null ? null : extra.Progress, DayEvent = extra == null ? null : extra.DayEvent };
            ValidationResult result = owner.Transactions.Validate(p.Batch); if (result.Error != ErrorCode.None) return result;
            prepared = p; return new ValidationResult(ErrorCode.None, "effect.valid");
        }
        private static ActorSnapshot CopyActor(ActorSnapshot a)
        { return new ActorSnapshot { Id = a.Id, Kind = a.Kind, DefinitionId = a.DefinitionId, Position = a.Position, PreviousPosition = a.PreviousPosition, CollisionRadius = a.CollisionRadius, CurrentHp = a.CurrentHp, MaxHp = a.MaxHp, PhysicalReduction = a.PhysicalReduction, ElementResistance = a.ElementResistance, TowerAttackStacks = a.TowerAttackStacks, SpawnSequence = a.SpawnSequence, Targetable = a.Targetable, DeathResolved = a.DeathResolved }; }
        internal ValidationResult CheckShape(EffectSpec e)
        {
            if (e == null || !CombatMath.Finite(e.Probability) || e.Probability < 0 || e.Probability > 1 || (e.Kind != EffectKind.ApplyStatus && e.Probability != 1) || !Enum.IsDefined(typeof(EffectKind), e.Kind) || !Enum.IsDefined(typeof(EffectTargetPolicy), e.TargetPolicy) || !CombatMath.Finite(e.PhysicalDamage) || !CombatMath.Finite(e.Amount) || !CombatMath.Finite(e.DurationOverride) || !CombatMath.ValidAmounts(e.ElementDamage, false) || !CombatMath.ValidAmounts(e.Gauge, true)) return Invalid(ErrorCode.InvalidArgument, "effect.fields");
            if ((e.Kind == EffectKind.Damage || e.Kind == EffectKind.AreaDamage || e.Kind == EffectKind.ChainDamage) && (e.PhysicalDamage < 0 || !CombatMath.ValidAmounts(e.Gauge, false))) return Invalid(ErrorCode.InvalidArgument, "effect.damage");
            if (e.TargetPolicy == EffectTargetPolicy.SourceActor && e.Kind != EffectKind.ApplyStatus) return Invalid(ErrorCode.InvalidTarget, "effect.source_actor_kind");
            if (e.Kind == EffectKind.Heal && e.Amount <= 0) return Invalid(ErrorCode.InvalidArgument, "effect.heal");
            if (e.Kind == EffectKind.ApplyStatus && (String.IsNullOrEmpty(e.ReferenceId) || e.Stacks <= 0 || e.DurationOverride < 0)) return Invalid(ErrorCode.InvalidArgument, "effect.status");
            if (e.Kind == EffectKind.AddTowerStacks && e.Stacks == 0) return Invalid(ErrorCode.InvalidArgument, "effect.stacks");
            if (e.Kind != EffectKind.Damage && e.Kind != EffectKind.AreaDamage && e.Kind != EffectKind.ChainDamage && e.Kind != EffectKind.ModifyProjectile && (e.PhysicalDamage != 0 || !CombatMath.Zero(e.ElementDamage))) return Invalid(ErrorCode.InvalidArgument, "effect.unused_damage");
            if (e.Kind != EffectKind.Damage && e.Kind != EffectKind.AddGauge && e.Kind != EffectKind.ModifyProjectile && !CombatMath.Zero(e.Gauge)) return Invalid(ErrorCode.InvalidArgument, "effect.unused_gauge");
            if (e.Kind != EffectKind.Heal && e.Amount != 0) return Invalid(ErrorCode.InvalidArgument, "effect.unused_amount");
            if (e.Kind != EffectKind.ApplyStatus && e.Kind != EffectKind.ChainDamage && e.DurationOverride != 0) return Invalid(ErrorCode.InvalidArgument, "effect.unused_duration");
            if (e.Kind != EffectKind.ApplyStatus && e.Kind != EffectKind.ChainDamage && e.Kind != EffectKind.AddTowerStacks && e.Stacks != 0) return Invalid(ErrorCode.InvalidArgument, "effect.unused_stacks");
            if (e.Kind != EffectKind.ApplyStatus && e.Kind != EffectKind.ChainDamage && e.Kind != EffectKind.AddResource && !String.IsNullOrEmpty(e.ReferenceId)) return Invalid(ErrorCode.InvalidArgument, "effect.unused_reference");
            if (e.Kind != EffectKind.AddResource && e.ResourceAmount != 0) return Invalid(ErrorCode.InvalidArgument, "effect.unused_resource");
            if (e.Kind != EffectKind.ModifyProjectile && (e.DamageMultiplier != 1 || e.SpeedMultiplier != 1)) return Invalid(ErrorCode.InvalidArgument, "effect.unused_multiplier");
            if (!CombatMath.Finite(e.RadiusInCells) || e.RadiusInCells < 0 || e.MaxHits < 1 || e.MaxHits > 64 || !CombatMath.Finite(e.RetainedDamageMultiplier) || e.RetainedDamageMultiplier < 0 || e.RetainedDamageMultiplier > 1 || !CombatMath.Finite(e.DurationSeconds) || !CombatMath.Finite(e.TickIntervalSeconds) || !CombatMath.Finite(e.LingeringSeconds) || e.LingeringSeconds < 0) return Invalid(ErrorCode.InvalidArgument,"effect.advanced_fields");
            bool advanced = e.Kind == EffectKind.AreaDamage || e.Kind == EffectKind.ChainDamage || e.Kind == EffectKind.CreateArea;
            if (advanced && e.TargetPolicy != EffectTargetPolicy.SelectedActor) return Invalid(ErrorCode.InvalidTarget,"effect.advanced_target");
            if ((e.Kind == EffectKind.AreaDamage || e.Kind == EffectKind.CreateArea) && e.RadiusInCells <= 0) return Invalid(ErrorCode.InvalidArgument,"effect.radius");
            if (!advanced && e.Kind != EffectKind.ConfigurePiercing && (e.RadiusInCells != 0 || e.MaxHits != 1 || e.RetainedDamageMultiplier != 1)) return Invalid(ErrorCode.InvalidArgument,"effect.unused_advanced");
            if (e.Kind != EffectKind.ChainDamage && !String.IsNullOrEmpty(e.RequiredStatusId)) return Invalid(ErrorCode.InvalidArgument,"effect.unused_required_status");
            if (e.Kind != EffectKind.CreateArea && (e.DurationSeconds != 0 || e.TickIntervalSeconds != 0 || e.LingeringSeconds != 0 || (e.AreaEffects != null && e.AreaEffects.Count > 0))) return Invalid(ErrorCode.InvalidArgument,"effect.unused_area");
            if (e.Kind == EffectKind.ChainDamage && e.RadiusInCells <= 0) return Invalid(ErrorCode.InvalidArgument,"effect.chain_radius");
            StatusDefinition sd;
            if (e.Kind == EffectKind.ChainDamage && ((!String.IsNullOrEmpty(e.ReferenceId) && (e.Stacks <= 0 || e.DurationOverride < 0 || !owner.Catalog.TryGetStatus(e.ReferenceId,out sd) || sd.IsReactionInput)) || (!String.IsNullOrEmpty(e.RequiredStatusId) && !owner.Catalog.TryGetStatus(e.RequiredStatusId,out sd)))) return Invalid(ErrorCode.InvalidDefinition,"effect.chain_status");
            if(e.Kind==EffectKind.ChainDamage&&String.IsNullOrEmpty(e.ReferenceId)&&(e.Stacks!=0||e.DurationOverride!=0))return Invalid(ErrorCode.InvalidArgument,"effect.chain_unused_status");
            if (e.Kind == EffectKind.CreateArea)
            {
                if (e.DurationSeconds <= 0 || e.TickIntervalSeconds < 1f/Math.Max(1,owner.Catalog.GetSettings().LogicTicksPerGameSecond) || e.AreaEffects == null || e.AreaEffects.Count == 0) return Invalid(ErrorCode.InvalidArgument,"effect.area");
                foreach (EffectSpec child in e.AreaEffects) { if (child == null || (child.Kind != EffectKind.Damage && child.Kind != EffectKind.ApplyStatus && child.Kind != EffectKind.Heal) || (child.Kind == EffectKind.Heal ? child.TargetPolicy != EffectTargetPolicy.AllTowers && child.TargetPolicy != EffectTargetPolicy.Spring : child.TargetPolicy != EffectTargetPolicy.SelectedActor) || !CombatMath.Zero(child.Gauge) || (child.AreaEffects != null && child.AreaEffects.Count > 0)) return Invalid(ErrorCode.InvalidArgument,"effect.area_child"); ValidationResult shape=CheckShape(child); if(shape.Error!=ErrorCode.None)return shape; if(child.Kind==EffectKind.ApplyStatus&&(!owner.Catalog.TryGetStatus(child.ReferenceId,out sd)||sd.IsReactionInput))return Invalid(ErrorCode.InvalidDefinition,"effect.area_status"); }
            }
            return new ValidationResult(ErrorCode.None, "effect.valid");
        }
    }
}
