using System.Globalization;
using FunProject.Currencies;
using FunProject.Staffing;
using Godot;

namespace FunProject.Geoscape;

public sealed partial class StaffHiringControl : Control
{
  [Export] public StaffRole Role { get; set; }
  [Signal] public delegate void QuantityChangedEventHandler();
  [Signal] public delegate void HireRequestedEventHandler(uint quantity);
  public uint Quantity { get; private set; }

  public override void _Ready()
  {
    var quantity = GetNode<SpinBox>("%HireQuantity");
    Quantity = (uint)quantity.Value;
    quantity.ValueChanged += value => ChangeQuantity((uint)value);
    quantity.GetLineEdit().TextChanged += text => ChangeQuantity(ParseQuantity(text));
    quantity.GetLineEdit().TextSubmitted += _ => ChangeQuantity((uint)quantity.Value);
    quantity.GetLineEdit().FocusExited += () => ChangeQuantity((uint)quantity.Value);
    GetNode<Button>("%HireButton").Pressed += () =>
    {
      ChangeQuantity(ParseQuantity(quantity.GetLineEdit().Text));
      if (GetNode<Button>("%HireButton").Disabled) return;
      quantity.Value = Quantity;
      EmitSignal(SignalName.HireRequested, Quantity);
    };
  }

  private void ChangeQuantity(uint quantity)
  {
    Quantity = quantity;
    EmitSignal(SignalName.QuantityChanged);
  }

  private static uint ParseQuantity(string text) => uint.TryParse(text,
    NumberStyles.None, CultureInfo.InvariantCulture, out uint quantity) ? quantity : 0;

  public void Present(StaffHiringOption option)
  {
    string roleName = Role == StaffRole.Researcher ? "Researchers" : "Engineers";
    var hire = GetNode<Button>("%HireButton");
    hire.Text = $"Hire {roleName}";
    hire.Disabled = !option.Affordable;
    GetNode<Label>("%StaffCount").Text = $"{roleName}: {option.Count}";
    GetNode<Label>("%WorkRate").Text = $"Work rate: {option.Speed.ToString("0.0###", CultureInfo.InvariantCulture)}x" +
      $" (+{(option.SpeedBonus * 100).ToString("0.###", CultureInfo.InvariantCulture)}% per hire)";
    var cost = GetNode<Label>("%HireCosts");
    cost.Text = Quantity == 0 ? "Enter a positive whole quantity." : Currency.FormatCosts(option.Costs) +
      (option.Affordable ? "" : "\nInsufficient funds.");
    if (hire.Disabled) cost.AddThemeColorOverride("font_color", Colors.Red);
    else cost.RemoveThemeColorOverride("font_color");
  }
}
