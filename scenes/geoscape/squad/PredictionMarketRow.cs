using System;
using System.Collections.Generic;
using CampaignGameState = FunProject.GameState.GameState;
using FunProject.Currencies;
using FunProject.Strategic;
using Godot;

namespace FunProject.Geoscape;

public sealed partial class PredictionMarketRow : VBoxContainer
{
  private PredictionMarket _market = null!;
  private readonly List<Currency> _currencies = [];
  private OptionButton CurrencyInput => GetNode<OptionButton>("%Currency");
  private SpinBox Stake => GetNode<SpinBox>("%Stake");
  public event Action? Changed;

  public Option<MissionPrediction> SelectedPrediction => Stake.Value > 0 && CurrencyInput.Selected >= 0
    ? Some(new MissionPrediction(_market, _currencies[CurrencyInput.Selected], (ulong)Stake.Value)) : None;

  public void Configure(PredictionMarket market, CampaignGameState state)
  {
    _market = market;
    GetNode<Label>("%Description").Text = $"{market.Description} - {market.Multiplier}x total return";
    Stake.MaxValue = market.MaxStake;
    Stake.ValueChanged += _ => Changed?.Invoke();
    CurrencyInput.ItemSelected += _ => Changed?.Invoke();
    Refresh(state);
  }

  public void Refresh(CampaignGameState state)
  {
    foreach (var currency in state.Currencies)
      if (!_currencies.AsValueEnumerable().Any(existing => ReferenceEquals(existing, currency)))
      {
        _currencies.Add(currency);
        CurrencyInput.AddItem(currency.Name);
      }
    CurrencyInput.Disabled = _currencies.Count == 0;
    Stake.Editable = _currencies.Count > 0;
    if (_currencies.Count == 0)
    {
      GetNode<Label>("%Return").Text = "No currency available.";
      return;
    }
    if (CurrencyInput.Selected < 0) CurrencyInput.Select(0);
    var selected = _currencies[CurrencyInput.Selected];
    GetNode<Label>("%Return").Text = $"Return {_market.TotalReturn((ulong)Stake.Value)}\nBalance {state.GetCurrencyBalance(selected)}, cap {_market.MaxStake}";
  }
}
