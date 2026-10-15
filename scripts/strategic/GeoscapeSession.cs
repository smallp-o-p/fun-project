using CampaignGameState = global::FunProject.GameState.GameState;
using System;
using System.Collections.Generic;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Currencies;
using FunProject.Engineering;
using FunProject.GameState;
using FunProject.Items;
using FunProject.Research;
using FunProject.Staffing;
using LanguageExt.UnsafeValueAccess;

namespace FunProject.Strategic;

/// <summary>
/// The ephemeral geoscape runtime constructed over the campaign truth (<see cref="GameState"/>):
/// playback speed, the real-seconds accumulator, event dispatch, and resolution orchestration.
/// Mutates the state it wraps — the BattleSession-over-BattleBoardState pattern. One session
/// per campaign; rebuildable at any time (loading a save later reconstructs state, then session).
/// </summary>
public sealed class GeoscapeSession
{
  // One tick advances the in-game clock by one minute.
  public const int TickGameSeconds = CampaignGameState.TickGameSeconds;

  private const double BaseSecondsPerTick = 0.1;
  private static double SecondsPerTick(TimeSpeed speed) => speed switch
  {
    TimeSpeed.Paused => 0.0,
    TimeSpeed.Normal => BaseSecondsPerTick,
    TimeSpeed.Fast => BaseSecondsPerTick / 5.0,
    TimeSpeed.VeryFast => BaseSecondsPerTick / 12.5,
    TimeSpeed.VeryVeryFast => BaseSecondsPerTick / 25.0,
    _ => throw new InvalidOperationException($"Unknown time speed '{speed}'."),
  };

  private readonly CampaignGameState _state;
  private double _accumulator;

  public GeoscapeSession(CampaignGameState state)
  {
    ArgumentNullException.ThrowIfNull(state);
    _state = state;
  }

  public event Action<IGeoscapeEvent> EventCommitted = delegate { };

  public long Tick => _state.Tick;

  public TimeSpeed Speed { get; private set; } = TimeSpeed.Paused;

  public DateTime CurrentTime => _state.CurrentTime;

  public int CurrentDay => _state.CurrentDay;

  public IReadOnlyList<RegionData> Regions => _state.Regions;

  public int IndexOfRegion(string name) => _state.IndexOfRegion(name);

  public IReadOnlyList<GeoscapeEvent> ActiveEvents => _state.ActiveEvents;

  public Option<PendingResolution> PendingResolution => _state.Pending;

  /// <summary>Campaign-held association for the currently launched mission, if any.</summary>
  public Option<MissionDeployment> ActiveMission => _state.ActiveMission;

  public Option<ManufacturingJob> ActiveManufacturing => _state.Engineering.ActiveJob;

  public Option<ResearchJob> ActiveResearch => _state.Research.ActiveJob;

  public IReadOnlyList<ResearchProject> GetResearchProjects() => _state.Research.Projects;

  public IReadOnlyList<ResearchProject> GetAvailableResearchProjects()
    => _state.Research.GetAvailableProjects(_state);

  public Either<ResearchStartFailure, ResearchJob> StartResearch(ResearchProject project)
    => _state.Research.ResolveResearch(project, _state).Bind<ResearchJob>(definition =>
    {
      var job = new ResearchJob(definition.Project, new JobProgress(Tick,
        CampaignGameState.TicksFromDays(definition.DurationDays), _state.Staff.ResearchSpeed,
        _state.GetGlobalModifier(GlobalProcess.ResearchSpeed)));
      if (!_state.TrySpendCurrencyCosts(definition.Project.Costs))
        return ResearchStartFailure.InsufficientCurrencies;
      var started = _state.Research.Start(job);
      Commit(started);
      return started.Job;
    });

  public IReadOnlyList<ManufacturingOption> GetManufacturingOptions()
    => _state.Engineering.GetManufacturingOptions(_state);

  public Either<ManufacturingStartFailure, ManufacturingJob> StartManufacturing(ManufacturingRecipe recipe)
    => _state.Engineering.ResolveManufacturing(recipe, _state.Armory).Bind<ManufacturingJob>(project =>
    {
      var job = new ManufacturingJob(project, new JobProgress(Tick,
        CampaignGameState.TicksFromDays(project.DurationDays), _state.Staff.ManufacturingSpeed,
        _state.GetGlobalModifier(GlobalProcess.ManufacturingSpeed)));
      if (!_state.TrySpendCurrencyCosts(project.Costs))
        return ManufacturingStartFailure.InsufficientCurrencies;
      ManufacturingStarted started = _state.Engineering.Start(job);
      Commit(started);
      return started.Job;
    });

