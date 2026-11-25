# Embedding the server

## A first session with the shell

The shell session runs in automated tests with PostgreSQL and a fake COM worker. The
lab ran the Read and Write scripts against the repository's 1C test base and verified
the installed agent end to end against the shell over HTTPS.

Use the shell to try one Read and one Write against the repository's test configuration.
You need .NET 10, a PostgreSQL database with UTF-8 encoding, and an agent whose local policy
opens the test base under the key `test` for Read and Write. Follow
[Installing and running an agent](agent.md) to prepare the agent machine.

Set `OUTPOST_SHELL_POSTGRES` in the shell's environment to the database connection
string. The database account needs permission to create and migrate the `outpost`
schema. Create a private directory for the signing key and substitute its path below.
From `src`, start the shell.

```text shell-start
dotnet run --project Moltaro.OnecOutpost.Shell -- --key-folder <private-key-dir> --listen http://127.0.0.1:0
```

The shell prints its agent URL after the migrations finish and the listener starts.
Port zero picks a free port on a numeric HTTP loopback address. Without `--listen` the
shell binds to `http://127.0.0.1:5000`. On subsequent starts, use the same directory so
the shell uses the signing key the agent pinned during pairing.

For an agent on another machine, set `--listen` to the concrete HTTPS origin on port
443 and give `--certificate` the path to its PFX. The agent machine must trust the
certificate for that host. Set `OUTPOST_SHELL_CERTIFICATE_PASSWORD` in the shell's
environment if the PFX requires one. Give the administrator the exact agent URL printed
by the shell, including the `/outpost` path.

