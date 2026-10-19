using System;
using Spotlight.Contracts;
using UnityEngine;

namespace Spotlight.Gameplay.Definitions
{
    [Serializable]
    public sealed class AreaEffectConfig
    {
        public string Id;
        [Tooltip("区域子效果只允许Damage或ApplyStatus。")]
        public EffectKind Kind;
        public EffectTargetPolicy TargetPolicy;
        public string ReferenceId;
        public float PhysicalDamage;
        public ElementAmountsConfig ElementDamage = new ElementAmountsConfig();
        public ElementAmountsConfig Gauge = new ElementAmountsConfig();
        public float Amount;
        public bool UseAreaLease = true;
        public float Probability = 1f;
        public int Stacks;
        public float DurationOverride;
        public float DamageMultiplier = 1f;

        public EffectSpec ToDefinition()
        {
            if (Kind != EffectKind.Damage && Kind != EffectKind.ApplyStatus && Kind != EffectKind.Heal)
                throw new InvalidOperationException("Area effects must be Damage, ApplyStatus or Heal.");
            return new EffectSpec
            {
                Id = Id, Kind = Kind, TargetPolicy = TargetPolicy, ReferenceId = ReferenceId,
                PhysicalDamage = PhysicalDamage,
                ElementDamage = (ElementDamage ?? new ElementAmountsConfig()).ToDefinition(),
                Gauge = (Gauge ?? new ElementAmountsConfig()).ToDefinition(),
                Amount = Amount, Probability = Probability, UseAreaLease = UseAreaLease, Stacks = Stacks, DurationOverride = DurationOverride, DamageMultiplier = DamageMultiplier
            };
        }
    }
}
