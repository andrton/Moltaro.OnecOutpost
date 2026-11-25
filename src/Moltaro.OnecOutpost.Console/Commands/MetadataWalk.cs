using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Console.Workers;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Console.Commands;

// Every Read of metadata uses MetadataScripts.Walk in a single read session at a time, in safe mode under the base's
// worker account or the console's own restricted token. Each Read uses the read mode's run limit. The census,
// then the probe, a page of the first object of the first category that has one, with the storage, which a storage
// error of its own makes unavailable to the rest of the walk; then each category from its first object in pages of 50,
// a page that gives result-too-large or no outcome within the run limit read again as two halves, the first the larger,
// down to one object; and the common storage while the storage is available. A Read that ended its worker makes the
// next one open a new session with the same account and its token; one that does not open is the last tried, and every
// Read after it is recorded as not run, with its reason. It keeps every Read, every object read and the counts of each
// category, for the report, and checks the token before each Read. A result of another form than the script's is an
// InvalidOperationException or a KeyNotFoundException, which ends metadata with Failure. The walk owns its session and
// is run from one task at a time.
internal sealed class MetadataWalk
{
    // A granularity nobody observes, since a page that does not fit an outcome or the run limit is halved (principle
    // 10).
    private const int PageSize = 50;

    private readonly List<WalkRead> _reads = [];

    private readonly List<WalkObject> _objects = [];

    private readonly List<WalkCategory> _categories = [];

    private readonly List<string> _censusErrors = [];

    private readonly ConsoleParts _parts;

    private readonly IWorkerAccount? _account;

    private readonly string _program;

    private readonly string _baseKey;

    private readonly WorkerConnectionLine _connection;

    private readonly TimeSpan _runLimit;

    // The session of the Reads, null before the first open and after one that failed.
    private WorkerSession? _session;

    // The reason of the session that did not open, after which no Read runs.
    private string? _unopened;

    // The failed probe object, for the first slot of its category.
    private WalkObject? _failedProbe;

    // The first page that was not read, and whether any was.
    private WalkRead? _firstFailedPage;

    private bool _pageRead;

    public MetadataWalk(ConsoleParts parts, string program, AgentBasePolicy served, string password, IWorkerAccount? account)
    {
        _parts = parts;
        _program = program;
        _baseKey = served.Key;
        _account = account;
        _runLimit = served.Read.RunLimit;
        _connection = WorkerConnectionLine.For(
            served,
            served.Read with { SafeMode = true },
            password,
            ProgramFolderLayout.ExecutorOf(parts.ProgramFolder));
    }

    // What the census gave: the version of the platform and the configuration's name, version and compatibility mode.
    public JsonElement Platform { get; private set; }

    public JsonElement Configuration { get; private set; }

    public IReadOnlyList<string> CensusErrors => _censusErrors;

    // The reason the probe gave when its storage inspection could not complete, null while storage is available.
    public string? StorageReason { get; private set; }

    // The tables of no object of the fifteen categories, null when they were not read.
    public IReadOnlyList<JsonElement>? CommonStorage { get; private set; }

    public IReadOnlyList<WalkRead> Reads => _reads;

    // The objects read, each category's in its order, in the census's order of the categories.
    public IReadOnlyList<WalkObject> Objects => _objects;

    public IReadOnlyList<WalkCategory> Categories => _categories;

    // What the progress of the pages and the common storage counts: every object found, and the common storage.
    public int Steps => _categories.Sum(category => category.Found) + (StorageReason is null ? 1 : 0);

    // Null once a page was read; otherwise how the first page ended, as step 9 names it.
    public string? NoPageRead
    {
        get
        {
            if (_pageRead || _firstFailedPage is not WalkRead first)
            {
                return null;
            }

            string objects = first.Count == 1 ? "1 object" : $"{first.Count} objects";

            return $"the page of {first.Category} from {first.From}, {objects}, {first.Ended(_runLimit)}";
        }
    }

    // A session of the base, the first of step 5 and each that replaces one whose worker ended: null once it opened,
    // otherwise why not, a start that Windows refused included.
    public async Task<string?> OpenAsync(CancellationToken cancellationToken)
    {
        try
        {
            (_session, string? reason) = await WorkerSession.OpenAsync(
                _parts.Workers,
                _account,
                _program,
                _baseKey,
                _connection,
                _runLimit,
                _parts.Clock,
                cancellationToken);

            return reason;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException)
        {
            return $"{_program} did not start: {CommandEndException.ReasonOf(exception)}";
        }
    }

