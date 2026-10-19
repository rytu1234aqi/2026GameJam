// Generated verbatim from Docs/Contracts/Spotlight.Contracts.cs (2.1.0).
using System;
using System.Collections.Generic;

namespace Spotlight.Contracts
{
    public sealed class EffectSpec
    {
        public float RadiusInCells;
        public int MaxHits = 1;
        public float RetainedDamageMultiplier = 1f;
        public float DurationSeconds;
        public float TickIntervalSeconds;
        public float LingeringSeconds;
        public string RequiredStatusId;
        public IReadOnlyList<EffectSpec> AreaEffects;

        public string Id;
        public EffectKind Kind;
        public EffectTargetPolicy TargetPolicy;
        public string ReferenceId;
        public ResourceBucket ResourceBucket;
        public long ResourceAmount;
        public float PhysicalDamage;
        public ElementAmounts ElementDamage;
        public ElementAmounts Gauge;
        public bool UseAreaLease = true;
        public float Probability = 1f;
        public float Amount;
        public int Stacks;
        public float DurationOverride;
        public float DamageMultiplier = 1f;
        public float SpeedMultiplier = 1f;
    }
}

