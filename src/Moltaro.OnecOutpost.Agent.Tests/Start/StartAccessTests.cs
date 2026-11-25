using System.Text.Json;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeWorkers;
using static Moltaro.OnecOutpost.Agent.Tests.AuditEntries;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Start;

// The data folder admits nobody else ("The files of the agent"): a start checks the access lists of the folder, of
// audit\ and outcomes\, and of every file it reads, since a file moved into the folder keeps its own, and one that
// admits anybody else stops it, recorded with the path and the accounts. The log is read and cut before that, so the
// started of a failed check goes after a cut torn line (§13 of docs/protocol.md).
public sealed class StartAccessTests
{
    // The command whose outbox file the folder holds, named in the cases of the theory.
    private const string Command = "9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6";

    private static readonly DateTimeOffset Before = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly PairedFolder _folder = PairedFolder.Create();

    private readonly FakeClock _clock = new(TestTimes.Start);

    public StartAccessTests()
    {
        // A sent outcome not yet confirmed, so that the folder holds an audit file and an outbox file.
        Guid command = Guid.Parse(Command);
        _folder.Data.Audit.Seed(
            FileOf(Before),
            Received(Before, command, OutpostModeEnum.Write) + Ended(Before, command, "succeeded", null, 5));
        _folder.Data.Outcomes.Seed($"{Command}.json", TestOutcomes.Succeeded());
        _folder.Data.Root.Seed("notes.txt", "the administrator's notes");
    }

    [Fact]
    public void A_start_checks_the_folder_its_two_folders_and_every_file_it_reads()
    {
        _folder.Start(_clock).Log.Dispose();

        HashSet<string> expected =
        [
            "",
            "audit",
            "outcomes",
            "policy.json",
            "agent.json",
            "secrets.dat",
            @"audit\2026-11-21.jsonl",
            $@"outcomes\{Command}.json",
        ];

        Assert.Superset(expected, new HashSet<string>(_folder.Access.Asked));
    }

    [Theory]
    [InlineData("")]
    [InlineData("audit")]
    [InlineData("outcomes")]
    [InlineData("policy.json")]
    [InlineData("agent.json")]
    [InlineData("secrets.dat")]
    [InlineData(@"audit\2026-11-21.jsonl")]
    [InlineData(@"outcomes\" + Command + ".json")]
    public void An_entry_whose_access_list_admits_anybody_else_stops_the_start_naming_its_path_and_the_accounts(string path)
    {
        _folder.Access.Admit(path, @"PC-1\intruder", @"BUILTIN\Users");

        List<JsonElement> entries = StoppedStart.Of(_folder, _clock);

        string error = StoppedStart.Error(entries);
        Assert.Single(entries);
        Assert.Contains(path, error, StringComparison.Ordinal);
        Assert.Contains(@"PC-1\intruder", error, StringComparison.Ordinal);
        Assert.Contains(@"BUILTIN\Users", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_access_list_that_cannot_be_read_stops_the_start()
    {
        _folder.Access.MakeUnreadable("secrets.dat");

        Assert.Single(StoppedStart.Of(_folder, _clock));
    }

    // A torn last line, then a start that stops on an access list that admits someone else, then
    // a start with the access lists fixed: the last start runs normally.
    [Fact]
    public void After_a_torn_line_and_a_start_stopped_by_an_access_list_a_start_with_the_lists_fixed_runs_normally()
    {
        string kept = Connection(TestTimes.Start.AddHours(-1));
        _folder.Data.Audit.Seed("2026-11-22.jsonl", kept + "{\"time\":\"2026-11-22T09:15:00.000Z\",\"en");
        _folder.Access.Admit("policy.json", @"PC-1\intruder");
        int count = _folder.Data.Changes.Count;

        StoppedStart.Of(_folder, _clock);

        Assert.Equal(
            [$"cut audit\\2026-11-22.jsonl to {kept.Length}", "append audit\\2026-11-22.jsonl"],
            _folder.Data.LinesSince(count));

        _folder.Access.Fix();
        count = _folder.Data.Changes.Count;

        _folder.Start(_clock).Log.Dispose();

        Assert.Equal(["started", "policy"], KindsSince(_folder.Data, count));
        AssertNone(AppendedSince(_folder.Data, count)[0], "error");
    }
}
