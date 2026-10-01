using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class UnitActionCacheTest
{
  private sealed class TestActionDefinition(UnitActionCondition condition) : UnitActionDefinition
  {
    private readonly IReadOnlyList<UnitActionCondition> _conditions = [condition];

    internal override bool ExistsFor(BattleUnitState unit) => true;
    internal override IReadOnlyList<UnitActionCondition> Conditions => _conditions;
  }

  private sealed class CountingCondition : UnitActionCondition
  {
    public int Calls { get; private set; }

    internal override bool IsMet(BattleReadContext context, AliveUnit unit)
    {
      Calls++;
      return true;
    }

    internal override bool MayChange(BattleEvent battleEvent, BattleUnitState unit)
      => battleEvent is UnitMovedBattleEvent moved && moved.Unit == unit;
  }

  private sealed class ThrowingCondition(Exception failure) : UnitActionCondition
  {
    public int Calls { get; private set; }
    public bool ShouldThrow { get; set; } = true;

    internal override bool IsMet(BattleReadContext context, AliveUnit unit)
    {
      Calls++;
      if (ShouldThrow)
        throw failure;
      return true;
    }

    internal override bool MayChange(BattleEvent battleEvent, BattleUnitState unit) => false;
  }

  private sealed class SpendThenThrow(BattleUnitState unit, Exception failure) : BattleAction
  {
    public override Result Execute(BattleSession session)
    {
      unit.SpendActionPoints(1);
      throw failure;
    }
  }

  private sealed class SpendThenReject(BattleUnitState unit) : BattleAction
  {
    public override Result Execute(BattleSession session)
    {
      unit.SpendActionPoints(1);
      return Result.Rejected;
    }
  }

  private sealed class RejectingInterruptOnce(BattleUnitState unit) : BattleHook
  {
    private bool _spent;

    public override bool NeedsToUnregister => _spent;

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      _spent = true;
      return [new SpendThenReject(unit)];
    }
  }

  private sealed class InterruptOnce(BattleAction action) : BattleHook
  {
    private bool _spent;

    public override bool NeedsToUnregister => _spent;

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      _spent = true;
      return [action];
    }
  }

  private sealed class DamageUnitOnce(BattleUnitState target, int amount, DamageKind kind) : BattleHook
  {
    private bool _spent;

    public override bool NeedsToUnregister => _spent;

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      _spent = true;
      return context.Read.State.TryGetAlive(target).Match(
        alive => (IReadOnlyList<BattleAction>)[BattleAction.ApplyDamage(alive, amount, kind)],
        () => []);
    }
  }

  private static UnitAction Row<TDefinition>(IReadOnlyList<UnitAction> actions)
    where TDefinition : UnitActionDefinition =>
    actions.AsValueEnumerable().Single(action => action.Action is TDefinition);

  private static void EvaluateAll(IReadOnlyList<UnitAction> actions)
  {
    foreach (UnitAction action in actions)
      _ = action.IsAvailable;
  }

  private static int MaterializedSetCount(UnitActionCache cache)
  {
    var field = typeof(UnitActionCache).GetField("_entries",
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    var entries = (System.Collections.IDictionary)field.GetValue(cache);
    return entries.Count;
  }

  [TestCase]
  public void RetainedQueryListAndEntriesSurviveApSpendAndRefresh()
  {
    using var battle = BattleFixture.Duel();
    var query = new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit));

    IReadOnlyList<UnitAction> actions = battle.Query(query);
    UnitAction move = Row<MoveActionDefinition>(actions);
    IReadOnlyList<UnitAction> repeated = battle.Query(query);

    Assert.True(ReferenceEquals(actions, repeated));
    Assert.True(ReferenceEquals(move, Row<MoveActionDefinition>(repeated)));
    Assert.True(ReferenceEquals(battle.PlayerUnit, move.Unit));
    Assert.True(move.IsAvailable);

    battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2)], actionPointCost: 4);

    IReadOnlyList<UnitAction> afterSpend = battle.Query(query);
    Assert.True(ReferenceEquals(actions, afterSpend));
    Assert.True(ReferenceEquals(move, Row<MoveActionDefinition>(afterSpend)));
    Assert.False(move.IsAvailable);

    battle.AdvanceTurn();
    battle.AdvanceTurn();

    IReadOnlyList<UnitAction> afterRefresh = battle.Query(query);
    Assert.True(ReferenceEquals(actions, afterRefresh));
    Assert.True(ReferenceEquals(move, Row<MoveActionDefinition>(afterRefresh)));
    Assert.Equal(4, battle.PlayerUnit.CurrentActionPoints);
    Assert.True(move.IsAvailable);
  }

  [TestCase]
  public void AttackAndReloadTrackRetainedMagazineState()
  {
    var weapon = TestData.MakeAmmoWeapon("Pistol", magazine: 1);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Weapon: weapon),
      enemy: new("Durable", Health: 100),
      hitChanceCalculator: new AlwaysHitCalculator());
    IReadOnlyList<UnitAction> actions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    UnitAction attack = Row<AttackActionDefinition>(actions);
    UnitAction reload = Row<ReloadActionDefinition>(actions);
    Assert.True(attack.IsAvailable);
    Assert.False(reload.IsAvailable);

    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    Assert.False(attack.IsAvailable);
    Assert.True(reload.IsAvailable);

    battle.Reload(battle.PlayerUnit, weapon);

    Assert.True(attack.IsAvailable);
    Assert.False(reload.IsAvailable);
  }

  [TestCase]
  public void UseAndThrowSpendingLastActionPointDisableActorVerbs()
  {
    using var useBattle = BattleFixture.Duel(player: new("Alpha", ActionPoints: 1, Weapon: TestData.MakeWeapon("Rifle")));
    ItemWith<ChargesCapability> usable = TestData.MakeUsableItem("Medkit");
    useBattle.PlayerUnit.AddInventoryItem(usable.Item);
    IReadOnlyList<UnitAction> useActions = useBattle.Query(
      new GetAvailableActionsForUnit(useBattle.Alive(useBattle.PlayerUnit)));
    EvaluateAll(useActions);

    useBattle.Use(useBattle.PlayerUnit, usable);

    Assert.False(Row<MoveActionDefinition>(useActions).IsAvailable);
    Assert.False(Row<AttackActionDefinition>(useActions).IsAvailable);
    Assert.False(Row<PassActionDefinition>(useActions).IsAvailable);

    using var throwBattle = BattleFixture.Duel(player: new("Alpha", ActionPoints: 1, Weapon: TestData.MakeWeapon("Rifle")));
    ItemWith<ThrowableCapability> throwable = TestData.MakeThrowable("Rock");
    throwBattle.PlayerUnit.AddInventoryItem(throwable.Item);
    IReadOnlyList<UnitAction> throwActions = throwBattle.Query(
      new GetAvailableActionsForUnit(throwBattle.Alive(throwBattle.PlayerUnit)));
    EvaluateAll(throwActions);

    throwBattle.Throw(throwBattle.PlayerUnit, throwable, new Vector3I(4, 0, 2));

    Assert.False(Row<MoveActionDefinition>(throwActions).IsAvailable);
    Assert.False(Row<AttackActionDefinition>(throwActions).IsAvailable);
    Assert.False(Row<PassActionDefinition>(throwActions).IsAvailable);
  }

  [TestCase]
  public void PassDisablesOnlyPassedActorsVerbs()
  {
    using var battle = BattleFixture.UiBattle();
    IReadOnlyList<UnitAction> actions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    EvaluateAll(actions);

    battle.Pass(battle.PlayerUnit);

    Assert.False(Row<MoveActionDefinition>(actions).IsAvailable);
    Assert.False(Row<AttackActionDefinition>(actions).IsAvailable);
    Assert.False(Row<PassActionDefinition>(actions).IsAvailable);
    Assert.True(Row<EndTurnActionDefinition>(actions).IsAvailable);
  }

  [TestCase]
  public void ActionPointBuffRefreshesRetainedAvailabilityAfterTurnStartedRead()
  {
    var apBuff = TestData.MakeBuff(
      "Adrenaline",
      new HealthBelowPercentCondition { Percent = 50f },
      statMods: [new ActionPointsStatMod { Modifiers = [StatModifier.Add(2)] }]);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", ActionPoints: 0, Weapon: TestData.MakeWeapon("Rifle"), Buffs: [apBuff]),
      enemy: new("Durable", Health: 100));
    IReadOnlyList<UnitAction> actions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    UnitAction attack = Row<AttackActionDefinition>(actions);
    Assert.False(attack.IsAvailable);

    battle.EndFactionTurn(battle.PlayerFaction);
    battle.ApplyDamage(battle.PlayerUnit, 11);
    Assert.False(attack.IsAvailable);
    Assert.False(attack.IsDirty);

    bool attackAtTurnStarted = true;
    int actionPointsAtTurnStarted = -1;
    battle.Runtime.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is not TurnStartedBattleEvent started || started.Faction != battle.PlayerFaction)
        return;
      actionPointsAtTurnStarted = battle.PlayerUnit.CurrentActionPoints;
      attackAtTurnStarted = attack.IsAvailable;
      Assert.True(attack.IsDirty);
    };

    battle.EndFactionTurn(battle.EnemyFaction);

    Assert.Equal(0, actionPointsAtTurnStarted);
    Assert.False(attackAtTurnStarted);
    Assert.Equal(2, battle.PlayerUnit.MaxActionPoints);
    Assert.Equal(2, battle.PlayerUnit.CurrentActionPoints);
    Assert.True(attack.IsAvailable);
    Assert.False(attack.IsDirty);
  }

  [TestCase]
  public void BuffClampIncapacitatesUnselectedUnitAndUpdatesRetainedOptions()
  {
    var collapse = TestData.MakeBuff(
      "Collapse",
      new HealthBelowPercentCondition { Percent = 50f },
      statMods: [new HealthStatMod { Modifiers = [StatModifier.Add(-15)] }]);
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    battle.Spawn(TestData.MakeCombatant("Lead", playerFaction), new Vector3I(1, 0, 1));
    BattleUnitState support = battle.Spawn(
      TestData.MakeCombatant("Support", playerFaction, buffs: [collapse]), new Vector3I(2, 0, 1));
    battle.Spawn(TestData.MakeCombatant("Durable", enemyFaction, health: 100), new Vector3I(1, 0, 5));
    battle.Start();
    IReadOnlyList<UnitAction> actions = battle.Query(new GetAvailableActionsForUnit(battle.Alive(support)));
    UnitAction move = Row<MoveActionDefinition>(actions);
    UnitAction endTurn = Row<EndTurnActionDefinition>(actions);
    Assert.True(move.IsAvailable);
    Assert.True(endTurn.IsAvailable);

    battle.EndFactionTurn(playerFaction);
    battle.ApplyDamage(support, 11);
    battle.ApplyDamage(support, 8, DamageKind.Stun);
    Assert.False(support.IsIncapacitated);
    Assert.False(move.IsAvailable);
    Assert.False(endTurn.IsAvailable);

    bool incapacitatedAtTurnStarted = true;
    bool moveAtTurnStarted = false;
    bool endTurnAtTurnStarted = false;
    battle.Runtime.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is not TurnStartedBattleEvent started || started.Faction != playerFaction)
        return;
      incapacitatedAtTurnStarted = support.IsIncapacitated;
      moveAtTurnStarted = move.IsAvailable;
      endTurnAtTurnStarted = endTurn.IsAvailable;
    };

    battle.EndFactionTurn(enemyFaction);

    Assert.False(incapacitatedAtTurnStarted);
    Assert.True(moveAtTurnStarted);
    Assert.True(endTurnAtTurnStarted);
    Assert.Equal(5, support.MaxHealth);
    Assert.Equal(5, support.CurrentHealth);
    Assert.True(support.IsUnconscious);
    Assert.False(move.IsAvailable);
    Assert.False(endTurn.IsAvailable);
  }

  [TestCase]
  public void StatusUnconsciousnessAndDeathDisableEveryRetainedOption()
  {
    using var statusBattle = BattleFixture.UiBattle();
    IReadOnlyList<UnitAction> statusActions = statusBattle.Query(
      new GetAvailableActionsForUnit(statusBattle.Alive(statusBattle.PlayerUnit)));
    EvaluateAll(statusActions);
    statusBattle.Session.ApplyStatusEffectTo(statusBattle.PlayerUnit, TestData.MakeStun());
    Assert.True(statusActions.AsValueEnumerable().All(action => !action.IsAvailable));

    using var unconsciousBattle = BattleFixture.UiBattle();
    IReadOnlyList<UnitAction> unconsciousActions = unconsciousBattle.Query(
      new GetAvailableActionsForUnit(unconsciousBattle.Alive(unconsciousBattle.PlayerUnit)));
    EvaluateAll(unconsciousActions);
    unconsciousBattle.ApplyDamage(unconsciousBattle.PlayerUnit, 20, DamageKind.Stun);
    Assert.True(unconsciousActions.AsValueEnumerable().All(action => !action.IsAvailable));

    using var deadBattle = BattleFixture.UiBattle();
    IReadOnlyList<UnitAction> deadActions = deadBattle.Query(
      new GetAvailableActionsForUnit(deadBattle.Alive(deadBattle.PlayerUnit)));
    EvaluateAll(deadActions);
    deadBattle.ApplyDamage(deadBattle.PlayerUnit, 999);
    Assert.True(deadActions.AsValueEnumerable().All(action => !action.IsAvailable));
  }

  [TestCase]
  public void ImmobilizationExpiryUpdatesRetainedOptionsDuringUpkeepAndNextTurn()
  {
    using var battle = BattleFixture.Duel(enemy: new("Durable", Health: 100));
    IReadOnlyList<UnitAction> actions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    UnitAction move = Row<MoveActionDefinition>(actions);
    UnitAction endTurn = Row<EndTurnActionDefinition>(actions);
    Assert.True(move.IsAvailable);
    Assert.True(endTurn.IsAvailable);

    battle.Session.ApplyStatusEffectTo(battle.PlayerUnit, TestData.MakeStun(duration: 1));

    Assert.False(move.IsAvailable);
    Assert.False(endTurn.IsAvailable);

    bool observedExpiry = false;
    bool moveAtExpiry = false;
    bool endTurnAtExpiry = false;
    battle.Runtime.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is not UnitStatusEffectExpiredBattleEvent expired || expired.Unit != battle.PlayerUnit)
        return;
      observedExpiry = true;
      moveAtExpiry = move.IsAvailable;
      endTurnAtExpiry = endTurn.IsAvailable;
      Assert.True(move.IsDirty);
      Assert.True(endTurn.IsDirty);
    };

    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.True(observedExpiry);
    Assert.True(moveAtExpiry);
    Assert.True(endTurnAtExpiry);
    Assert.False(battle.PlayerUnit.IsImmobilized);
    Assert.False(move.IsAvailable);
    Assert.False(endTurn.IsAvailable);

    battle.EndFactionTurn(battle.EnemyFaction);

    Assert.True(move.IsAvailable);
    Assert.True(endTurn.IsAvailable);
  }

  [TestCase]
  public void AdjacentOccupancyChangesTargetingWithoutDirtyingObserverEntries()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = BattleFixture.Started(new Vector3I(5, 1, 1),
      new UnitPlacement(
        new UnitLoadout(TestData.MakeCombatant("Observer", player), TestData.MakeWeapon("Rifle")),
        new Vector3I(1, 0, 0)),
      new UnitPlacement(
        new UnitLoadout(TestData.MakeCombatant("Ally", player)),
        new Vector3I(2, 0, 0)),
      new UnitPlacement(
        new UnitLoadout(TestData.MakeCombatant("Enemy", enemy)),
        new Vector3I(4, 0, 0)));
    BattleUnitState observer = battle.UnitAt(new Vector3I(1, 0, 0));
    BattleUnitState ally = battle.UnitAt(new Vector3I(2, 0, 0));
    IReadOnlyList<UnitAction> actions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(observer)));
    UnitAction move = Row<MoveActionDefinition>(actions);
    UnitAction attack = Row<AttackActionDefinition>(actions);
    _ = move.IsAvailable;
    _ = attack.IsAvailable;
    var targeting = new MoveTargeting(battle.Runtime, observer);

    Assert.False(targeting.Begin().AsValueEnumerable().Contains(new Vector3I(2, 0, 0)));

    battle.Move(ally, [new Vector3I(3, 0, 0)]);

    Assert.False(move.IsDirty);
    Assert.False(attack.IsDirty);
    Assert.True(targeting.Begin().AsValueEnumerable().Contains(new Vector3I(2, 0, 0)));

    battle.Move(ally, [new Vector3I(2, 0, 0)]);

    Assert.False(move.IsDirty);
    Assert.False(attack.IsDirty);
    Assert.False(targeting.Begin().AsValueEnumerable().Contains(new Vector3I(2, 0, 0)));
    Assert.True(ReferenceEquals(actions, battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(observer)))));
  }

  [TestCase]
  public void EnemyRangeAndSightChangesTargetingWithoutDirtyingObserverEntries()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var weapon = TestData.MakeWeapon("Rifle", range: 2);
    using var battle = BattleFixture.Started(new Vector3I(6, 1, 1),
      new UnitPlacement(
        new UnitLoadout(TestData.MakeCombatant("Observer", player, vision: 3), weapon),
        new Vector3I(1, 0, 0)),
      new UnitPlacement(
        new UnitLoadout(TestData.MakeCombatant("Enemy", enemy)),
        new Vector3I(3, 0, 0)));
    BattleUnitState observer = battle.UnitAt(new Vector3I(1, 0, 0));
    BattleUnitState target = battle.UnitAt(new Vector3I(3, 0, 0));
    var targeting = new AttackTargeting(battle.Runtime, observer);

    Assert.True(targeting.Begin().AsValueEnumerable().Contains(new Vector3I(3, 0, 0)));
    Assert.Equal(1, battle.Events.EventsOf<UnitSpottedBattleEvent>().AsValueEnumerable()
      .Count(e => ReferenceEquals(e.Unit, observer) && ReferenceEquals(e.Target, target)));

    battle.EndFactionTurn(player);
    IReadOnlyList<UnitAction> actions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(observer)));
    UnitAction move = Row<MoveActionDefinition>(actions);
    UnitAction attack = Row<AttackActionDefinition>(actions);
    _ = move.IsAvailable;
    _ = attack.IsAvailable;

    battle.Move(target, [new Vector3I(4, 0, 0)]);

    Assert.True(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));
    Assert.False(move.IsDirty);
    Assert.False(attack.IsDirty);
    Assert.False(targeting.Begin().AsValueEnumerable().Contains(new Vector3I(4, 0, 0)));

    battle.Move(target, [new Vector3I(5, 0, 0)]);

    Assert.False(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));
    Assert.False(move.IsDirty);
    Assert.False(attack.IsDirty);
    Assert.False(targeting.Begin().AsValueEnumerable().Contains(new Vector3I(5, 0, 0)));
    Assert.Equal(1, battle.Events.EventsOf<UnitSpottedBattleEvent>().AsValueEnumerable()
      .Count(e => ReferenceEquals(e.Unit, observer) && ReferenceEquals(e.Target, target)));

    battle.Move(target, [new Vector3I(4, 0, 0), new Vector3I(3, 0, 0)]);

    Assert.Equal(0, target.CurrentActionPoints);
    Assert.True(targeting.Begin().AsValueEnumerable().Contains(new Vector3I(3, 0, 0)));
    Assert.False(move.IsDirty);
    Assert.False(attack.IsDirty);
    Assert.Equal(1, battle.Events.EventsOf<UnitSpottedBattleEvent>().AsValueEnumerable()
      .Count(e => ReferenceEquals(e.Unit, observer) && ReferenceEquals(e.Target, target)));
    Assert.True(ReferenceEquals(actions, battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(observer)))));
  }

  [TestCase]
  public void SameActorMovementDirtiesOnlyActionPointDependentEntries()
  {
    using var battle = BattleFixture.UiBattle();
    IReadOnlyList<UnitAction> actions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    EvaluateAll(actions);

    battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2)]);

    Assert.True(Row<MoveActionDefinition>(actions).IsDirty);
    Assert.True(Row<AttackActionDefinition>(actions).IsDirty);
    Assert.True(Row<PassActionDefinition>(actions).IsDirty);
    Assert.False(Row<EndTurnActionDefinition>(actions).IsDirty);
  }

  [TestCase]
  public void SharedWeaponEventsInvalidateEveryHoldersWeaponEntries()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var shared = TestData.MakeAmmoWeapon("Shared", magazine: 1);
    using var battle = BattleFixture.Started(new Vector3I(8, 1, 8),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Actor", player), shared), new Vector3I(1, 0, 1)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Holder", player), shared), new Vector3I(2, 0, 1)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Durable", enemy, health: 100)), new Vector3I(1, 0, 4)));
    BattleUnitState actor = battle.UnitAt(new Vector3I(1, 0, 1));
    BattleUnitState holder = battle.UnitAt(new Vector3I(2, 0, 1));
    BattleUnitState target = battle.UnitAt(new Vector3I(1, 0, 4));
    IReadOnlyList<UnitAction> holderActions = battle.Query(new GetAvailableActionsForUnit(battle.Alive(holder)));
    UnitAction attack = Row<AttackActionDefinition>(holderActions);
    UnitAction reload = Row<ReloadActionDefinition>(holderActions);
    _ = attack.IsAvailable;
    _ = reload.IsAvailable;

    battle.Attack(actor, target);

    Assert.True(attack.IsDirty);
    Assert.True(reload.IsDirty);
    Assert.False(attack.IsAvailable);
    Assert.True(reload.IsAvailable);

    battle.Reload(actor, shared);

    Assert.True(attack.IsDirty);
    Assert.True(reload.IsDirty);
    Assert.True(attack.IsAvailable);
    Assert.False(reload.IsAvailable);
  }

  [TestCase]
  public void KnownIrrelevantEventsLeaveMaterializedOptionsClean()
  {
    using var battle = BattleFixture.UiBattle();
    IReadOnlyList<UnitAction> playerActions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    IReadOnlyList<UnitAction> supportActions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.SupportUnit)));
    EvaluateAll(playerActions);
    EvaluateAll(supportActions);
    Assert.Equal(2, MaterializedSetCount(battle.Session.ActionOptions));

    battle.Session.RaiseEvents(new UnitArmorRegeneratedBattleEvent(battle.PlayerUnit, 1, 1));

    Assert.True(playerActions.AsValueEnumerable().All(action => !action.IsDirty));
    Assert.True(supportActions.AsValueEnumerable().All(action => !action.IsDirty));
  }

  [TestCase]
  public void ReadsDuringTurnStartedObservePreRefreshWithoutCaching()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    BattleUnitState player = battle.Spawn(TestData.MakeCombatant("Player", playerFaction), new Vector3I(1, 0, 1));
    BattleUnitState support = battle.Spawn(TestData.MakeCombatant("Support", playerFaction), new Vector3I(2, 0, 1));
    battle.Spawn(TestData.MakeCombatant("Enemy", enemyFaction), new Vector3I(1, 0, 5));
    // A session-start hook drains the two player units' AP before the turn-start dispatch,
    // standing in for pre-refresh state the turn-start reads must observe without caching.
    battle.RegisterHook<SessionStartedBattleEvent>(new SpendActionPointsHook([player, support]));
    IReadOnlyList<UnitAction> createdDuringCallback = null;
    bool existingDuringCallback = true;
    bool createdDuringCallbackValue = true;
    UnitAction existingMove = null;
    battle.OnCommitted(battleEvent =>
    {
      if (battleEvent is SessionStartedBattleEvent)
      {
        IReadOnlyList<UnitAction> existing = battle.Query(
          new GetAvailableActionsForUnit(battle.Alive(player)));
        existingMove = Row<MoveActionDefinition>(existing);
        Assert.False(existingMove.IsAvailable);
        return;
      }
      if (battleEvent is not TurnStartedBattleEvent)
        return;
      existingDuringCallback = existingMove.IsAvailable;
      createdDuringCallback = battle.Query(
        new GetAvailableActionsForUnit(battle.Alive(support)));
      createdDuringCallbackValue = Row<MoveActionDefinition>(createdDuringCallback).IsAvailable;
      Assert.True(existingMove.IsDirty);
      Assert.True(Row<MoveActionDefinition>(createdDuringCallback).IsDirty);
    });

    battle.Start();

    Assert.False(existingDuringCallback);
    Assert.False(createdDuringCallbackValue);
    Assert.True(existingMove.IsAvailable);
    Assert.True(Row<MoveActionDefinition>(createdDuringCallback).IsAvailable);
    Assert.False(existingMove.IsDirty);
    Assert.False(Row<MoveActionDefinition>(createdDuringCallback).IsDirty);
  }

  private sealed class SpendActionPointsHook(IReadOnlyList<BattleUnitState> units) : BattleHook<SessionStartedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, SessionStartedBattleEvent evt)
    {
      foreach (BattleUnitState unit in units)
        unit.TrySpendActionPoints(unit.CurrentActionPoints);
      return [];
    }
  }

  [TestCase]
  public void MoveInterruptIncapacitationUpdatesOtherActorsRetainedEntries()
  {
    using var battle = BattleFixture.UiBattle();
    IReadOnlyList<UnitAction> supportActions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.SupportUnit)));
    EvaluateAll(supportActions);
    battle.RegisterHook<UnitMovedBattleEvent>(
      new DamageUnitOnce(battle.SupportUnit, 20, DamageKind.Stun));

    battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2)]);

    Assert.True(battle.SupportUnit.IsUnconscious);
    Assert.True(supportActions.AsValueEnumerable().All(action => !action.IsAvailable));
  }

  [TestCase]
  public void RejectingMoveInterruptInvalidatesRetainedEntriesAndClearsQueue()
  {
    using var battle = BattleFixture.Duel(
      player: new("Alpha", ActionPoints: 2),
      enemy: new("Durable", Health: 100));
    IReadOnlyList<UnitAction> playerActions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    IReadOnlyList<UnitAction> enemyActions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.EnemyUnit)));
    EvaluateAll(playerActions);
    EvaluateAll(enemyActions);
    battle.RegisterHook<UnitMovedBattleEvent>(new RejectingInterruptOnce(battle.PlayerUnit));
    battle.ClearEvents();

    Assert.Throws<InvalidOperationException>(() =>
      battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2), new Vector3I(4, 0, 3)]));

    Assert.Equal(new Vector3I(4, 0, 2), battle.PositionOf(battle.PlayerUnit).RequireSome().Raw);
    Assert.Equal(0, battle.PlayerUnit.CurrentActionPoints);
    Assert.Equal(1, battle.Events.EventsOf<UnitMovedBattleEvent>().AsValueEnumerable().Count());
    Assert.Equal(1, battle.Events.EventsOf<TileOccupiedBattleEvent>().AsValueEnumerable().Count());
    Assert.True(playerActions.AsValueEnumerable().All(action => action.IsDirty));
    Assert.True(enemyActions.AsValueEnumerable().All(action => action.IsDirty));
    Assert.False(battle.Session.ActionOptions.IsExecuting);
    Assert.False(Row<MoveActionDefinition>(playerActions).IsAvailable);
    Assert.False(Row<PassActionDefinition>(playerActions).IsAvailable);
    Assert.True(Row<EndTurnActionDefinition>(playerActions).IsAvailable);
    Assert.True(enemyActions.AsValueEnumerable().All(action => !action.IsAvailable));

    battle.Pass(battle.PlayerUnit);

    Assert.Equal(new Vector3I(4, 0, 2), battle.PositionOf(battle.PlayerUnit).RequireSome().Raw);
    Assert.Equal(1, battle.Events.EventsOf<UnitMovedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase]
  public void FailingMoveInterruptPreservesFailureAndCommittedStateAndClearsQueue()
  {
    using var battle = BattleFixture.Duel(player: new("Alpha", ActionPoints: 2));
    IReadOnlyList<UnitAction> actions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    EvaluateAll(actions);
    var failure = new ApplicationException("interrupt failed");
    battle.RegisterHook<UnitMovedBattleEvent>(new InterruptOnce(new SpendThenThrow(battle.PlayerUnit, failure)));

    Exception caught = null;
    try
    {
      battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2), new Vector3I(4, 0, 3)]);
    }
    catch (Exception exception)
    {
      caught = exception;
    }

    Assert.True(ReferenceEquals(failure, caught));
    Assert.Equal(new Vector3I(4, 0, 2), battle.PositionOf(battle.PlayerUnit).RequireSome().Raw);
    Assert.Equal(0, battle.PlayerUnit.CurrentActionPoints);
    Assert.False(Row<MoveActionDefinition>(actions).IsAvailable);

    battle.Submit(BattleAction.PassUnit(battle.Alive(battle.PlayerUnit)));
    Assert.Equal(new Vector3I(4, 0, 2), battle.PositionOf(battle.PlayerUnit).RequireSome().Raw);
  }

  [TestCase]
  public void EventlessFailureInvalidatesEveryMaterializedEntry()
  {
    using var battle = BattleFixture.Duel(player: new("Alpha", ActionPoints: 1));
    IReadOnlyList<UnitAction> playerActions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    IReadOnlyList<UnitAction> enemyActions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.EnemyUnit)));
    EvaluateAll(playerActions);
    EvaluateAll(enemyActions);
    var failure = new ApplicationException("eventless failure");

    Exception caught = null;
    try
    {
      battle.Submit(new SpendThenThrow(battle.PlayerUnit, failure));
    }
    catch (Exception exception)
    {
      caught = exception;
    }

    Assert.True(ReferenceEquals(failure, caught));
    Assert.True(playerActions.AsValueEnumerable().All(action => action.IsDirty));
    Assert.True(enemyActions.AsValueEnumerable().All(action => action.IsDirty));
    Assert.False(Row<MoveActionDefinition>(playerActions).IsAvailable);
    Assert.False(Row<PassActionDefinition>(playerActions).IsAvailable);
    Assert.True(Row<EndTurnActionDefinition>(playerActions).IsAvailable);
  }

  [TestCase]
  public void CleanReadsEvaluateOnceAndOnlyRelevantEventsDirtyTheEntry()
  {
    using var battle = BattleFixture.Duel();
    var condition = new CountingCondition();
    var action = new UnitAction(battle.Session.ActionOptions, battle.PlayerUnit,
      new TestActionDefinition(condition));

    Assert.True(action.IsAvailable);
    Assert.True(action.IsAvailable);
    Assert.Equal(1, condition.Calls);

    action.Invalidate(new UnitSpottedBattleEvent(battle.PlayerUnit, battle.EnemyUnit));
    Assert.True(action.IsAvailable);
    Assert.Equal(1, condition.Calls);

    action.Invalidate(new UnitMovedBattleEvent(battle.PlayerUnit, battle.At(4, 0, 2), battle.At(4, 0, 1)));
    Assert.True(action.IsAvailable);
    Assert.Equal(2, condition.Calls);
  }

  [TestCase]
  public void EvaluationFailureLeavesEntryDirtyForRetry()
  {
    using var battle = BattleFixture.Duel();
    var failure = new ApplicationException("condition failed");
    var condition = new ThrowingCondition(failure);
    var action = new UnitAction(battle.Session.ActionOptions, battle.PlayerUnit,
      new TestActionDefinition(condition));

    Exception caught = null;
    try
    {
      _ = action.IsAvailable;
    }
    catch (Exception exception)
    {
      caught = exception;
    }

    Assert.True(ReferenceEquals(failure, caught));
    Assert.True(action.IsDirty);
    condition.ShouldThrow = false;
    Assert.True(action.IsAvailable);
    Assert.False(action.IsDirty);
    Assert.True(action.IsAvailable);
    Assert.Equal(2, condition.Calls);
  }
}