  public StaffHiringOption GetHiringOption(StaffRole role, uint quantity)
  {
    var prepared = _state.Staff.PrepareHire(role, quantity, _state.GetGlobalModifier(GlobalProcess.HiringCost));
    return new StaffHiringOption(
      role == StaffRole.Researcher ? _state.Staff.Researchers : _state.Staff.Engineers,
      role == StaffRole.Researcher ? _state.ResearchSpeed : _state.ManufacturingSpeed,
      role == StaffRole.Researcher ? _state.Staff.ResearcherSpeedBonus : _state.Staff.EngineerSpeedBonus,
      prepared.Costs, quantity > 0 && _state.CanAffordCurrencyCosts(prepared.Costs));
  }

  public Either<StaffHireFailure, StaffHired> HireResearchers(uint quantity) => Hire(StaffRole.Researcher, quantity);
  public Either<StaffHireFailure, StaffHired> HireEngineers(uint quantity) => Hire(StaffRole.Engineer, quantity);

  private Either<StaffHireFailure, StaffHired> Hire(StaffRole role, uint quantity)
  {
    if (quantity == 0) return StaffHireFailure.InvalidQuantity;
    var prepared = _state.Staff.PrepareHire(role, quantity, _state.GetGlobalModifier(GlobalProcess.HiringCost));
    var research = role == StaffRole.Researcher
      ? ActiveResearch.Map(job => job.WithSpeed(Tick, prepared.Speed)) : None;
    var manufacturing = role == StaffRole.Engineer
      ? ActiveManufacturing.Map(job => job.WithSpeed(Tick, prepared.Speed)) : None;
    var hired = new StaffHired(role, prepared.Count, prepared.Speed);
    if (!_state.TrySpendCurrencyCosts(prepared.Costs)) return StaffHireFailure.InsufficientCurrencies;

    // Commit all prepared values before notifying subscribers.
    if (research.Case is ResearchJob researchJob) _state.Research.UpdateJob(researchJob);
    if (manufacturing.Case is ManufacturingJob manufacturingJob) _state.Engineering.UpdateJob(manufacturingJob);
    _state.Staff.Commit(prepared);
    Commit(hired);
    return hired;
  }

  public void OpenResolution(GeoscapeEvent @event)
  {
    ArgumentNullException.ThrowIfNull(@event);
    if (_state.Pending.IsSome)
      throw new InvalidOperationException("A resolution is already pending; complete it before opening another.");

    var pending = new PendingResolution(@event);
    _state.Pending = pending;

    Commit(new ResolutionEventOpened(pending));
  }

  public void CompleteResolution(ResolutionOutcome outcome)
  {
    if (_state.Pending.IsNone)
      throw new InvalidOperationException("CompleteResolution called with no resolution pending.");
    if (_state.ActiveMission.IsSome)
      throw new InvalidOperationException(
        "The active mission must complete before its resolution closes.");

    // Clear before committing: synchronous subscribers (HUD/map refreshes) must observe the
    // resolution as already closed — the battle layer's "hooks act on post-state" convention.
    _state.Pending.IfSome(pending =>
    {
      _state.Pending = Option<PendingResolution>.None;
      _state.ActiveEvents.Remove(pending.Event);
      Commit(new ResolutionEventClosed(pending, outcome));
    });
  }

  public void ChangeSpeed(TimeSpeed speed)
  {
    Speed = speed;
  }

  public Option<MissionReceipt> LastMissionReceipt => _state.LastMissionReceipt;

  public static MissionTerms GetMissionTerms(GeoscapeEvent mission)
    => mission.Terms ??= MissionTerms.Resolve(mission.Definition.TacticalMission!,
      mission.BattleSeed.ValueUnsafe());