    // The census, {"read":"census"}: the platform, the configuration and the count of each category. One that does not
    // succeed ends metadata with ReportFailed.
    public async Task CensusAsync(CancellationToken cancellationToken)
    {
        (WalkRead read, JsonElement? result) = await ReadAsync(new JsonObject { ["read"] = "census" }, "census", null, null, null, cancellationToken);

        if (result is not JsonElement census)
        {
            throw ReportFailed($"the census {read.Ended(_runLimit)}");
        }

        Platform = ReadCensusValue(census, "platform");
        Configuration = census.GetProperty("configuration");
        ReadCensusValue(Configuration, "name");
        ReadCensusValue(Configuration, "version");
        ReadCensusValue(Configuration, "compatibilityMode");

        foreach (JsonElement category in census.GetProperty("categories").EnumerateArray())
        {
            string name = TextOf(category, "category") ?? throw new InvalidOperationException("The census gave a category without a name.");

            _categories.Add(new WalkCategory(name, category.GetProperty("count").GetInt32()));
        }
    }

    // The probe: a page of the first object of the first category that has one, with the storage, which runs before the
    // walk so that a base whose objects cannot be read gives no report in which each object carries the same failure.
    // A probe Read that does not succeed ends metadata with ReportFailed, as does a configuration without an object.
    // The storage error makes the storage unavailable to the walk. A failed probe object keeps its error and supplies
    // its readable data when its regular page has none.
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        if (_categories.FirstOrDefault(category => category.Found > 0) is not WalkCategory first)
        {
            throw ReportFailed("the configuration has no object to read");
        }

        (WalkRead read, JsonElement? result) = await ReadPageAsync(first.Name, 0, 1, storage: true, cancellationToken);

        if (result is not JsonElement page)
        {
            throw ReportFailed($"the probe, the first object of {first.Name}, {read.Ended(_runLimit)}");
        }

        JsonElement objects = page.GetProperty("objects");

        if (objects.GetArrayLength() == 0)
        {
            throw ReportFailed("the configuration has no object to read");
        }

        JsonElement item = objects[0];

        if (TextOf(item, "error") is string error)
        {
            _failedProbe = new WalkObject(first.Name, item, error);
        }

