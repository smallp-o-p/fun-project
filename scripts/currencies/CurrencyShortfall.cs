namespace FunProject.Currencies;

public sealed record CurrencyShortfall(Currency Currency, long Required, long Available);