  /// <summary>Snapshot mission rewards and participants before startup; the view confirms installation.</summary>
  public Either<BattleSetupFailure, MissionBattle> LaunchMission(
    GeoscapeEvent mission, IReadOnlyList<Combatant> squad)
  {
    if (_state.MissionStarting || _state.ActiveMission.IsSome
      || !_state.Pending.Exists(pending => ReferenceEquals(pending.Event, mission)))
      return new BattleSetupFailure(BattleSetupFailureReason.InvalidMissionState, "This mission cannot launch now.");
    TacticalMissionData authored = mission.Definition.TacticalMission!;
    MissionTerms terms = GetMissionTerms(mission);
    var participants = squad.AsValueEnumerable().Distinct()
      .Take((int)Math.Min(authored.Size.MaxPlayerUnits, (uint)squad.Count)).ToArray();
    if (participants.AsValueEnumerable().Any(unit => !_state.Roster.AsValueEnumerable()
      .Any(member => ReferenceEquals(member, unit)) || !CanDeploy(unit, mission.Definition)))
      return new BattleSetupFailure(BattleSetupFailureReason.InvalidMissionState, "The selected squad is no longer eligible.");
    var deployment = new MissionDeployment(mission, System.Array.AsReadOnly(participants), terms);
    int seed = mission.BattleSeed.ValueUnsafe();
    var player = new PlayerDeployment(_state.PlayerFaction,
      [.. deployment.Participants.AsValueEnumerable().Select(combatant => new UnitLoadout(
        combatant, combatant.EquippedWeapon, combatant.EquippedArmor)
      {
        StatMods = _state.Conditions.StatContributions(combatant),
      })]);
    SideDeployment side = MissionEnemyResolver.Resolve(authored, seed);
    _state.MissionStarting = true;
    try
    {
      return BattleSetupResolver.Resolve(authored.BattleType, seed, Some(player), Some(side))
        .Bind(setup => BattleFactory.Start(setup).Bind<MissionBattle>(runtime =>
        {
          if (!_state.Pending.Exists(pending => ReferenceEquals(pending.Event, mission)))
          {
            runtime.Dispose();
            return new BattleSetupFailure(BattleSetupFailureReason.InvalidMissionState, "The mission closed during startup.");
          }
          _state.ActiveMission = Some(deployment);
          return new MissionBattle(deployment, setup, runtime);
        }));
    }
    finally { _state.MissionStarting = false; }
  }

  public void ConfirmMissionDeployment(MissionDeployment deployment)
  {
    RequireActiveMission(deployment);
    deployment.Installed = true;
  }

  /// <summary>Technical installation failure retains the pending mission and never spends.</summary>
  public void AbortMissionPresentation()
  {
    if (_state.ActiveMission.Exists(deployment => deployment.Installed))
      throw new InvalidOperationException("An installed mission must be abandoned explicitly.");
    _state.ActiveMission = None;
  }

  public MissionReceipt AbandonMission(MissionDeployment deployment)
  {
    if (deployment.Receipt.Case is MissionReceipt receipt) return receipt;
    RequireActiveMission(deployment);
    if (!deployment.Installed) throw new InvalidOperationException("Only an installed mission can be abandoned.");
    var abandoned = new MissionReceipt(deployment.Mission.Definition.TacticalMission!.BattleType.Name, None,
      new Dictionary<Currency, ulong>());
    ConsumeMission(deployment, abandoned);
    Commit(new ResolutionEventClosed(new PendingResolution(deployment.Mission), ResolutionOutcome.Declined));
    return abandoned;
  }

  private void RequireActiveMission(MissionDeployment deployment)
  {
    if (!_state.ActiveMission.Exists(active => ReferenceEquals(active, deployment))
      || !_state.Pending.Exists(pending => ReferenceEquals(pending.Event, deployment.Mission)))
      throw new InvalidOperationException("This deployment is not the active mission.");
  }

  /// <summary>Prepare the entire monetary return before mutation, then consume before callbacks.</summary>
  internal MissionReceipt CompleteMission(MissionDeployment deployment, FactionBattleSummary summary, BattleResult battleResult)
  {
    if (deployment.Receipt.Case is MissionReceipt existing) return existing;
    RequireActiveMission(deployment);
    if (!deployment.Installed || !ReferenceEquals(summary.Faction, _state.PlayerFaction))
      throw new InvalidOperationException("Completion requires an installed mission and its player summary.");
    var rewards = deployment.Terms.VictoryRewards.AsValueEnumerable().ToDictionary(
      pair => pair.Key, pair => summary.Outcome == BattleOutcome.Victory ? pair.Value : 0UL);
    var receipt = new MissionReceipt(deployment.Mission.Definition.TacticalMission!.BattleType.Name, Some(summary.Outcome),
      new System.Collections.ObjectModel.ReadOnlyDictionary<Currency, ulong>(rewards))
    { Tally = Some(MissionTally.From(summary, battleResult)) };
    var returns = summary.HealthByCombatant.AsValueEnumerable().Select(pair =>
      (Combatant: pair.Key, Conditions: _state.Conditions.PrepareMissionReturn(
        pair.Key, pair.Value.HealthDamageTaken, pair.Value.MaxHealth, Tick))).ToArray();
    _state.CreditCurrencies(rewards);
    foreach (var (combatant, conditions) in returns)
      _state.Conditions.CommitMissionReturn(combatant, conditions.Injury, conditions.Fatigue);
    _state.RemoveDeadFromRoster(summary.CombatantsDead);
    foreach (Combatant captured in summary.CapturedEnemies) _state.Captivity.Add(captured);
    ConsumeMission(deployment, receipt);

    foreach (Combatant combatant in summary.HealthByCombatant.Keys)
      Commit(new CombatantConditionsChanged(combatant));
    Commit(new ResolutionEventClosed(new PendingResolution(deployment.Mission), ResolutionOutcome.Engaged));
    return receipt;
  }

