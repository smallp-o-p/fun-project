using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Combatants;
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

    internal override bool IsMet(BattleSession session, AliveUnit unit)
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

    internal override bool IsMet(BattleSession session, AliveUnit unit)
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
      return context.Session.TryGetAlive(target).Match(
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

  [TestCase]
  public void RetainedQueryListAndEntriesSurviveApSpendAndRefresh()
  {
    using var battle = BattleFixture.Duel();
    var query = new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit));

    IReadOnlyList<UnitAction> actions = battle.Query(query);
    UnitAction move = Row<MoveActionDefinition>(actions);
    IReadOnlyList<UnitAction> repeated = battle.Query(query);

    battle.Read(() => Assert.True(ReferenceEquals(actions, repeated)));
    battle.Read(() => Assert.True(ReferenceEquals(move, Row<MoveActionDefinition>(repeated))));
    battle.Read(() => Assert.True(ReferenceEquals(battle.PlayerUnit, move.Unit)));
    battle.Read(() => Assert.True(move.IsAvailable));

    battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2)], actionPointCost: 4);

    IReadOnlyList<UnitAction> afterSpend = battle.Query(query);
    battle.Read(() => Assert.True(ReferenceEquals(actions, afterSpend)));
    battle.Read(() => Assert.True(ReferenceEquals(move, Row<MoveActionDefinition>(afterSpend))));
    battle.Read(() => Assert.False(move.IsAvailable));

    battle.AdvanceTurn();
    battle.AdvanceTurn();

    IReadOnlyList<UnitAction> afterRefresh = battle.Query(query);
    battle.Read(() => Assert.True(ReferenceEquals(actions, afterRefresh)));
    battle.Read(() => Assert.True(ReferenceEquals(move, Row<MoveActionDefinition>(afterRefresh))));
    battle.Read(() => Assert.Equal(4, battle.PlayerUnit.CurrentActionPoints));
    battle.Read(() => Assert.True(move.IsAvailable));
  }

  [TestCase]
  public void CompletionDisablesRetainedOptions()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    var query = new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit));
    IReadOnlyList<UnitAction> actions = battle.Query(query);
    battle.Read(() => EvaluateAll(actions));

    battle.ApplyDamage(battle.EnemyUnit, 999); // the player's eliminate-all objective wins the battle

    battle.Read(() => Assert.False(battle.Query(new CanUnitActNow(battle.PlayerUnit))));
    foreach (UnitAction action in actions)
      battle.Read(() => Assert.False(action.IsAvailable));

    IReadOnlyList<UnitAction> afterWin = battle.Query(query);
    battle.Read(() => Assert.True(ReferenceEquals(actions, afterWin)));
    battle.Read(() => Assert.True(ReferenceEquals(Row<MoveActionDefinition>(actions), Row<MoveActionDefinition>(afterWin))));
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
    battle.Read(() => Assert.True(attack.IsAvailable));
    battle.Read(() => Assert.False(reload.IsAvailable));

    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    battle.Read(() => Assert.False(attack.IsAvailable));
    battle.Read(() => Assert.True(reload.IsAvailable));

    battle.Reload(battle.PlayerUnit, weapon);

    battle.Read(() => Assert.True(attack.IsAvailable));
    battle.Read(() => Assert.False(reload.IsAvailable));
  }

  [TestCase]
  public void UseAndThrowSpendingLastActionPointDisableActorVerbs()
  {
    using var useBattle = BattleFixture.Duel(player: new("Alpha", ActionPoints: 1, Weapon: TestData.MakeWeapon("Rifle")));
    ItemWith<ChargesCapability> usable = TestData.MakeUsableItem("Medkit");
    useBattle.Mutate(() => useBattle.PlayerUnit.AddInventoryItem(usable.Item));
    IReadOnlyList<UnitAction> useActions = useBattle.Query(
      new GetAvailableActionsForUnit(useBattle.Alive(useBattle.PlayerUnit)));
    useBattle.Read(() => EvaluateAll(useActions));

    useBattle.Use(useBattle.PlayerUnit, usable);

    useBattle.Read(() => Assert.False(Row<MoveActionDefinition>(useActions).IsAvailable));
    useBattle.Read(() => Assert.False(Row<AttackActionDefinition>(useActions).IsAvailable));
    useBattle.Read(() => Assert.False(Row<PassActionDefinition>(useActions).IsAvailable));

    using var throwBattle = BattleFixture.Duel(player: new("Alpha", ActionPoints: 1, Weapon: TestData.MakeWeapon("Rifle")));
    ItemWith<ThrowableCapability> throwable = TestData.MakeThrowable("Rock");
    throwBattle.Mutate(() => throwBattle.PlayerUnit.AddInventoryItem(throwable.Item));
    IReadOnlyList<UnitAction> throwActions = throwBattle.Query(
      new GetAvailableActionsForUnit(throwBattle.Alive(throwBattle.PlayerUnit)));
    throwBattle.Read(() => EvaluateAll(throwActions));

    throwBattle.Throw(throwBattle.PlayerUnit, throwable, new Vector3I(4, 0, 2));

    throwBattle.Read(() => Assert.False(Row<MoveActionDefinition>(throwActions).IsAvailable));
    throwBattle.Read(() => Assert.False(Row<AttackActionDefinition>(throwActions).IsAvailable));
    throwBattle.Read(() => Assert.False(Row<PassActionDefinition>(throwActions).IsAvailable));
  }

  [TestCase]
  public void PassDisablesOnlyPassedActorsVerbs()
  {
    using var battle = BattleFixture.UiBattle();
    IReadOnlyList<UnitAction> actions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    battle.Read(() => EvaluateAll(actions));

    battle.Pass(battle.PlayerUnit);

    battle.Read(() => Assert.False(Row<MoveActionDefinition>(actions).IsAvailable));
    battle.Read(() => Assert.False(Row<AttackActionDefinition>(actions).IsAvailable));
    battle.Read(() => Assert.False(Row<PassActionDefinition>(actions).IsAvailable));
    battle.Read(() => Assert.True(Row<EndTurnActionDefinition>(actions).IsAvailable));
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
    battle.Read(() => Assert.False(attack.IsAvailable));

    battle.EndFactionTurn(battle.PlayerFaction);
    battle.ApplyDamage(battle.PlayerUnit, 11);
    battle.Read(() => Assert.False(attack.IsAvailable));

    bool attackAtTurnStarted = true;
    int actionPointsAtTurnStarted = -1;
    battle.Runtime.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is not TurnStartedBattleEvent started || started.Faction != battle.PlayerFaction)
        return;
      actionPointsAtTurnStarted = battle.PlayerUnit.CurrentActionPoints;
      attackAtTurnStarted = attack.IsAvailable;
    };

    battle.EndFactionTurn(battle.EnemyFaction);

    battle.Read(() => Assert.Equal(0, actionPointsAtTurnStarted));
    battle.Read(() => Assert.False(attackAtTurnStarted));
    battle.Read(() => Assert.Equal(2, battle.PlayerUnit.MaxActionPoints));
    battle.Read(() => Assert.Equal(2, battle.PlayerUnit.CurrentActionPoints));
    battle.Read(() => Assert.True(attack.IsAvailable));
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
    using var battle = new BattleFixture(new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(8, 1, 8)),
      [
        new BattleSideSetup(playerFaction, [new FakeObjectiveData()],
          [
            new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Lead", playerFaction)), new Vector3I(1, 0, 1)),
            new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Support", playerFaction, buffs: [collapse])), new Vector3I(2, 0, 1)),
          ]),
        new BattleSideSetup(enemyFaction, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Durable", enemyFaction, health: 100)), new Vector3I(1, 0, 5))]),
      ],
      Seed: 7));
    BattleUnitState support = battle.UnitAt(new Vector3I(2, 0, 1));
    IReadOnlyList<UnitAction> actions = battle.Query(new GetAvailableActionsForUnit(battle.Alive(support)));
    UnitAction move = Row<MoveActionDefinition>(actions);
    UnitAction endTurn = Row<EndTurnActionDefinition>(actions);
    battle.Read(() => Assert.True(move.IsAvailable));
    battle.Read(() => Assert.True(endTurn.IsAvailable));

    battle.EndFactionTurn(playerFaction);
    battle.ApplyDamage(support, 11);
    battle.ApplyDamage(support, 8, DamageKind.Stun);
    battle.Read(() => Assert.False(support.IsIncapacitated));
    battle.Read(() => Assert.False(move.IsAvailable));
    battle.Read(() => Assert.False(endTurn.IsAvailable));

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

    battle.Read(() => Assert.False(incapacitatedAtTurnStarted));
    battle.Read(() => Assert.True(moveAtTurnStarted));
    battle.Read(() => Assert.True(endTurnAtTurnStarted));
    battle.Read(() => Assert.Equal(5, support.MaxHealth));
    battle.Read(() => Assert.Equal(5, support.CurrentHealth));
    battle.Read(() => Assert.True(support.IsUnconscious));
    battle.Read(() => Assert.False(move.IsAvailable));
    battle.Read(() => Assert.False(endTurn.IsAvailable));
  }

  [TestCase]
  public void StatusUnconsciousnessAndDeathDisableEveryRetainedOption()
  {
    using var statusBattle = BattleFixture.UiBattle();
    IReadOnlyList<UnitAction> statusActions = statusBattle.Query(
      new GetAvailableActionsForUnit(statusBattle.Alive(statusBattle.PlayerUnit)));
    statusBattle.Read(() => EvaluateAll(statusActions));
    statusBattle.Mutate(() => statusBattle.Session.ApplyStatusEffectTo(statusBattle.PlayerUnit, TestData.MakeStun()));
    statusBattle.Read(() => Assert.True(statusActions.AsValueEnumerable().All(action => !action.IsAvailable)));

    using var unconsciousBattle = BattleFixture.UiBattle();
    IReadOnlyList<UnitAction> unconsciousActions = unconsciousBattle.Query(
      new GetAvailableActionsForUnit(unconsciousBattle.Alive(unconsciousBattle.PlayerUnit)));
    unconsciousBattle.Read(() => EvaluateAll(unconsciousActions));
    unconsciousBattle.ApplyDamage(unconsciousBattle.PlayerUnit, 20, DamageKind.Stun);
    unconsciousBattle.Read(() => Assert.True(unconsciousActions.AsValueEnumerable().All(action => !action.IsAvailable)));

    using var deadBattle = BattleFixture.UiBattle();
    IReadOnlyList<UnitAction> deadActions = deadBattle.Query(
      new GetAvailableActionsForUnit(deadBattle.Alive(deadBattle.PlayerUnit)));
    deadBattle.Read(() => EvaluateAll(deadActions));
    deadBattle.ApplyDamage(deadBattle.PlayerUnit, 999);
    deadBattle.Read(() => Assert.True(deadActions.AsValueEnumerable().All(action => !action.IsAvailable)));
  }

  [TestCase]
  public void ImmobilizationExpiryUpdatesRetainedOptionsDuringUpkeepAndNextTurn()
  {
    using var battle = BattleFixture.Duel(enemy: new("Durable", Health: 100));
    IReadOnlyList<UnitAction> actions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    UnitAction move = Row<MoveActionDefinition>(actions);
    UnitAction endTurn = Row<EndTurnActionDefinition>(actions);
    battle.Read(() => Assert.True(move.IsAvailable));
    battle.Read(() => Assert.True(endTurn.IsAvailable));

    battle.Mutate(() => battle.Session.ApplyStatusEffectTo(battle.PlayerUnit, TestData.MakeStun(duration: 1)));

    battle.Read(() => Assert.False(move.IsAvailable));
    battle.Read(() => Assert.False(endTurn.IsAvailable));

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
    };

    battle.EndFactionTurn(battle.PlayerFaction);

    battle.Read(() => Assert.True(observedExpiry));
    battle.Read(() => Assert.True(moveAtExpiry));
    battle.Read(() => Assert.True(endTurnAtExpiry));
    battle.Read(() => Assert.False(battle.PlayerUnit.IsImmobilized));
    battle.Read(() => Assert.False(move.IsAvailable));
    battle.Read(() => Assert.False(endTurn.IsAvailable));

    battle.EndFactionTurn(battle.EnemyFaction);

    battle.Read(() => Assert.True(move.IsAvailable));
    battle.Read(() => Assert.True(endTurn.IsAvailable));
  }

  [TestCase]
  public void AdjacentOccupancyChangesTargetingAndRetainsCachedEntries()
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
    var targeting = new MoveTargeting(battle.Runtime, observer);

    battle.Read(() => Assert.False(targeting.Begin().AsValueEnumerable().Contains(new Vector3I(2, 0, 0))));

    battle.Move(ally, [new Vector3I(3, 0, 0)]);

    battle.Read(() => Assert.True(targeting.Begin().AsValueEnumerable().Contains(new Vector3I(2, 0, 0))));

    battle.Move(ally, [new Vector3I(2, 0, 0)]);

    battle.Read(() => Assert.False(targeting.Begin().AsValueEnumerable().Contains(new Vector3I(2, 0, 0))));
    battle.Read(() => Assert.True(ReferenceEquals(actions, battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(observer))))));
  }

  [TestCase]
  public void EnemyRangeAndSightChangesTargetingAndRetainsCachedEntries()
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

    battle.Read(() => Assert.True(targeting.Begin().AsValueEnumerable().Contains(new Vector3I(3, 0, 0))));
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<UnitSpottedBattleEvent>().AsValueEnumerable()
      .Count(e => ReferenceEquals(e.Unit, observer) && ReferenceEquals(e.Target, target))));

    battle.EndFactionTurn(player);
    IReadOnlyList<UnitAction> actions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(observer)));

    battle.Move(target, [new Vector3I(4, 0, 0)]);

    battle.Read(() => Assert.True(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target)))));
    battle.Read(() => Assert.False(targeting.Begin().AsValueEnumerable().Contains(new Vector3I(4, 0, 0))));

    battle.Move(target, [new Vector3I(5, 0, 0)]);

    battle.Read(() => Assert.False(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target)))));
    battle.Read(() => Assert.False(targeting.Begin().AsValueEnumerable().Contains(new Vector3I(5, 0, 0))));
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<UnitSpottedBattleEvent>().AsValueEnumerable()
      .Count(e => ReferenceEquals(e.Unit, observer) && ReferenceEquals(e.Target, target))));

    battle.Move(target, [new Vector3I(4, 0, 0), new Vector3I(3, 0, 0)]);

    battle.Read(() => Assert.Equal(0, target.CurrentActionPoints));
    battle.Read(() => Assert.True(targeting.Begin().AsValueEnumerable().Contains(new Vector3I(3, 0, 0))));
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<UnitSpottedBattleEvent>().AsValueEnumerable()
      .Count(e => ReferenceEquals(e.Unit, observer) && ReferenceEquals(e.Target, target))));
    battle.Read(() => Assert.True(ReferenceEquals(actions, battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(observer))))));
  }

  [TestCase]
  public void SharedMagazineUpdatesRefreshHoldersRetainedOptions()
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
    battle.Read(() => { _ = attack.IsAvailable; });
    battle.Read(() => { _ = reload.IsAvailable; });

    battle.Attack(actor, target);

    battle.Read(() => Assert.False(attack.IsAvailable));
    battle.Read(() => Assert.True(reload.IsAvailable));

    battle.Reload(actor, shared);

    battle.Read(() => Assert.True(attack.IsAvailable));
    battle.Read(() => Assert.False(reload.IsAvailable));
  }

  // The startup and continuation turn-start dispatches are separate orchestration bodies
  // (factory bootstrap vs the round-rollover advance); both must observe pre-refresh availability
  // from retained entries without caching stale results. The startup instance arranges and
  // observes through declared setup systems, because fixture construction already runs the
  // first turn; the continuation instance can arrange and subscribe after construction.
  [TestCase(TestName = "Reads during the startup TurnStarted observe pre-refresh availability without caching")]
  public void ReadsDuringStartupTurnStartedObservePreRefreshWithoutCaching()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    BattleUnitState support = null;
    IReadOnlyList<UnitAction> existing = null;
    IReadOnlyList<UnitAction> createdDuringCallback = null;
    UnitAction existingMove = null;
    bool existingDuringCallback = true;
    bool createdDuringCallbackValue = true;

    void ArrangeAtSessionStart(HookContext context)
    {
      BattleUnitState player = context.Session.GetFactionAliveUnits(playerFaction)
        .AsValueEnumerable().Single(unit => unit.Combatant.Name == "Player");
      support = context.Session.GetFactionAliveUnits(playerFaction)
        .AsValueEnumerable().Single(unit => unit.Combatant.Name == "Support");
      player.TrySpendActionPoints(player.CurrentActionPoints);
      support.TrySpendActionPoints(support.CurrentActionPoints);
      existing = new GetAvailableActionsForUnit(
        context.Session.TryGetAlive(player).RequireSome()).Execute(context.Session);
      existingMove = Row<MoveActionDefinition>(existing);
    }

    void ObserveAtFirstTurnStart(HookContext context)
    {
      if (context.Session.TurnNumber != 1)
        return;
      existingDuringCallback = existingMove.IsAvailable;
      createdDuringCallback = new GetAvailableActionsForUnit(
        context.Session.TryGetAlive(support).RequireSome()).Execute(context.Session);
      createdDuringCallbackValue = Row<MoveActionDefinition>(createdDuringCallback).IsAvailable;
    }

    using var battle = new BattleFixture(ThreeUnitSetup(playerFaction, enemyFaction) with
    {
      Systems = [new SetupSystemData { OnRegister = registered =>
      {
        registered.RegisterHook<SessionStartedBattleEvent>(new HookBody<SessionStartedBattleEvent>(ArrangeAtSessionStart));
        registered.RegisterHook<TurnStartedBattleEvent>(new HookBody<TurnStartedBattleEvent>(ObserveAtFirstTurnStart));
      } }],
    });

    battle.Read(() => Assert.False(existingDuringCallback));
    battle.Read(() => Assert.False(createdDuringCallbackValue));
    battle.Read(() => Assert.True(existingMove.IsAvailable));
    battle.Read(() => Assert.True(Row<MoveActionDefinition>(createdDuringCallback).IsAvailable));
  }

  [TestCase(TestName = "Reads during a continuation TurnStarted observe pre-refresh availability without caching")]
  public void ReadsDuringContinuationTurnStartedObservePreRefreshWithoutCaching()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(ThreeUnitSetup(playerFaction, enemyFaction));
    BattleUnitState player = battle.UnitAt(new Vector3I(1, 0, 1));
    BattleUnitState support = battle.UnitAt(new Vector3I(2, 0, 1));
    battle.Mutate(() => player.TrySpendActionPoints(player.CurrentActionPoints));
    battle.Mutate(() => support.TrySpendActionPoints(support.CurrentActionPoints));
    IReadOnlyList<UnitAction> existing = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(player)));
    UnitAction existingMove = Row<MoveActionDefinition>(existing);
    battle.Read(() => Assert.False(existingMove.IsAvailable));
    IReadOnlyList<UnitAction>? createdDuringCallback = null;
    bool existingDuringCallback = true;
    bool createdDuringCallbackValue = true;
    battle.Session.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is not TurnStartedBattleEvent startedBattleEvent || startedBattleEvent.Faction != playerFaction)
        return;
      existingDuringCallback = existingMove.IsAvailable;
      createdDuringCallback = battle.Query(
        new GetAvailableActionsForUnit(battle.Alive(support)));
      createdDuringCallbackValue = Row<MoveActionDefinition>(createdDuringCallback).IsAvailable;
    };

    // Round-rollover continuation reaches the player's next TurnStarted.
    battle.EndFactionTurn(playerFaction);
    battle.EndFactionTurn(enemyFaction);

    IReadOnlyList<UnitAction> created = createdDuringCallback ??
      throw new InvalidOperationException("the TurnStarted callback never queried the support unit");
    battle.Read(() => Assert.False(existingDuringCallback));
    battle.Read(() => Assert.False(createdDuringCallbackValue));
    battle.Read(() => Assert.True(existingMove.IsAvailable));
    battle.Read(() => Assert.True(Row<MoveActionDefinition>(created).IsAvailable));
  }

  private static BattleSetup ThreeUnitSetup(Faction playerFaction, Faction enemyFaction) => new(
    TestData.MakeOpenBattleMap(new Vector3I(8, 1, 8)),
    [
      new BattleSideSetup(playerFaction, [new FakeObjectiveData()],
        [
          new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Player", playerFaction)), new Vector3I(1, 0, 1)),
          new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Support", playerFaction)), new Vector3I(2, 0, 1)),
        ]),
      new BattleSideSetup(enemyFaction, [new FakeObjectiveData()],
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Enemy", enemyFaction)), new Vector3I(1, 0, 5))]),
    ],
    Seed: 7);

  private sealed class HookBody<TEvent>(Action<HookContext> body) : BattleHook<TEvent>
    where TEvent : BattleEvent
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TEvent evt)
    {
      body(context);
      return [];
    }
  }

  [TestCase]
  public void MoveInterruptIncapacitationUpdatesOtherActorsRetainedEntries()
  {
    using var battle = BattleFixture.UiBattle();
    IReadOnlyList<UnitAction> supportActions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.SupportUnit)));
    battle.Read(() => EvaluateAll(supportActions));
    battle.RegisterHook<UnitMovedBattleEvent>(
      new DamageUnitOnce(battle.SupportUnit, 20, DamageKind.Stun));

    battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2)]);

    battle.Read(() => Assert.True(battle.SupportUnit.IsUnconscious));
    battle.Read(() => Assert.True(supportActions.AsValueEnumerable().All(action => !action.IsAvailable)));
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
    battle.Read(() => EvaluateAll(playerActions));
    battle.Read(() => EvaluateAll(enemyActions));
    battle.RegisterHook<UnitMovedBattleEvent>(new RejectingInterruptOnce(battle.PlayerUnit));
    battle.ClearEvents();

    Assert.Throws<InvalidOperationException>(() =>
      battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2), new Vector3I(4, 0, 3)]));

    battle.Read(() => Assert.Equal(new Vector3I(4, 0, 2), battle.Session.GetUnitPosition(battle.PlayerUnit).RequireSome().Raw));
    battle.Read(() => Assert.Equal(0, battle.PlayerUnit.CurrentActionPoints));
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<UnitMovedBattleEvent>().AsValueEnumerable().Count()));
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<TileOccupiedBattleEvent>().AsValueEnumerable().Count()));
    battle.Read(() => Assert.False(Row<MoveActionDefinition>(playerActions).IsAvailable));
    battle.Read(() => Assert.False(Row<PassActionDefinition>(playerActions).IsAvailable));
    battle.Read(() => Assert.True(Row<EndTurnActionDefinition>(playerActions).IsAvailable));
    battle.Read(() => Assert.True(enemyActions.AsValueEnumerable().All(action => !action.IsAvailable)));

    battle.Pass(battle.PlayerUnit);

    battle.Read(() => Assert.Equal(new Vector3I(4, 0, 2), battle.Session.GetUnitPosition(battle.PlayerUnit).RequireSome().Raw));
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<UnitMovedBattleEvent>().AsValueEnumerable().Count()));
  }

  [TestCase]
  public void FailingMoveInterruptPreservesFailureAndCommittedStateAndClearsQueue()
  {
    using var battle = BattleFixture.Duel(player: new("Alpha", ActionPoints: 2));
    IReadOnlyList<UnitAction> actions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    battle.Read(() => EvaluateAll(actions));
    var failure = new ApplicationException("interrupt failed");
    battle.RegisterHook<UnitMovedBattleEvent>(new InterruptOnce(new SpendThenThrow(battle.PlayerUnit, failure)));

    Exception? caught = null;
    try
    {
      battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2), new Vector3I(4, 0, 3)]);
    }
    catch (Exception exception)
    {
      caught = exception;
    }

    battle.Read(() => Assert.True(ReferenceEquals(failure, caught)));
    battle.Read(() => Assert.Equal(new Vector3I(4, 0, 2), battle.Session.GetUnitPosition(battle.PlayerUnit).RequireSome().Raw));
    battle.Read(() => Assert.Equal(0, battle.PlayerUnit.CurrentActionPoints));
    battle.Read(() => Assert.False(Row<MoveActionDefinition>(actions).IsAvailable));

    battle.Submit(BattleAction.PassUnit(battle.Alive(battle.PlayerUnit)));
    battle.Read(() => Assert.Equal(new Vector3I(4, 0, 2), battle.Session.GetUnitPosition(battle.PlayerUnit).RequireSome().Raw));
  }

  [TestCase]
  public void EventlessFailureRefreshesPlayerOptionsAfterSpentCost()
  {
    using var battle = BattleFixture.Duel(player: new("Alpha", ActionPoints: 1));
    IReadOnlyList<UnitAction> playerActions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    battle.Read(() => EvaluateAll(playerActions));
    var failure = new ApplicationException("eventless failure");

    Exception? caught = null;
    try
    {
      battle.Submit(new SpendThenThrow(battle.PlayerUnit, failure));
    }
    catch (Exception exception)
    {
      caught = exception;
    }

    battle.Read(() => Assert.True(ReferenceEquals(failure, caught)));
    battle.Read(() => Assert.False(Row<MoveActionDefinition>(playerActions).IsAvailable));
    battle.Read(() => Assert.False(Row<PassActionDefinition>(playerActions).IsAvailable));
    battle.Read(() => Assert.True(Row<EndTurnActionDefinition>(playerActions).IsAvailable));
  }

  [TestCase]
  public void CleanReadsEvaluateOnceAndOnlyRelevantEventsReevaluateTheEntry()
  {
    using var battle = BattleFixture.Duel();
    var condition = new CountingCondition();
    var action = new UnitAction(battle.Session.ActionOptions, battle.PlayerUnit,
      new TestActionDefinition(condition));

    battle.Read(() => Assert.True(action.IsAvailable));
    battle.Read(() => Assert.True(action.IsAvailable));
    battle.Read(() => Assert.Equal(1, condition.Calls));

    battle.Mutate(() => action.Invalidate(new UnitSpottedBattleEvent(battle.PlayerUnit, battle.EnemyUnit)));
    battle.Read(() => Assert.True(action.IsAvailable));
    battle.Read(() => Assert.Equal(1, condition.Calls));

    battle.Mutate(() => action.Invalidate(new UnitMovedBattleEvent(battle.PlayerUnit, battle.At(4, 0, 2), battle.At(4, 0, 1))));
    battle.Read(() => Assert.True(action.IsAvailable));
    battle.Read(() => Assert.Equal(2, condition.Calls));
  }

  [TestCase]
  public void EvaluationFailureRetriesAndCachesSuccess()
  {
    using var battle = BattleFixture.Duel();
    var failure = new ApplicationException("condition failed");
    var condition = new ThrowingCondition(failure);
    var action = new UnitAction(battle.Session.ActionOptions, battle.PlayerUnit,
      new TestActionDefinition(condition));

    Exception? caught = null;
    try
    {
      battle.Read(() => { _ = action.IsAvailable; });
    }
    catch (Exception exception)
    {
      caught = exception;
    }

    battle.Read(() => Assert.True(ReferenceEquals(failure, caught)));
    condition.ShouldThrow = false;
    battle.Read(() => Assert.True(action.IsAvailable));
    battle.Read(() => Assert.True(action.IsAvailable));
    battle.Read(() => Assert.Equal(2, condition.Calls));
  }
}
