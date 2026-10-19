using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Spotlight.Bootstrap;
using Spotlight.Contracts;
using Spotlight.Core.Data;
using Spotlight.Elements.Definitions;
using UnityEditor;
using UnityEngine;

namespace Spotlight.EditorTools
{
    /// <summary>Reads authored assets; combat runs only in a disposable, detached composition.</summary>
    public sealed class ElementCombinationLab : EditorWindow
    {
        private PrototypeCatalogSO catalog;
        private ReactionDefinitionSO reaction;
        private Vector2 scroll;
        private string output = "选择目录与反应。预览仅显示计划；沙盒按钮会实际运行独立战斗。";
        [MenuItem("聚光灯/效果库/组合试验台")]
        public static void Open() { GetWindow<ElementCombinationLab>("组合试验台").Show(); }
        private void OnEnable() { catalog = AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(AdvancedEffectSetupTool.CatalogPath); }
        private void OnGUI()
        {
            catalog = (PrototypeCatalogSO)EditorGUILayout.ObjectField("正式目录", catalog, typeof(PrototypeCatalogSO), false);
            reaction = (ReactionDefinitionSO)EditorGUILayout.ObjectField("反应资产", reaction, typeof(ReactionDefinitionSO), false);
            EditorGUILayout.HelpBox("半径单位为格；持续/结算间隔为游戏秒；Probability只控制ApplyStatus。SourceActor作用于来源塔；SelectedActor区域子效果筛选范围内敌人；AllTowers回血筛选范围内友方。新增数值是可调原型。土土禁用，土木战斗禁用但生产单独可用。", MessageType.Info);
            if (reaction != null) EditorGUILayout.LabelField(reaction.Id, "Enabled=" + reaction.Enabled + " / " + reaction.Context + " / " + reaction.A + "+" + reaction.B);
            using (new EditorGUI.DisabledScope(catalog == null))
            {
                if (GUILayout.Button("读取真实配置验证（只读）")) output = ValidateReadOnly(catalog);
                if (GUILayout.Button("21种组合域清单（只读）")) output = DescribeCombinations(catalog);
                using (new EditorGUI.DisabledScope(reaction == null))
                {
                    if (GUILayout.Button("仅计划预览（未战斗执行）")) output = Preview(catalog, reaction);
                    if (GUILayout.Button("运行独立战斗沙盒（6游戏秒）"))
                    {
                        try { output = RunSandbox(catalog, reaction); }
                        catch (Exception error) { output = "沙盒失败（未修改正式资产或运行中的服务）：\n" + error; }
                    }
                }
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(output, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
        public static string ValidateReadOnly(PrototypeCatalogSO asset)
        {
            var errors = asset.Validate();
            return errors.Count == 0 ? "真实目录配置校验无错误（未执行战斗）。" : string.Join("\n", errors.ToArray());
        }
        public static string DescribeCombinations(PrototypeCatalogSO asset)
        {
            var text = new StringBuilder("21种无序组合；战斗与生产独立显示；数值读取当前资产。\n");
            for (int a = 1; a <= 6; a++) for (int b = a; b <= 6; b++)
            {
                var x = (ElementType)a; var y = (ElementType)b;
                text.Append(x).Append('+').Append(y).Append(": ");
                foreach (ReactionContext context in new[] { ReactionContext.EnemyStatus, ReactionContext.Projectile })
                {
                    var slots = (asset.Reactions ?? new ReactionDefinitionSO[0]).Where(r => r != null && r.Context == context && Pair(r.A, r.B, x, y)).ToArray();
                    text.Append(context).Append('=').Append(slots.Length == 0 ? "缺槽" : string.Join(",", slots.Select(r => (r.Enabled ? "可用" : "禁用") + "[" + r.Id + "]").ToArray())).Append("; ");
                }
                var production = (asset.ProductionRules ?? new ProductionRuleSO[0]).Where(r => r != null && r.Match == ProductionMatch.AdjacentPair && Pair(r.A, r.B, x, y)).ToArray();
                text.Append("生产=").Append(production.Length == 0 ? "无相邻规则" : string.Join(",", production.Select(r => (r.Enabled ? "可用" : "禁用") + "[" + r.Id + ":" + r.OutputElementId + "×" + r.OutputCount + "]").ToArray()));
                if (x == ElementType.Earth && y == ElementType.Earth) text.Append("；障碍方案未实现；现地单土生产保留（偏原案）");
                text.AppendLine();
            }
            return text.ToString();
        }
        private static bool Pair(ElementType a, ElementType b, ElementType x, ElementType y) { return (a == x && b == y) || (a == y && b == x); }
        public static string Preview(PrototypeCatalogSO asset, ReactionDefinitionSO selected)
        {
            using (var sandbox = new Sandbox(asset))
            {
                var plan = sandbox.Runtime.Elements.ResolveReactions(new ReactionInput { Context = selected.Context, Tokens = new[] { new ReactionToken { TokenId = 1, Element = selected.A }, new ReactionToken { TokenId = 2, Element = selected.B } }, AppliedRuleIds = new string[0] });
                return "仅计划预览；未战斗执行。\n" + string.Join("\n", plan.Matches.Select(m => m.RuleId).ToArray());
            }
        }
        public static string RunSandbox(PrototypeCatalogSO asset, ReactionDefinitionSO selected)
        {
            if (!asset.Reactions.Contains(selected)) throw new InvalidOperationException("所选反应未注册到目录。");
            if (!selected.Enabled) return "所选正式反应禁用：未执行。生产规则请查看21种组合域清单。";
            using (var sandbox = new Sandbox(asset))
            {
                return sandbox.Run(selected);
            }
        }
        [Serializable]
        private sealed class SandboxCase
        {
            public string id, context, result, error;
            public bool succeeded;
        }
        [Serializable]
        private sealed class SandboxReport
        {
            public string utc, combinations, limitation;
            public bool succeeded;
            public List<SandboxCase> cases = new List<SandboxCase>();
        }
        public static void ExportFormalSandboxReport()
        {
            AdvancedEffectSetupTool.CreateOrComplete();
            var asset = AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(AdvancedEffectSetupTool.CatalogPath);
            var report = new SandboxReport
            {
                utc = DateTime.UtcNow.ToString("o"), succeeded = true,
                combinations = DescribeCombinations(asset),
                limitation = "Detached six-second combat experiments with stationary high-HP enemies. Reaction events and actual state are recorded; these are not seven-night balance or probability distribution proofs."
            };
            foreach (var selected in asset.Reactions.Where(r => r.Enabled).OrderBy(r => r.Id, StringComparer.Ordinal))
            {
                var entry = new SandboxCase { id = selected.Id, context = selected.Context.ToString() };
                try
                {
                    entry.result = RunSandbox(asset, selected);
                    entry.succeeded = entry.result.Contains("实际反应=" + selected.Id + " / ");
                    if (!entry.succeeded) entry.error = "The selected reaction did not execute.";
                }
                catch (Exception error) { entry.error = error.ToString(); }
                report.succeeded &= entry.succeeded;
                report.cases.Add(entry);
            }
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../QA/R3B/Combinations/sandbox-report.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            Debug.Log("组合独立沙盒报告：" + path + "，执行 " + report.cases.Count + " 条反应。");
            if (!report.succeeded) throw new InvalidOperationException("组合沙盒存在未执行或失败项，请查看报告。");
        }
        private sealed class Sandbox : IDisposable
        {
            internal readonly ModuleComposition Runtime;
            private readonly string directory;
            internal Sandbox(PrototypeCatalogSO asset)
            {
                var errors = asset.Validate();
                if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors.ToArray()));
                // BuildCatalogData returns detached DTOs. No asset or live composition is edited.
                CatalogData data = asset.BuildCatalogData();
                foreach (var tower in data.Towers) tower.RangeInCells = .01f;
                directory = Path.Combine(Path.GetTempPath(), "Spotlight_CombinationLab_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try { Runtime = new ModuleComposition(data, directory); }
                catch { DeleteTemporaryDirectory(); throw; }
            }
            private CommandContext Command() { return Runtime.Core.Commands.Create(Runtime.Core.Transactions.Revision); }
            private void Require(OperationResult result) { if (result.State != OperationState.Committed) throw new InvalidOperationException(result.Error + ":" + result.MessageKey); }
            private void RequireQueued(OperationResult result)
            {
                if (result.State != OperationState.Queued && result.State != OperationState.Committed)
                    throw new InvalidOperationException(result.Error + ":" + result.MessageKey);
            }
            internal string Run(ReactionDefinitionSO selected)
            {
                var initialized = Runtime.Core.Store.Initialize(new NewRunRequest { LevelId = "level.prototype", Mode = GameMode.Story, Seed = 123 });
                if (initialized.Error != ErrorCode.None) throw new InvalidOperationException(initialized.Error.ToString());
                var board = Runtime.Core.Board.GetSnapshot();
                // Select three adjacent, non-path cells so earth/wood placement uses ordinary transaction validation.
                CellCoord[] cells = null;
                foreach (var cell in board.Cells)
                {
                    var candidate = new[] { cell.Cell, new CellCoord(cell.Cell.X + 1, cell.Cell.Y), new CellCoord(cell.Cell.X + 2, cell.Cell.Y) };
                    if (candidate.All(p => board.Cells.Any(c => c.Cell.Equals(p) && !c.Reserved && !c.OnGroundPath && c.OccupantKind == OccupantKind.None))) { cells = candidate; break; }
                }
                if (cells == null) throw new InvalidOperationException("找不到连续三格独立试验空间。");
                var towerId = Runtime.Core.Ids.Allocate();
                var towerPosition = Runtime.Core.Board.CellToWorld(cells[0]);
                TowerDefinition tower; if (!Runtime.Catalog.TryGetTower("tower.basic", out tower)) throw new InvalidOperationException("缺少tower.basic");
                float towerHp = tower.HpPerStack * tower.InitialAttackStacks;
                var actor = new ActorSnapshot { Id = towerId, Kind = ActorKind.Tower, DefinitionId = tower.Id, Position = towerPosition, CurrentHp = towerHp / 2, MaxHp = towerHp, TowerAttackStacks = tower.InitialAttackStacks, Targetable = true, CollisionRadius = .1f };
                Require(Runtime.Core.Transactions.TryCommit(new StateMutationBatch { Context = Command(), Reason = "lab.register", Board = new[] { new BoardMutation { Kind = BoardMutationKind.Place, EntityId = towerId, OccupantKind = OccupantKind.Tower, DefinitionId = tower.Id, To = cells[0] } }, Actors = new[] { new ActorMutation { Kind = ActorMutationKind.Register, ActorId = towerId, Registration = actor } } }));
                var enemies = new List<EntityId>();
                var center = Runtime.Core.Board.CellToWorld(cells[2]);
                float enemyX = selected.Context == ReactionContext.Projectile ? center.X + 1 : towerPosition.X + .5f;
                for (int i = 0; i < 3; i++)
                {
                    EntityId id = Runtime.Core.Ids.Allocate(); enemies.Add(id);
                    Require(Runtime.Core.Transactions.TryCommit(new StateMutationBatch { Context = Command(), Reason = "lab.register.enemy", Actors = new[] { new ActorMutation { Kind = ActorMutationKind.Register, ActorId = id, Registration = new ActorSnapshot { Id = id, Kind = ActorKind.Enemy, DefinitionId = "enemy.goblin.melee", Position = new WorldPoint(enemyX + i * .5f, center.Y), CurrentHp = 10000, MaxHp = 10000, Targetable = true, CollisionRadius = .15f, SpawnSequence = i + 1 } } } }));
                }
                Runtime.Combat.BeginSession(new SessionStartContext { RunId = Runtime.Core.Store.GetFlowSnapshot().RunId, LevelId = "level.prototype", DayIndex = 1, Mode = GameMode.Story, SpringId = Runtime.Core.World.SpringId });
                Require(Runtime.Core.Transactions.TryCommit(new StateMutationBatch { Context = Command(), Reason = "lab.night", Progress = new ProgressPatch { Phase = GamePhase.Night, DayIndex = 1, TutorialShown = true } }));
                Runtime.Advance(0);
                var text = new StringBuilder("实际独立沙盒：真实正式配置、独立DTO和临时存档；未启动波次，三个静止大血量敌人。\n" + selected.Id + "\n");
                text.AppendLine(Snapshot(towerId, enemies, 0));
                using (Runtime.Events.Subscribe<CombatEffectEvent>(e => text.AppendLine("effect=" + e.Kind)))
                using (Runtime.Events.Subscribe<ReactionResolvedEvent>(e => text.AppendLine("实际反应=" + e.RuleId + " / " + e.Context)))
                {
                    if (selected.Context == ReactionContext.Projectile)
                    {
                        ElementDefinition a = FindElement(selected.A), b = FindElement(selected.B);
                        Require(Runtime.Core.Transactions.TryCommit(new StateMutationBatch { Context = Command(), Reason = "lab.two.blocks", Board = new[] { new BoardMutation { Kind = BoardMutationKind.Place, EntityId = Runtime.Core.Ids.Allocate(), OccupantKind = OccupantKind.ElementBlock, DefinitionId = a.Id, To = cells[1] }, new BoardMutation { Kind = BoardMutationKind.Place, EntityId = Runtime.Core.Ids.Allocate(), OccupantKind = OccupantKind.ElementBlock, DefinitionId = b.Id, To = cells[2] } } }));
                        Require(Runtime.Combat.Projectiles.Spawn(new ProjectileSpawnRequest { Source = towerId, DefinitionId = tower.ProjectileId, Origin = towerPosition, AimPoint = new WorldPoint(center.X + 8, center.Y), PhysicalDamage = 20, DamageOrigin = DamageOrigin.Tower }));
                    }
                    else
                    {
                        if (selected.A == ElementType.Water && selected.B == ElementType.Thunder)
                            foreach (var id in enemies.Skip(1))
                                RequireQueued(Runtime.Combat.Status.EnqueueStatus(new StatusApplyRequest
                                {
                                    Source = towerId, Target = id, StatusId = FindInputStatus(ElementType.Water).Id,
                                    Stacks = 1, DurationOverride = 10
                                }));
                        int inputOrdinal = 0;
                        foreach (ElementType element in new[] { selected.A, selected.B })
                        {
                            StatusDefinition status = FindInputStatus(element);
                            RequireQueued(Runtime.Combat.Status.EnqueueStatus(new StatusApplyRequest { ScopeId = "lab.input." + inputOrdinal++, Source = towerId, Target = enemies[0], StatusId = status.Id, Stacks = 1, DurationOverride = 10 }));
                        }
                    }
                    for (int i = 0; i < 180; i++)
                    {
                        Runtime.Advance(1f / 30);
                        if (i == 29 || i == 59 || i == 179) text.AppendLine(Snapshot(towerId, enemies, (i + 1) / 30f));
                    }
                }
                return text.ToString();
            }
            private ElementDefinition FindElement(ElementType type)
            {
                ElementDefinition value;
                if (!Runtime.Catalog.TryGetElement("elm." + type.ToString().ToLowerInvariant(), out value)) throw new InvalidOperationException("缺少元素：" + type);
                return value;
            }
            private StatusDefinition FindInputStatus(ElementType type)
            {
                StatusDefinition status;
                string id = "status." + type.ToString().ToLowerInvariant();
                if (!Runtime.Catalog.TryGetStatus(id, out status) || !status.IsReactionInput) throw new InvalidOperationException("缺少可反应基础状态：" + id);
                return status;
            }
            private string Snapshot(EntityId tower, List<EntityId> enemies, float seconds)
            {
                var text = new StringBuilder("t=" + seconds + "秒 / 区域=" + Runtime.Combat.GetAreaEffects().Count);
                foreach (EntityId id in new[] { tower, Runtime.Core.World.SpringId }.Concat(enemies))
                {
                    ActorSnapshot actor; if (!Runtime.Core.World.TryGetActor(id, out actor)) continue;
                    text.Append("\n").Append(actor.Kind).Append('#').Append(id.Value).Append(" HP=").Append(actor.CurrentHp).Append(" 状态=").Append(string.Join(",", Runtime.Combat.Status.GetStatuses(id).Select(s => s.DefinitionId + "×" + s.Layers.Count).ToArray()));
                    text.Append(" 计量=").Append(string.Join(",", Runtime.Combat.Status.GetGauges(id).Where(g => g.Value > 0).Select(g => g.Element + ":" + g.Value).ToArray()));
                    var rate = Runtime.Combat.Status.GetModifiers(id).AttackRateMultiplier;
                    text.Append(" 攻速倍率=").Append(rate);
                }
                return text.ToString();
            }
            public void Dispose()
            {
                Runtime.Dispose();
                DeleteTemporaryDirectory();
            }
            private void DeleteTemporaryDirectory()
            {
                string full = Path.GetFullPath(directory);
                string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(full).StartsWith("Spotlight_CombinationLab_", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected sandbox cleanup path.");
                if (Directory.Exists(full)) Directory.Delete(full, true);
            }
        }
    }
}
