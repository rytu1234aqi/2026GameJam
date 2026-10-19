using System;
using System.Collections.Generic;
using Spotlight.Contracts;
using UnityEngine;

namespace Spotlight.Presentation
{
    /// <summary>Game-clock-only feedback. Rings never participate in the board or physics.</summary>
    public sealed class AdvancedEffectVisual2D : MonoBehaviour
    {
        public float CellSize = 1;
        private IGameClock clock;
        private IDisposable subscription;
        private IDisposable phaseSubscription;
        private Material material;
        private sealed class Stroke { public LineRenderer Line; public double End; public bool Area; }
        private readonly List<Stroke> strokes = new List<Stroke>();
        private readonly Dictionary<long, Stroke> areas = new Dictionary<long, Stroke>();
        private readonly HashSet<long> seen = new HashSet<long>();

        public void Bind(IEventBus events, IGameClock gameClock)
        {
            Unbind(); clock = gameClock;
            if (events != null)
            {
                subscription = events.Subscribe<CombatEffectEvent>(OnEffect);
                phaseSubscription = events.Subscribe<PhaseChangedEvent>(OnPhaseChanged);
            }
        }
        private void OnPhaseChanged(PhaseChangedEvent phase)
        {
            if (phase != null && phase.NewPhase != GamePhase.Night) ClearVisuals();
        }
        private Stroke Rent(bool area)
        {
            foreach (Stroke stroke in strokes) if (!stroke.Line.gameObject.activeSelf)
            { stroke.Area = area; stroke.Line.gameObject.SetActive(true); return stroke; }
            if (material == null) material = new Material(Shader.Find("Sprites/Default"));
            GameObject child = new GameObject("Effect range"); child.transform.SetParent(transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>(); line.sharedMaterial = material;
            line.useWorldSpace = true; line.widthMultiplier = .045f; line.sortingOrder = 35;
            Stroke created = new Stroke { Line = line, Area = area }; strokes.Add(created); return created;
        }
        private void Ring(Stroke stroke, WorldPoint center, float radius)
        {
            stroke.Line.loop = true; stroke.Line.positionCount = 48;
            for (int i = 0; i < 48; i++)
            {
                float angle = i * Mathf.PI * 2 / 48;
                stroke.Line.SetPosition(i, new Vector3(center.X + Mathf.Cos(angle) * radius * CellSize, center.Y + Mathf.Sin(angle) * radius * CellSize, 0));
            }
        }
        private void OnEffect(CombatEffectEvent effect)
        {
            if (effect == null || clock == null) return;
            if (effect.Kind != EffectKind.AreaDamage && effect.Kind != EffectKind.ChainDamage) return;
            Stroke stroke = Rent(false); stroke.End = clock.GetSnapshot().GameTime + .35;
            if (effect.Kind == EffectKind.ChainDamage)
            {
                stroke.Line.loop = false; stroke.Line.positionCount = 2;
                stroke.Line.SetPosition(0, new Vector3(effect.Position.X, effect.Position.Y, 0));
                stroke.Line.SetPosition(1, new Vector3(effect.TargetPosition.X, effect.TargetPosition.Y, 0));
            }
            else Ring(stroke, effect.Position, effect.RadiusInCells);
            Tint(stroke, 1);
        }
        public void ApplyAreas(IReadOnlyList<AreaEffectSnapshot> snapshots)
        {
            seen.Clear();
            if (snapshots != null) foreach (AreaEffectSnapshot area in snapshots)
            {
                seen.Add(area.AreaId); Stroke stroke;
                if (!areas.TryGetValue(area.AreaId, out stroke)) { stroke = Rent(true); areas.Add(area.AreaId, stroke); }
                Ring(stroke, area.Position, area.RadiusInCells); Tint(stroke, .55f);
            }
            foreach (long id in new List<long>(areas.Keys)) if (!seen.Contains(id))
            { areas[id].Line.gameObject.SetActive(false); areas.Remove(id); }
        }
        private static void Tint(Stroke stroke, float alpha)
        { Color color = stroke.Area ? new Color(1, .55f, .15f, alpha) : new Color(.5f, .85f, 1, alpha); stroke.Line.startColor = color; stroke.Line.endColor = color; }
        public void ApplyVisualTime(double gameTime)
        {
            foreach (Stroke stroke in strokes) if (!stroke.Area && stroke.Line.gameObject.activeSelf)
            { if (gameTime >= stroke.End) stroke.Line.gameObject.SetActive(false); else Tint(stroke, Mathf.Clamp01((float)((stroke.End - gameTime) / .35))); }
        }
        public void ClearVisuals() { areas.Clear(); foreach (Stroke stroke in strokes) if (stroke.Line != null) stroke.Line.gameObject.SetActive(false); }
        public void Unbind() { subscription?.Dispose(); phaseSubscription?.Dispose(); subscription = null; phaseSubscription = null; clock = null; ClearVisuals(); }
        private void OnDestroy() { Unbind(); foreach (Stroke stroke in strokes) if (stroke.Line != null) PresentationObjects.Release(stroke.Line.gameObject); strokes.Clear(); PresentationObjects.Release(material); }
    }
}
