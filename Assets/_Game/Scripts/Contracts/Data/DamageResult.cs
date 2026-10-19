// Generated verbatim from Docs/Contracts/Spotlight.Contracts.cs (2.1.0).
using System;
using System.Collections.Generic;

namespace Spotlight.Contracts
{
    public sealed class DamageResult
    {
        public ErrorCode Error;
        public string PacketId;
        public EntityId Source;
        public EntityId Target;
        public float FinalDamage;
        public float RemainingHp;
        public bool NewlyKilled;
        public bool Missed;
    }
}

