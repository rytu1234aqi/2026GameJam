// Generated verbatim from Docs/Contracts/Spotlight.Contracts.cs (2.1.0).
using System;
using System.Collections.Generic;

namespace Spotlight.Contracts
{
    public sealed class ProjectileDefinition
    {
        public bool PiercingEnabled;
        public int MaxHits = 1;
        public float RetainedDamageMultiplier = 1f;

        public string Id;
        public string PrefabKey;
        public float SpeedInCellsPerSecond;
        public float LifetimeSeconds;
        public float CollisionRadiusInCells;
    }
}

