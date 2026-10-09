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
        private ModuleComposition services;
        private ViewPool pool;
        private sealed class DisplayEntry
        {
            internal PoolLease Lease;
            internal GameObject View;
            internal SpriteRenderer Renderer;
            internal ElementVisual2D Element;
            internal ProjectileVisual2D Projectile;
            internal EnemyHealthBar2D HealthBar;
            internal EnemyFacingVisual2D Facing;
            internal WorldPoint LastPosition;
            internal bool HasPosition;
        }
        private readonly Dictionary<string, DisplayEntry> views = new Dictionary<string, DisplayEntry>();
        private readonly List<GameObject> tiles = new List<GameObject>();
        private readonly HashSet<string> visible = new HashSet<string>();
        private string runId;
        private Sprite tileSprite;
        private Texture2D tileTexture;

        public void Bind(ModuleComposition services, ViewPool pool)
        {
            Unbind();
            this.services = services;
            this.pool = pool;
            tileTexture = new Texture2D(1, 1);
            tileTexture.SetPixel(0, 0, Color.white);
            tileTexture.Apply();
            tileSprite = Sprite.Create(tileTexture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1);
        }

        private void LateUpdate()
        {
            if (services == null || pool == null) return;
            FlowSnapshot flow = services.Flow.GetSnapshot();
            if (flow.RunId != runId)
            {
                ClearViews();
                runId = flow.RunId;
                if (!string.IsNullOrEmpty(runId)) BuildTiles();
            }
            if (string.IsNullOrEmpty(runId)) return;
            double gameTime = services.Clock.GetSnapshot().GameTime;
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
                if (!visible.Contains(key)) ReturnView(key);
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
                        Facing = view.GetComponent<EnemyFacingVisual2D>()
                    };
                    // Enemy bodies live below the positioning root; never select the health bar.
                    if (entry.Facing != null && entry.Facing.BodyRenderer != null)
                        entry.Renderer = entry.Facing.BodyRenderer;
                    views.Add(key, entry);
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
            if (entry.Projectile != null && projectile != null)
            {
                entry.Projectile.SetDirection(projectile.Direction);
                entry.Projectile.SetElements(projectile.AppliedElementMask);
            }
            if (actor != null && actor.Kind == ActorKind.Enemy)
            {
                if (entry.HealthBar != null) entry.HealthBar.ApplyHealth(actor.CurrentHp, actor.MaxHp);
                if (entry.Facing != null)
                    entry.Facing.SetDirection(entry.HasPosition
                        ? new WorldPoint(position.X - entry.LastPosition.X, position.Y - entry.LastPosition.Y)
                        : new WorldPoint(0, 0));
                entry.LastPosition = position;
                entry.HasPosition = true;
            }
            visible.Add(key);
        }

        private void ReturnView(string key)
        {
            DisplayEntry entry = views[key];
            views.Remove(key);
            if (pool != null) pool.Return(entry.Lease.LeaseId);
        }

        private void BuildTiles()
        {
            LevelDefinition level = services.Core.Store.Level;
            if (level == null) return;
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
                tiles.Add(tile);
            }
        }

        private void ClearViews()
        {
            foreach (string key in new List<string>(views.Keys)) ReturnView(key);
            visible.Clear();
            foreach (GameObject tile in tiles) if (tile != null) Destroy(tile);
            tiles.Clear();
        }

        public void Unbind()
        {
            ClearViews(); services = null; pool = null; runId = null;
            if (tileSprite != null) Destroy(tileSprite);
            if (tileTexture != null) Destroy(tileTexture);
            tileSprite = null; tileTexture = null;
        }

        private void OnDestroy() { Unbind(); }
    }
}
