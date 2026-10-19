#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Spotlight.Bootstrap;
using Spotlight.Contracts;
namespace Spotlight.Tests.Integration
{
    public sealed class ThirdRoundScenarioTests
    {
        [Serializable] sealed class EventChoice { public int day; public string definition, option, runId; }
        [Serializable] sealed class LayoutEvidence
        {
            public bool succeeded; public string status, utc, configVersion;
            public int seed=456; public string layout="Zero-based Day1: tower.basic (1,1)/(3,1)/(5,1)/(5,3)/(7,1), elm.water (1,2), existing item.tower_stack on (5,1). Day2: existing item.heal on Spring; tower.basic (6,3) purchased from actual gold drops. Unmodified catalog and HP.";
            public RuntimeQaRecorder.Quantity[] initialResources;
            public List<EventChoice> eventChoices=new List<EventChoice>();
            public RuntimeQaRecorder.Snapshot nightOne,nightTwo;
            public int firstNightObservedBirths,secondNightObservedBirths;
            public RuntimeQaRecorder.Settlement[] production;
        }
        LayoutEvidence evidence;
        ModuleComposition runtime; RuntimeQaRecorder recorder; GameObject observer; string directory;
        [SetUp] public void Setup()
        {
            evidence=null;
            if(TestContext.CurrentContext.Test.MethodName==nameof(RealUnmodifiedCatalogTwoNightsObserverProductionSaveContinueAndRestart))
            { evidence=new LayoutEvidence {status="started; not verified",utc=DateTime.UtcNow.ToString("o")};WriteEvidence(); }
            PrototypeCatalogSO asset=AssetDatabase.LoadAssetAtPath<PrototypeCatalogSO>(PrototypeCatalogFactory.CatalogPath);Assert.NotNull(asset);
            directory=Path.Combine(Path.GetTempPath(),"Spotlight_R3_Scenario_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            runtime=new ModuleComposition(asset.BuildCatalogData(),directory);observer=new GameObject("R3 read-only recorder test");recorder=observer.AddComponent<RuntimeQaRecorder>();recorder.Bind(runtime);NewRun();
            if(evidence!=null){evidence.configVersion=runtime.Catalog.GetSettings().ConfigVersion;evidence.initialResources=recorder.Capture().resources;}
        }
        [TearDown] public void Cleanup()
        {
            if(evidence!=null && TestContext.CurrentContext.Result.Outcome.Status!=NUnit.Framework.Interfaces.TestStatus.Passed)
            {evidence.succeeded=false;evidence.status="test failed or incomplete";WriteEvidence();}
            if(observer!=null)UnityEngine.Object.DestroyImmediate(observer);if(runtime!=null)runtime.Dispose();
            if(directory!=null&&Directory.Exists(directory))
            {
                string full=Path.GetFullPath(directory),root=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                Assert.IsTrue(full.StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("Spotlight_R3_Scenario_",StringComparison.Ordinal));Directory.Delete(full,true);
            }
        }
        void WriteEvidence()
        {
            try
            {
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../QA/R3/Runtime/verified-layout.json"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(evidence,true));
            }
            catch(Exception error){TestContext.WriteLine("Layout evidence unavailable: "+error.Message);}
        }
        [Serializable] sealed class Candidate {public string layout,firstPhase,secondPhase;public bool succeeded;public RuntimeQaRecorder.Snapshot nightOne,nightTwo;}
        [Serializable] sealed class Exploration {public string utc;public Candidate[] candidates;}
        [Test] public void ExploreTwoLegalLayoutsWithoutChangingCatalogOrHp()
        {
            var candidates=new List<Candidate>();
            for(int variant=0;variant<2;variant++)
            {
                if(variant>0){Committed(runtime.Flow.TryReturnToMenu(Command()));Advance(0);NewRun();}
                var candidate=new Candidate {layout=variant==0?"Original three towers 1/3/5 y1, water (1,2)":"Five towers 1/3/5/7 y1 and (5,3), stack (5,1), Day2 heal+add (6,3)"};candidates.Add(candidate);
                if(variant==0){foreach(int x in new[]{1,3,5})Place("tower.basic",OccupantKind.Tower,x,1);Place("elm.water",OccupantKind.ElementBlock,1,2);}else BuildVerifiedCandidate();
                Committed(runtime.Flow.TryStartNight(Command()));for(int tick=0;tick<10800&&runtime.Flow.GetSnapshot().Phase==GamePhase.Night;tick++)Advance(1f/30f);
                candidate.nightOne=recorder.Capture();candidate.firstPhase=runtime.Flow.GetSnapshot().Phase.ToString();
                if(runtime.Flow.GetSnapshot().Phase!=GamePhase.NightResult)continue;
                Committed(runtime.Flow.TryConfirmNightResult(Command()));Advance(0);ChooseEvent();if(variant>0)ImproveDayTwo();
                Committed(runtime.Flow.TryStartNight(Command()));for(int tick=0;tick<10800&&runtime.Flow.GetSnapshot().Phase==GamePhase.Night;tick++)Advance(1f/30f);
                candidate.nightTwo=recorder.Capture();candidate.secondPhase=runtime.Flow.GetSnapshot().Phase.ToString();candidate.succeeded=runtime.Flow.GetSnapshot().Phase==GamePhase.NightResult;
            }
            try{string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../QA/R3/Runtime/layout-exploration.json"));Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(new Exploration {utc=DateTime.UtcNow.ToString("o"),candidates=candidates.ToArray()},true));}
            catch(Exception error){TestContext.WriteLine("Exploration report unavailable: "+error.Message);}
            Assert.IsTrue(candidates[1].succeeded,"Improved legal candidate must actually survive both nights; see layout-exploration.json.");
        }
        CommandContext Command(){return runtime.Core.Commands.Create(runtime.Core.Transactions.Revision);}
        void Committed(OperationResult result){Assert.AreEqual(OperationState.Committed,result.State,result.Error.ToString());}
        void NewRun(){Committed(runtime.Flow.TryNewRun(Command(),new NewRunRequest {LevelId="level.prototype",Mode=GameMode.Story,Seed=456}));Advance(0);ChooseEvent();Committed(runtime.Flow.TryDismissTutorial(Command()));Advance(0);}
        void ChooseEvent()
        {
            if(runtime.Flow.GetSnapshot().Phase==GamePhase.Build)return;DayEventSnapshot day=runtime.Gameplay.DayEvents.GetSnapshot();
            // This layout experiment reserves its gold for towers. Paid event branches are tested separately.
            var options=new List<EventOptionDefinition>(day.Options);
            options.Sort((a,b)=>(a.Cost==null||a.Cost.Count==0?0:1).CompareTo(b.Cost==null||b.Cost.Count==0?0:1));
            bool chosen=false;foreach(EventOptionDefinition option in options)if(runtime.Gameplay.DayEvents.TryChoose(Command(),day.State.InstanceId,option.Id).State==OperationState.Committed){chosen=true;if(evidence!=null)evidence.eventChoices.Add(new EventChoice {day=day.State.DayIndex,definition=day.State.DefinitionId,option=option.Id,runId=runtime.Flow.GetSnapshot().RunId});break;}
            Assert.IsTrue(chosen);Advance(0);Assert.AreEqual(GamePhase.Build,runtime.Flow.GetSnapshot().Phase);
        }
        void Advance(float delta){runtime.Advance(delta);recorder.Observe();}
        void Place(string id,OccupantKind kind,int x,int y){Committed(runtime.Core.Deployment.TryPlace(Command(),new PlacementRequest {DefinitionId=id,Kind=kind,Cell=new CellCoord(x,y)}));Advance(0);}
        void BuildVerifiedCandidate()
        {
            foreach(int x in new[]{1,3,5,7})Place("tower.basic",OccupantKind.Tower,x,1);Place("tower.basic",OccupantKind.Tower,5,3);Place("elm.water",OccupantKind.ElementBlock,1,2);
            EntityId tower=new EntityId();foreach(CellSnapshot cell in runtime.Core.Board.GetSnapshot().Cells)if(cell.Cell.X==5&&cell.Cell.Y==1)tower=cell.OccupantId;
            Committed(runtime.Gameplay.Items.TryUse(Command(),new ItemUseRequest {ItemId="item.tower_stack",Target=tower}));Advance(0);
        }
        void ImproveDayTwo()
        {
            Committed(runtime.Gameplay.Items.TryUse(Command(),new ItemUseRequest {ItemId="item.heal",Target=runtime.Core.World.SpringId}));Advance(0);
            Place("tower.basic",OccupantKind.Tower,6,3);
        }
        void CompleteNight(int night)
        {
            Committed(runtime.Flow.TryStartNight(Command()));
            for(int tick=0;tick<10800&&runtime.Flow.GetSnapshot().Phase==GamePhase.Night;tick++)Advance(1f/30f);
            Assert.AreEqual(GamePhase.NightResult,runtime.Flow.GetSnapshot().Phase,"Unmodified catalog night "+night+" must survive; do not replace with forced HP/damage.");
        }
        Dictionary<string,long> Resources()
        {
            var values=new Dictionary<string,long>();foreach(ResourceBucket bucket in Enum.GetValues(typeof(ResourceBucket)))foreach(ResourceAmount r in runtime.Core.Resources.GetSnapshot(bucket))values[bucket+":"+r.DefinitionId]=r.Amount;return values;
        }
        Dictionary<string,long> Recorded(RuntimeQaRecorder.Quantity[] quantities)
        {
            var values=new Dictionary<string,long>();foreach(RuntimeQaRecorder.Quantity q in quantities)values[q.bucket+":"+q.definition]=q.amount;return values;
        }
        [Test] public void RecorderDistinguishesPreboundNewRunFromLateAttachmentAndReadsSpeed()
        {
            // Setup binds before the real TryNewRun and then dispatches its events.
            Assert.IsFalse(recorder.Capture().attachedDuringActiveRun);
            Assert.AreEqual(GameSpeed.Normal.ToString(),recorder.Capture().speed);
            GameObject lateObject=new GameObject("R3 deliberately late observer");
            try
            {
                RuntimeQaRecorder late=lateObject.AddComponent<RuntimeQaRecorder>();late.Bind(runtime);
                Assert.IsTrue(late.Capture().attachedDuringActiveRun,"Binding to an existing Build phase must disclose missing past births.");
                Committed(runtime.Clock.TrySetSpeed(Command(),GameSpeed.Double));Advance(0);
                Assert.AreEqual(GameSpeed.Double.ToString(),late.Capture().speed);
                Assert.AreEqual(GameSpeed.Double.ToString(),recorder.Capture().speed);
                Committed(runtime.Flow.TryReturnToMenu(Command()));Advance(0);NewRun();
                Assert.IsFalse(late.Capture().attachedDuringActiveRun,"A late observer is already bound for the next new run.");
                Assert.IsFalse(recorder.Capture().attachedDuringActiveRun);
            }
            finally {UnityEngine.Object.DestroyImmediate(lateObject);}
        }
        [Test] public void RealUnmodifiedCatalogTwoNightsObserverProductionSaveContinueAndRestart()
        {
            BuildVerifiedCandidate();
            Committed(runtime.Flow.TryStartNight(Command()));Guid pause=runtime.Clock.AcquirePause(PauseReason.Player);ClockSnapshot before=runtime.Clock.GetSnapshot();Advance(1);
            Assert.AreEqual(before.GameTime,runtime.Clock.GetSnapshot().GameTime);Assert.AreEqual(before.TickIndex,runtime.Clock.GetSnapshot().TickIndex);runtime.Clock.ReleasePause(pause);
            Committed(runtime.Clock.TrySetSpeed(Command(),GameSpeed.Double));long ticks=runtime.Clock.GetSnapshot().TickIndex;Advance(1f/30f);Assert.AreEqual(ticks+2,runtime.Clock.GetSnapshot().TickIndex);
            for(int tick=0;tick<10800&&runtime.Flow.GetSnapshot().Phase==GamePhase.Night;tick++)Advance(1f/30f);
            Assert.AreEqual(GamePhase.NightResult,runtime.Flow.GetSnapshot().Phase,"Fixed real layout must pass first night.");
            RuntimeQaRecorder.Snapshot nightOne=recorder.Capture();int expected=0;foreach(WaveDefinition wave in runtime.Catalog.GetWaves(1,GameMode.Story))foreach(SpawnGroupDefinition group in wave.Groups)expected+=group.Count;
            Assert.AreEqual(expected,nightOne.totalObservedEnemyBirths);Assert.AreEqual(0,nightOne.aliveEnemies);
            evidence.nightOne=nightOne;evidence.firstNightObservedBirths=nightOne.totalObservedEnemyBirths;
            foreach(RuntimeQaRecorder.Birth birth in nightOne.births)if(birth.kind==ActorKind.Enemy.ToString())Assert.AreEqual(1,birth.day);
            Dictionary<string,long> resourcesBefore=Resources();DawnSettledEvent settlement=null;IDisposable subscription=runtime.Events.Subscribe<DawnSettledEvent>(e=>settlement=e);
            Committed(runtime.Flow.TryConfirmNightResult(Command()));Advance(0);Assert.NotNull(settlement);
            var expectedAfter=new Dictionary<string,long>(resourcesBefore);foreach(ResourceAmount delta in settlement.Rewards){string key=delta.Bucket+":"+delta.DefinitionId;long value;expectedAfter.TryGetValue(key,out value);expectedAfter[key]=value+delta.Amount;}
            CollectionAssert.AreEquivalent(expectedAfter,Resources());RuntimeQaRecorder.Snapshot dawn=recorder.Capture();Assert.AreEqual(1,dawn.settlements.Length);Assert.NotNull(dawn.settlements[0].beforeLastObservedNightResult);Assert.NotNull(dawn.settlements[0].afterAtEventDispatch);
            CollectionAssert.AreEquivalent(resourcesBefore,Recorded(dawn.settlements[0].beforeLastObservedNightResult));CollectionAssert.AreEquivalent(expectedAfter,Recorded(dawn.settlements[0].afterAtEventDispatch));subscription.Dispose();
            evidence.production=dawn.settlements;
            ChooseEvent();ImproveDayTwo();Committed(runtime.Saves.SaveCheckpoint(Command(),SaveReason.Manual));SaveReadResult saved=runtime.Saves.ReadCheckpoint();Assert.AreEqual(ErrorCode.None,saved.Error);Dictionary<string,long> savedResources=Resources();
            Committed(runtime.Flow.TryReturnToMenu(Command()));Advance(0);Committed(runtime.Flow.TryLoadRun(Command(),saved.Data));Advance(0);Advance(0);
            CollectionAssert.AreEquivalent(savedResources,Resources());Assert.AreEqual(1,runtime.Flow.GetSnapshot().LastSettledNight,"Loading must not produce again.");
            CompleteNight(2);int dayTwoExpected=0;foreach(WaveDefinition wave in runtime.Catalog.GetWaves(2,GameMode.Story))foreach(SpawnGroupDefinition group in wave.Groups)dayTwoExpected+=group.Count;
            Assert.AreEqual(dayTwoExpected,recorder.Capture().totalObservedEnemyBirths,"After menu/load observer totals cover loaded run's observed births only.");
            evidence.nightTwo=recorder.Capture();evidence.secondNightObservedBirths=evidence.nightTwo.totalObservedEnemyBirths;
            Committed(runtime.Flow.TryReturnToMenu(Command()));Advance(0);NewRun();Assert.AreEqual(0,recorder.Capture().totalObservedEnemyBirths);Assert.AreEqual(0,recorder.Capture().settlements.Length);
            evidence.succeeded=true;evidence.status="verified two real nights, production, pause/2x, save/continue and restart";evidence.utc=DateTime.UtcNow.ToString("o");WriteEvidence();
        }
        [TestCase("elm.water")][TestCase("elm.fire")][TestCase("elm.earth")][TestCase("elm.wood")][TestCase("elm.wind")][TestCase("elm.thunder")]
        public void ActualSingleProductionDoesNotMutateBoardOrResources(string definition)
        {
            ElementDefinition element;Assert.IsTrue(runtime.Catalog.TryGetElement(definition,out element));Place(definition,OccupantKind.ElementBlock,2,4);
            Dictionary<string,long> before=Resources();BoardSnapshot board=runtime.Core.Board.GetSnapshot();
            ProductionResult result=runtime.Elements.ResolveProduction(new ProductionInput {SettlementId="qa.single",CompletedNight=1,Board=board,Random=runtime.Core.Random.GetState(RandomStream.Production)});
            long output=0;foreach(ResourceAmount delta in result.HandDeltas)if(delta.DefinitionId==definition)output+=delta.Amount;Assert.AreEqual(1,output);Assert.AreEqual(0,result.Board.Count);CollectionAssert.AreEquivalent(before,Resources());
        }
        [TestCase(0,1)][TestCase(1,0)][TestCase(0,-1)][TestCase(-1,0)][TestCase(1,1)]
        public void ActualConfiguredProductionChecksCardinalAndDiagonalLayouts(int dx,int dy)
        {
            Place("elm.water",OccupantKind.ElementBlock,3,4);Place("elm.fire",OccupantKind.ElementBlock,3+dx,4+dy);
            ProductionResult result=runtime.Elements.ResolveProduction(new ProductionInput {SettlementId="qa.adjacency",CompletedNight=1,Board=runtime.Core.Board.GetSnapshot(),Random=runtime.Core.Random.GetState(RandomStream.Production)});
            // Six single rules and four wood adjacency rules, including Wood/Wood.
            Assert.AreEqual(10,runtime.Catalog.GetProductionRules().Count);
            long water=0,fire=0;foreach(ResourceAmount delta in result.HandDeltas){if(delta.DefinitionId=="elm.water")water+=delta.Amount;if(delta.DefinitionId=="elm.fire")fire+=delta.Amount;}
            Assert.AreEqual(1,water);Assert.AreEqual(1,fire);Assert.AreEqual(0,result.Board.Count);
        }
    }
}
#endif
