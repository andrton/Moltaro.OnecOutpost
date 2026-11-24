using Moltaro.OnecOutpost.Agent.Outcomes;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;
using Moltaro.OnecOutpost.TestSupport.FakeWorkers;

namespace Moltaro.OnecOutpost.Agent.Tests.Outcomes;

// The outbox in outcomes\ ("The audit log" of the plan, the S6a brief): one file {id}.json per command whose outcome the
// server has not confirmed, written under a temporary name and renamed. At its opening it removes every temporary file,
// torn or left by a stop, and stops on any other name; any failure of a read, a write or a removal stops the agent, as
// a failed write of the log does.
public sealed class OutboxTests
{
    private readonly MemoryDataFolder _data = new();

    // The acceptance of S6a: a torn temporary file is removed at a start.
    [Fact]
    public void Opening_removes_every_temporary_file_and_lists_the_outcome_files()
    {
        Guid kept = Guid.NewGuid();
        Guid torn = Guid.NewGuid();
        _data.Outcomes.Seed($"{kept}.json", TestOutcomes.Succeeded());
        _data.Outcomes.Seed($"{torn}.json.tmp", "{\"kind\":\"succ");
        _data.Outcomes.Seed("left-by-a-stop.tmp", "");

        Outbox outbox = Outbox.Open(_data.Outcomes);

        Assert.Equal(
            [$"delete outcomes\\{torn}.json.tmp", "delete outcomes\\left-by-a-stop.tmp"],
            _data.LinesSince(0).Order(StringComparer.Ordinal));
        Assert.Equal([kept], outbox.Commands);
    }

    // Only the agent writes outcomes\, and only files of its form: anything else was planted or is not understood.
    [Theory]
    [InlineData("notes.txt")]
    [InlineData("9C1E2F3A-4B5C-4D6E-8F70-A1B2C3D4E5F6.json")]
    [InlineData("9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6.json.bak")]
    [InlineData("{9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6}.json")]
    public void Opening_stops_the_agent_on_a_file_of_any_other_name_naming_it(string name)
    {
        _data.Outcomes.Seed(name, "{}");

        AgentStopException exception = Assert.Throws<AgentStopException>(() => Outbox.Open(_data.Outcomes));

        Assert.Contains($"outcomes\\{name}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Put_writes_the_file_of_the_command_with_Replace_Read_gives_its_bytes_and_Remove_deletes_it()
    {
        Guid command = Guid.NewGuid();
        Outbox outbox = Outbox.Open(_data.Outcomes);
        byte[] outcome = TestOutcomes.Succeeded();

        outbox.Put(command, outcome);

        Assert.Equal([$"replace outcomes\\{command}.json"], _data.LinesSince(0));
        Assert.Equal(outcome, _data.Outcomes.Bytes($"{command}.json"));
        Assert.Equal(outcome, outbox.Read(command));
        Assert.Equal([command], outbox.Commands);

        outbox.Remove(command);

        Assert.Equal($"delete outcomes\\{command}.json", _data.LinesSince(1).Single());
        Assert.False(_data.Outcomes.Has($"{command}.json"));
        Assert.Empty(outbox.Commands);
    }

    // A replacement for size writes the file again in place of the first.
    [Fact]
    public void Put_replaces_the_file_that_a_command_already_has()
    {
        Guid command = Guid.NewGuid();
        Outbox outbox = Outbox.Open(_data.Outcomes);
        byte[] replacement = TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Unknown, "result-too-large");

        outbox.Put(command, TestOutcomes.Succeeded());
        outbox.Put(command, replacement);

        Assert.Equal(replacement, outbox.Read(command));
        Assert.Equal([command], outbox.Commands);
    }

    [Fact]
    public void A_file_that_cannot_be_read_stops_the_agent()
    {
        Guid command = Guid.NewGuid();
        _data.Outcomes.Seed($"{command}.json", TestOutcomes.Succeeded());
        _data.Outcomes.FailOpenRead($"{command}.json");
        Outbox outbox = Outbox.Open(_data.Outcomes);

        Assert.Throws<AgentStopException>(() => outbox.Read(command));
    }

    [Fact]
    public void A_write_that_fails_stops_the_agent_and_the_next_opening_removes_its_torn_temporary_file()
    {
        Guid command = Guid.NewGuid();
        Outbox outbox = Outbox.Open(_data.Outcomes);
        _data.Outcomes.FailNextWrite(bytesKept: 9);

        Assert.Throws<AgentStopException>(() => outbox.Put(command, TestOutcomes.Succeeded()));

        _data.Outcomes.Heal();
        int count = _data.Changes.Count;
        Outbox reopened = Outbox.Open(_data.Outcomes);

        Assert.Equal([$"delete outcomes\\{command}.json.tmp"], _data.LinesSince(count));
        Assert.Empty(reopened.Commands);
    }

    [Fact]
    public void A_replace_that_fails_stops_the_agent()
    {
        Guid command = Guid.NewGuid();
        Outbox outbox = Outbox.Open(_data.Outcomes);
        _data.Outcomes.FailReplace($"{command}.json");

        Assert.Throws<AgentStopException>(() => outbox.Put(command, TestOutcomes.Succeeded()));
    }

    [Fact]
    public void A_removal_that_fails_stops_the_agent()
    {
        Guid command = Guid.NewGuid();
        _data.Outcomes.Seed($"{command}.json", TestOutcomes.Succeeded());
        _data.Outcomes.FailDelete($"{command}.json");
        Outbox outbox = Outbox.Open(_data.Outcomes);

        Assert.Throws<AgentStopException>(() => outbox.Remove(command));
    }

    [Fact]
    public void A_temporary_file_that_cannot_be_removed_stops_the_agent_at_its_opening()
    {
        _data.Outcomes.Seed("left-by-a-stop.tmp", "");
        _data.Outcomes.FailDelete("left-by-a-stop.tmp");

        Assert.Throws<AgentStopException>(() => Outbox.Open(_data.Outcomes));
    }
}
