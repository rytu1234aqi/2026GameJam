// Generated verbatim from Docs/Contracts/Spotlight.Contracts.cs (2.1.0).
using System;
using System.Collections.Generic;

namespace Spotlight.Contracts
{
    public sealed class EffectBatchRequest
    {
        public bool HasImpactPosition;
        public WorldPoint ImpactPosition;

        public CommandContext Context;
        public EntityId Source;
        public EntityId SelectedTarget;
        public DamageOrigin Origin;
        public string ReactionId;
        public float ReactionDamageMultiplier = 1f;
        public IReadOnlyList<EffectSpec> Effects;
        public StateMutationBatch CommitExtras;
    }
}

