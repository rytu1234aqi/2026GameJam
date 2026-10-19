using System;
using System.Collections.Generic;
using Spotlight.Contracts;
namespace Spotlight.Core.Data
{
    public sealed class GameCatalog:IGameCatalog
    {
        readonly CatalogData data;
        readonly List<string> indexErrors=new List<string>();
        Func<int,GameMode,IReadOnlyList<WaveDefinition>> endlessResolver;
        readonly Dictionary<string,LevelDefinition> levels=new Dictionary<string,LevelDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,ElementDefinition> elements=new Dictionary<string,ElementDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,TowerDefinition> towers=new Dictionary<string,TowerDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,ProjectileDefinition> projectiles=new Dictionary<string,ProjectileDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,StatusDefinition> statuses=new Dictionary<string,StatusDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,EnemyDefinition> enemies=new Dictionary<string,EnemyDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,ItemDefinition> items=new Dictionary<string,ItemDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,ResourceDefinition> resources=new Dictionary<string,ResourceDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,DayEventDefinition> dayevents=new Dictionary<string,DayEventDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,BossSkillDefinition> bossskills=new Dictionary<string,BossSkillDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,DropTableDefinition> droptables=new Dictionary<string,DropTableDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,SpecialSkillDefinition> skills=new Dictionary<string,SpecialSkillDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,ProductionRuleDefinition> productionrules=new Dictionary<string,ProductionRuleDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,ReactionDefinition> reactions=new Dictionary<string,ReactionDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,WaveDefinition> storywaves=new Dictionary<string,WaveDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string,WaveDefinition> wavetemplates=new Dictionary<string,WaveDefinition>(StringComparer.Ordinal);
        public GameCatalog(CatalogData source)
        {
            if(source==null)throw new ArgumentNullException("source");
            data=DtoCopy.Clone(source);
            Index(data.Levels,levels,delegate(LevelDefinition d)
            {
                return d.Id;
            }
            ,"Levels");
            Index(data.Elements,elements,delegate(ElementDefinition d)
            {
                return d.Id;
            }
            ,"Elements");
            Index(data.Towers,towers,delegate(TowerDefinition d)
            {
                return d.Id;
            }
            ,"Towers");
            Index(data.Projectiles,projectiles,delegate(ProjectileDefinition d)
            {
                return d.Id;
            }
            ,"Projectiles");
            Index(data.Statuses,statuses,delegate(StatusDefinition d)
            {
                return d.Id;
            }
            ,"Statuses");
            Index(data.Enemies,enemies,delegate(EnemyDefinition d)
            {
                return d.Id;
            }
            ,"Enemies");
            Index(data.Items,items,delegate(ItemDefinition d)
            {
                return d.Id;
            }
            ,"Items");
            Index(data.Resources,resources,delegate(ResourceDefinition d)
            {
                return d.Id;
            }
            ,"Resources");
            Index(data.DayEvents,dayevents,delegate(DayEventDefinition d)
            {
                return d.Id;
            }
            ,"DayEvents");
            Index(data.BossSkills,bossskills,delegate(BossSkillDefinition d)
            {
                return d.Id;
            }
            ,"BossSkills");
            Index(data.DropTables,droptables,delegate(DropTableDefinition d)
            {
                return d.Id;
            }
            ,"DropTables");
            Index(data.Skills,skills,delegate(SpecialSkillDefinition d)
            {
                return d.Id;
            }
            ,"Skills");
            Index(data.ProductionRules,productionrules,delegate(ProductionRuleDefinition d)
            {
                return d.Id;
            }
            ,"ProductionRules");
            Index(data.Reactions,reactions,delegate(ReactionDefinition d)
            {
                return d.Id;
            }
            ,"Reactions");
            Index(data.StoryWaves,storywaves,delegate(WaveDefinition d)
            {
                return d.Id;
            }
            ,"StoryWaves");
            Index(data.WaveTemplates,wavetemplates,delegate(WaveDefinition d)
            {
                return d.Id;
            }
            ,"WaveTemplates");
        }
        void Index<T>(T[] values,Dictionary<string,T> index,Func<T,string> id,string kind) where T:class
        {
            if(values==null)
            {
                indexErrors.Add(kind+": null collection");
                return;
            }
            foreach(T value in values)
            {
                if(value==null)
                {
                    indexErrors.Add(kind+": null definition");
                    continue;
                }
                string key=id(value);
                if(string.IsNullOrEmpty(key))
                {
                    indexErrors.Add(kind+": empty ID");
                    continue;
                }
                if(index.ContainsKey(key))indexErrors.Add(kind+": duplicate "+key);
                else index.Add(key,value);
            }
        }
        bool Get<T>(Dictionary<string,T> index,string id,out T value)
        {
            T found;
            bool ok=id!=null&&index.TryGetValue(id,out found);
            value=ok?DtoCopy.Clone(index[id]):default(T);
            return ok;
        }
        public bool TryGetLevel(string id,out LevelDefinition value)
        {
            return Get(levels,id,out value);
        }
        public bool TryGetElement(string id,out ElementDefinition value)
        {
            return Get(elements,id,out value);
        }
        public bool TryGetTower(string id,out TowerDefinition value)
        {
            return Get(towers,id,out value);
        }
        public bool TryGetProjectile(string id,out ProjectileDefinition value)
        {
            return Get(projectiles,id,out value);
        }
        public bool TryGetStatus(string id,out StatusDefinition value)
        {
            return Get(statuses,id,out value);
        }
        public bool TryGetEnemy(string id,out EnemyDefinition value)
        {
            return Get(enemies,id,out value);
        }
        public bool TryGetItem(string id,out ItemDefinition value)
        {
            return Get(items,id,out value);
        }
        public bool TryGetResource(string id,out ResourceDefinition value)
        {
            return Get(resources,id,out value);
        }
        public bool TryGetEvent(string id,out DayEventDefinition value)
        {
            return Get(dayevents,id,out value);
        }
        public bool TryGetBossSkill(string id,out BossSkillDefinition value)
        {
            return Get(bossskills,id,out value);
        }
        public bool TryGetDropTable(string id,out DropTableDefinition value)
        {
            return Get(droptables,id,out value);
        }
        public bool TryGetSkill(string id,out SpecialSkillDefinition value)
        {
            return Get(skills,id,out value);
        }
        public bool TryGetWaveTemplate(string id,out WaveDefinition value)
        {
            return Get(wavetemplates,id,out value);
        }
        public GameSettings GetSettings()
        {
            return DtoCopy.Clone(data.Settings);
        }
        public EndlessDefinition GetEndlessDefinition()
        {
            return DtoCopy.Clone(data.Endless);
        }
        public IReadOnlyList<ProductionRuleDefinition> GetProductionRules()
        {
            return DtoCopy.Clone(data.ProductionRules)??new ProductionRuleDefinition[0];
        }
        public IReadOnlyList<GaugeDefinition> GetGaugeDefinitions()
        {
            return DtoCopy.Clone(data.Gauges)??new GaugeDefinition[0];
        }
        public IReadOnlyList<DayEventDefinition> GetDayEvents()
        {
            return DtoCopy.Clone(data.DayEvents)??new DayEventDefinition[0];
        }
        public IReadOnlyList<ReactionDefinition> GetReactionRules(ReactionContext context)
        {
            List<ReactionDefinition> list=new List<ReactionDefinition>();
            foreach(ReactionDefinition d in reactions.Values)if(d.Context==context)list.Add(DtoCopy.Clone(d));
            list.Sort(delegate(ReactionDefinition a,ReactionDefinition b)
            {
                int c=b.Priority.CompareTo(a.Priority);return c!=0?c:string.CompareOrdinal(a.Id,b.Id);
            }
            );
            return list.ToArray();
        }
        public void SetEndlessWaveResolver(Func<int,GameMode,IReadOnlyList<WaveDefinition>> resolver)
        {
            endlessResolver=resolver;
        }
        public IReadOnlyList<WaveDefinition> GetWaves(int dayIndex,GameMode mode)
        {
            if(mode==GameMode.Endless && endlessResolver!=null)return DtoCopy.Clone(endlessResolver(dayIndex,mode))??new WaveDefinition[0];
            List<WaveDefinition> list=new List<WaveDefinition>();
            foreach(WaveDefinition w in storywaves.Values)if(w.DayIndex==dayIndex)list.Add(DtoCopy.Clone(w));
            list.Sort(delegate(WaveDefinition a,WaveDefinition b)
            {
                int c=a.WaveIndex.CompareTo(b.WaveIndex);return c!=0?c:string.CompareOrdinal(a.Id,b.Id);
            }
            );
            return list.ToArray();
        }
        static bool Finite(float n)
        {
            return !float.IsNaN(n)&&!float.IsInfinity(n);
        }
        static bool Pos(float n)
        {
            return Finite(n)&&n>0;
        }
        static bool Nonneg(float n)
        {
            return Finite(n)&&n>=0;
        }
        static bool Amounts(ElementAmounts a,bool resistance)
        {
            float[] values=
            {
                a.Water,a.Fire,a.Earth,a.Wood,a.Wind,a.Thunder
            }
            ;
            foreach(float v in values)if(!Nonneg(v)||(resistance&&v>1))return false;
            return true;
        }
        static bool Zero(ElementAmounts a)
        {
            return a.Water==0&&a.Fire==0&&a.Earth==0&&a.Wood==0&&a.Wind==0&&a.Thunder==0;
        }
        void Check(List<string> errors,bool condition,string message)
        {
            if(!condition)errors.Add(message);
        }
        bool Resource(ResourceBucket bucket,string id)
        {
            return id!=null&&(bucket==ResourceBucket.Hand?elements.ContainsKey(id):bucket==ResourceBucket.Inventory&&(items.ContainsKey(id)||resources.ContainsKey(id)));
        }
        void Costs(List<string> errors,IReadOnlyList<ResourceAmount> values,string owner)
        {
            if(values==null)
            {
                errors.Add(owner+": null resource list");
                return;
            }
            Dictionary<string,long> sum=new Dictionary<string,long>();
            foreach(ResourceAmount r in values)
            {
                Check(errors,r.Amount>=0&&Resource(r.Bucket,r.DefinitionId),owner+": invalid resource "+r.DefinitionId);
                string key=((int)r.Bucket)+":"+r.DefinitionId;
                long n;
                sum.TryGetValue(key,out n);
                try
                {
                    sum[key]=checked(n+r.Amount);
                }
                catch(OverflowException)
                {
                    errors.Add(owner+": resource overflow");
                }
            }
        }
        void Effects(List<string> errors,IReadOnlyList<EffectSpec> values,string owner,ReactionContext? context)
        {
            if(values==null)
            {
                errors.Add(owner+": null effects");
                return;
            }
            foreach(EffectSpec f in values)
            {
                if(f==null)
                {
                    errors.Add(owner+": null effect");
                    continue;
                }
                Check(errors,Enum.IsDefined(typeof(EffectKind),f.Kind)&&Enum.IsDefined(typeof(EffectTargetPolicy),f.TargetPolicy)&&Nonneg(f.PhysicalDamage)&&Amounts(f.ElementDamage,false)&&Amounts(f.Gauge,false)&&Nonneg(f.Probability)&&f.Probability<=1&&(f.Kind==EffectKind.ApplyStatus||f.Probability==1)&&Nonneg(f.Amount)&&Nonneg(f.DurationOverride)&&Pos(f.DamageMultiplier)&&Pos(f.SpeedMultiplier)&&Nonneg(f.RadiusInCells)&&Nonneg(f.DurationSeconds)&&Nonneg(f.TickIntervalSeconds)&&Nonneg(f.LingeringSeconds)&&Nonneg(f.RetainedDamageMultiplier),owner+": invalid effect");
                Check(errors,f.TargetPolicy!=EffectTargetPolicy.SourceActor||f.Kind==EffectKind.ApplyStatus,owner+": source actor only supports status");
                if(f.TargetPolicy==EffectTargetPolicy.SourceActor&&f.ReferenceId!=null&&statuses.ContainsKey(f.ReferenceId))Check(errors,!statuses[f.ReferenceId].IsReactionInput,owner+": source buff must not feed reactions");
                if(f.Kind==EffectKind.ApplyStatus)Check(errors,f.ReferenceId!=null&&statuses.ContainsKey(f.ReferenceId)&&f.Stacks>0,owner+": invalid status reference/stacks");
                if(f.Kind==EffectKind.AddResource)Check(errors,f.ResourceAmount>=0&&Resource(f.ResourceBucket,f.ReferenceId),owner+": invalid reward");
                if(f.Kind==EffectKind.AddTowerStacks)Check(errors,f.Stacks>0,owner+": invalid tower stacks");
                Check(errors,string.IsNullOrEmpty(f.RequiredStatusId)||statuses.ContainsKey(f.RequiredStatusId),owner+": unknown required status");
                if(f.Kind==EffectKind.AreaDamage||f.Kind==EffectKind.ChainDamage||f.Kind==EffectKind.CreateArea)
                    Check(errors,f.TargetPolicy==EffectTargetPolicy.SelectedActor,owner+": advanced effect requires selected actor");
                if(f.Kind==EffectKind.ConfigurePiercing)
                    Check(errors,f.TargetPolicy==EffectTargetPolicy.CurrentProjectile,owner+": piercing requires current projectile");
                if(f.Kind==EffectKind.AreaDamage)
                    Check(errors,Pos(f.RadiusInCells)&&Zero(f.Gauge)&&string.IsNullOrEmpty(f.ReferenceId)&&f.Stacks==0&&f.DurationOverride==0,owner+": invalid area damage");
                if(f.Kind==EffectKind.ChainDamage)
                {
                    Check(errors,Pos(f.RadiusInCells)&&Zero(f.Gauge)&&f.MaxHits>=1&&f.MaxHits<=64&&f.RetainedDamageMultiplier<=1,owner+": invalid chain radius/hits/retention");
                    if(!string.IsNullOrEmpty(f.ReferenceId))
                    {
                        StatusDefinition output;
                        Check(errors,statuses.TryGetValue(f.ReferenceId,out output)&&f.Stacks>0,owner+": invalid chain status reference/stacks");
                        if(output!=null)Check(errors,!output.IsReactionInput,owner+": reaction output must not feed input");
                    }
                    else Check(errors,f.Stacks==0&&f.DurationOverride==0,owner+": chain status parameters require reference");
                }
                if(f.Kind==EffectKind.CreateArea)
                {
                    float tick=data.Settings!=null&&data.Settings.LogicTicksPerGameSecond>0?1f/data.Settings.LogicTicksPerGameSecond:1f/30;
                    Check(errors,Pos(f.RadiusInCells)&&Pos(f.DurationSeconds)&&f.TickIntervalSeconds>=tick,owner+": invalid area timing/radius");
                    Check(errors,f.AreaEffects!=null&&f.AreaEffects.Count>0,owner+": missing area effects");
                    if(f.AreaEffects!=null)foreach(EffectSpec nested in f.AreaEffects)
                    {
                        if(nested==null){errors.Add(owner+": null area effect");continue;}
                        Check(errors,((nested.Kind==EffectKind.Damage||nested.Kind==EffectKind.ApplyStatus)&&nested.TargetPolicy==EffectTargetPolicy.SelectedActor)||(nested.Kind==EffectKind.Heal&&(nested.TargetPolicy==EffectTargetPolicy.AllTowers||nested.TargetPolicy==EffectTargetPolicy.Spring)),owner+": area effects require selected actor damage/status");
                        Check(errors,nested.AreaEffects==null||nested.AreaEffects.Count==0,owner+": nested areas are forbidden");
                        if(nested.Kind==EffectKind.Damage||nested.Kind==EffectKind.ApplyStatus||nested.Kind==EffectKind.Heal)
                            Effects(errors,new EffectSpec[]{nested},owner+" area",nested.Kind==EffectKind.Heal?(ReactionContext?)null:ReactionContext.EnemyStatus);
                    }
                }
                else Check(errors,f.AreaEffects==null||f.AreaEffects.Count==0,owner+": area effects require CreateArea");
                if(f.Kind==EffectKind.ConfigurePiercing)
                    Check(errors,context.HasValue&&context.Value==ReactionContext.Projectile&&f.MaxHits>=1&&f.MaxHits<=64&&f.RetainedDamageMultiplier<=1,owner+": invalid piercing domain/hits/retention");
                if(context.HasValue)
                {
                    StatusDefinition reactionOutput;
                    if(f.Kind==EffectKind.ApplyStatus&&f.ReferenceId!=null&&statuses.TryGetValue(f.ReferenceId,out reactionOutput))Check(errors,!reactionOutput.IsReactionInput,owner+": reaction output must not feed input");
                    if(context.Value==ReactionContext.Projectile)Check(errors,f.Kind==EffectKind.ModifyProjectile||f.Kind==EffectKind.ConfigurePiercing||f.Kind==EffectKind.AreaDamage||f.Kind==EffectKind.ChainDamage||f.Kind==EffectKind.CreateArea||f.Kind==EffectKind.AddGauge||f.Kind==EffectKind.ApplyStatus,owner+": invalid projectile reaction output");
                    else
                    {
                        Check(errors,f.Kind==EffectKind.Damage||f.Kind==EffectKind.ApplyStatus||f.Kind==EffectKind.AreaDamage||f.Kind==EffectKind.ChainDamage||f.Kind==EffectKind.CreateArea||f.Kind==EffectKind.AddGauge,owner+": invalid enemy reaction output");
                        if(f.Kind==EffectKind.Damage)Check(errors,Zero(f.Gauge),owner+": reaction damage cannot supply gauge");
                        StatusDefinition status;
                        if(f.Kind==EffectKind.ApplyStatus&&f.ReferenceId!=null&&statuses.TryGetValue(f.ReferenceId,out status))Check(errors,!status.IsReactionInput,owner+": reaction output must not feed input");
                    }
                }
            }
        }
        bool Bounds(LevelDefinition l,CellCoord c)
        {
            return c.X>=0&&c.Y>=0&&c.X<l.Columns&&c.Y<l.Rows;
        }
        void Group(List<string> errors,SpawnGroupDefinition g,string owner)
        {
            EnemyDefinition enemy;
            if(g==null)
            {
                errors.Add(owner+": null group");
                return;
            }
            Check(errors,g.Count>=0&&Nonneg(g.StartDelaySeconds)&&Pos(g.SpawnIntervalSeconds)&&Pos(g.HpMultiplier)&&Nonneg(g.DamageMultiplier),owner+": invalid spawn timing/count");
            if(g.EnemyId==null||!enemies.TryGetValue(g.EnemyId,out enemy))
            {
                errors.Add(owner+": unknown enemy");
                return;
            }
            Check(errors,enemy.Enabled,owner+": disabled enemy "+g.EnemyId);
            bool found=false;
            foreach(LevelDefinition l in levels.Values)if(l.Paths!=null)foreach(PathDefinition p in l.Paths)if(p!=null&&p.Id==g.PathId&&p.SpawnPointId==g.SpawnPointId&&p.Movement==enemy.Movement)found=true;
            Check(errors,found,owner+": spawn/path/movement mismatch");
        }
        public IReadOnlyList<string> ValidateAll()
        {
            List<string> e=new List<string>(indexErrors);
            GameSettings s=data.Settings;
            Check(e,s!=null,"Settings missing");
            if(s!=null)
            {
                Check(e,s.StoryDays>0&&s.LogicTicksPerGameSecond>0&&Pos(s.SpringMaxHp)&&Nonneg(s.SpringCollisionRadiusInCells)&&s.MaxReactionActionsPerTick>0&&!string.IsNullOrEmpty(s.ConfigVersion),"Invalid settings");
                Check(e,s.SpecialSkillId!=null&&skills.ContainsKey(s.SpecialSkillId),"Unknown special skill");
            }
            float step=s!=null&&s.LogicTicksPerGameSecond>0?1f/s.LogicTicksPerGameSecond:1f/30;
            foreach(LevelDefinition l in levels.Values)ValidateLevel(e,l);
            foreach(ElementDefinition d in elements.Values)
            {
                Check(e,d.Element!=ElementType.None&&Enum.IsDefined(typeof(ElementType),d.Element),d.Id+": invalid element");
                Check(e,string.IsNullOrEmpty(d.BaseStatusId)||statuses.ContainsKey(d.BaseStatusId),d.Id+": unknown base status");
                ProjectileModifierDefinition p=d.ProjectileModifier;
                Check(e,p!=null&&p.Element==d.Element&&Nonneg(p.PhysicalAdd)&&Amounts(p.DamageAdd,false)&&Amounts(p.GaugeAdd,false)&&Pos(p.DamageMultiplier)&&Pos(p.SpeedMultiplier),d.Id+": invalid modifier");
                Check(e,d.ContactEffects==null||d.ContactEffects.Count==0||Finite(d.ContactIntervalSeconds)&&d.ContactIntervalSeconds>=step,d.Id+": contact interval too small");
                Effects(e,d.ContactEffects,d.Id,null);
            }
            foreach(TowerDefinition d in towers.Values)
            {
                Check(e,Pos(d.HpPerStack)&&d.InitialAttackStacks>=1&&d.MaxAttackStacks>=d.InitialAttackStacks&&Nonneg(d.BasePhysicalDamage)&&Nonneg(d.DamagePerExtraStack)&&Pos(d.RangeInCells)&&Finite(d.AttackIntervalSeconds)&&d.AttackIntervalSeconds>=step&&Nonneg(d.CollisionRadiusInCells)&&d.PlacementLimit>=0&&Nonneg(d.RecallRefundRatio)&&d.RecallRefundRatio<=1,d.Id+": invalid tower values");
                Check(e,d.ProjectileId!=null&&projectiles.ContainsKey(d.ProjectileId),d.Id+": unknown projectile");
                Costs(e,d.BuildCost,d.Id);
            }
            foreach(ProjectileDefinition d in projectiles.Values)Check(e,Pos(d.SpeedInCellsPerSecond)&&Pos(d.LifetimeSeconds)&&Nonneg(d.CollisionRadiusInCells)&&d.MaxHits>=1&&d.MaxHits<=64&&Nonneg(d.RetainedDamageMultiplier)&&d.RetainedDamageMultiplier<=1,d.Id+": invalid projectile values");
            HashSet<ElementType> gauges=new HashSet<ElementType>();
            if(data.Gauges==null)e.Add("Missing gauges");
            else foreach(GaugeDefinition g in data.Gauges)
            {
                if(g==null)
                {
                    e.Add("Null gauge");
                    continue;
                }
                Check(e,g.Element!=ElementType.None&&Enum.IsDefined(typeof(ElementType),g.Element)&&gauges.Add(g.Element)&&Pos(g.MaxValue)&&Pos(g.Threshold)&&g.Threshold<=g.MaxValue&&Nonneg(g.DecayPerSecond)&&Nonneg(g.DecayDelaySeconds)&&Pos(g.IncomingMultiplier),"Invalid/duplicate gauge "+g.Element);
            }
            foreach(StatusDefinition d in statuses.Values)Check(e,Pos(d.DurationSeconds)&&Finite(d.TickIntervalSeconds)&&d.TickIntervalSeconds>=step&&d.MaxStacks>=1&&Nonneg(d.MoveSlowPerStack)&&Nonneg(d.AttackSlowPerStack)&&Nonneg(d.AttackHastePerStack)&&Amounts(d.OutgoingElementDamageBonusPerStack,false)&&Amounts(d.OutgoingGaugeBonusPerStack,false)&&Amounts(d.IncomingGaugeBonusPerStack,false)&&Nonneg(d.AccuracyReductionPerStack)&&d.AccuracyReductionPerStack<=1&&Nonneg(d.HealPerTick)&&Nonneg(d.ArmorBreakPerStack)&&Amounts(d.ResistanceBreakPerStack,false)&&Nonneg(d.DotPhysicalPerStack)&&Amounts(d.DotElementPerStack,false)&&Enum.IsDefined(typeof(StatusRefreshPolicy),d.RefreshPolicy),d.Id+": invalid status values");
            foreach(SpecialSkillDefinition d in skills.Values)Check(e,d.UsesPerNight>0&&Pos(d.DurationSeconds)&&Pos(d.ReactionDamageMultiplier),d.Id+": invalid skill values");
            int bosses=0;
            foreach(EnemyDefinition d in enemies.Values)
            {
                if(d.Enabled&&d.IsFinalBoss)bosses++;
                Check(e,Pos(d.MaxHp)&&Nonneg(d.PhysicalDamage)&&Pos(d.SpeedInCellsPerSecond)&&Nonneg(d.RangeInCells)&&Finite(d.AttackIntervalSeconds)&&d.AttackIntervalSeconds>=step&&Nonneg(d.CollisionRadiusInCells)&&Nonneg(d.PhysicalReduction)&&d.PhysicalReduction<=1&&Amounts(d.Resistance,true)&&Enum.IsDefined(typeof(MovementKind),d.Movement),d.Id+": invalid enemy values");
                Check(e,string.IsNullOrEmpty(d.ProjectileId)||projectiles.ContainsKey(d.ProjectileId),d.Id+": unknown projectile");
                Check(e,string.IsNullOrEmpty(d.DropTableId)||droptables.ContainsKey(d.DropTableId),d.Id+": unknown drop table");
                if(d.BossSkillIds!=null)foreach(string id in d.BossSkillIds)Check(e,id!=null&&bossskills.ContainsKey(id),d.Id+": unknown boss skill");
            }
            Check(e,bosses==1,"Exactly one enabled final boss required");
            foreach(BossSkillDefinition d in bossskills.Values)
            {
                Check(e,Pos(d.PeriodSeconds)&&Nonneg(d.FirstDelaySeconds)&&Nonneg(d.ProjectileDamage),d.Id+": invalid boss skill");
                Check(e,string.IsNullOrEmpty(d.ProjectileId)||projectiles.ContainsKey(d.ProjectileId),d.Id+": unknown projectile");
                Effects(e,d.Effects,d.Id,null);
                if(d.Summons!=null)foreach(SpawnGroupDefinition g in d.Summons)Group(e,g,d.Id);
            }
            HashSet<string> storyIndices=new HashSet<string>();
            foreach(WaveDefinition d in storywaves.Values)
            {
                Wave(e,d,true);
                Check(e,storyIndices.Add(d.DayIndex+":"+d.WaveIndex),d.Id+": duplicate day/wave index");
            }
            foreach(WaveDefinition d in wavetemplates.Values)Wave(e,d,false);
            foreach(DropTableDefinition d in droptables.Values)
            {
                Check(e,d.Rolls>=0,d.Id+": negative rolls");
                long total=0;
                HashSet<string> ids=new HashSet<string>();
                if(d.Entries!=null)foreach(DropEntry r in d.Entries)
                {
                    if(r==null)
                    {
                        e.Add(d.Id+": null drop");
                        continue;
                    }
                    Check(e,!string.IsNullOrEmpty(r.Id)&&ids.Add(r.Id)&&r.Weight>0&&r.MinAmount>=0&&r.MaxAmountInclusive>=r.MinAmount&&Resource(r.Bucket,r.ResourceId),d.Id+": invalid drop");
                    total+=r.Weight;
                }
                Check(e,total<=int.MaxValue&&(d.Rolls==0||total>0),d.Id+": invalid drop weights");
            }
            foreach(ProductionRuleDefinition d in productionrules.Values)
            {
                Check(e,Enum.IsDefined(typeof(ProductionMatch),d.Match)&&d.OutputCount>=0,d.Id+": invalid production");
                if(d.Enabled)Check(e,d.A!=ElementType.None&&Enum.IsDefined(typeof(ElementType),d.A)&&(d.Match==ProductionMatch.SingleBlock||(d.B!=ElementType.None&&Enum.IsDefined(typeof(ElementType),d.B)))&&d.OutputElementId!=null&&elements.ContainsKey(d.OutputElementId),d.Id+": invalid production reference");
            }
            foreach(ReactionDefinition d in reactions.Values)
            {
                Check(e,Enum.IsDefined(typeof(ReactionContext),d.Context)&&d.ConsumeA>=0&&d.ConsumeB>=0,d.Id+": invalid reaction");
                if(d.Enabled)
                {
                    Check(e,d.A!=ElementType.None&&d.B!=ElementType.None&&Enum.IsDefined(typeof(ElementType),d.A)&&Enum.IsDefined(typeof(ElementType),d.B),d.Id+": invalid reaction elements");
                    if(d.Context==ReactionContext.EnemyStatus)Check(e,d.ConsumeA>0&&d.ConsumeB>0,d.Id+": enemy reaction must consume both inputs");
                    Effects(e,d.Effects,d.Id,d.Context);
                }
            }
            foreach(ItemDefinition d in items.Values)
            {
                Check(e,Nonneg(d.ActiveDurationSeconds)&&d.AllowedPhases!=null&&d.AllowedPhases.Count>0,d.Id+": invalid item phases/duration");
                if(d.AllowedPhases!=null)foreach(GamePhase phase in d.AllowedPhases)Check(e,Enum.IsDefined(typeof(GamePhase),phase),d.Id+": invalid phase");
                Effects(e,d.Effects,d.Id,null);
            }
            foreach(DayEventDefinition d in dayevents.Values)
            {
                Check(e,d.MinDayInclusive>=1&&d.MaxDayInclusive>=d.MinDayInclusive&&d.Weight>0&&d.Modes!=null&&d.Modes.Count>0,d.Id+": invalid event bounds/weight/mode");
                bool free=false;
                HashSet<string> ids=new HashSet<string>();
                if(d.Modes!=null)foreach(GameMode mode in d.Modes)Check(e,Enum.IsDefined(typeof(GameMode),mode),d.Id+": invalid mode");
                if(d.Options!=null)foreach(EventOptionDefinition option in d.Options)
                {
                    if(option==null)
                    {
                        e.Add(d.Id+": null option");
                        continue;
                    }
                    Check(e,!string.IsNullOrEmpty(option.Id)&&ids.Add(option.Id),d.Id+": duplicate/empty option");
                    Costs(e,option.Cost,d.Id);
                    bool zero=true;
                    if(option.Cost!=null)foreach(ResourceAmount c in option.Cost)if(c.Amount>0)zero=false;
                    if(zero)free=true;
                    Effects(e,option.Effects,d.Id,null);
                }
                Check(e,free,d.Id+": no unconditional option");
            }
            if(data.Endless==null)e.Add("Missing endless definition");
            else
            {
                EndlessDefinition d=data.Endless;
                Check(e,!string.IsNullOrEmpty(d.Id)&&Nonneg(d.HpGrowthPerNight)&&Nonneg(d.DamageGrowthPerNight)&&d.MilestoneNight>0&&d.WaveTemplateIds!=null&&d.WaveTemplateIds.Count>0,"Invalid endless definition");
                if(d.WaveTemplateIds!=null)foreach(string id in d.WaveTemplateIds)Check(e,id!=null&&wavetemplates.ContainsKey(id),"Unknown endless template "+id);
                Costs(e,d.MilestoneRewards,"Endless");
            }
            e.Sort(StringComparer.Ordinal);
            return e.ToArray();
        }
        void Wave(List<string> e,WaveDefinition d,bool story)
        {
            Check(e,d.DayIndex>=1&&d.WaveIndex>=1&&Nonneg(d.DelayAfterPreviousWaveSeconds),d.Id+": invalid wave index/delay");
            if(story&&data.Settings!=null)Check(e,d.DayIndex<=data.Settings.StoryDays,d.Id+": day outside story");
            if(d.Groups==null)
            {
                e.Add(d.Id+": missing groups");
                return;
            }
            HashSet<string> ids=new HashSet<string>();
            foreach(SpawnGroupDefinition g in d.Groups)
            {
                if(g!=null)Check(e,!string.IsNullOrEmpty(g.Id)&&ids.Add(g.Id),d.Id+": invalid/duplicate group ID");
                Group(e,g,d.Id);
            }
        }
        void ValidateLevel(List<string> e,LevelDefinition l)
        {
            string owner="Level "+l.Id;
            Check(e,l.Rows>0&&l.Columns>0&&Pos(l.CellSize)&&Finite(l.Origin.X)&&Finite(l.Origin.Y)&&Bounds(l,l.SpringCell),owner+": invalid dimensions/origin/spring");
            HashSet<CellCoord> reserved=new HashSet<CellCoord>();
            reserved.Add(l.SpringCell);
            if(l.ReservedCells!=null)foreach(CellCoord c in l.ReservedCells)
            {
                Check(e,Bounds(l,c),owner+": reserved out of bounds");
                reserved.Add(c);
            }
            Dictionary<string,SpawnPointDefinition> spawns=new Dictionary<string,SpawnPointDefinition>();
            if(l.SpawnPoints!=null)foreach(SpawnPointDefinition p in l.SpawnPoints)
            {
                if(p==null||string.IsNullOrEmpty(p.Id)||spawns.ContainsKey(p.Id))
                {
                    e.Add(owner+": invalid/duplicate spawn");
                    continue;
                }
                spawns.Add(p.Id,p);
                Check(e,Bounds(l,p.Cell),owner+": spawn out of bounds");
                reserved.Add(p.Cell);
            }
            HashSet<string> pathIds=new HashSet<string>();
            HashSet<CellCoord> ground=new HashSet<CellCoord>();
            if(l.Paths!=null)foreach(PathDefinition p in l.Paths)
            {
                if(p==null||string.IsNullOrEmpty(p.Id)||!pathIds.Add(p.Id))
                {
                    e.Add(owner+": invalid/duplicate path");
                    continue;
                }
                Check(e,p.SpawnPointId!=null&&spawns.ContainsKey(p.SpawnPointId),owner+": unknown path spawn");
                Check(e,Enum.IsDefined(typeof(MovementKind),p.Movement),owner+": invalid movement");
                if(p.Cells==null||p.Cells.Count<2)
                {
                    e.Add(owner+": path too short");
                    continue;
                }
                if(p.SpawnPointId!=null&&spawns.ContainsKey(p.SpawnPointId))Check(e,p.Cells[0].Equals(spawns[p.SpawnPointId].Cell),owner+": path must start at spawn");
                Check(e,p.Cells[p.Cells.Count-1].Equals(l.SpringCell),owner+": path must end at spring");
                HashSet<CellCoord> visited=new HashSet<CellCoord>();
                for(int i=0;i<p.Cells.Count;i++)
                {
                    CellCoord c=p.Cells[i];
                    Check(e,Bounds(l,c)&&visited.Add(c),owner+": path outside bounds/repeats");
                    if(i>0)Check(e,Math.Abs((long)c.X-p.Cells[i-1].X)+Math.Abs((long)c.Y-p.Cells[i-1].Y)==1,owner+": noncontiguous path");
                    if(p.Movement==MovementKind.Ground)ground.Add(c);
                }
            }
            HashSet<CellCoord> occupied=new HashSet<CellCoord>();
            if(l.InitialOccupants!=null)foreach(InitialOccupant c in l.InitialOccupants)
            {
                if(c==null)
                {
                    e.Add(owner+": null occupant");
                    continue;
                }
                Check(e,Bounds(l,c.Cell)&&!reserved.Contains(c.Cell)&&occupied.Add(c.Cell),owner+": illegal initial placement");
                bool blocks=false;
                if(c.Kind==OccupantKind.Tower)
                {
                    Check(e,c.DefinitionId!=null&&towers.ContainsKey(c.DefinitionId),owner+": unknown tower");
                    blocks=true;
                }
                else if(c.Kind==OccupantKind.ElementBlock)
                {
                    Check(e,c.DefinitionId!=null&&elements.ContainsKey(c.DefinitionId),owner+": unknown element");
                    if(c.DefinitionId!=null&&elements.ContainsKey(c.DefinitionId))blocks=elements[c.DefinitionId].BlocksGround;
                }
                else e.Add(owner+": invalid occupant kind");
                Check(e,!blocks||!ground.Contains(c.Cell),owner+": blocking initial occupant on ground path");
            }
            Costs(e,l.InitialResources,owner);
        }
    }
}

