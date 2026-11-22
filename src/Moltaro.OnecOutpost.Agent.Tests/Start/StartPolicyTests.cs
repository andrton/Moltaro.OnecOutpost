using System.Text;
using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Agent.Tests.AuditEntries;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Start;

// policy.json at a start, its last step (the S6a brief, "Settled"): an invalid policy stops the agent, recorded as
// started with an error and then policy with valid false, and nothing is served or recovered until a start with a valid
// policy; the file is read as UTF-8, a byte order mark skipped and not counted in the columns of an error, and bytes
// that are not UTF-8 make it invalid with an empty path; a mode that the policy serves without its password stops the
// start too. started lists the bases that write without safe mode.
public sealed class StartPolicyTests
{
    private static readonly byte[] ByteOrderMark = [0xEF, 0xBB, 0xBF];

    private readonly PairedFolder _folder = PairedFolder.Create();

    private readonly FakeClock _clock = new(TestTimes.Start);

    // Its outbox waits on the disk for a start with a valid policy: the received Write before the start gets no
    // outcome yet.
    [Fact]
    public void An_invalid_policy_serves_nothing_and_is_recorded_as_started_with_an_error_then_policy_not_valid()
    {
        const string Invalid = """{ "bases": [ { "key": "accounting", "server": "", "database": "accounting", "read": { "account": "outpost-read" } } ] }""";
        string readerError = Assert.Throws<AgentPolicyException>(() => AgentPolicyReader.Read(Invalid)).Message;
        Guid write = Guid.NewGuid();
        _folder.Data.Audit.Seed(FileOf(TestTimes.Start.AddDays(-1)), Received(TestTimes.Start.AddDays(-1), write, OutpostModeEnum.Write));
        _folder.Data.Root.Seed("policy.json", Invalid);
        int count = _folder.Data.Changes.Count;

        List<JsonElement> entries = StoppedStart.Of(_folder, _clock);

        Assert.Equal(["started", "policy"], entries.Select(Kind));
        Assert.False(entries[1].GetProperty("valid").GetBoolean());
        Assert.Contains(readerError, entries[1].GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_folder.Data.LinesSince(count), line => line.StartsWith("replace outcomes", StringComparison.Ordinal));
        Assert.False(_folder.Data.Outcomes.Has($"{write}.json"));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("unreadable")]
    public void A_policy_that_cannot_be_read_stops_the_start_and_is_recorded_as_not_valid(string fault)
    {
        if (fault == "absent")
        {
            _folder.Data.Root.Unseed("policy.json");
        }
        else
        {
            _folder.Data.Root.FailOpenRead("policy.json");
        }

        List<JsonElement> entries = StoppedStart.Of(_folder, _clock);

        Assert.Equal(["started", "policy"], entries.Select(Kind));
        Assert.False(entries[1].GetProperty("valid").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(entries[1].GetProperty("error").GetString()));
    }

    [Fact]
    public void A_policy_that_starts_with_a_byte_order_mark_is_read()
    {
        _folder.Data.Root.Seed("policy.json", [.. ByteOrderMark, .. Encoding.UTF8.GetBytes(PairedFolder.Policy)]);

        _folder.Start(_clock).Log.Dispose();
    }

    // The comment starts at the third character of the first line, counted without the mark.
    [Fact]
    public void A_byte_order_mark_is_not_counted_in_the_columns_of_an_error()
    {
        _folder.Data.Root.Seed("policy.json", [.. ByteOrderMark, .. """{ /* the bases */ "bases": [] }"""u8]);

        List<JsonElement> entries = StoppedStart.Of(_folder, _clock);

        string error = entries[1].GetProperty("error").GetString()!;
        Assert.Matches(@"\bline 1\b", error);
        Assert.Matches(@"\bcolumn 3\b", error);
    }

    // Decoded leniently, the byte 0xFF in the folder of the base would become U+FFFD and the file a valid policy. The
    // fault is the file as a whole, so the error names no member.
    [Fact]
    public void Bytes_that_are_not_UTF8_make_the_policy_invalid_with_an_empty_path()
    {
        byte[] policy =
        [
            .. "{ \"bases\": [ { \"key\": \"warehouse\", \"file\": \"D:\\\\1C\\\\"u8,
            0xFF,
            .. "\", \"read\": { \"account\": \"outpost-read\" } } ] }"u8,
        ];
        _folder.Data.Root.Seed("policy.json", policy);
        _folder.SeedSecrets(("warehouse", "read", "read-pw"));

        List<JsonElement> entries = StoppedStart.Of(_folder, _clock);

        Assert.Equal(["started", "policy"], entries.Select(Kind));
        Assert.False(entries[1].GetProperty("valid").GetBoolean());
        Assert.DoesNotContain("bases[0]", entries[1].GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    // The console's secret writes the passwords after pair; a start without one would only fail later, in every
    // command of that mode.
    [Fact]
    public void A_mode_that_the_policy_serves_without_its_password_stops_the_start_naming_the_base_and_the_mode()
    {
        _folder.SeedSecrets(("accounting", "read", "read-pw"), ("warehouse", "read", ""));

        string error = StoppedStart.Error(StoppedStart.Of(_folder, _clock));

        Assert.Contains("accounting", error, StringComparison.Ordinal);
        Assert.Contains("write", error, StringComparison.Ordinal);
    }

    // The owner's change of D12: started records which bases write without safe mode, in the order of the policy.
    [Fact]
    public void Started_lists_the_bases_that_write_without_safe_mode_in_the_order_of_the_policy()
    {
        _folder.Data.Root.Seed(
            "policy.json",
            """
            {
              "bases": [
                { "key": "payroll", "file": "D:\\1C\\Payroll", "read": { "account": "r" }, "write": { "account": "w", "safeMode": false } },
                { "key": "warehouse", "file": "D:\\1C\\Warehouse", "read": { "account": "r" } },
                { "key": "accounting", "file": "D:\\1C\\Accounting", "read": { "account": "r" }, "write": { "account": "w", "safeMode": false } },
                { "key": "stock", "file": "D:\\1C\\Stock", "read": { "account": "r" }, "write": { "account": "w", "safeMode": true } }
              ]
            }
            """);
        _folder.SeedSecrets(
            ("payroll", "read", "pw"),
            ("payroll", "write", "pw"),
            ("warehouse", "read", "pw"),
            ("accounting", "read", "pw"),
            ("accounting", "write", "pw"),
            ("stock", "read", "pw"),
            ("stock", "write", "pw"));
        int count = _folder.Data.Changes.Count;

        _folder.Start(_clock).Log.Dispose();

        JsonElement started = AppendedSince(_folder.Data, count)[0];
        Assert.Equal("started", Kind(started));
        Assert.Equal(["payroll", "accounting"], Strings(started.GetProperty("withoutSafeMode")));
    }
}
