using System;
using System.Collections.Generic;
namespace Spotlight.Contracts
{
    public sealed class CombatEffectEvent
    {
        public EffectKind Kind;
        public WorldPoint Position;
        public WorldPoint TargetPosition;
        public float RadiusInCells;
    }
}