In the shell, ask for a pairing token. The output includes the agent ID, the token and
its expiry. Send the token privately to the administrator and have them follow the
[pairing steps](agent.md#pairing) using the printed URL and token. Start the agent and
list it in the same shell.

```text shell
token Test base agent
```

```text shell
agents
```

Find the paired, connected agent and confirm that it holds `test` with `readOpen` and
`writeOpen` both true. Paste this Read as one block. The `.parameters` line starts its
JSON parameters and the `.` line submits the command. The shell shows `Succeeded` and
the result object with `description` set to `Test product`.

```text shell
read test
Result = New Structure("description", Parameters["description"]);
.parameters
{
  "description": "Test product"
}
.
```

Paste this Write to add one product to the test catalog. The shell shows `Succeeded`
and the description it wrote. Paste the same block again to see the retained outcome
with the same command ID. Use a new caller key when you mean to create another product.

```text shell
write test first-shell-product
Product = Catalogs.Products.CreateItem();
Product.Description = Parameters["description"];
Product.Write();
Result = New Structure("description", Product.Description);
.parameters
{
  "description": "Test product"
}
.
```

End input to stop the shell. In a terminal, use Ctrl+D on Unix or Ctrl+Z followed by
Enter on Windows. Ctrl+C cancels the shell's wait. A submitted command can still run.

### When the session fails

| What you see | What to check |
| --- | --- |
| Usage error | Check the startup options or the command header. End a Read or Write with a line containing only `.` and give `.parameters` a JSON object when you use it. |
| Infrastructure failure | Check the PostgreSQL connection and migration permissions, the signing key directory and the listener settings. Keep any passwords in the environment. |
| `Unavailable` | Start the agent and check that it is polling the printed URL. |
| `Refused` | Check the outcome code, the base key and the modes the local policy opens. For `caller-key-conflict`, use the script and parameters of the first call to retrieve its outcome. |
| `Expired` | The command did not reach the agent in time. Check the agent's connection and try again. |
| `Failed` | Read the outcome code and any error text from 1C. A failed Write made no database changes. |
| `Unknown` | Repeat the identical Write with the same caller key to read its retained outcome. Check what it did in the base before submitting a new Write. |

See [Writing scripts](scripts.md) for the script contract, outcome codes and more
examples on the test configuration.

The shell supports `--help` at startup and inside the session. `--help read`, for
example, shows help for the `read` command inside the session. To manage a registration
in the shell, use `revoke <id>` or `release <base>`.

At end of input the shell returns the first nonzero status in the session, or 0 when
all commands succeeded. An error in one command leaves the session open for the next.
Status 1 means infrastructure failure, 2 means a usage error and 5 means cancellation.
The statuses for a command outcome are 0 for `Succeeded`, 10 for `Failed`, 11 for
`Refused`, 12 for `Unavailable`, 13 for `Expired` and 14 for `Unknown`. A completed
administrative command returns 0.

## Add the server to an application

Add the `Moltaro.OnecOutpost.Server` NuGet package to your ASP.NET Core .NET 10 project.
The package includes the matching protocol assembly. Register your `ISignals`, call
`AddOutpostServer` on the service collection and map the agent endpoints with
`MapOutpostAgents`. Resolve `IOutpostServer` from dependency injection when your
application needs to manage agents or send commands.

The repository's [sample](../../src/Moltaro.OnecOutpost.Samples/Program.cs) uses a
one-node host. Set `OUTPOST_SAMPLE_POSTGRES` to a connection string for a dedicated
PostgreSQL database in UTF-8 and run the sample from `src`.

```sh
dotnet run --project Moltaro.OnecOutpost.Samples
```

It generates a signing key for this run, migrates the `outpost` schema, and binds to a
free loopback port. It prints the pending registration, reports `NotHeld` when
releasing `accounting`, then revokes the registration and returns `Refused unknown-base`
for the Read. It is a short example of embedding and managing the server. The shell
session above shows how to run a command through a paired agent.

```csharp
using System.Net;
using System.Security.Cryptography;
using Moltaro.OnecOutpost.Server;
using Moltaro.Signals;

// An ASP.NET Core application that embeds the server on one node. Its store needs PostgreSQL: the connection string
// comes from OUTPOST_SAMPLE_POSTGRES, and without it the sample says so and ends.
string? connectionString = Environment.GetEnvironmentVariable("OUTPOST_SAMPLE_POSTGRES");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("The sample stops before AddOutpostServer: set OUTPOST_SAMPLE_POSTGRES to a PostgreSQL connection string.");
    return;
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");

// The application provides the ISignals of Moltaro.Signals: on one node, an in-process SignalHost.
builder.Services.AddSingleton<ISignals>(_ => new SignalHost(new SignalHostOptions
{
    ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
    Token = "sample-token",
}));

// A real application keeps its signing key in its own secret storage; the sample makes a new one at every run.
using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

// The server and the migrations name the same schema.
const string Schema = "outpost";

builder.Services.AddOutpostServer(options =>
{
    options.ConnectionString = connectionString;
    options.Schema = Schema;
    options.SigningKeyPem = signingKey.ExportPkcs8PrivateKeyPem();
});

await using WebApplication app = builder.Build();

await OutpostMigrations.MigrateAsync(connectionString, Schema);

app.MapOutpostAgents("/outpost");
await app.StartAsync();

IOutpostServer server = app.Services.GetRequiredService<IOutpostServer>();

// The token itself is a credential for the administrator of the agent machine; its text leaves it out.
OutpostPairingToken token = await server.CreatePairingTokenAsync("Sample agent");
Console.WriteLine(token);

// The agent of the token is pending until the console on the agent machine pairs with the token. Once paired, it
// polls, and the list shows whether it is connected and which bases it declared.
IReadOnlyList<OutpostAgent> agents = await server.GetAgentsAsync();
OutpostAgent created = agents.Single(agent => agent.Id == token.AgentId);
Console.WriteLine($"The server knows {agents.Count} agents; the new one is {created.State}.");

// A base key belongs to the first agent that declares it. Releasing it lets another agent take it, once the agent
// that holds it no longer declares it; here no agent has declared it.
OutpostReleaseResultEnum released = await server.ReleaseBaseAsync("accounting");
Console.WriteLine($"Releasing the base accounting: {released}.");

// Revoking an agent refuses its requests from then on and frees its base keys; for a pending agent, it ends its
// token, which the sample never gives an agent.
bool revoked = await server.RevokeAgentAsync(token.AgentId);
Console.WriteLine($"Revoking the new agent: {revoked}.");

// No agent holds the base accounting, so the Read ends at once as Refused with unknown-base, and no command is created.
OutpostOutcome outcome = await server.ReadAsync(new OutpostReadRequest { BaseKey = "accounting", Script = "Result = 1 + 1;" });
Console.WriteLine($"The read in the base accounting ended as {outcome.Kind} {outcome.Code}.");

await app.StopAsync();
```

### What the application provides

- **PostgreSQL.** Use a UTF-8 database because the store compares parameters as `jsonb`.
  Apply the store's migrations before serving requests, using the same database and
  schema as `AddOutpostServer`. Choose one of these two methods.
  - `OutpostMigrations.MigrateAsync(connectionString, schema)` applies the missing
    migrations. The migration role needs permission to create and update the store.
    It can also create a missing database if that role has the right. Use a direct
    connection or a pooler in session mode because migration takes a session advisory
    lock for the schema.
  - `OutpostMigrations.GetScripts(schema)` returns the SQL and migration identifier for
    each migration, oldest first. Apply the missing scripts in that order. Each script
    carries its own transaction and records its migration in the store's history.
- **Moltaro.Signals.** Register an `ISignals` for waking waiting polls and calls. One
  node can run an in-process `SignalHost`, as the sample does. With several nodes, use
  one host and connect the other nodes with clients. Allow one subscription per waiting
  agent poll and per waiting command call.
- **Signing key.** Keep an ECDSA P-256 private key in the application's secret storage.
  Supply it as a single unencrypted PKCS#8 `PRIVATE KEY` PEM block. Every node uses the
  same key. The agent pins its public key at pairing, so replacing it requires pairing
  each agent anew.
- **Caller authorization.** Authorize Read, Write and agent management in the application
  before calling `IOutpostServer`. `MapOutpostAgents("/outpost")` exposes the three signed
  agent requests under `/outpost/v1/`. The library authenticates those requests itself.
  Give the agent administrator the scheme, host and `/outpost` prefix.

### Server options

`AddOutpostServer` validates the options during registration and keeps a copy. All
ranges include their endpoints.

| Option | Default and range | Effect |
| --- | --- | --- |
| `ConnectionString` | Required | Points to the store's PostgreSQL database. Use the same database across the nodes of one server. |
| `Schema` | `outpost`. 1–63 lowercase letters, digits and underscores, starting with a letter | Isolates the store's tables and migration history. Choose a name PostgreSQL allows. It reserves `pg_` prefixes and migrations report the PostgreSQL error. |
| `SigningKeyPem` | Required | Signs commands and server answers the agent acts on. Keep it in the application's secret storage. |
| `SignalKeyPrefix` | `outpost`. Up to 64 characters from `A–Z a–z 0–9 . _ : -`, with nonempty dot-separated segments | Gives this store its own signal names when several applications share a Signals host. |
| `PollTimeout` | 25 seconds. 1 second–5 minutes | Caps how long a poll waits for commands and how long a waiting caller goes between store reads if a wake-up is lost. The poll waits for the smaller of this value and the timeout it carries. |
| `LeaseGrace` | 10 seconds. 1 second–5 minutes | Adds time for the agent to read the poll answer and send its next poll before it shows as disconnected. A longer grace can keep a stopped agent listed as connected for longer. |
| `TakeTimeout` | 30 seconds. 1 second–1 hour | Limits how long a command can wait to be taken, including when workers are busy. An already handed-out command expires according to the agent's signed time. The local policy separately limits how long the script runs. |
| `ClockSkew` | 2 minutes. 1 second–1 hour | Sets the allowed difference between clocks for judging a command's issue time and deadline. It also helps bound how far a delayed poll can extend a lease. |
| `CommandSizeLimit` | 1 MiB. 4 KiB–16 MiB | Caps the escaped script and compact parameters together in UTF-8. A larger command raises `ArgumentException` before it is created. |
| `OutcomeSizeLimit` | 16 MiB. 64 KiB–64 MiB | Caps the entire UTF-8 outcome object, including its result, messages and error text. The executor trims messages and error text first. It checks the size before committing a Write. |
| `OutcomeRetention` | 7 days. 1 hour–365 days | Sets how long ended commands are kept before purge makes their outcomes and held caller keys unavailable. |
| `PairingTokenLifetime` | 15 minutes. 1 minute–1 day | Sets how long a new pairing credential can be redeemed. |
| `PurgeInterval` | 1 hour. 1 minute–1 day | Sets how often each node removes expired pairing tokens and ended commands past their retention. |
| `TimeProvider` | `TimeProvider.System`. Required | Supplies the clock for leases, deadlines and retention. Tests can inject a fake clock. |

### Pair and manage agents

1. Call `CreatePairingTokenAsync` with a display name. Give the agent administrator the
   endpoint prefix URL and the returned `Token` privately. The token works once until
   `ExpiresAt`. `AgentId` identifies the pending registration. Logging the object with
   `ToString()` shows the ID and expiry and omits the credential.
2. After the administrator [pairs and starts the agent](agent.md#pairing), call
   `GetAgentsAsync` to find it by ID. `Connected` reflects the lease of the newest
   poll. Each base entry shows `Held`, `ReadOpen` and `WriteOpen`. A declared key can
   remain held by another agent.
3. To move a held base key, remove it from the old agent's local policy and wait for
   that agent to poll with the new policy. Call `ReleaseBaseAsync` with the key.
   `StillDeclared` means the holder's newest poll still declares it. `Released` means
   the next agent to declare it can take it, and `NotHeld` means it was already free.
4. To remove an agent's access, call `RevokeAgentAsync`. This also frees its held keys
   and invalidates an unused token, if any. A revoked agent stays in the list and
   requires a fresh pairing to work again. A Write already taken can end as
   `Unknown` with `revoked`. Check its effect before issuing a new one.

### Read and Write

Use `OutpostReadRequest` with `ReadAsync` or `OutpostWriteRequest` with `WriteAsync`.
Both take a `BaseKey`, a 1C `Script` and optional `Parameters` as a `JsonObject`. The
script reads `Parameters` and sets `Result`. A caller key is required for a Write and
optional for a Read. See [Writing scripts](scripts.md) for the 1C types and script
rules.

After the agent is connected and holds `test` for Write, the following uses the same
script and parameters as the shell session. It assumes `server` is the injected
`IOutpostServer` and `cancellationToken` belongs to the calling operation.

```csharp
OutpostWriteRequest write = new()
{
    BaseKey = "test",
    CallerKey = "first-shell-product",
    Script = """
        Product = Catalogs.Products.CreateItem();
        Product.Description = Parameters["description"];
        Product.Write();
        Result = New Structure("description", Product.Description);
        """,
    Parameters = new System.Text.Json.Nodes.JsonObject { ["description"] = "Test product" },
};

OutpostOutcome outcome = await server.WriteAsync(write, cancellationToken);
```

A cancellation ends the wait. A command already submitted can still run. Treat
exceptions from the returned task as failures of the call, with the command's effect
still to be determined. Repeat the same base, mode, script, parameters and caller key
to follow the first command while it holds the key. Matching compares the script
exactly and the parameters as JSON values. A conflicting repeat leaves the original
command and its key in place. Expiry or refusal of a created command frees its key
immediately. Purge releases other held keys after retention, so a repeat after purge
can create a fresh command. Keep the key tied to one business operation and use the
script's `CallerKey` to mark what it creates when recovery may need to find it later.

### Read the outcome

| `Kind` | Meaning |
| --- | --- |
| `Succeeded` | The script finished. A Write committed. |
| `Failed` | The command failed. For a Write the script either never started or its transaction rolled back. |
| `Refused` | A check refused the command, so it never ran. |
| `Unavailable` | The agent's poll lease had expired. Start the agent or restore its connection and retry. |
| `Expired` | The command never reached the agent in time. Check its connection or worker availability before retrying. |
| `Unknown` | A Write may have committed. Reuse the same caller key to retrieve the outcome and check the base before attempting a new Write. |

Use `CommandId` to find a created command in the agent's audit log. For a success,
`Result` is a `JsonElement`, with JSON `null` when the script sets no result.
`Messages` contains 1C user messages for successes and failures. `ErrorText` is
available for `script-error`. `MessagesTrimmed` tells whether the executor trimmed the
messages or error text. `Code` gives details for `Failed`, `Refused` and `Unknown`.
Compare it to the constants in `OutpostCodes`.

| Code | Kind | What to do |
| --- | --- | --- |
| `script-error` | Failed | Read `ErrorText` and `Messages`. Fix the script or its data. |
| `result-type` | Failed or Unknown | Return supported JSON values within the nesting limit. For Unknown, reconcile the Write first. |
| `result-too-large` | Failed or Unknown | Return less data or check the server and ingress limits. For Unknown, reconcile the Write first. |
| `run-limit` | Failed for Read, Unknown for Write | Shorten the script or ask the administrator to review the mode's run limit. Reconcile an Unknown Write before trying again. |
| `lost` | Failed | Check why the agent or worker stopped or went offline, then retry the Read. |
| `base-unreachable` | Failed | Ask the agent administrator to run the console's `check` and verify the base connection and executor. The script never started. |
| `connection-lost` | Failed for Read, Unknown for Write | Check the base's availability. Reconcile an Unknown Write before trying again. |
| `caller-key-conflict` | Refused | Reuse the original mode, script and parameters to follow the existing command. |
| `unknown-base` | Refused | Check the key and who holds it in `GetAgentsAsync`. |
| `mode-closed` | Refused | Check the modes the holder declared in its latest poll. |
| `not-served` | Refused | Have the administrator check the agent's current policy for the base and mode. |
| `not-addressed` | Refused | Check the agent registration and which agent holds the base key. |
| `protocol-version` | Refused | Check that the agent and server use compatible protocol versions. |
| `malformed` | Refused | Compare the server and agent builds and inspect the agent audit log. The signed command broke the protocol rules. |
| `expired` | Refused | Check transport delays and the clocks. The agent refused a command past its deadline and allowed skew. |
| `clock-skew` | Refused | Synchronize the server and agent clocks. The command's issue time was too far ahead for the agent. |
| `revoked` | Refused or Unknown | Replace the revoked agent's pairing. For Unknown, reconcile the Write first. |
| `disconnected` | Unknown | Restore the agent's connection and check what the Write did. |
| `agent-stopped` | Unknown | Read the agent's audit log and check what the Write did. |
| `transaction-ended` | Unknown | Check the base and let the executor control the transaction in future scripts. |

### Run several nodes

Configure every node with the same database, schema, signing key and Signals prefix.
Synchronize their clocks with the agent machines. Any node can serve any agent request
or application call, so the ingress can balance them without sticky sessions.

Serve HTTPS on port 443 to remote agents. Keep the idle timeout of every proxy and the
ingress above `PollTimeout`. Allow `OutcomeSizeLimit` plus 4 KiB for the full `outcome`
request body, including its envelope. Preserve the signed request and response bodies
and their headers. See [the protocol](../protocol.md#9-answers-refusals-and-lost-answers)
for how a lost answer or a body size refusal affects the outcome.

### Watch the running service

Use `GetAgentsAsync` to watch connection state, `LastPollAt`, held bases and agent
versions. Record command kinds, codes and IDs in the application so you can correlate
them with the agent's audit log. Watch for app-level task exceptions and the purge
warning under `Moltaro.OnecOutpost.Server`. An `ended` without `confirmed` can point
to outcome delivery trouble even when polls and the console's `check` succeed.

Supply any health endpoint or metrics in the application. Outpost exposes the agent
list and per-command outcomes. It has no built-in metrics or health endpoint, and
the server receives no copy of the agent's audit log.
