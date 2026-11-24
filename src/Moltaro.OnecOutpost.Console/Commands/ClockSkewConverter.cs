namespace Moltaro.OnecOutpost.Console.Commands;

// Reads --clock-skew; CheckCommand.Settings checks the range.
internal sealed class ClockSkewConverter() : TimeOptionConverter(CheckCommand.Settings.ClockSkewRule);
