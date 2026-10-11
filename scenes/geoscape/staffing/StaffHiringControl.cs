using System.Globalization;
using FunProject.Currencies;
using FunProject.Staffing;
using FunProject.Strategic;
using Godot;
using CampaignGameState = FunProject.GameState.GameState;

namespace FunProject.Geoscape;

public sealed partial class StaffHiringControl : Control
{
  [Export] public StaffRole Role { get; set; }
  [Signal] public delegate void HireRequestedEventHandler(int quantity);
  private CampaignGameState? _state;
  private GeoscapeSession? _session;
  private int _quantity;

  public override void _Ready()
  {
    var quantity = GetNode<SpinBox>("%HireQuantity");
    _quantity = (int)quantity.Value;
    quantity.ValueChanged += value => { _quantity = (int)value; Refresh(); };
    quantity.GetLineEdit().TextChanged += text => { _quantity = ParseQuantity(text); Refresh(); };
    quantity.GetLineEdit().TextSubmitted += _ => { _quantity = (int)quantity.Value; Refresh(); };
    quantity.GetLineEdit().FocusExited += () => { _quantity = (int)quantity.Value; Refresh(); };
    GetNode<Button>("%HireButton").Pressed += () =>
    {
      int requested = ParseQuantity(quantity.GetLineEdit().Text);
      Refresh(requested);
      if (GetNode<Button>("%HireButton").Disabled) return;
      _quantity = requested;
      quantity.Value = requested;
      EmitSignal(SignalName.HireRequested, requested);
    };
    Refresh();
  }

  private static int ParseQuantity(string text) => int.TryParse(text,
    NumberStyles.None, CultureInfo.InvariantCulture, out int quantity) ? quantity : 0;

  public void Present(CampaignGameState state, GeoscapeSession session)
  {
    _state = state;
    _session = session;
    Refresh();
  }

  private void Refresh(int? quantity = null)
  {
    var hire = GetNode<Button>("%HireButton");
    string roleName = Role == StaffRole.Researcher ? "Researchers" : "Engineers";
    hire.Text = $"Hire {roleName}";
    int count = Role == StaffRole.Researcher ? _state?.Staff.Researchers ?? 0 : _state?.Staff.Engineers ?? 0;
    long rate = Role == StaffRole.Researcher ? _state?.Staff.ResearchRateUnits ?? WorkProgress.RateScale
      : _state?.Staff.ManufacturingRateUnits ?? WorkProgress.RateScale;
    GetNode<Label>("%StaffCount").Text = $"{roleName}: {count}";
    GetNode<Label>("%WorkRate").Text = $"Work rate: {Multiplier(rate)}x";
    var cost = GetNode<Label>("%HireCosts");
    var result = _session?.GetHiringOption(Role, quantity ?? _quantity);
    var option = result?.Match<StaffHiringOption?>(Right: value => value, Left: _ => null);
    if (option is not null)
    {
      cost.Text = Currency.FormatCosts(option.Costs) + (option.Affordable ? "" : "\nInsufficient funds.");
      GetNode<Label>("%WorkRate").Text += $" (+{Multiplier(option.BonusUnits)}x per hire)";
      hire.Disabled = !option.Affordable;
    }
    else
    {
      cost.Text = result?.Match(
        Right: _ => "", Left: reason => reason == StaffHireFailure.NotConfigured
          ? "Hiring unavailable: no staffing rules." : $"Hiring unavailable: {reason}.") ?? "Hiring unavailable.";
      hire.Disabled = true;
    }
    if (hire.Disabled) cost.AddThemeColorOverride("font_color", Colors.Red);
    else cost.RemoveThemeColorOverride("font_color");
  }

  private static string Multiplier(long units)
    => ((decimal)units / WorkProgress.RateScale).ToString("0.0###", CultureInfo.InvariantCulture);
}
