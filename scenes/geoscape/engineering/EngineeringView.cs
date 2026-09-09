using System;
using System.Collections.Generic;
using FunProject.Engineering;
using FunProject.Items;
using FunProject.Strategic;
using Godot;

// Displays pushed campaign snapshots and emits requests; the composition root owns starts.
public sealed partial class EngineeringView : PanelContainer, IGeoscapeView
{
  private Action? _requestClose;
  private IReadOnlyList<ManufacturingOption> _options = [];
  private Option<ManufacturingJob> _active = None;
  private Option<ManufacturingOption> _selection = None;

  public event Action<EquippableItemData>? ManufactureRequested;

  public void ArmClose(Action requestClose) => _requestClose = requestClose;

  public override void _Ready()
  {
    GetNode<Button>("%BackButton").Pressed += () => _requestClose?.Invoke();
    GetNode<Button>("%ManufactureButton").Pressed += () =>
    {
      // Disabled controls can still receive programmatic signals.
      if (!GetNode<Button>("%ManufactureButton").Disabled)
        _selection.IfSome(option => ManufactureRequested?.Invoke(option.Project.Item));
    };
    RefreshSelection();
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

  private void RebuildItems()
  {
    var list = GetNode<VBoxContainer>("%ManufacturableItems");
    foreach (Node child in list.GetChildren())
    {
      list.RemoveChild(child);
      child.QueueFree();
    }
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
      option => $"Duration: {ProjectTimeText.Duration(option.Project.DurationDays)}", () => "");
    GetNode<Label>("%Stock").Text = _selection.Match(
      option => option.Project.UnlimitedStock ? "Stock: Not established" : $"Stock: {option.Remaining}", () => "");
    GetNode<Label>("%SupplyEffect").Text = _selection.Match(
      option => option.Project.UnlimitedStock ? "Establishes unlimited supply." : "Produces 1 item.", () => "");
    GetNode<Button>("%ManufactureButton").Disabled = _active.IsSome || _selection.IsNone;
  }
}
