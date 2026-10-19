using System;
using System.Collections.Generic;
using UnityEngine;
using Spotlight.Contracts;
using Spotlight.Presentation;

namespace Spotlight.Bootstrap
{
    /// <summary>只读取 DTO 和租借显示对象；碰撞、HP、寿命由业务模块决定。</summary>
    public sealed class RuntimeWorldRenderer2D : MonoBehaviour
    {
        public ThirdPartyUiThemeSO ArtTheme;
        public Camera WorldCamera;
        private ModuleComposition services;
        private ViewPool pool;
        private IDisposable damageSubscription;
        private IDisposable removedSubscription;
        private SpriteRenderer background;
        private Material fallbackWorldMaterial;
        private Material tileMaterial;
        private AdvancedEffectVisual2D advancedEffects;
        private readonly Dictionary<string, double> pendingHits = new Dictionary<string, double>();
        private sealed class DisplayEntry
        {
            internal PoolLease Lease;
            internal GameObject View;
            internal SpriteRenderer Renderer;
            internal ElementVisual2D Element;
            internal ProjectileVisual2D Projectile;
            internal EnemyHealthBar2D HealthBar;
            internal EnemyFacingVisual2D Facing;
            internal HitFlashVisual2D HitFlash;
            internal EnemySpriteAnimator2D Animation;
            internal bool Moving, Dying;
            internal double DeathUntil, LastVisualTime;
            internal WorldPoint LastPosition;
            internal bool HasPosition;
        }
        private readonly Dictionary<string, DisplayEntry> views = new Dictionary<string, DisplayEntry>();
        private readonly List<GameObject> tiles = new List<GameObject>();
        private sealed class TileVisual
        {
            internal SpriteRenderer Renderer;
            internal bool Reserved, OnGroundPath;
        }
        private readonly List<TileVisual> tileVisuals = new List<TileVisual>();
        private bool hasTilePhase, tilesAtNight;
        private readonly HashSet<string> visible = new HashSet<string>();
        private string runId;
        private Sprite tileSprite;
        private Texture2D tileTexture;

        public void Bind(ModuleComposition services, ViewPool pool)
        {
            Unbind();
            this.services = services;
            this.pool = pool;
            advancedEffects = GetComponent<AdvancedEffectVisual2D>();
            if (advancedEffects == null) advancedEffects = gameObject.AddComponent<AdvancedEffectVisual2D>();
            advancedEffects.Bind(services == null ? null : services.Events, services == null ? null : services.Clock);
            if (services != null)
            {
                damageSubscription = services.Events.Subscribe<DamageAppliedEvent>(OnDamageApplied);
                removedSubscription = services.Events.Subscribe<ActorRemovedEvent>(OnActorRemoved);
            }
            tileTexture = new Texture2D(1, 1);
            tileTexture.SetPixel(0, 0, Color.white);
            tileTexture.Apply();
            tileSprite = Sprite.Create(tileTexture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1);
        }

