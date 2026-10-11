using CampaignGameState = global::FunProject.GameState.GameState;
using System;
using System.Collections.Generic;
using FunProject.Battle;
using FunProject.Combatants;
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

  /// <summary>Starts the selected mission and stores its deployment after startup succeeds.
  /// The view owns the returned runtime.</summary>
  public Either<BattleSetupFailure, MissionBattle> LaunchMission(
    GeoscapeEvent mission, IReadOnlyList<Combatant> squad)
  {
    TacticalMissionData authored = mission.Definition.TacticalMission!;
    // Capture ordered, unique participants before startup callbacks can change the selection.
    var deployment = new MissionDeployment([.. squad.AsValueEnumerable().Distinct()
      .Take((int)Math.Min(authored.Size.MaxPlayerUnits, (uint)squad.Count))]);
    int seed = mission.BattleSeed.ValueUnsafe();

    var player = new PlayerDeployment(_state.PlayerFaction,
      [.. deployment.Participants.AsValueEnumerable().Select(combatant => new UnitLoadout(
        combatant, combatant.EquippedWeapon, combatant.EquippedArmor)
      {
        StatMods = _state.Conditions.StatContributions(combatant),
      })]);
    SideDeployment side = MissionEnemyResolver.Resolve(authored, seed);

    return BattleSetupResolver.Resolve(authored.BattleType, seed, Some(player), Some(side))
      .Bind(setup => BattleFactory.Start(setup).Map(runtime =>
      {
        _state.ActiveMission = Some(deployment);
        return new MissionBattle(deployment, setup, runtime);
      }));
  }

  /// <summary>Installation failed; retain the pending event and seed for another attempt.
  /// Runtime disposal belongs to the view.</summary>
  public void AbortMissionPresentation() => _state.ActiveMission = None;

  /// <summary>Applies the active mission's terminal summary and consumes the mission before
  /// broadcasting. The completion signal supplies the trusted player summary.</summary>
  internal void CompleteMission(FactionBattleSummary summary)
  {
    PendingResolution pending = _state.Pending.ValueUnsafe()!;

    foreach ((Combatant combatant, BattleHealthSummary health) in summary.HealthByCombatant)
      _state.Conditions.ApplyMissionReturn(combatant, health.HealthDamageTaken, health.MaxHealth, Tick);
    _state.RemoveDeadFromRoster(summary.CombatantsDead);
    foreach (Combatant captured in summary.CapturedEnemies)
      _state.Captivity.Add(captured);

    // Commit campaign consumption before notifying observers.
    _state.ActiveMission = Option<MissionDeployment>.None;
    _state.Pending = Option<PendingResolution>.None;
    _state.ActiveEvents.Remove(pending.Event);

    foreach (Combatant combatant in summary.HealthByCombatant.Keys)
      Commit(new CombatantConditionsChanged(combatant));
    Commit(new ResolutionEventClosed(pending, ResolutionOutcome.Engaged));
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
