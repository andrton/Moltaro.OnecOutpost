using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// The line Subscriptions of check (the S10b brief, "check", and concept §1 and §12, O1), for each base with write: the
// event subscriptions that an external connection can raise, all but those to FormGetProcessing, whose handler's module
// is no common module or one that an external connection lacks, as the read account's second script gives them, each
// as subscription, event, module and whether a common module of that name was found. A Write that reaches one fails, so
// each is named with its event and its module.
public sealed class SubscriptionsLineTests : IDisposable
{
    private readonly CheckedMachine _checked = new();

    private TestMachine Machine => _checked.Machine;

    public void Dispose()
    {
        _checked.Dispose();
    }

    [Fact]
    public async Task A_subscription_whose_common_module_an_external_connection_lacks_fails_the_line_naming_it_its_event_and_its_module()
    {
        Machine.Workers.Sessions[("accounting", "outpost-read")].Gives(
            FakeCheckProcess.SubscriptionsScript,
            new JsonArray(Subscription("NoteBeforeWrite", "BeforeWrite", "NoteChecks", found: true)));

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal(
            "a Write that reaches these fails: NoteBeforeWrite (BeforeWrite) in NoteChecks; set \"External connection\" of each module",
            run.Failed(CheckLineEnum.Subscriptions, "accounting"));
        Assert.Single(run.Rows, row => !row.Passed);
    }

    [Fact]
    public async Task A_subscription_whose_handler_names_no_common_module_is_named_as_such_beside_the_others()
    {
        Machine.Workers.Sessions[("accounting", "outpost-read")].Gives(
            FakeCheckProcess.SubscriptionsScript,
            new JsonArray(
                Subscription("NoteBeforeWrite", "BeforeWrite", "NoteChecks", found: true),
                Subscription("StockOnWrite", "OnWrite", "StockEvents", found: false)));

        CheckRun run = await _checked.RunAsync();

        string reason = run.Failed(CheckLineEnum.Subscriptions, "accounting");
        Assert.StartsWith("a Write that reaches these fails: NoteBeforeWrite (BeforeWrite) in NoteChecks, ", reason, StringComparison.Ordinal);
        Assert.Contains("StockOnWrite (OnWrite) in StockEvents, which is not a common module", reason, StringComparison.Ordinal);
        Assert.EndsWith("; set \"External connection\" of each module", reason, StringComparison.Ordinal);
    }

    // A base that the policy lets be read alone takes no Write, so its subscriptions are not judged.
    [Fact]
    public async Task A_base_without_write_has_no_line_of_its_subscriptions()
    {
        CheckRun run = await _checked.RunAsync();

        Assert.Single(run.RowsOf(CheckLineEnum.Subscriptions));
        run.Passed(CheckLineEnum.Subscriptions, "accounting");
    }

    private static JsonObject Subscription(string name, string eventName, string module, bool found)
    {
        return new JsonObject { ["subscription"] = name, ["event"] = eventName, ["module"] = module, ["found"] = found };
    }
}
