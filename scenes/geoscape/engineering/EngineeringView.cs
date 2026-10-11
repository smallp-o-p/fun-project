using System;
using System.Collections.Generic;
using CampaignGameState = FunProject.GameState.GameState;
using FunProject.Engineering;
using FunProject.Currencies;
using FunProject.Items;
using FunProject.Scenes.Ext;
using FunProject.Strategic;
using FunProject.Staffing;
using FunProject.Geoscape;
using Godot;

namespace FunProject.Scenes;

public sealed partial class EngineeringView : GeoscapeView
{
  private GeoscapeSession _session = null!;
  private CampaignGameState _state = null!;
  private IReadOnlyList<ManufacturingOption> _options = [];
  private Option<ManufacturingJob> _active = None;
  private Option<ManufacturingOption> _selection = None;

  public override void _Ready()
  {
    base._Ready();
    GetNode<Button>("%ManufactureButton").Pressed += OnManufacturePressed;
    GetNode<StaffHiringControl>("%StaffHiring").QuantityChanged += PresentHiring;
    GetNode<StaffHiringControl>("%StaffHiring").HireRequested += quantity =>
    {
      var result = _session.HireEngineers(quantity);
      Present(_state, _session);
      result.IfLeft(reason => GetNode<Label>("%Status").Text = $"Hiring rejected: {reason}.");
    };
    RefreshSelection();
  }

  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    _session = session;
    Present(state, session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);
    PresentHiring();
  }

  public void Present(CampaignGameState state, IReadOnlyList<ManufacturingOption> options,
    Option<ManufacturingJob> active, long tick)
  {
    _state = state;
    _options = options;
    _active = active;
    _selection = _selection.Bind(selected =>
    {
      foreach (var option in _options)
        if (ReferenceEquals(option.Project, selected.Project))
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

  public void ShowFailure(EquippableItemData item, ManufacturingStartFailure failure)
    => GetNode<Label>("%Status").Text = $"Manufacturing start rejected for {item.Name}: {failure}.";

  private void PresentHiring()
  {
    var hiring = GetNode<StaffHiringControl>("%StaffHiring");
    hiring.Present(_session.GetHiringOption(StaffRole.Engineer, hiring.Quantity));
  }

  private void OnManufacturePressed()
  {
    if (GetNode<Button>("%ManufactureButton").Disabled)
      return;
    _selection.IfSome(option =>
    {
      var result = _session.StartManufacturing(option.Project);
      Present(_state, _session);
      result.IfLeft(reason => ShowFailure(option.Project.Item, reason)); // after the refresh: it clears the status line
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
        ToggleMode = true,
        Alignment = HorizontalAlignment.Left,
        CustomMinimumSize = new Vector2(0, 36),
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
    var rows = GetNode<VBoxContainer>("%ManufacturableItems");
    for (int index = 0; index < _options.Count; index++)
    {
      int rowIndex = index;
      rows.GetChild<Button>(index).SetPressedNoSignal(_selection.Match(
        selected => ReferenceEquals(selected.Project, _options[rowIndex].Project),
        () => false));
    }
    GetNode<Label>("%ItemName").Text = _selection.Match(
      option => option.Project.Item.Name, () => "Select an item to manufacture.");
    GetNode<Label>("%ItemDescription").Text = _selection.Match(
      option => option.Project.Item.Description, () => "");
    GetNode<Label>("%ManufacturingDuration").Text = _selection.Match(
      option => $"Duration: {option.Project.DurationDays}d", () => "");
    GetNode<Label>("%Stock").Text = _selection.Match(
      option => option.Project.Item.UnlimitedStock ? "Stock: Not established" : $"Stock: {option.Remaining}", () => "");
    var costLabel = GetNode<Label>("%CurrencyCosts");
    costLabel.Text = _selection.Match(option => Currency.FormatCosts(option.Project.Costs), () => "");
    costLabel.Visible = _selection.IsSome;
    bool affordable = _selection.Exists(option => _state.CanAffordCurrencyCosts(option.Project.Costs));
    if (_selection.IsSome && !affordable)
      costLabel.AddThemeColorOverride("font_color", Colors.Red);
    else
      costLabel.RemoveThemeColorOverride("font_color");
    GetNode<Label>("%SupplyEffect").Text = _selection.Match(
      option => option.Project.Item.UnlimitedStock ? "Establishes unlimited supply." : "Produces 1 item.", () => "");
    GetNode<Button>("%ManufactureButton").Disabled = _active.IsSome || !affordable;
  }
}