        private void LateUpdate()
        {
            if (services == null || pool == null) return;
            FlowSnapshot flow = services.Flow.GetSnapshot();
            ApplyBackground(flow.Phase);
            if (flow.RunId != runId)
            {
                ClearViews();
                runId = flow.RunId;
                if (!string.IsNullOrEmpty(runId)) BuildTiles();
            }
            if (string.IsNullOrEmpty(runId)) return;
            ApplyTilePhase(flow.Phase);
            double gameTime = services.Clock.GetSnapshot().GameTime;
            advancedEffects.CellSize = services.Core.Store.Level == null ? 1 : services.Core.Store.Level.CellSize;
            advancedEffects.ApplyAreas(services.Combat.GetAreaEffects());
            advancedEffects.ApplyVisualTime(gameTime);
            visible.Clear();
            foreach (ActorKind kind in new[] { ActorKind.Spring, ActorKind.Tower, ActorKind.Enemy })
                foreach (ActorSnapshot actor in services.Core.World.GetActors(kind))
                {
                    string prefabKey = ActorPrefab(actor);
                    Show("actor:" + actor.Id.Value, prefabKey, actor.Position, kind == ActorKind.Spring ? 10 : 20, actor, null, gameTime);
                }
            foreach (CellSnapshot cell in services.Core.Board.GetSnapshot().Cells)
            {
                if (cell.OccupantKind != OccupantKind.ElementBlock) continue;
                ElementDefinition definition;
                if (services.Catalog.TryGetElement(cell.DefinitionId, out definition))
                    Show("block:" + cell.OccupantId.Value, definition.PrefabKey, services.Core.Board.CellToWorld(cell.Cell), 5, null, null, gameTime);
            }
            foreach (ProjectileSnapshot projectile in services.Combat.Projectiles.GetSnapshot())
            {
                ProjectileDefinition definition;
                if (services.Catalog.TryGetProjectile(services.Combat.GetProjectileDefinitionId(projectile.ProjectileId), out definition))
                    Show("projectile:" + projectile.ProjectileId, definition.PrefabKey, projectile.Position, 30, null, projectile, gameTime);
            }
            foreach (string key in new List<string>(views.Keys))
                if (!visible.Contains(key))
                {
                    DisplayEntry entry = views[key];
                    if (entry.Dying && entry.View != null && gameTime < entry.DeathUntil)
                    {
                        entry.Animation.ApplyVisualTime(gameTime, false);
                        if (entry.HitFlash != null) entry.HitFlash.ApplyVisualTime(gameTime);
                    }
                    else ReturnView(key);
                }
            pendingHits.Clear();
        }

        private void OnDamageApplied(DamageAppliedEvent damage)
        {
            if (services == null || damage == null || damage.Result == null) return;
            DamageResult result = damage.Result;
            if (result.Error != ErrorCode.None || result.FinalDamage <= 0 || float.IsNaN(result.FinalDamage) || float.IsInfinity(result.FinalDamage)) return;
            string currentRun = services.Flow.GetSnapshot().RunId;
            if (string.IsNullOrEmpty(currentRun)) return;
            if (currentRun != runId) { ClearViews(); runId = currentRun; BuildTiles(); }
            string key = "actor:" + result.Target.Value;
            double time = services.Clock.GetSnapshot().GameTime;
            DisplayEntry entry;
            // Projectile hits already played their source animation when the view was rented.
            if (result.PacketId == null || !result.PacketId.StartsWith("projectile:", StringComparison.Ordinal))
                PlaySourceAttack(result.Source, time);
            if (views.TryGetValue(key, out entry) && entry.HitFlash != null) entry.HitFlash.PlayHit(time);
            else pendingHits[key] = time;
        }

        private void PlaySourceAttack(EntityId source, double gameTime)
        {
            DisplayEntry entry;
            if (views.TryGetValue("actor:" + source.Value, out entry) && !entry.Dying && entry.Animation != null)
                entry.Animation.PlayAttack(gameTime);
        }

        private void OnActorRemoved(ActorRemovedEvent removed)
        {
            if (services == null || removed == null) return;
            string currentRun = services.Flow.GetSnapshot().RunId;
            if (currentRun != runId || string.IsNullOrEmpty(currentRun)) return;
            string key = "actor:" + removed.Actor.Value;
            DisplayEntry entry;
            if (!views.TryGetValue(key, out entry)) return;
            if (removed.Reason != DeathReason.Killed || !HasDeathFrames(entry.Animation))
            {
                ReturnView(key);
                return;
            }
            if (entry.Dying) return;
            double gameTime = services.Clock.GetSnapshot().GameTime;
            float seconds = entry.Animation.DeathSeconds;
            double duration = !float.IsNaN(seconds) && !float.IsInfinity(seconds) && seconds > 0 ? seconds : 0d;
            entry.Dying = true;
            entry.DeathUntil = gameTime + duration;
            entry.Animation.PlayDeath(gameTime);
            if (entry.HealthBar != null) entry.HealthBar.ApplyHealth(0, 1);
        }

