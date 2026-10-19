// Generated verbatim from Docs/Contracts/Spotlight.Contracts.cs (2.1.0).
using System;
using System.Collections.Generic;

namespace Spotlight.Contracts
{
    public sealed class StatusApplyRequest
    {
        public string ScopeId;

        public EntityId Source;
        public EntityId Target;
        public string StatusId;
        public string ReactionId;
        public int Stacks;
        public float DurationOverride;
        public float SnapshotDamageMultiplier = 1f;
    }
}

