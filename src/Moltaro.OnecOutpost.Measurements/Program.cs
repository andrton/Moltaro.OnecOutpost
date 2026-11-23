using System.Runtime.InteropServices;
using Microsoft.Win32;
using Moltaro.OnecOutpost.Measurements;
using Moltaro.OnecOutpost.Server;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;

// The measurement of the latency target of concept §11, run by hand from src:
//
//     dotnet run -c Release --project Moltaro.OnecOutpost.Measurements
//
// It starts PostgreSQL in a container, as the tests do, so it needs Docker. On loopback, without 1C, it measures each
// case: one node, then two that share a store and a Signals host, with the call on the first and the agent on the
// second; each with the default PollTimeout and with 5 minutes. It prints the machine, never its host name, then p50,
// p95 and the maximum of each time in milliseconds, as plain text. Exit code 0 when every case ran; any failure ends it
// with the unhandled exception and a code other than 0.
const int WarmUp = 50;
const int Count = 500;

CancellationToken cancellationToken = CancellationToken.None;
TimeSpan[] pollTimeouts = [new OutpostServerOptions().PollTimeout, TimeSpan.FromMinutes(5)];

Console.WriteLine("Latency on loopback without 1C (concept §11: 100 ms at p95)");
Console.WriteLine($"Processor: {Processor()}, {Environment.ProcessorCount} logical processors");
Console.WriteLine($"Operating system: {RuntimeInformation.OSDescription}");
Console.WriteLine($".NET: {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"Rounds per case: {Count}, after {WarmUp} of warm-up");
Console.WriteLine();
Console.WriteLine($"{"Nodes",-6}{"PollTimeout",-13}{"Time",-10}{"p50 ms",9}{"p95 ms",9}{"max ms",9}");

await using PostgreSqlFixture postgres = new();

foreach (TimeSpan pollTimeout in pollTimeouts)
{
    void Configure(OutpostServerOptions options) => options.PollTimeout = pollTimeout;

    string suffix = ((int)pollTimeout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);

    await using (LoopbackServer node = await LoopbackServer.StartAsync(
        postgres,
        "measure_one_" + suffix,
        Configure,
        cancellationToken))
    {
        Report(1, pollTimeout, await Rounds.MeasureAsync(node, node, WarmUp, Count, cancellationToken));
    }

    await using (TwoNodes nodes = await TwoNodes.StartAsync(
        postgres,
        "measure_two_" + suffix,
        Configure,
        cancellationToken))
    {
        Report(2, pollTimeout, await Rounds.MeasureAsync(nodes.First, nodes.Second, WarmUp, Count, cancellationToken));
    }
}

return 0;

static void Report(int nodes, TimeSpan pollTimeout, (List<TimeSpan> ToPoll, List<TimeSpan> ToCaller) times)
{
    Line(nodes, pollTimeout, "to poll", times.ToPoll);
    Line(nodes, pollTimeout, "to caller", times.ToCaller);
}

static void Line(int nodes, TimeSpan pollTimeout, string time, List<TimeSpan> values)
{
    List<double> sorted = [.. values.Select(value => value.TotalMilliseconds).Order()];

    Console.WriteLine(
        $"{nodes,-6}{pollTimeout,-13:c}{time,-10}{Percentile(sorted, 0.50),9:F2}{Percentile(sorted, 0.95),9:F2}{sorted[^1],9:F2}");
}

// The nearest-rank percentile: the smallest value that at least this share of the values does not exceed.
static double Percentile(List<double> sorted, double share)
{
    int rank = (int)Math.Ceiling(share * sorted.Count);

    return sorted[rank - 1];
}

// The name of the processor where the system gives one, else its architecture.
static string Processor()
{
    if (OperatingSystem.IsWindows())
    {
        object? name = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "ProcessorNameString",
            null);

        if (name is string text)
        {
            return text.Trim();
        }
    }

    return RuntimeInformation.ProcessArchitecture.ToString();
}