        private static bool HasDeathFrames(EnemySpriteAnimator2D animation)
        {
            if (animation == null || animation.DeathFrames == null) return false;
            foreach (Sprite frame in animation.DeathFrames) if (frame != null) return true;
            return false;
        }

        private string ActorPrefab(ActorSnapshot actor)
        {
            if (actor.Kind == ActorKind.Spring) return "view.spring";
            TowerDefinition tower;
            if (actor.Kind == ActorKind.Tower && services.Catalog.TryGetTower(actor.DefinitionId, out tower)) return tower.PrefabKey;
            EnemyDefinition enemy;
            if (actor.Kind == ActorKind.Enemy && services.Catalog.TryGetEnemy(actor.DefinitionId, out enemy)) return enemy.PrefabKey;
            return null;
        }

        private void Show(string key, string prefabKey, WorldPoint position, int order,
            ActorSnapshot actor, ProjectileSnapshot projectile, double gameTime)
        {
            if (string.IsNullOrEmpty(prefabKey)) return;
            DisplayEntry entry;
            if (views.TryGetValue(key, out entry) &&
                (entry.View == null || entry.Lease.PrefabKey != prefabKey))
            {
                ReturnView(key);
                entry = null;
            }
            if (entry == null)
            {
                PoolLease lease = null;
                try
                {
                    lease = pool.Rent(prefabKey);
                    GameObject view = lease == null ? null : lease.View as GameObject;
                    if (view == null)
                    {
                        if (lease != null) pool.Return(lease.LeaseId);
                        return;
                    }
                    entry = new DisplayEntry
                    {
                        Lease = lease, View = view,
                        Renderer = view.GetComponent<SpriteRenderer>(),
                        Element = view.GetComponent<ElementVisual2D>(),
                        Projectile = view.GetComponent<ProjectileVisual2D>(),
                        HealthBar = view.GetComponent<EnemyHealthBar2D>(),
                        Facing = view.GetComponent<EnemyFacingVisual2D>(),
                        Animation = actor != null && actor.Kind == ActorKind.Enemy ? view.GetComponent<EnemySpriteAnimator2D>() : null
                    };
                    // Enemy bodies live below the positioning root; never select the health bar.
                    if (entry.Facing != null && entry.Facing.BodyRenderer != null)
                        entry.Renderer = entry.Facing.BodyRenderer;
                    if (entry.Animation != null)
                    {
                        if (entry.Animation.BodyRenderer == null) entry.Animation.BodyRenderer = entry.Renderer;
                        entry.Animation.ResetVisual();
                    }
                    if (actor != null && actor.Kind == ActorKind.Enemy && entry.Renderer != null)
                    {
                        entry.HitFlash = view.GetComponent<HitFlashVisual2D>();
                        if (entry.HitFlash == null) entry.HitFlash = view.AddComponent<HitFlashVisual2D>();
                        entry.HitFlash.BindBody(entry.Renderer);
                        if (entry.HitFlash.FlashColor == entry.Renderer.color)
                            entry.HitFlash.FlashColor = new Color(1f, .3f, .3f, entry.Renderer.color.a);
                        double hitTime;
                        if (pendingHits.TryGetValue(key, out hitTime)) entry.HitFlash.PlayHit(hitTime);
                    }
                    views.Add(key, entry);
                    if (projectile != null) PlaySourceAttack(projectile.Source, gameTime);
                }
                catch (Exception exception)
                {
                    if (lease != null) pool.Return(lease.LeaseId);
                    Debug.LogWarning("Unable to rent presentation view " + prefabKey + ": " + exception.Message, this);
                    return;
                }
            }
            entry.View.transform.position = new Vector3(position.X, position.Y, 0);
            if (entry.Renderer != null) entry.Renderer.sortingOrder = order;
            if (entry.Element != null) entry.Element.ApplyVisualTime(gameTime);
            if (entry.HitFlash != null) entry.HitFlash.ApplyVisualTime(gameTime);
            if (entry.Projectile != null && projectile != null)
            {
                entry.Projectile.SetDirection(projectile.Direction);
                entry.Projectile.SetElements(projectile.AppliedElementMask);
                entry.Projectile.ApplyVisualTime(gameTime);
            }
            if (actor != null && actor.Kind == ActorKind.Enemy)
            {
                if (!entry.HasPosition || gameTime != entry.LastVisualTime)
                {
                    float dx = position.X - entry.LastPosition.X;
                    float dy = position.Y - entry.LastPosition.Y;
                    entry.Moving = entry.HasPosition && dx * dx + dy * dy > .00000001f;
                }
                if (entry.Animation != null) entry.Animation.ApplyVisualTime(gameTime, entry.Moving);
                if (entry.HealthBar != null) entry.HealthBar.ApplyHealth(actor.CurrentHp, actor.MaxHp);
                if (entry.Facing != null)
                    entry.Facing.SetDirection(entry.HasPosition
                        ? new WorldPoint(position.X - entry.LastPosition.X, position.Y - entry.LastPosition.Y)
                        : new WorldPoint(0, 0));
                entry.LastPosition = position;
                entry.HasPosition = true;
                entry.LastVisualTime = gameTime;
            }
            visible.Add(key);
        }

