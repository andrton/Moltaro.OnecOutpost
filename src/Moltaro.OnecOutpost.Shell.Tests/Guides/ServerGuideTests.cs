using System.Text.Json;
using System.Text.RegularExpressions;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Shell.Tests.Sessions;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Guides;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using Moltaro.OnecOutpost.TestSupport.Programs;
using Moltaro.OnecOutpost.TestSupport.RunningAgent;

namespace Moltaro.OnecOutpost.Shell.Tests.Guides;

[Trait("Requires", "PostgreSQL")]
public sealed class ServerGuideTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_first_session_in_the_guide_pairs_lists_reads_and_writes_using_the_commands_in_the_markdown()
    {
        ShellGuide guide = ShellGuide.Read();
        string[] commands = [.. guide.Commands];
        Assert.Equal(4, commands.Length);
        Assert.StartsWith("token ", commands[0], StringComparison.Ordinal);
        Assert.Equal("agents", commands[1]);
        var read = ShellGuide.ReadFrame(commands[2]);
        var write = ShellGuide.ReadFrame(commands[3]);
        Assert.Equal("read", read.Mode);
        Assert.Equal("write", write.Mode);
        Assert.Equal(read.BaseKey, write.BaseKey);

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connection = await ShellDatabase.CreateAsync(postgres, cancellationToken);
        using TemporaryFolder keys = new();
        await using ShellSession shell = StartGuide(guide, connection, keys);
        Uri address = await shell.AddressAsync();
        await shell.SendAsync(commands[0] + "\n");
        LoopbackAgent agent = await LoopbackAgent.PairAsync(address, await shell.TokenAsync(), read.BaseKey, cancellationToken);
        await using HostRun run = agent.Start(TimeProvider.System);
        await agent.WaitForConnectionAsync();

        await shell.SendAsync(string.Join('\n', commands[1..]) + "\n" + commands[3] + "\n");
        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(0, end.ExitCode);
        Assert.Contains("Paired", end.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Connected", end.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(read.BaseKey, end.Output, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(end.Output, @"\bSucceeded\b", RegexOptions.IgnoreCase).Count);
        Assert.Equal(3, Regex.Matches(end.Output, @"""description""\s*:\s*""Test product""").Count);

        var receivedRead = Assert.Single(agent.Commands(OutpostModeEnum.Read));
        Assert.Equal(read.Script, receivedRead.Script);
        Assert.Null(receivedRead.CallerKey);
        AssertSameJson(read.Parameters, receivedRead.ParametersJson);

        var receivedWrite = Assert.Single(agent.Commands(OutpostModeEnum.Write));
        Assert.Equal(write.Script, receivedWrite.Script);
        Assert.Equal(write.CallerKey, receivedWrite.CallerKey);
        AssertSameJson(write.Parameters, receivedWrite.ParametersJson);
    }

    private static ShellSession StartGuide(ShellGuide guide, string connection, TemporaryFolder keys)
    {
        string[] command = guide.Startup.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("dotnet", command[0]);
        Assert.Equal("run", command[1]);
        int separator = Array.IndexOf(command, "--");
        Assert.True(separator > 1);

        DirectoryInfo output = new(AppContext.BaseDirectory);
        string configuration = output.Parent!.Name;
        DirectoryInfo? source = output;
        while (source is not null && !File.Exists(Path.Combine(source.FullName, "Moltaro.OnecOutpost.slnx")))
        {
            source = source.Parent;
        }

        Assert.NotNull(source);

        return ShellSession.StartDotnet(
            connection,
            source.FullName,
            [
                .. command[1..separator],
                "--no-build",
                "--configuration",
                configuration,
                .. command[separator..].Select(word => word == "<private-key-dir>" ? keys.Path : word),
            ]);
    }

    private static void AssertSameJson(string expected, string actual)
    {
        using JsonDocument expectedJson = JsonDocument.Parse(expected);
        using JsonDocument actualJson = JsonDocument.Parse(actual);
        Assert.True(JsonElement.DeepEquals(expectedJson.RootElement, actualJson.RootElement));
    }
}
