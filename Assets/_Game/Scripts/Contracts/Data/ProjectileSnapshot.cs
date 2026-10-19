// Generated verbatim from Docs/Contracts/Spotlight.Contracts.cs (2.1.0).
using System;
using System.Collections.Generic;

namespace Spotlight.Contracts
{
    public sealed class ProjectileSnapshot
    {
        public int HitCount;
        public int MaxHits = 1;
        public bool PiercingEnabled;

        public long ProjectileId;
        public EntityId Source;
        public WorldPoint Position;
        public WorldPoint Direction;
        public int AppliedElementMask;
        public IReadOnlyList<string> AppliedReactionIds;
    }
}