        private void ReturnView(string key)
        {
            DisplayEntry entry = views[key];
            views.Remove(key);
            pendingHits.Remove(key);
            if (entry.HitFlash != null) entry.HitFlash.ResetVisual();
            if (entry.Animation != null) entry.Animation.ResetVisual();
            if (pool != null) pool.Return(entry.Lease.LeaseId);
        }

        private void BuildTiles()
        {
            LevelDefinition level = services.Core.Store.Level;
            if (level == null) return;
            bool night = IsNightPhase(services.Flow.GetSnapshot().Phase);
            foreach (CellSnapshot cell in services.Core.Board.GetSnapshot().Cells)
            {
                GameObject tile = new GameObject("Cell_" + cell.Cell.X + "_" + cell.Cell.Y);
                tile.transform.SetParent(transform, false);
                WorldPoint center = services.Core.Board.CellToWorld(cell.Cell);
                tile.transform.position = new Vector3(center.X, center.Y, 0);
                tile.transform.localScale = new Vector3(level.CellSize * .94f, level.CellSize * .94f, 1);
                SpriteRenderer renderer = tile.AddComponent<SpriteRenderer>();
                renderer.sprite = tileSprite;
                renderer.sortingOrder = -10;
                renderer.color = cell.Reserved ? new Color(.18f, .35f, .4f) : cell.OnGroundPath ? new Color(.32f, .28f, .22f) : new Color(.19f, .23f, .28f);
                if (ArtTheme != null)
                {
                    renderer.color = TileColor(cell.Reserved, cell.OnGroundPath, night);
                    Material material = TileMaterial();
                    if (material != null) renderer.sharedMaterial = material;
                }
                tiles.Add(tile);
                tileVisuals.Add(new TileVisual { Renderer = renderer, Reserved = cell.Reserved, OnGroundPath = cell.OnGroundPath });
            }
            hasTilePhase = ArtTheme != null;
            tilesAtNight = night;
        }

        private static bool IsNightPhase(GamePhase phase)
        {
            return phase == GamePhase.Night || phase == GamePhase.NightResult || phase == GamePhase.GameOver;
        }

        private static Color TileColor(bool reserved, bool path, bool night)
        {
            if (night)
                return reserved ? new Color(.16f, .55f, .65f, .55f) :
                    path ? new Color(.30f, .25f, .20f, .48f) : new Color(.13f, .20f, .32f, .45f);
            return reserved ? new Color(.25f, .65f, .7f, .55f) :
                path ? new Color(.68f, .48f, .25f, .55f) : new Color(.94f, .83f, .60f, .55f);
        }