        StorageReason = TextOf(page, "storageError");
    }

    // Steps 7 and 8: each category in pages, then the common storage while the storage is available, each step of the
    // progress given to advance as it ends: the objects of a page read or recorded as failed, and the common storage.
    public async Task PagesAsync(Action<int> advance, CancellationToken cancellationToken)
    {
        foreach (WalkCategory category in _categories)
        {
            for (int from = 0; from < category.Found; from += PageSize)
            {
                await PageAsync(category, from, Math.Min(PageSize, category.Found - from), advance, cancellationToken);
            }
        }

        if (StorageReason is not null)
        {
            return;
        }

        await ReadCommonStorageAsync(cancellationToken);
        advance(1);
    }

    // The full storage read can see tables omitted by scoped page reads. Ask for unseen rows and attach those whose
    // owners have readable storage. The common storage holds the other rows returned by the Read.
    private async Task ReadCommonStorageAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, WalkObject> owners = new(StringComparer.Ordinal);
        HashSet<string> seenStorageNames = new(StringComparer.Ordinal);

        foreach (WalkObject item in _objects)
        {
            JsonElement storage = item.Item.GetProperty("storage");

            if (storage.ValueKind != JsonValueKind.Array || TextOf(item.Item, "fullName") is not { Length: > 0 } fullName)
            {
                continue;
            }

            owners.TryAdd(fullName, item);

            foreach (JsonElement table in storage.EnumerateArray())
            {
                string name = TextOf(table, "storageName") ?? throw new InvalidOperationException("The page gave a storage table without a name.");
                seenStorageNames.Add(name);
            }
        }

        JsonObject parameters = new()
        {
            ["read"] = "common",
            ["owners"] = new JsonArray([.. owners.Keys.Select(name => JsonValue.Create(name))]),
            ["seenStorageNames"] = new JsonArray([.. seenStorageNames.Select(name => JsonValue.Create(name))]),
        };
        (_, JsonElement? result) = await ReadAsync(parameters, "common", null, null, null, cancellationToken);

        if (result is not JsonElement rows)
        {
            return;
        }

        List<JsonElement> common = [];

        foreach (JsonElement row in rows.EnumerateArray())
        {
            JsonElement table = row.GetProperty("table");

            if (TextOf(row, "owner") is string owner)
            {
                (owners[owner].AdditionalStorage ??= []).Add(table);
            }
            else
            {
                common.Add(table);
            }
        }

        CommonStorage = common;
    }

    // Releases the current session before opening another one or writing the report.
    public ValueTask CloseSessionAsync()
    {
        WorkerSession? session = _session;
        _session = null;

        return session?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private static CommandEndException ReportFailed(string reason)
    {
        return new CommandEndException(ConsoleExitCodes.ReportFailed, reason);
    }

    // The text of a member, null for null; an absent member is a KeyNotFoundException, one of another kind an
    // InvalidOperationException.
    private static string? TextOf(JsonElement element, string member)
    {
        JsonElement value = element.GetProperty(member);

        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    // A census value retains the string or error object from the script. The caption names each error in report order.
    private JsonElement ReadCensusValue(JsonElement census, string property)
    {
        JsonElement value = census.GetProperty(property);

        if (value.ValueKind == JsonValueKind.Object && TextOf(value, "error") is string error)
        {
            _censusErrors.Add($"{property} ({error})");
        }
        else if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"The census did not give a value or an error for {property}.");
        }

        return value;
    }

    // One page of the category, halved depth first while it does not fit, its first half the larger.
    private async Task PageAsync(WalkCategory category, int from, int count, Action<int> advance, CancellationToken cancellationToken)
    {
        (WalkRead read, JsonElement? result) = await ReadPageAsync(category.Name, from, count, StorageReason is null, cancellationToken);
        WalkObject? failedProbe = from == 0 && _failedProbe?.Category == category.Name ? _failedProbe : null;

        if (result is JsonElement page)
        {
            Add(category, page, failedProbe);
            _pageRead = true;
            advance(count);

            return;
        }

        if (read.DoesNotFit && count > 1)
        {
            int first = (count + 1) / 2;

            await PageAsync(category, from, first, advance, cancellationToken);
            await PageAsync(category, from + first, count - first, advance, cancellationToken);

            return;
        }

        if (failedProbe is not null)
        {
            _objects.Add(failedProbe);
        }

        category.Failed += count;
        _firstFailedPage ??= read;
        advance(count);
    }

    // The objects of a page read, with the probe's error on its original slot and any error given by this page.
    // An empty page can still record its failed probe. The page's storage error applies to objects with no error of
    // their own.
    private void Add(WalkCategory category, JsonElement page, WalkObject? failedProbe)
    {
        string? storageError = TextOf(page, "storageError");

        foreach (JsonElement item in page.GetProperty("objects").EnumerateArray())
        {
            string? error = TextOf(item, "error");

            if (failedProbe?.Error is string probeError && probeError != error)
            {
                error = error is null ? probeError : $"{probeError}; {error}";
            }

            failedProbe = null;

            if (error is null && storageError is not null)
            {
                error = $"storage: {storageError}";
            }

            _objects.Add(new WalkObject(category.Name, item, error));

            if (error is null)
            {
                category.Read++;
            }
            else
            {
                category.Failed++;
            }
        }

        if (failedProbe is not null)
        {
            _objects.Add(failedProbe);
            category.Failed++;
        }
    }

    private Task<(WalkRead Read, JsonElement? Result)> ReadPageAsync(
        string category,
        int from,
        int count,
        bool storage,
        CancellationToken cancellationToken)
    {
        JsonObject parameters = new()
        {
            ["read"] = "page",
            ["category"] = category,
            ["from"] = from,
            ["count"] = count,
            ["storage"] = storage,
        };

        return ReadAsync(parameters, "page", category, from, count, cancellationToken);
    }

    // One Read of the walk with the parameters, recorded with the bytes of its outcome, and its result when it
    // succeeded. A session whose worker ended is replaced first; once one did not open, the Read is recorded as not run.
    private async Task<(WalkRead Read, JsonElement? Result)> ReadAsync(
        JsonObject parameters,
        string name,
        string? category,
        int? from,
        int? count,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_unopened is null && _session is not { Ended: false })
        {
            await CloseSessionAsync();
            _unopened = await OpenAsync(cancellationToken);
        }

        WalkRead read;
        JsonElement? result = null;

        if (_unopened is string reason)
        {
            read = new WalkRead(name, category, from, count, 0, OutpostAgentOutcomeKindEnum.Failed, null, reason);
        }
        else
        {
            (OutpostOutcomeMessage? outcome, long size) = await _session!.ReadAsync(MetadataScripts.Walk, parameters.ToJsonString(), cancellationToken);

            if (outcome is null)
            {
                read = new WalkRead(name, category, from, count, 0, OutpostAgentOutcomeKindEnum.Failed, OutpostOutcomeCodes.RunLimit, null);
            }
            else
            {
                read = new WalkRead(name, category, from, count, size, outcome.Kind, outcome.Code, outcome.ErrorText);
                result = read.Succeeded ? outcome.Result : null;
            }
        }

        _reads.Add(read);

        return (read, result);
    }
}
