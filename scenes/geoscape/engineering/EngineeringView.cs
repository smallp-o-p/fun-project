using System;
using System.Collections.Generic;
using CampaignGameState = FunProject.GameState.GameState;
using FunProject.Engineering;
using FunProject.Scenes.Ext;
using FunProject.Strategic;
using Godot;

public sealed partial class EngineeringView : GeoscapeView
{
  private GeoscapeSession _session = null!;
  private IReadOnlyList<ManufacturingOption> _options = [];
  private Option<ManufacturingJob> _active = None;
  private Option<ManufacturingOption> _selection = None;

  public override void _Ready()
  {
    base._Ready();
    GetNode<Button>("%ManufactureButton").Pressed += OnManufacturePressed;
    RefreshSelection();
  }

  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    _session = session;
    Present(session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);
  }

  public void Present(IReadOnlyList<ManufacturingOption> options,
    Option<ManufacturingJob> active, long tick)
  {
    _options = options;
    _active = active;
    _selection = _selection.Bind(selected =>
    {
      foreach (var option in _options)
        if (ReferenceEquals(option.Project.Item, selected.Project.Item))
          return Some(option);
      return None;
    });

    GetNode<Label>("%Status").Text = "";
    GetNode<Label>("%ActiveJob").Text = _active.Match(
      job => $"Manufacturing: {job.Project.Item.Name} — {ProjectTimeText.Remaining(job.CompletesAtTick, tick)} remaining",
      () => "No active manufacturing.");
    RebuildItems();
    RefreshSelection();
  }

  public void ShowFailure(ManufacturingStartFailure failure)
  {
    GetNode<Label>("%Status").Text = failure switch
    {
      ManufacturingStartFailure.UnknownItem => "This item is not available for manufacturing.",
      ManufacturingStartFailure.Busy => "Another manufacturing project is already in progress.",
      ManufacturingStartFailure.AlreadyAvailable => "Unlimited supply for this item is already established.",
      _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown manufacturing failure."),
    };
  }

  private void OnManufacturePressed()
  {
    if (GetNode<Button>("%ManufactureButton").Disabled)
      return;
    _selection.IfSome(option =>
    {
      var result = _session.StartManufacturing(option.Project.Item);
      Present(_session.GetManufacturingOptions(), _session.ActiveManufacturing, _session.Tick);
      result.IfLeft(ShowFailure); // after the refresh: it clears the status line
    });
  }

  private void RebuildItems()
  {
    var list = GetNode<VBoxContainer>("%ManufacturableItems");

    list.QueueFreeAllChildren();
    foreach (var option in _options)
    {
      var button = new Button
      {
        Text = option.Project.Item.Name,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
      };
      button.Pressed += () =>
      {
        _selection = Some(option);
        RefreshSelection();
      };
      list.AddChild(button);
    }
    if (_options.Count == 0)
      list.AddChild(new Label
      {
        Text = "No items available to manufacture.",
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
      });
  }

  private void RefreshSelection()
  {
    GetNode<Label>("%ItemName").Text = _selection.Match(
      option => option.Project.Item.Name, () => "Select an item to manufacture.");
    GetNode<Label>("%ItemDescription").Text = _selection.Match(
      option => option.Project.Item.Description, () => "");
    GetNode<Label>("%ManufacturingDuration").Text = _selection.Match(
      option => $"Duration: {option.Project.DurationDays}d", () => "");
    GetNode<Label>("%Stock").Text = _selection.Match(
      option => option.Project.UnlimitedStock ? "Stock: Not established" : $"Stock: {option.Remaining}", () => "");
    GetNode<Label>("%SupplyEffect").Text = _selection.Match(
      option => option.Project.UnlimitedStock ? "Establishes unlimited supply." : "Produces 1 item.", () => "");
    GetNode<Button>("%ManufactureButton").Disabled = _active.IsSome || _selection.IsNone;
  }
}
