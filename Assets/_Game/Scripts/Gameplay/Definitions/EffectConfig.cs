using System;
using System.Collections.Generic;
using Spotlight.Contracts;
using UnityEngine;
namespace Spotlight.Gameplay.Definitions
{
    [Serializable]
    public sealed class EffectConfig
    {
        public string Id;
        public EffectKind Kind;
        public EffectTargetPolicy TargetPolicy;
        public string ReferenceId;
        public ResourceBucket ResourceBucket;
        public long ResourceAmount;
        public float PhysicalDamage;
        public ElementAmountsConfig ElementDamage = new ElementAmountsConfig();
        public ElementAmountsConfig Gauge = new ElementAmountsConfig();
        public bool UseAreaLease = true;
        public float Probability = 1f;
        public float Amount;
        public int Stacks;
        public float DurationOverride;
        public float DamageMultiplier = 1f;
        public float SpeedMultiplier = 1f;
        [Tooltip("作用半径，单位为格。")]
        public float RadiusInCells;
        [Tooltip("最大命中数，包含首目标；重复命中也计数（1–64）。")]
        public int MaxHits = 1;
        [Tooltip("每次后续命中保留的伤害倍率，范围0–1。")]
        public float RetainedDamageMultiplier = 1f;
        [Tooltip("区域持续时间，单位为游戏秒。")]
        public float DurationSeconds;
        [Tooltip("区域结算间隔，单位为游戏秒，不能小于逻辑tick。")]
        public float TickIntervalSeconds;
        [Tooltip("离开区域后的状态残留时间，单位为游戏秒。")]
        public float LingeringSeconds;
        [Tooltip("追加连锁目标必须具有的状态ID；留空不筛选。")]
        public string RequiredStatusId;
        [Tooltip("区域内只允许基础Damage或ApplyStatus，不允许嵌套区域。")]
        public AreaEffectConfig[] AreaEffects = new AreaEffectConfig[0];
        private EffectSpec[] ConvertAreaEffects()
        {
            if (AreaEffects == null) return null;
            var result = new EffectSpec[AreaEffects.Length];
            for (int i = 0; i < result.Length; i++)
            {
                var effect = AreaEffects[i];
                if (effect == null) continue;
                result[i] = effect.ToDefinition();
            }
            return result;
        }
        public EffectSpec ToDefinition()
        {
            return new EffectSpec { Id=Id,Kind=Kind,TargetPolicy=TargetPolicy,ReferenceId=ReferenceId,ResourceBucket=ResourceBucket,ResourceAmount=ResourceAmount,PhysicalDamage=PhysicalDamage,ElementDamage=(ElementDamage==null?new ElementAmountsConfig():ElementDamage).ToDefinition(),Gauge=(Gauge==null?new ElementAmountsConfig():Gauge).ToDefinition(),Amount=Amount,Probability=Probability,UseAreaLease=UseAreaLease,Stacks=Stacks,DurationOverride=DurationOverride,DamageMultiplier=DamageMultiplier,SpeedMultiplier=SpeedMultiplier,RadiusInCells=RadiusInCells,MaxHits=MaxHits,RetainedDamageMultiplier=RetainedDamageMultiplier,DurationSeconds=DurationSeconds,TickIntervalSeconds=TickIntervalSeconds,LingeringSeconds=LingeringSeconds,RequiredStatusId=RequiredStatusId,AreaEffects=ConvertAreaEffects() };
        }
    }
}
