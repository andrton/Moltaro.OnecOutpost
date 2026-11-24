namespace Moltaro.OnecOutpost.Console.Commands;

// Reads --stop-timeout; ServiceSettings checks the range.
internal sealed class StopTimeoutConverter() : TimeOptionConverter(ServiceSettings.StopTimeoutRule);
