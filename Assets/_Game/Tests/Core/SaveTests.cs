using System;
using System.IO;
using System.Collections.Generic;
using NUnit.Framework;
using Spotlight.Contracts;
using Spotlight.Core;
using Spotlight.Core.Save;
namespace Spotlight.Tests.Core {
public sealed class SaveTests {
 string directory;CoreServices core;Catalog catalog;SaveService save;
 [SetUp]public void Setup(){directory=Path.Combine(Path.GetTempPath(),"spotlight-save-tests-"+Guid.NewGuid().ToString("N"));catalog=new Catalog();core=new CoreServices(catalog,null);Assert.AreEqual(ErrorCode.None,core.Store.Initialize(new NewRunRequest{LevelId="level",Mode=GameMode.Story,Seed=1}).Error);save=new SaveService(core.Store,catalog,null,directory);}
 [TearDown]public void Teardown(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
 void AssertSaved(OperationResult result){Assert.AreEqual(OperationState.Committed,result.State,result.Error+" / "+result.MessageKey);}
 CommandContext Command(){return core.Commands.Create(core.Transactions.Revision);}
 [Test]public void SavedReadonlyValuesAndCommandIdsRoundTripWithoutRestoringLiveStore(){var c=Command();core.Transactions.TryCommit(new StateMutationBatch{Context=c,ResourceDeltas=new[]{new ResourceAmount(ResourceBucket.Hand,"water",3)}});AssertSaved(save.SaveCheckpoint(Command(),SaveReason.Manual));core.Transactions.TryCommit(new StateMutationBatch{Context=Command(),ResourceDeltas=new[]{new ResourceAmount(ResourceBucket.Hand,"water",2)}});var read=save.ReadCheckpoint();Assert.AreEqual(ErrorCode.None,read.Error);Assert.AreEqual(3,read.Data.Resources[0].Amount);Assert.AreEqual(c.CommandId,read.Data.CommittedCommandIds[0]);Assert.AreEqual(5,core.Resources.GetQuantity(ResourceBucket.Hand,"water"));Assert.AreEqual(core.World.SpringId,read.Data.SpringId);}
 [Test]public void NativeJsonUtilityPreservesAbsentAndPresentDayEvent(){var data=core.Store.CaptureCheckpoint(catalog);Assert.IsTrue(data.DayEvent==null);var absent=UnityEngine.JsonUtility.FromJson<CheckpointFileModel>(UnityEngine.JsonUtility.ToJson(CheckpointFileModel.FromCheckpoint(data))).ToCheckpoint();Assert.IsTrue(absent.DayEvent==null);Assert.AreEqual(data.RunSeed,absent.RunSeed);Assert.AreEqual(data.SpringId,absent.SpringId);Assert.AreEqual(data.RandomStates.Count,absent.RandomStates.Count);for(int i=0;i<data.RandomStates.Count;i++)Assert.AreEqual(data.RandomStates[i].State,absent.RandomStates[i].State,"native random stream "+i);data.DayEvent=new DayEventState{DayIndex=1,InstanceId="none",Resolved=true};var present=UnityEngine.JsonUtility.FromJson<CheckpointFileModel>(UnityEngine.JsonUtility.ToJson(CheckpointFileModel.FromCheckpoint(data))).ToCheckpoint();Assert.IsTrue(present.DayEvent!=null);Assert.AreEqual(1,present.DayEvent.DayIndex);Assert.AreEqual("none",present.DayEvent.InstanceId);Assert.IsTrue(present.DayEvent.Resolved);} [Test]public void SavedHistoryRestoresExactRetryAndRejectsDifferentBody(){var command=Command();var batch=new StateMutationBatch{Context=command,ResourceDeltas=new[]{new ResourceAmount(ResourceBucket.Hand,"water",3)}};var original=core.Transactions.TryCommit(batch);AssertSaved(save.SaveCheckpoint(Command(),SaveReason.Manual));var read=save.ReadCheckpoint();Assert.AreEqual(ErrorCode.None,core.Store.Restore(read.Data).Error);Assert.AreEqual(ErrorCode.None,save.RestoreHistoryFor(read.Data).Error);Assert.AreEqual(original.Revision,core.Transactions.TryCommit(batch).Revision);Assert.AreEqual(3,core.Resources.GetQuantity(ResourceBucket.Hand,"water"));batch.ResourceDeltas=new[]{new ResourceAmount(ResourceBucket.Hand,"water",5)};Assert.AreEqual(ErrorCode.DuplicateCommand,core.Transactions.TryCommit(batch).Error);} [Test]public void SaveReceiptPersistsAndRejectsDifferentReasonOrTransactionId(){var command=Command();var result=save.SaveCheckpoint(command,SaveReason.Manual);AssertSaved(result);Assert.AreEqual(ErrorCode.DuplicateCommand,save.SaveCheckpoint(command,SaveReason.BeforeNight).Error);var read=save.ReadCheckpoint();Assert.AreEqual(ErrorCode.None,core.Store.Restore(read.Data).Error);Assert.AreEqual(ErrorCode.None,save.RestoreHistoryFor(read.Data).Error);Assert.AreEqual(result.Revision,save.SaveCheckpoint(command,SaveReason.Manual).Revision);Assert.AreEqual(ErrorCode.DuplicateCommand,save.SaveCheckpoint(command,SaveReason.BeforeNight).Error);var tx=Command();core.Transactions.TryCommit(new StateMutationBatch{Context=tx,ResourceDeltas=new[]{new ResourceAmount(ResourceBucket.Hand,"water",1)}});Assert.AreEqual(ErrorCode.DuplicateCommand,save.SaveCheckpoint(tx,SaveReason.Manual).Error);} [Test]public void CorruptPrimaryFallsBackAndNextSavePreservesValidBackup(){AssertSaved(save.SaveCheckpoint(Command(),SaveReason.Manual));core.Transactions.TryCommit(new StateMutationBatch{Context=Command(),ResourceDeltas=new[]{new ResourceAmount(ResourceBucket.Hand,"water",1)}});AssertSaved(save.SaveCheckpoint(Command(),SaveReason.Manual));string backup=File.ReadAllText(Path.Combine(directory,"checkpoint.bak"));File.WriteAllText(Path.Combine(directory,"checkpoint.json"),"broken");Assert.IsTrue(save.ReadCheckpoint().UsedBackup);AssertSaved(save.SaveCheckpoint(Command(),SaveReason.Manual));Assert.AreEqual(backup,File.ReadAllText(Path.Combine(directory,"checkpoint.bak")));}
 [Test]public void WriteFailureLeavesPreviousCheckpointUsable(){AssertSaved(save.SaveCheckpoint(Command(),SaveReason.Manual));Directory.CreateDirectory(Path.Combine(directory,"checkpoint.tmp"));Assert.AreEqual(ErrorCode.SaveIoFailure,save.SaveCheckpoint(Command(),SaveReason.Manual).Error);Assert.AreEqual(ErrorCode.None,save.ReadCheckpoint().Error);}
 [Test]public void UnsupportedVersionAndNegativeResourceReportDistinctErrors(){AssertSaved(save.SaveCheckpoint(Command(),SaveReason.Manual));var model=CheckpointFileModel.FromCheckpoint(core.Store.CaptureCheckpoint(catalog));model.SaveVersion=99;File.WriteAllText(Path.Combine(directory,"checkpoint.json"),UnityEngine.JsonUtility.ToJson(model));Assert.AreEqual(ErrorCode.SaveVersionUnsupported,save.ReadCheckpoint().Error);model.SaveVersion=1;model.Resources=new[]{new CheckpointFileModel.ResourceEntry{Bucket=0,DefinitionId="water",Amount=-1}};File.WriteAllText(Path.Combine(directory,"checkpoint.json"),UnityEngine.JsonUtility.ToJson(model));Assert.AreEqual(ErrorCode.SaveCorrupt,save.ReadCheckpoint().Error);}

 [Test]public void LegacyContractReadPreservesFileStateAndExactCommandHistoryWithoutRewards()
 {
  var command=Command();var batch=new StateMutationBatch{Context=command,ResourceDeltas=new[]{new ResourceAmount(ResourceBucket.Hand,"water",3)}};
  var original=core.Transactions.TryCommit(batch);AssertSaved(save.SaveCheckpoint(Command(),SaveReason.Manual));
  string path=Path.Combine(directory,"checkpoint.json");
  var model=UnityEngine.JsonUtility.FromJson<CheckpointFileModel>(File.ReadAllText(path));
  model.ContractVersion="2.1.0";model.DayIndex=2;model.LastSettledNight=1;
  model.HasDayEvent=true;model.DayEvent=new CheckpointFileModel.EventEntry{DayIndex=2,InstanceId="already-resolved",Resolved=true};
  model.Occupants=new[]{new CheckpointFileModel.OccupantEntry{Id=model.NextEntityId++,DefinitionId="water",Kind=(int)OccupantKind.ElementBlock,X=1,Y=1}};
  File.WriteAllText(path,UnityEngine.JsonUtility.ToJson(model,true));byte[] originalBytes=File.ReadAllBytes(path);
  core.Transactions.TryCommit(new StateMutationBatch{Context=Command(),ResourceDeltas=new[]{new ResourceAmount(ResourceBucket.Hand,"water",2)}});
  string liveBefore=UnityEngine.JsonUtility.ToJson(CheckpointFileModel.FromCheckpoint(core.Store.CaptureCheckpoint(catalog)));
  var read=save.ReadCheckpoint();Assert.AreEqual(ErrorCode.None,read.Error,read.MessageKey);Assert.AreEqual(ContractVersion.Value,read.Data.ContractVersion);
  CollectionAssert.AreEqual(originalBytes,File.ReadAllBytes(path));
  Assert.AreEqual(liveBefore,UnityEngine.JsonUtility.ToJson(CheckpointFileModel.FromCheckpoint(core.Store.CaptureCheckpoint(catalog))));
  model.ContractVersion=ContractVersion.Value;
  Assert.AreEqual(UnityEngine.JsonUtility.ToJson(CheckpointFileModel.FromCheckpoint(model.ToCheckpoint())),
   UnityEngine.JsonUtility.ToJson(CheckpointFileModel.FromCheckpoint(read.Data)),"All checkpoint fields except the contract version must survive unchanged.");
  Assert.AreEqual(ErrorCode.None,core.Store.Restore(read.Data).Error);Assert.AreEqual(ErrorCode.None,save.RestoreHistoryFor(read.Data).Error);
  Assert.AreEqual(3,core.Resources.GetQuantity(ResourceBucket.Hand,"water"));Assert.AreEqual(1,core.Store.GetFlowSnapshot().LastSettledNight);
  CellSnapshot cell;Assert.IsTrue(core.Board.TryGetCell(new CellCoord(1,1),out cell));Assert.AreEqual("water",cell.DefinitionId);
  Assert.AreEqual(original.Revision,core.Transactions.TryCommit(batch).Revision);Assert.AreEqual(3,core.Resources.GetQuantity(ResourceBucket.Hand,"water"));
  batch.ResourceDeltas=new[]{new ResourceAmount(ResourceBucket.Hand,"water",7)};
  Assert.AreEqual(ErrorCode.DuplicateCommand,core.Transactions.TryCommit(batch).Error);
  var second=save.ReadCheckpoint();Assert.AreEqual(ErrorCode.None,second.Error);Assert.AreEqual(3,core.Resources.GetQuantity(ResourceBucket.Hand,"water"));
  CollectionAssert.AreEqual(originalBytes,File.ReadAllBytes(path));
 }
 [TestCase("2.0.0",1)] [TestCase("2.3.0",1)] [TestCase("unknown",1)] [TestCase("2.1.0",99)]
 public void LegacyMigrationRejectsUnlistedContractOrSaveVersions(string contract,int version)
 {
  Directory.CreateDirectory(directory);var model=CheckpointFileModel.FromCheckpoint(core.Store.CaptureCheckpoint(catalog));
  model.ContractVersion=contract;model.SaveVersion=version;string path=Path.Combine(directory,"checkpoint.json");
  File.WriteAllText(path,UnityEngine.JsonUtility.ToJson(model));byte[] original=File.ReadAllBytes(path);
  Assert.AreEqual(ErrorCode.SaveVersionUnsupported,save.ReadCheckpoint().Error);CollectionAssert.AreEqual(original,File.ReadAllBytes(path));
 }
 [Test]public void LegacyMigrationRetainsConfigurationAndCorruptionChecks()
 {
  Directory.CreateDirectory(directory);var model=CheckpointFileModel.FromCheckpoint(core.Store.CaptureCheckpoint(catalog));
  model.ContractVersion="2.1.0";model.ConfigVersion="different";string path=Path.Combine(directory,"checkpoint.json");
  File.WriteAllText(path,UnityEngine.JsonUtility.ToJson(model));Assert.AreEqual(ErrorCode.ConfigMismatch,save.ReadCheckpoint().Error);
  model.ConfigVersion=catalog.GetSettings().ConfigVersion;model.RandomStates[0].State=0;
  File.WriteAllText(path,UnityEngine.JsonUtility.ToJson(model));Assert.AreEqual(ErrorCode.SaveCorrupt,save.ReadCheckpoint().Error);
 }
 sealed class Catalog:IGameCatalog {
  public GameSettings GetSettings(){return new GameSettings{ConfigVersion="test",StoryDays=7,SpringMaxHp=100};}public bool TryGetLevel(string id,out LevelDefinition v){v=new LevelDefinition{Id="level",Rows=3,Columns=3,CellSize=1,SpringCell=new CellCoord(0,0),SpawnPoints=new SpawnPointDefinition[0],Paths=new PathDefinition[0],InitialOccupants=new InitialOccupant[0],InitialResources=new ResourceAmount[0],ReservedCells=new CellCoord[0]};return id=="level";}public bool TryGetElement(string id,out ElementDefinition v){v=new ElementDefinition{Id="water"};return id=="water";}
  public bool TryGetTower(string id,out TowerDefinition v){v=null;return false;}public bool TryGetProjectile(string id,out ProjectileDefinition v){v=null;return false;}public bool TryGetStatus(string id,out StatusDefinition v){v=null;return false;}public bool TryGetEnemy(string id,out EnemyDefinition v){v=null;return false;}public bool TryGetItem(string id,out ItemDefinition v){v=null;return false;}public bool TryGetResource(string id,out ResourceDefinition v){v=null;return false;}public bool TryGetEvent(string id,out DayEventDefinition v){v=null;return false;}public bool TryGetBossSkill(string id,out BossSkillDefinition v){v=null;return false;}public bool TryGetDropTable(string id,out DropTableDefinition v){v=null;return false;}public bool TryGetSkill(string id,out SpecialSkillDefinition v){v=null;return false;}public EndlessDefinition GetEndlessDefinition(){return null;}public IReadOnlyList<ProductionRuleDefinition> GetProductionRules(){return new ProductionRuleDefinition[0];}public IReadOnlyList<ReactionDefinition> GetReactionRules(ReactionContext c){return new ReactionDefinition[0];}public IReadOnlyList<GaugeDefinition> GetGaugeDefinitions(){return new GaugeDefinition[0];}public IReadOnlyList<DayEventDefinition> GetDayEvents(){return new DayEventDefinition[0];}public IReadOnlyList<WaveDefinition> GetWaves(int d,GameMode m){return new WaveDefinition[0];}public IReadOnlyList<string> ValidateAll(){return new string[0];}
 }
}
}




