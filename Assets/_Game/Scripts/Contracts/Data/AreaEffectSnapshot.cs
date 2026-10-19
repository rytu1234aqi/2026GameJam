using System;
using System.Collections.Generic;
namespace Spotlight.Contracts
{
    public sealed class AreaEffectSnapshot
    {
        public long AreaId;
        public EntityId Source;
        public WorldPoint Position;
        public float RadiusInCells;
        public float RemainingSeconds;
        public string EffectId;
        public string ReactionId;
    }
}
