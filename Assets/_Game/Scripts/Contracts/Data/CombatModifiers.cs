// Generated verbatim from Docs/Contracts/Spotlight.Contracts.cs (2.1.0).
using System;
using System.Collections.Generic;

namespace Spotlight.Contracts
{
    public sealed class CombatModifiers
    {
        public float MoveMultiplier = 1f;
        public float AttackRateMultiplier = 1f;
        public ElementAmounts OutgoingElementDamageBonus;
        public ElementAmounts OutgoingGaugeBonus;
        public ElementAmounts IncomingGaugeBonus;
        public float AccuracyMultiplier = 1f;
        public float ArmorBreak;
        public ElementAmounts ResistanceBreak;
        public bool PreventMovement;
        public bool PreventAttack;
    }
}

