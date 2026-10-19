using System;
using System.Collections.Generic;
using Spotlight.Contracts;

namespace Spotlight.Elements
{
    /// <summary>Deterministic planners: no mutation, event publication or live RNG advancement.</summary>
    public sealed class ElementRuleService : IElementRuleService
    {
        private readonly IGameCatalog catalog;
        private readonly IRandomService random;
        public ElementRuleService(IGameCatalog catalog, IRandomService random)
        { if (catalog == null || random == null) throw new ArgumentNullException(); this.catalog = catalog; this.random = random; }
        private sealed class Candidate
        {
            public ProductionRuleDefinition Rule; public CellSnapshot A, B;
        }
        public ProductionResult ResolveProduction(ProductionInput input)
        {
            if (input == null || input.Board == null || input.Board.Cells == null) throw new ArgumentException("production.input");
            IRandomCursor cursor = random.CreateCursor(input.Random);
            List<CellSnapshot> blocks = new List<CellSnapshot>();
            foreach (CellSnapshot c in input.Board.Cells) if (c.OccupantKind == OccupantKind.ElementBlock) blocks.Add(c);
            blocks.Sort(delegate(CellSnapshot a, CellSnapshot b) { return a.OccupantId.Value.CompareTo(b.OccupantId.Value); });
            List<ProductionRuleDefinition> rules = new List<ProductionRuleDefinition>(catalog.GetProductionRules());
            rules.Sort(delegate(ProductionRuleDefinition a, ProductionRuleDefinition b) { return StringComparer.Ordinal.Compare(a.Id, b.Id); });
            List<Candidate> candidates = new List<Candidate>();
            foreach (ProductionRuleDefinition r in rules)
            {
                if (!r.Enabled) continue;
                ElementDefinition output;
                if (r.OutputCount <= 0 || !catalog.TryGetElement(r.OutputElementId, out output)) throw new ArgumentException("production.definition:" + r.Id);
                foreach (CellSnapshot a in blocks)
                {
                    ElementDefinition ea; if (!catalog.TryGetElement(a.DefinitionId, out ea)) throw new ArgumentException("production.element:" + a.DefinitionId);
                    if (r.Match == ProductionMatch.SingleBlock)
                    { if (ea.Element == r.A) candidates.Add(new Candidate { Rule = r, A = a }); continue; }
                    foreach (CellSnapshot b in blocks)
                    {
                        if (a.OccupantId.Value >= b.OccupantId.Value || Math.Abs(a.Cell.X - b.Cell.X) + Math.Abs(a.Cell.Y - b.Cell.Y) != 1) continue;
                        ElementDefinition eb; if (!catalog.TryGetElement(b.DefinitionId, out eb)) throw new ArgumentException("production.element:" + b.DefinitionId);
                        if (ea.Element == r.A && eb.Element == r.B) candidates.Add(new Candidate { Rule = r, A = a, B = b });
                        else if (ea.Element == r.B && eb.Element == r.A) candidates.Add(new Candidate { Rule = r, A = b, B = a });
                    }
                }
            }
            candidates.Sort(delegate(Candidate a, Candidate b)
            {
                bool ac = a.Rule.ConsumeA || a.Rule.ConsumeB, bc = b.Rule.ConsumeA || b.Rule.ConsumeB;
                int v = ac.CompareTo(bc); if (v != 0) return v;
                v = b.Rule.Priority.CompareTo(a.Rule.Priority); if (v != 0) return v;
                v = StringComparer.Ordinal.Compare(a.Rule.Id, b.Rule.Id); if (v != 0) return v;
                v = a.A.OccupantId.Value.CompareTo(b.A.OccupantId.Value); if (v != 0) return v;
                return (a.B == null ? 0 : a.B.OccupantId.Value).CompareTo(b.B == null ? 0 : b.B.OccupantId.Value);
            });
            // Shuffle only opted-in slots within the same priority/consumption phase.
            // Keeping fixed slots avoids an inconsistent mixed random/stable sort comparator.
            for (int start = 0; start < candidates.Count; )
            {
                int end = start + 1; Candidate first = candidates[start]; bool consuming = first.Rule.ConsumeA || first.Rule.ConsumeB;
                while (end < candidates.Count && candidates[end].Rule.Priority == first.Rule.Priority && (candidates[end].Rule.ConsumeA || candidates[end].Rule.ConsumeB) == consuming) end++;
                List<int> slots = new List<int>(); for (int i = start; i < end; i++) if (candidates[i].Rule.RandomTieBreak) slots.Add(i);
                for (int i = slots.Count - 1; i > 0; i--) { int j = cursor.NextInt(0, i + 1); Candidate temp = candidates[slots[i]]; candidates[slots[i]] = candidates[slots[j]]; candidates[slots[j]] = temp; }
                start = end;
            }
            HashSet<long> used = new HashSet<long>(); List<BoardMutation> removals = new List<BoardMutation>();
            List<ResourceAmount> rewards = new List<ResourceAmount>(); List<string> applied = new List<string>();
            foreach (Candidate c in candidates)
            {
                if ((c.Rule.ConsumeA || c.Rule.ConsumeB) && (used.Contains(c.A.OccupantId.Value) || (c.B != null && used.Contains(c.B.OccupantId.Value)))) continue;
                if (c.Rule.ConsumeA) Remove(c.A, used, removals);
                if (c.Rule.ConsumeB && c.B != null) Remove(c.B, used, removals);
                rewards.Add(new ResourceAmount(ResourceBucket.Hand, c.Rule.OutputElementId, c.Rule.OutputCount)); applied.Add(c.Rule.Id);
            }
            return new ProductionResult { SettlementId = input.SettlementId, CompletedNight = input.CompletedNight, ExpectedRevision = input.Board.Revision,
                Board = removals.AsReadOnly(), HandDeltas = rewards.AsReadOnly(), NextRandom = cursor.Capture(), AppliedRuleIds = applied.AsReadOnly() };
        }
        private static void Remove(CellSnapshot c, HashSet<long> used, List<BoardMutation> mutations)
        { if (used.Add(c.OccupantId.Value)) mutations.Add(new BoardMutation { Kind = BoardMutationKind.Remove, EntityId = c.OccupantId, OccupantKind = c.OccupantKind, DefinitionId = c.DefinitionId, From = c.Cell }); }
        public ReactionPlan ResolveReactions(ReactionInput input)
        {
            if (input == null || input.Tokens == null) throw new ArgumentException("reaction.input");
            List<ReactionToken> tokens = new List<ReactionToken>(); HashSet<long> ids = new HashSet<long>();
            foreach (ReactionToken t in input.Tokens) { if (!ids.Add(t.TokenId)) throw new ArgumentException("reaction.duplicate_token"); tokens.Add(new ReactionToken { TokenId = t.TokenId, Element = t.Element, DefinitionId = t.DefinitionId }); }
            tokens.Sort(delegate(ReactionToken a, ReactionToken b) { return a.TokenId.CompareTo(b.TokenId); });
            HashSet<string> applied = new HashSet<string>(input.AppliedRuleIds ?? new string[0], StringComparer.Ordinal);
            List<ReactionDefinition> rules = new List<ReactionDefinition>(catalog.GetReactionRules(input.Context));
            rules.Sort(delegate(ReactionDefinition a, ReactionDefinition b) { int v = b.Priority.CompareTo(a.Priority); return v != 0 ? v : StringComparer.Ordinal.Compare(a.Id, b.Id); });
            List<ReactionMatch> matches = new List<ReactionMatch>(); HashSet<string> ruleIds = new HashSet<string>();
            foreach (ReactionDefinition r in rules)
            {
                if (!ruleIds.Add(r.Id)) throw new ArgumentException("reaction.duplicate_rule:" + r.Id);
                if (!r.Enabled || r.Context != input.Context || (input.Context == ReactionContext.Projectile && applied.Contains(r.Id))) continue;
                if (r.ConsumeA < 0 || r.ConsumeB < 0 || (input.Context == ReactionContext.EnemyStatus && r.ConsumeA + r.ConsumeB == 0)) throw new ArgumentException("reaction.consumption:" + r.Id);
                foreach (EffectSpec e in r.Effects)
                {
                    if (input.Context == ReactionContext.Projectile && e.Kind != EffectKind.ModifyProjectile && e.Kind != EffectKind.ConfigurePiercing && e.Kind != EffectKind.AreaDamage && e.Kind != EffectKind.ChainDamage && e.Kind != EffectKind.CreateArea && e.Kind != EffectKind.AddGauge && e.Kind != EffectKind.ApplyStatus) throw new ArgumentException("reaction.projectile_effect:" + r.Id);
                    if (input.Context == ReactionContext.EnemyStatus && e.Kind != EffectKind.Damage && e.Kind != EffectKind.ApplyStatus && e.Kind != EffectKind.AreaDamage && e.Kind != EffectKind.ChainDamage && e.Kind != EffectKind.CreateArea && e.Kind != EffectKind.AddGauge) throw new ArgumentException("reaction.status_effect:" + r.Id);
                    if (input.Context == ReactionContext.EnemyStatus && e.Kind == EffectKind.Damage && Nonzero(e.Gauge)) throw new ArgumentException("reaction.gauge_loop:" + r.Id);
                    StatusDefinition sd; if (e.Kind == EffectKind.ApplyStatus && (!catalog.TryGetStatus(e.ReferenceId, out sd) || sd.IsReactionInput)) throw new ArgumentException("reaction.output_input:" + r.Id);
                }
                while (true)
                {
                    List<ReactionToken> selected = new List<ReactionToken>();
                    int needA = Math.Max(1, r.ConsumeA), needB = Math.Max(1, r.ConsumeB);
                    foreach (ReactionToken t in tokens) if (t.Element == r.A && selected.Count < needA) selected.Add(t);
                    if (selected.Count < needA) break;
                    int countB = 0; foreach (ReactionToken t in tokens) if (t.Element == r.B && !selected.Contains(t) && countB < needB) { selected.Add(t); countB++; }
                    if (countB < needB) break;
                    List<long> consumed = new List<long>();
                    for (int i = 0; i < r.ConsumeA; i++) consumed.Add(selected[i].TokenId);
                    for (int i = 0; i < r.ConsumeB; i++) consumed.Add(selected[needA + i].TokenId);
                    tokens.RemoveAll(delegate(ReactionToken t) { return consumed.Contains(t.TokenId); });
                    List<EffectSpec> effects = new List<EffectSpec>(); foreach (EffectSpec e in r.Effects) effects.Add(Clone(e));
                    matches.Add(new ReactionMatch { RuleId = r.Id, ConsumedTokenIds = consumed.AsReadOnly(), Effects = effects.AsReadOnly() });
                    if (input.Context == ReactionContext.Projectile) { applied.Add(r.Id); break; }
                }
            }
            return new ReactionPlan { Matches = matches.AsReadOnly(), RemainingTokens = tokens.AsReadOnly() };
        }
        private static bool Nonzero(ElementAmounts a) { return a.Water != 0 || a.Fire != 0 || a.Earth != 0 || a.Wood != 0 || a.Wind != 0 || a.Thunder != 0; }
        private static IReadOnlyList<EffectSpec> CloneAreaEffects(IReadOnlyList<EffectSpec> input) { if(input==null)return null; var result=new EffectSpec[input.Count]; for(int i=0;i<result.Length;i++)result[i]=input[i]==null?null:Clone(input[i]); return result; }
        private static EffectSpec Clone(EffectSpec e) { return new EffectSpec { Id = e.Id, Kind = e.Kind, TargetPolicy = e.TargetPolicy, ReferenceId = e.ReferenceId, ResourceBucket = e.ResourceBucket, ResourceAmount = e.ResourceAmount, PhysicalDamage = e.PhysicalDamage, ElementDamage = e.ElementDamage, Gauge = e.Gauge, Amount = e.Amount, Probability = e.Probability, UseAreaLease = e.UseAreaLease, Stacks = e.Stacks, DurationOverride = e.DurationOverride, DamageMultiplier = e.DamageMultiplier, SpeedMultiplier = e.SpeedMultiplier,RadiusInCells=e.RadiusInCells,MaxHits=e.MaxHits,RetainedDamageMultiplier=e.RetainedDamageMultiplier,DurationSeconds=e.DurationSeconds,TickIntervalSeconds=e.TickIntervalSeconds,LingeringSeconds=e.LingeringSeconds,RequiredStatusId=e.RequiredStatusId,AreaEffects=CloneAreaEffects(e.AreaEffects) }; }
        public ProjectileModifierDefinition GetProjectileModifier(ElementType element)
        {
            ProjectileModifierDefinition found = null;
            string[] ids = { "elm.water", "elm.fire", "elm.earth", "elm.wood", "elm.wind", "elm.thunder" }; ElementDefinition direct;
            if ((int)element >= 1 && (int)element <= 6 && catalog.TryGetElement(ids[(int)element - 1], out direct)) found = direct.ProjectileModifier;
            // Element identities are discovered from deployed/catalogued production outputs; no invented ID conventions.
            if (found == null) foreach (ProductionRuleDefinition r in catalog.GetProductionRules())
            { ElementDefinition d; if (catalog.TryGetElement(r.OutputElementId, out d) && d.Element == element && d.ProjectileModifier != null) { found = d.ProjectileModifier; break; } }
            if (found == null) throw new ArgumentException("element.modifier_missing:" + element);
            return new ProjectileModifierDefinition { Element = found.Element, PhysicalAdd = found.PhysicalAdd, DamageAdd = found.DamageAdd, GaugeAdd = found.GaugeAdd, DamageMultiplier = found.DamageMultiplier, SpeedMultiplier = found.SpeedMultiplier };
        }
    }
}
