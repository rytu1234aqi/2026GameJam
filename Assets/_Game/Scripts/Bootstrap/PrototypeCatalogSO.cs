using System;
using System.Collections.Generic;
using UnityEngine;
using Spotlight.Contracts;
using Spotlight.Core.Data;
using Spotlight.Elements.Definitions;
using Spotlight.Combat.Definitions;
using Spotlight.Enemies.Definitions;
using Spotlight.Gameplay.Definitions;
namespace Spotlight.Bootstrap
{
    [CreateAssetMenu(menuName="Spotlight/P1/Prototype Catalog",fileName="PrototypeCatalog")] public sealed class PrototypeCatalogSO:ScriptableObject
    {
        public GameConfigSO Settings;
        public EndlessConfigSO Endless;
        public PrefabEntry[] PrefabEntries=new PrefabEntry[0];
        public LevelConfigSO[] Levels=new LevelConfigSO[0];
        public ElementDefinitionSO[] Elements=new ElementDefinitionSO[0];
        public ProductionRuleSO[] ProductionRules=new ProductionRuleSO[0];
        public ReactionDefinitionSO[] Reactions=new ReactionDefinitionSO[0];
        public TowerDefinitionSO[] Towers=new TowerDefinitionSO[0];
        public ProjectileDefinitionSO[] Projectiles=new ProjectileDefinitionSO[0];
        public GaugeConfigSO[] Gauges=new GaugeConfigSO[0];
        public StatusEffectDefinitionSO[] Statuses=new StatusEffectDefinitionSO[0];
        public SpecialSkillDefinitionSO[] Skills=new SpecialSkillDefinitionSO[0];
        public EnemyDefinitionSO[] Enemies=new EnemyDefinitionSO[0];
        public WaveDefinitionSO[] StoryWaves=new WaveDefinitionSO[0];
        public WaveDefinitionSO[] WaveTemplates=new WaveDefinitionSO[0];
        public BossSkillDefinitionSO[] BossSkills=new BossSkillDefinitionSO[0];
        public DropTableSO[] DropTables=new DropTableSO[0];
        public ItemDefinitionSO[] Items=new ItemDefinitionSO[0];
        public DayEventDefinitionSO[] DayEvents=new DayEventDefinitionSO[0];
        public ResourceDefinitionSO[] Resources=new ResourceDefinitionSO[0];
#if UNITY_EDITOR
        // Only effect assets are collected here; unrelated catalog entries stay authored explicitly.
        public void CollectEffectAssetsInEditor()
        {
            Reactions = CollectInEditor(Reactions);
            Statuses = CollectInEditor(Statuses);
            UnityEditor.EditorUtility.SetDirty(this);
        }
        private static T[] CollectInEditor<T>(T[] existing) where T : ScriptableObject
        {
            List<T> result = new List<T>();
            if (existing != null) foreach (T asset in existing) if (asset != null && !result.Contains(asset)) result.Add(asset);
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { "Assets/_Game/Data" });
            Array.Sort(guids, StringComparer.Ordinal);
            foreach (string guid in guids)
            {
                T asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && !result.Contains(asset)) result.Add(asset);
            }
            return result.ToArray();
        }