        private void ApplyTilePhase(GamePhase phase)
        {
            if (ArtTheme == null) return;
            bool night = IsNightPhase(phase);
            if (hasTilePhase && tilesAtNight == night) return;
            foreach (TileVisual tile in tileVisuals)
                if (tile.Renderer != null) tile.Renderer.color = TileColor(tile.Reserved, tile.OnGroundPath, night);
            hasTilePhase = true;
            tilesAtNight = night;
        }

        private void ApplyBackground(GamePhase phase)
        {
            if (ArtTheme == null || WorldCamera == null || !WorldCamera.orthographic) return;
            bool night = IsNightPhase(phase);
            Sprite sprite = night ? ArtTheme.NightBackground : ArtTheme.DayBackground;
            if (sprite == null) sprite = night ? ArtTheme.DayBackground : ArtTheme.NightBackground;
            if (sprite == null) return;
            if (background == null)
            {
                GameObject view = new GameObject("ThirdPartyWorldBackground");
                view.transform.SetParent(transform, false);
                background = view.AddComponent<SpriteRenderer>();
                background.sortingOrder = -1000;
            }
            background.sprite = sprite;
            Material material = WorldMaterial();
            if (material != null) background.sharedMaterial = material;
            Vector2 size = sprite.bounds.size;
            if (size.x <= 0 || size.y <= 0) return;
            float height = WorldCamera.orthographicSize * 2f;
            float scale = Mathf.Max(height * WorldCamera.aspect / size.x, height / size.y);
            Vector3 center = WorldCamera.transform.position;
            background.transform.position = new Vector3(center.x, center.y, 0);
            // Use world scale so the cover remains correct if this host is scaled.
            background.transform.localScale = Vector3.one;
            Vector3 parentScale = background.transform.lossyScale;
            background.transform.localScale = new Vector3(
                Mathf.Abs(parentScale.x) > .00001f ? scale / Mathf.Abs(parentScale.x) : scale,
                Mathf.Abs(parentScale.y) > .00001f ? scale / Mathf.Abs(parentScale.y) : scale, 1);
        }

        private Material WorldMaterial()
        {
            if (ArtTheme != null && ArtTheme.WorldSpriteMaterial != null) return ArtTheme.WorldSpriteMaterial;
            if (fallbackWorldMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null) fallbackWorldMaterial = new Material(shader);
            }
            return fallbackWorldMaterial;
        }

        private Material TileMaterial()
        {
            if (tileMaterial == null)
            {
                // 白色单像素格子独立用材质，明确保持纯色贴图路径。
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    tileMaterial = new Material(shader) { name = "RuntimeFlatTileUnlit" };
                    tileMaterial.mainTexture = tileTexture;
                    tileMaterial.color = Color.white;
                }
            }
            return tileMaterial;
        }

        private void ClearViews()
        {
            if (advancedEffects != null) advancedEffects.ClearVisuals();
            foreach (string key in new List<string>(views.Keys)) ReturnView(key);
            visible.Clear();
            pendingHits.Clear();
            foreach (GameObject tile in tiles) if (tile != null) PresentationObjects.Release(tile);
            tiles.Clear();
            tileVisuals.Clear();
            hasTilePhase = tilesAtNight = false;
        }

        public void Unbind()
        {
            if (advancedEffects != null) advancedEffects.Unbind();
            if (damageSubscription != null) damageSubscription.Dispose();
            damageSubscription = null;
            if (removedSubscription != null) removedSubscription.Dispose();
            removedSubscription = null;
            if (background != null) PresentationObjects.Release(background.gameObject);
            background = null;
            if (fallbackWorldMaterial != null) PresentationObjects.Release(fallbackWorldMaterial);
            fallbackWorldMaterial = null;
            ClearViews(); services = null; pool = null; runId = null;
            if (tileMaterial != null) PresentationObjects.Release(tileMaterial);
            tileMaterial = null;
            if (tileSprite != null) PresentationObjects.Release(tileSprite);
            if (tileTexture != null) PresentationObjects.Release(tileTexture);
            tileSprite = null; tileTexture = null;
        }

        private void OnDestroy() { Unbind(); }
    }
}
