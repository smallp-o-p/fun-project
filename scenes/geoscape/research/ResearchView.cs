using System;
using CampaignGameState = FunProject.GameState.GameState;
using FunProject.Research;
using FunProject.Scenes.Ext;
using FunProject.Strategic;
using Godot;

namespace FunProject.Geoscape;

public sealed partial class ResearchView : GeoscapeView
{
  private GeoscapeSession _session = null!;
  private SysColGeneric.IReadOnlyList<ResearchProject> _projects = [];
  private Option<ResearchProject> _selection = None;

  public override void _Ready()
  {
    base._Ready();
    GetNode<Button>("%StartResearchButton").Pressed += OnStartResearchPressed;
  }

  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    _session = session;
    Refresh();
  }

  private void Refresh()
  {
    _projects = _session.GetAvailableResearchProjects();
    _selection = _selection.Bind(selected =>
    {
      foreach (var project in _projects)
        if (ReferenceEquals(project, selected))
          return Some(project);
      return None;
    });

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
      var button = new Button
      {
        Text = project.Name,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        ToggleMode = true,
        Alignment = HorizontalAlignment.Left,
        CustomMinimumSize = new Vector2(0, 36),
      };
      button.Pressed += () =>
      {
        _selection = Some(project);
        RefreshSelection();
      };
      list.AddChild(button);
    }
    if (_projects.Count == 0)
      list.AddChild(new Label
      {
        Text = "No projects available to research.",
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
      });
  }

  private void RefreshSelection()
  {
    var rows = GetNode<VBoxContainer>("%ResearchProjects");
    for (int index = 0; index < _projects.Count; index++)
    {
      int rowIndex = index;
      rows.GetChild<Button>(index).SetPressedNoSignal(_selection.Match(
        selected => ReferenceEquals(selected, _projects[rowIndex]), () => false));
    }
    GetNode<Label>("%ProjectName").Text = _selection.Match(
      project => project.Name, () => "Select a project to research.");
    GetNode<Label>("%ProjectDescription").Text = _selection.Match(
      project => project.Description, () => "");
    GetNode<Label>("%ResearchDuration").Text = _selection.Match(
      project => $"Duration: {project.DurationDays}d", () => "");
    GetNode<Label>("%ManufacturingUnlocks").Text = _selection.Match(
      project => "Manufacturing unlocks: " + (project.ManufacturingUnlocks.Length == 0
        ? "None"
        : string.Join(", ", project.ManufacturingUnlocks.AsValueEnumerable().Select(item => item.Name).ToArray())),
      () => "");
    GetNode<Button>("%StartResearchButton").Disabled = _session.ActiveResearch.IsSome || _selection.IsNone;
  }
}
