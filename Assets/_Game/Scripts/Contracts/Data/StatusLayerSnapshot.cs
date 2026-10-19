// Generated verbatim from Docs/Contracts/Spotlight.Contracts.cs (2.1.0).
using System;
using System.Collections.Generic;

namespace Spotlight.Contracts
{
    public sealed class StatusLayerSnapshot
    {
        public string ScopeId;

        public long LayerId;
        public EntityId Source;
        public string ReactionId;
        public float RemainingSeconds;
        public float SnapshotDamageMultiplier = 1f;
    }
}

