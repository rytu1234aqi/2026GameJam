using System;
using System.Collections.Generic;
using Spotlight.Bootstrap;
using Spotlight.Combat.Definitions;
using Spotlight.Contracts;
using Spotlight.Elements.Definitions;
using UnityEditor;
using UnityEngine;
using EffectConfig = Spotlight.Elements.Definitions.EffectConfig;

namespace Spotlight.EditorTools
{
    public static class AdvancedEffectSetupTool
    {
        public const string CatalogPath = "Assets/_Game/Data/Prototype/P1/PrototypeCatalog.asset";
        private const string Root = "Assets/_Game/Data/Prototype/AdvancedEffects";
        [MenuItem("聚光灯/效果库/创建或补齐配置")]
        public static void CreateOrComplete()
        {
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets/_Game/Data/Prototype", "AdvancedEffects");
            Status("Paralysis", "status.effect.paralysis", s => { s.ElementTag = ElementType.Thunder; s.DurationSeconds = 1.5f; s.PreventMovement = true; s.PreventAttack = true; });
            Status("MudSlow", "status.area.mud_slow", s => s.MoveSlowPerStack = .6f);
            Status("LavaSlow", "status.area.lava_slow", s => s.MoveSlowPerStack = .3f);
            Status("LavaBurn", "status.area.lava_burn", s => { s.ElementTag = ElementType.Fire; s.DotElementPerStack.Fire = 10; s.IsReactionDamage = true; });
            Configure("Water_Fire", AreaDamage(30));
            Configure("Fire_Thunder", AreaDamage(50), new EffectConfig { Id = "effect.explosion.gauge", Kind = EffectKind.AddGauge, TargetPolicy = EffectTargetPolicy.SelectedActor, Gauge = new Spotlight.Elements.Definitions.ElementAmountsConfig { Fire = 30, Thunder = 30 } });
            Configure("Thunder_Thunder", Chain(15, 3, 1.5f, null));
            Configure("Water_Thunder", Chain(20, 2, 1, "status.water"));
            Configure("Water_Earth", Area("mud", "status.area.mud_slow"));
            Configure("Fire_Earth", Area("lava", "status.area.lava_burn", "status.area.lava_slow"));
            CompleteCombinations();
            PrototypeCatalogSO catalog = AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("正式 PrototypeCatalog 不存在：" + CatalogPath);
            catalog.CollectEffectAssetsInEditor(); CompleteWoodProduction(catalog); AssetDatabase.SaveAssets();
            Debug.Log("21种组合配置已补齐。新增数值为实验基线；已填写配置保持不变。仅原样旧风风模板升级正式穿透。战斗与生产请在组合试验台分别检查。");
        }
        private static void CompleteCombinations()
        {
            Status("Haste", "status.combo.haste", s => { s.DurationSeconds = 6; s.AttackHastePerStack = .3f; });
            Status("WindSlow", "status.combo.wind_slow", s => { s.DurationSeconds = 3; s.MoveSlowPerStack = .2f; });
            Status("Blaze", "status.combo.blaze", s => { s.DurationSeconds = 8; s.OutgoingElementDamageBonusPerStack.Fire = .5f; s.OutgoingGaugeBonusPerStack.Fire = 1; });
            Status("FireBoost", "status.combo.fire_boost", s => { s.DurationSeconds = 6; s.OutgoingElementDamageBonusPerStack.Fire = .5f; });
            Status("WaterField", "status.combo.water_slow", s => { s.DurationSeconds = 6; s.MoveSlowPerStack = .15f; });
            Status("Freeze", "status.combo.freeze", s => { s.DurationSeconds = 1.5f; s.PreventMovement = true; s.PreventAttack = true; });
            Status("FireField", "status.combo.fire_dot", s => { s.DurationSeconds = 6; s.DotElementPerStack.Fire = 10; s.IsReactionDamage = true; });
            Status("Sand", "status.combo.sand", s => { s.DurationSeconds = 6; s.MoveSlowPerStack = .2f; s.AccuracyReductionPerStack = .3f; });
            Status("Magnetic", "status.combo.magnetic", s => { s.DurationSeconds = 6; s.ArmorBreakPerStack = .3f; s.IncomingGaugeBonusPerStack.Thunder = .5f; });
            ConfigureProjectile("Wind_Wind", Apply("status.combo.haste", EffectTargetPolicy.SourceActor, 6), new EffectConfig { Id = "effect.piercing", Kind = EffectKind.ConfigurePiercing, TargetPolicy = EffectTargetPolicy.CurrentProjectile, MaxHits = 3, RetainedDamageMultiplier = .8f }, Apply("status.combo.wind_slow", EffectTargetPolicy.SelectedActor, 3));
            ConfigureProjectile("Fire_Fire", Apply("status.combo.blaze", EffectTargetPolicy.SourceActor, 8));
            ConfigureProjectile("Fire_Wood", Apply("status.combo.fire_boost", EffectTargetPolicy.SourceActor, 6));
            Configure("Water_Wood", Field("water_wood", 2.5f, ChildStatus("status.combo.water_slow")));
            var freeze = ChildStatus("status.combo.freeze"); freeze.DurationOverride = 1.5f; freeze.Probability = .2f; freeze.UseAreaLease = false;
            Configure("Water_Wind", Field("water_wind", 2.5f, ChildStatus("status.combo.water_slow"), freeze));
            Configure("Fire_Wind", Field("fire_wind", 2.5f, ChildStatus("status.combo.fire_dot")));
            Configure("Earth_Wind", Field("sand", 1, ChildStatus("status.combo.sand")));
            Configure("Earth_Thunder", Field("magnetic", 1, ChildStatus("status.combo.magnetic")), Apply("status.effect.paralysis", EffectTargetPolicy.SelectedActor, 1.5f));
            Configure("Wood_Wood", Field("healing", 2,
                new AreaEffectConfig { Id = "effect.healing.towers", Kind = EffectKind.Heal, TargetPolicy = EffectTargetPolicy.AllTowers, Amount = 2 },
                new AreaEffectConfig { Id = "effect.healing.spring", Kind = EffectKind.Heal, TargetPolicy = EffectTargetPolicy.Spring, Amount = 2 },
                ChildStatus("status.entangle")));
        }
        private static EffectConfig Apply(string id, EffectTargetPolicy target, float duration) => new EffectConfig { Id = "effect." + id, Kind = EffectKind.ApplyStatus, ReferenceId = id, TargetPolicy = target, Stacks = 1, DurationOverride = duration };
        private static AreaEffectConfig ChildStatus(string id) => new AreaEffectConfig { Id = "effect." + id, Kind = EffectKind.ApplyStatus, ReferenceId = id, TargetPolicy = EffectTargetPolicy.SelectedActor, Stacks = 1, DurationOverride = 1.5f };
        private static EffectConfig Field(string id, float radius, params AreaEffectConfig[] children) => new EffectConfig { Id = "effect.area." + id, Kind = EffectKind.CreateArea, TargetPolicy = EffectTargetPolicy.SelectedActor, RadiusInCells = radius, DurationSeconds = 6, TickIntervalSeconds = 1, LingeringSeconds = 1.5f, AreaEffects = children };
        public static bool IsLegacyPiercingTemplate(ReactionDefinitionSO reaction)
        {
            if (reaction == null || reaction.Enabled || reaction.Context != ReactionContext.Projectile || reaction.A != ElementType.Wind || reaction.B != ElementType.Wind || reaction.Id != "reaction.Projectile.Wind.Wind" || reaction.ConsumeA != 1 || reaction.ConsumeB != 1 || reaction.Priority != 100 || reaction.Effects == null || reaction.Effects.Length != 1) return false;
            var template = new EffectConfig { Id = "effect.piercing.template", Kind = EffectKind.ConfigurePiercing, TargetPolicy = EffectTargetPolicy.CurrentProjectile, MaxHits = 3, RetainedDamageMultiplier = .8f };
            return JsonUtility.ToJson(reaction.Effects[0]) == JsonUtility.ToJson(template);
        }
        private static void ConfigureProjectile(string pair, params EffectConfig[] effects)
        {
            var reaction = AssetDatabase.LoadAssetAtPath<ReactionDefinitionSO>("Assets/_Game/Data/Prototype/P2/Reaction_Projectile_" + pair + ".asset");
            if (reaction == null) throw new InvalidOperationException("缺少正式弹道槽：" + pair);
            if ((!IsUntouchedSlot(reaction) && !IsLegacyPiercingTemplate(reaction)) || reaction.Context != ReactionContext.Projectile || reaction.Id != "reaction.Projectile." + pair.Replace('_', '.')) return;
            reaction.Enabled = true; reaction.Effects = effects; EditorUtility.SetDirty(reaction);
        }
        private static void CompleteWoodProduction(PrototypeCatalogSO catalog)
        {
            const string id = "production.wood_wood_bonus";
            ProductionRuleSO rule = null;
            foreach (string guid in AssetDatabase.FindAssets("t:ProductionRuleSO", new[] { "Assets/_Game/Data" }))
            {
                var found = AssetDatabase.LoadAssetAtPath<ProductionRuleSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (found != null && found.Id == id) { rule = found; break; }
            }
            if (rule == null)
            {
                rule = ScriptableObject.CreateInstance<ProductionRuleSO>(); rule.Id = id; rule.Enabled = true; rule.Match = ProductionMatch.AdjacentPair; rule.A = ElementType.Wood; rule.B = ElementType.Wood; rule.OutputElementId = "elm.wood"; rule.OutputCount = 1; rule.Priority = 100;
                AssetDatabase.CreateAsset(rule, Root + "/Production_WoodWoodBonus.asset");
            }
            var rules = new List<ProductionRuleSO>(catalog.ProductionRules ?? new ProductionRuleSO[0]);
            if (!rules.Contains(rule)) { rules.Add(rule); catalog.ProductionRules = rules.ToArray(); EditorUtility.SetDirty(catalog); }
        }
        private static void Status(string filename, string id, Action<StatusEffectDefinitionSO> initialize)
        {
            string path = Root + "/Status_" + filename + ".asset";
            if (AssetDatabase.LoadAssetAtPath<StatusEffectDefinitionSO>(path) != null) return;
            foreach (string guid in AssetDatabase.FindAssets("t:StatusEffectDefinitionSO", new[] { "Assets/_Game/Data" }))
                if (AssetDatabase.LoadAssetAtPath<StatusEffectDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid)).Id == id) return;
            StatusEffectDefinitionSO status = ScriptableObject.CreateInstance<StatusEffectDefinitionSO>();
            status.Id = id; status.IsReactionInput = false; status.DurationSeconds = 1.5f; status.TickIntervalSeconds = 1; status.MaxStacks = 1;
            initialize(status); AssetDatabase.CreateAsset(status, path);
        }
        private static EffectConfig AreaDamage(float damage) => new EffectConfig { Id = "effect.area_damage", Kind = EffectKind.AreaDamage, TargetPolicy = EffectTargetPolicy.SelectedActor, PhysicalDamage = damage, RadiusInCells = 2 };
        private static EffectConfig Chain(float damage, int hits, float duration, string required) => new EffectConfig { Id = "effect.chain_damage", Kind = EffectKind.ChainDamage, TargetPolicy = EffectTargetPolicy.SelectedActor, ElementDamage = new Spotlight.Elements.Definitions.ElementAmountsConfig { Thunder = damage }, RadiusInCells = 2, MaxHits = hits, RetainedDamageMultiplier = .8f, ReferenceId = "status.effect.paralysis", RequiredStatusId = required, Stacks = 1, DurationOverride = duration };
        private static EffectConfig Area(string name, params string[] statuses)
        {
            List<Spotlight.Elements.Definitions.AreaEffectConfig> effects = new List<Spotlight.Elements.Definitions.AreaEffectConfig>();
            foreach (string status in statuses) effects.Add(new Spotlight.Elements.Definitions.AreaEffectConfig { Id = "effect." + name + "." + effects.Count, Kind = EffectKind.ApplyStatus, TargetPolicy = EffectTargetPolicy.SelectedActor, ReferenceId = status, Stacks = 1, DurationOverride = 1.5f });
            return new EffectConfig { Id = "effect.area." + name, Kind = EffectKind.CreateArea, TargetPolicy = EffectTargetPolicy.SelectedActor, RadiusInCells = 1, DurationSeconds = 6, TickIntervalSeconds = 1, LingeringSeconds = 1.5f, AreaEffects = effects.ToArray() };
        }
        private static void Configure(string pair, params EffectConfig[] effects)
        {
            string path = "Assets/_Game/Data/Prototype/P2/Reaction_EnemyStatus_" + pair + ".asset";
            ReactionDefinitionSO reaction = AssetDatabase.LoadAssetAtPath<ReactionDefinitionSO>(path);
            if (reaction == null) throw new InvalidOperationException("缺少正式反应槽：" + path);
            // Only untouched disabled slots qualify. A designer's disabled custom configuration is also preserved.
            if (!IsUntouchedSlot(reaction) || reaction.Context != ReactionContext.EnemyStatus || reaction.Id != "reaction.EnemyStatus." + pair.Replace('_', '.')) return;
            reaction.Enabled = true; reaction.Effects = effects; EditorUtility.SetDirty(reaction);
        }
        private static bool IsUntouchedSlot(ReactionDefinitionSO reaction)
        {
            return !reaction.Enabled && (reaction.Effects == null || reaction.Effects.Length == 0)
                && reaction.ConsumeA == 1 && reaction.ConsumeB == 1 && reaction.Priority == 100;
        }
        public static IReadOnlyList<string> ValidateFormalCatalog()
        {
            PrototypeCatalogSO catalog = AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(CatalogPath);
            if (catalog == null) return new[] { "正式 PrototypeCatalog 不存在：" + CatalogPath };
            return catalog.Validate();
        }
        [MenuItem("聚光灯/效果库/仅校验配置")]
        public static void ValidateOnly()
        {
            IReadOnlyList<string> errors = ValidateFormalCatalog();
            foreach (string error in errors) Debug.LogError("效果库配置校验：" + error);
            if (errors.Count == 0) Debug.Log("效果库与正式目录校验通过。");
        }
    }
}
