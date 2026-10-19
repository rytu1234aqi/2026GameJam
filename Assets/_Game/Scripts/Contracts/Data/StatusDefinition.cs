// Generated verbatim from Docs/Contracts/Spotlight.Contracts.cs (2.1.0).
using System;
using System.Collections.Generic;

namespace Spotlight.Contracts
{
    public sealed class StatusDefinition
    {
        public string Id;
        public ElementType ElementTag;
        public bool IsReactionInput;
        public bool IsReactionDamage;
        public float DurationSeconds;
        public float TickIntervalSeconds;
        public int MaxStacks;
        public StatusRefreshPolicy RefreshPolicy;
        public float MoveSlowPerStack;
        public float AttackSlowPerStack;
        public float AttackHastePerStack;
        public ElementAmounts OutgoingElementDamageBonusPerStack;
        public ElementAmounts OutgoingGaugeBonusPerStack;
        public ElementAmounts IncomingGaugeBonusPerStack;
        public float AccuracyReductionPerStack;
        public float HealPerTick;

        public float ArmorBreakPerStack;
        public ElementAmounts ResistanceBreakPerStack;
        public bool PreventMovement;
        public bool PreventAttack;
        public float DotPhysicalPerStack;
        public ElementAmounts DotElementPerStack;
    }
}