#endif
        public CatalogData BuildCatalogData()
        {
            CatalogData data=new CatalogData();
            data.Settings=Settings==null?null:Settings.ToSettings();
            data.Endless=Endless==null?null:Endless.ToDefinition();
            List<LevelDefinition> levels=new List<LevelDefinition>();
            if(Levels!=null)foreach(LevelConfigSO asset in Levels)levels.Add(asset==null?null:asset.ToDefinition());
            data.Levels=levels.ToArray();
            List<ElementDefinition> elements=new List<ElementDefinition>();
            if(Elements!=null)foreach(ElementDefinitionSO asset in Elements)elements.Add(asset==null?null:asset.ToDefinition());
            data.Elements=elements.ToArray();
            List<ProductionRuleDefinition> productionrules=new List<ProductionRuleDefinition>();
            if(ProductionRules!=null)foreach(ProductionRuleSO asset in ProductionRules)productionrules.Add(asset==null?null:asset.ToDefinition());
            data.ProductionRules=productionrules.ToArray();
            List<ReactionDefinition> reactions=new List<ReactionDefinition>();
            if(Reactions!=null)foreach(ReactionDefinitionSO asset in Reactions)reactions.Add(asset==null?null:asset.ToDefinition());
            data.Reactions=reactions.ToArray();
            List<TowerDefinition> towers=new List<TowerDefinition>();
            if(Towers!=null)foreach(TowerDefinitionSO asset in Towers)towers.Add(asset==null?null:asset.ToDefinition());
            data.Towers=towers.ToArray();
            List<ProjectileDefinition> projectiles=new List<ProjectileDefinition>();
            if(Projectiles!=null)foreach(ProjectileDefinitionSO asset in Projectiles)projectiles.Add(asset==null?null:asset.ToDefinition());
            data.Projectiles=projectiles.ToArray();
            List<GaugeDefinition> gauges=new List<GaugeDefinition>();
            if(Gauges!=null)foreach(GaugeConfigSO asset in Gauges)gauges.Add(asset==null?null:asset.ToDefinition());
            data.Gauges=gauges.ToArray();
            List<StatusDefinition> statuses=new List<StatusDefinition>();
            if(Statuses!=null)foreach(StatusEffectDefinitionSO asset in Statuses)statuses.Add(asset==null?null:asset.ToDefinition());
            data.Statuses=statuses.ToArray();
            List<SpecialSkillDefinition> skills=new List<SpecialSkillDefinition>();
            if(Skills!=null)foreach(SpecialSkillDefinitionSO asset in Skills)skills.Add(asset==null?null:asset.ToDefinition());
            data.Skills=skills.ToArray();
            List<EnemyDefinition> enemies=new List<EnemyDefinition>();
            if(Enemies!=null)foreach(EnemyDefinitionSO asset in Enemies)enemies.Add(asset==null?null:asset.ToDefinition());
            data.Enemies=enemies.ToArray();
            List<WaveDefinition> storywaves=new List<WaveDefinition>();
            if(StoryWaves!=null)foreach(WaveDefinitionSO asset in StoryWaves)storywaves.Add(asset==null?null:asset.ToDefinition());
            data.StoryWaves=storywaves.ToArray();
            List<WaveDefinition> wavetemplates=new List<WaveDefinition>();
            if(WaveTemplates!=null)foreach(WaveDefinitionSO asset in WaveTemplates)wavetemplates.Add(asset==null?null:asset.ToDefinition());
            data.WaveTemplates=wavetemplates.ToArray();
            List<BossSkillDefinition> bossskills=new List<BossSkillDefinition>();
            if(BossSkills!=null)foreach(BossSkillDefinitionSO asset in BossSkills)bossskills.Add(asset==null?null:asset.ToDefinition());
            data.BossSkills=bossskills.ToArray();
            List<DropTableDefinition> droptables=new List<DropTableDefinition>();
            if(DropTables!=null)foreach(DropTableSO asset in DropTables)droptables.Add(asset==null?null:asset.ToDefinition());
            data.DropTables=droptables.ToArray();
            List<ItemDefinition> items=new List<ItemDefinition>();
            if(Items!=null)foreach(ItemDefinitionSO asset in Items)items.Add(asset==null?null:asset.ToDefinition());
            data.Items=items.ToArray();
            List<DayEventDefinition> dayevents=new List<DayEventDefinition>();
            if(DayEvents!=null)foreach(DayEventDefinitionSO asset in DayEvents)dayevents.Add(asset==null?null:asset.ToDefinition());
            data.DayEvents=dayevents.ToArray();
            List<ResourceDefinition> resources=new List<ResourceDefinition>();
            if(Resources!=null)foreach(ResourceDefinitionSO asset in Resources)resources.Add(asset==null?null:asset.ToDefinition());
            data.Resources=resources.ToArray();
            return data;
        }
        public IReadOnlyList<string> Validate()
        {
            List<string> errors=new List<string>(new GameCatalog(BuildCatalogData()).ValidateAll());
            HashSet<string> keys=new HashSet<string>();
            if(PrefabEntries!=null)foreach(PrefabEntry e in PrefabEntries)if(e==null||string.IsNullOrEmpty(e.Key)||!keys.Add(e.Key)||e.Prefab==null)errors.Add("Invalid or duplicate prefab mapping");
            return errors.ToArray();
        }
    }
}