  private void ConsumeMission(MissionDeployment deployment, MissionReceipt receipt)
  {
    deployment.Receipt = Some(receipt);
    _state.LastMissionReceipt = Some(receipt);
    _state.ActiveMission = None;
    _state.Pending = None;
    _state.ActiveEvents.Remove(deployment.Mission);
  }

  public void ApplyMissionReturn(FactionBattleSummary summary)
  {
    foreach ((Combatant combatant, BattleHealthSummary health) in summary.HealthByCombatant)
      _state.Conditions.ApplyMissionReturn(combatant, health.HealthDamageTaken, health.MaxHealth, Tick);
    foreach (Combatant combatant in summary.HealthByCombatant.Keys)
      Commit(new CombatantConditionsChanged(combatant));
  }

  public bool CanDeploy(Combatant combatant, GeoscapeEventDefinition mission)
    => _state.Conditions.CanDeploy(combatant, mission.AllowUnfitDeployment);

  public void Advance(double deltaSeconds)
  {
    if (Speed == TimeSpeed.Paused || _state.Pending.IsSome)
      return;

    _accumulator += deltaSeconds;
    double secondsPerTick = SecondsPerTick(Speed);
    int ticksToAdvance = (int)(_accumulator / secondsPerTick);

    _accumulator -= ticksToAdvance * secondsPerTick;

    for (int i = 0; i < ticksToAdvance; i++)
    {
      _state.Tick++;
      Commit(new TimeAdvanced(Tick, CurrentTime));
      FireDueSchedule();
      RemoveExpired();
      _state.Engineering.CompleteIfDue(Tick).IfSome(Handle);
      _state.Research.CompleteIfDue(Tick).IfSome(Handle);
      RecoverRoster();
    }
  }

  // Roster recovery, last in the tick order: the campaign registry advances every roster
  // record first (each ladder one tier per elapsed chained deadline), then this loop
  // broadcasts one change per returned identity; unchanged records never notify.
  private void RecoverRoster()
  {
    foreach (Combatant combatant in _state.Conditions.Recover(_state.Roster, Tick))
      Commit(new CombatantConditionsChanged(combatant));
  }

  private void Handle(ManufacturingCompleted completed)
  {
    var project = completed.Job.Project;
    _state.Armory.AddItem(project.Item, project.Item.UnlimitedStock);
    Commit(completed);
  }

  private void Handle(ResearchCompleted completed)
  {
    _state.Engineering.Unlock(completed.ManufacturingUnlocks);
    Commit(completed);
  }

  private void Commit(IGeoscapeEvent geoscapeEvent)
  {
    EventCommitted.Invoke(geoscapeEvent);
  }

  private void FireDueSchedule()
  {
    while (_state.NextScheduleIndex < _state.Timeline.Count
           && _state.Timeline[_state.NextScheduleIndex].AtTick <= Tick)
    {
      CampaignGameState.ScheduledFire fire = _state.Timeline[_state.NextScheduleIndex];
      _state.NextScheduleIndex++;

      var active = new GeoscapeEvent(
        fire.Definition,
        fire.TargetRegionIndex >= 0 ? Some(fire.TargetRegionIndex) : Option<int>.None,
        Tick,
        fire.ExpiresAtTick)
      {
        BattleSeed = fire.Definition.Kind == GeoscapeEventKind.TacticalBattle
          ? Some(Random.Shared.Next())
          : Option<int>.None,
      };
      _state.ActiveEvents.Add(active);
      Commit(new ScheduledEventFired(active));
    }
  }

  private void RemoveExpired()
  {
    for (int i = _state.ActiveEvents.Count - 1; i >= 0; i--)
    {
      GeoscapeEvent active = _state.ActiveEvents[i];
      active.ExpiresAtTick.IfSome(expiry =>
      {
        if (expiry > Tick) return;
        _state.ActiveEvents.RemoveAt(i);
        Commit(new EventExpired(active));
      });
    }
  }
}
