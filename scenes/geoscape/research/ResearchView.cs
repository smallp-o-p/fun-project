using System;
using CampaignGameState = FunProject.GameState.GameState;
using FunProject.Research;
using FunProject.Currencies;
using FunProject.Scenes.Ext;
using FunProject.Strategic;
using FunProject.Staffing;
using Godot;

namespace FunProject.Geoscape;

public sealed partial class ResearchView : GeoscapeView
{
  [Export] public PackedScene ProjectButtonScene { get; set; } = null!;
  [Export] public PackedScene EmptyCatalogScene { get; set; } = null!;

  private GeoscapeSession _session = null!;
  private CampaignGameState _state = null!;
  private SysColGeneric.IReadOnlyList<ResearchProject> _projects = [];
  private Option<ResearchProject> _selection = None;

  public override void _Ready()
  {
    base._Ready();
    GetNode<Button>("%StartResearchButton").Pressed += OnStartResearchPressed;
    GetNode<StaffHiringControl>("%StaffHiring").QuantityChanged += PresentHiring;
    GetNode<StaffHiringControl>("%StaffHiring").HireRequested += quantity =>
    {
      var result = _session.HireResearchers(quantity);
      Refresh();
      result.IfLeft(reason => GetNode<Label>("%Status").Text = $"Hiring rejected: {reason}.");
    };
  }

  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    _session = session;
    _state = state;
    Refresh();
  }

  private void PresentHiring()
  {
    var hiring = GetNode<StaffHiringControl>("%StaffHiring");
    hiring.Present(_session.GetHiringOption(StaffRole.Researcher, hiring.Quantity));
  }

  private void Refresh()
  {
    PresentHiring();
    _projects = _session.GetAvailableResearchProjects();
    _selection = _selection.Filter(selected =>
      _projects.AsValueEnumerable().Any(project => ReferenceEquals(project, selected)));

    GetNode<Label>("%Status").Text = "";
    GetNode<Label>("%ActiveJob").Text = _session.ActiveResearch.Match(
      job => $"Research: {job.Project.Name} - {ProjectTimeText.Remaining(job.CompletesAtTick, _session.Tick)} remaining",
      () => "No active research.");
    RebuildProjects();
    RefreshSelection();
  }

  private void OnStartResearchPressed()
  {
    if (GetNode<Button>("%StartResearchButton").Disabled)
      return;
    _selection.IfSome(project =>
    {
      var result = _session.StartResearch(project);
      Refresh();
      result.IfLeft(reason => ShowFailure(project, reason));
    });
  }

  private void ShowFailure(ResearchProject project, ResearchStartFailure reason)
    => GetNode<Label>("%Status").Text = $"Research start rejected for {project.Name}: {reason}.";

  private void RebuildProjects()
  {
    var list = GetNode<VBoxContainer>("%ResearchProjects");
    list.QueueFreeAllChildren();
    foreach (var project in _projects)
    {
      var button = ProjectButtonScene.Instantiate<Button>();
      button.Text = project.Name;
      button.Pressed += () =>
      {
        _selection = Some(project);
        RefreshSelection();
      };
      list.AddChild(button);
    }
    if (_projects.Count == 0)
      list.AddChild(EmptyCatalogScene.Instantiate<Label>());
  }

  private void RefreshSelection()
  {
    var start = GetNode<Button>("%StartResearchButton");
    var rows = GetNode<VBoxContainer>("%ResearchProjects");
    for (int index = 0; index < _projects.Count; index++)
    {
      rows.GetChild<Button>(index).SetPressedNoSignal(
        _selection.Case is ResearchProject selected && ReferenceEquals(selected, _projects[index]));
    }
    if (_selection.Case is ResearchProject project)
    {
      GetNode<Label>("%ProjectName").Text = project.Name;
      GetNode<Label>("%ProjectDescription").Text = project.Description;
      GetNode<Label>("%ResearchDuration").Text = $"Duration: {project.DurationDays}d";
      var costLabel = GetNode<Label>("%CurrencyCosts");
      costLabel.Text = Currency.FormatCosts(project.Costs);
      start.Disabled = !_state.CanAffordCurrencyCosts(project.Costs);
      if (start.Disabled)
        costLabel.AddThemeColorOverride("font_color", Colors.Red);
      else
        costLabel.RemoveThemeColorOverride("font_color");
      GetNode<Label>("%ManufacturingUnlocks").Text = "Manufacturing unlocks: " + (project.ManufacturingUnlocks.Length == 0
        ? "None"
        : string.Join(", ", project.ManufacturingUnlocks.AsValueEnumerable().Select(recipe => recipe.Item.Name).ToArray()));
    }
    else
    {
      GetNode<Label>("%ProjectName").Text = "Select a project to research.";
      GetNode<Label>("%ProjectDescription").Text = "";
      GetNode<Label>("%ResearchDuration").Text = "";
      GetNode<Label>("%CurrencyCosts").Text = "";
      start.Disabled = true;
      GetNode<Label>("%ManufacturingUnlocks").Text = "";
    }
    GetNode<Label>("%ProjectDescription").Visible = _selection.IsSome;
    GetNode<Label>("%ResearchDuration").Visible = _selection.IsSome;
    GetNode<Label>("%CurrencyCosts").Visible = _selection.IsSome;
    GetNode<Label>("%ManufacturingUnlocks").Visible = _selection.IsSome;
    start.Text = _session.ActiveResearch.IsSome ? "Switch Research" : "Start Research";
    GetNode<Label>("%SwitchHint").Visible = _session.ActiveResearch.IsSome;
  }
}
