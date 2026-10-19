#if UNITY_EDITOR
using NUnit.Framework;
using Spotlight.Contracts;
using Spotlight.Core.Clock;
using Spotlight.Core.Events;
using Spotlight.Presentation;
using UnityEngine;

namespace Spotlight.Integration.Tests
{
    public sealed class AdvancedEffectVisualLifecycleTests
    {
        [TestCase(GamePhase.NightResult)]
        [TestCase(GamePhase.Ending)]
        [TestCase(GamePhase.GameOver)]
        [TestCase(GamePhase.Build)]
        [TestCase(GamePhase.Menu)]
        public void LeavingNightHidesTransientAndAreaVisualsWithoutClockAdvancing(GamePhase destination)
        {
            GameEventBus events = new GameEventBus();
            events.OnListenerError = error => Assert.Fail(error.ToString());
            GameClock clock = new GameClock(() => new FlowSnapshot { Phase = GamePhase.Night }, events);
            GameObject host = new GameObject("Advanced visual lifecycle");
            try
            {
                AdvancedEffectVisual2D visual = host.AddComponent<AdvancedEffectVisual2D>();
                visual.Bind(events, clock);
                events.Publish(new CombatEffectEvent { Kind = EffectKind.AreaDamage, Position = new WorldPoint(1, 1), RadiusInCells = 2 });
                events.Publish(new CombatEffectEvent { Kind = EffectKind.ChainDamage, Position = new WorldPoint(1, 1), TargetPosition = new WorldPoint(2, 1) });
                events.Flush();
                visual.ApplyAreas(new[] { new AreaEffectSnapshot { AreaId = 1, Position = new WorldPoint(1, 1), RadiusInCells = 1 } });
                Assert.That(host.GetComponentsInChildren<LineRenderer>().Length, Is.EqualTo(3));
                double frozenTime = clock.GetSnapshot().GameTime;
                events.Publish(new PhaseChangedEvent { OldPhase = GamePhase.Night, NewPhase = destination });
                events.Flush();
                Assert.That(clock.GetSnapshot().GameTime, Is.EqualTo(frozenTime));
                Assert.That(host.GetComponentsInChildren<LineRenderer>(), Is.Empty);
                visual.ApplyVisualTime(frozenTime);
                Assert.That(host.GetComponentsInChildren<LineRenderer>(), Is.Empty);
                visual.Unbind();
                events.Publish(new CombatEffectEvent { Kind = EffectKind.AreaDamage, Position = new WorldPoint(1, 1), RadiusInCells = 2 });
                events.Flush();
                Assert.That(host.GetComponentsInChildren<LineRenderer>(), Is.Empty);
            }
            finally { Object.DestroyImmediate(host); }
        }
    }
}
#endif
