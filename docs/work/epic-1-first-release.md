# Epic 1: First release

**Status:** the plan is proposed and waits for the owner's approval; no sprint has
started. The owner answered PQ1–PQ6 on 20 November 2026. Merging this plan approves it and
the public contract of stage 1.

## Goal

The library as the [concept](../concept.md) describes it, released as its first version:
- the server library, which an ASP.NET Core application embeds, published on nuget.org;
- the agent with its COM worker, the executor and the console, as an archive on GitHub
  Releases;
- the shell, to try the tunnel by hand.

It is for an application that has to read and write 1C:Enterprise 8.3 databases deep in a
back office. A person or a program of that application runs a script in a database and
gets its result, a document is posted at most once however often a call is repeated, and
the administrator of the back office keeps the last word over what the agent may do.

## Scope

**Included.** Everything sections 2 to 7 and 11 of the concept describe:
- the two commands and the script contract;
- rules 1–14;
- pairing, and the protections of the threat table;
- liveness and hosting;
- the agent with its local policy, secrets and audit log;
- the COM worker and the executor, the console and the shell;
- the protocol v1 in `docs/protocol.md`;
- the tests, the latency measurement and the explanations;
- the answers to open items O1–O8, from the lab check L1.

**Left out**, as concept §9 says:
- a model of 1C objects;
- lists of allowed scripts;
- delivery to an agent that is not connected;
- other transports, and confidentiality beyond TLS;
- an agent on Linux, and driving 1C forms;
- access to the DBMS;
- systems other than 1C;
- rotating the signing key, results sent in parts, updating the agent from the server.

Also left out, because nothing needs them now:
- metrics, log lines and health checks: the agent records its own state in the audit
  log, and the server reports failures as exceptions of its tasks;
- binding the server options from `IConfiguration`;
- a user interface beyond the console and the shell;
- an installer package (MSI): the console installs the service from the archive;
- signing the programs of the archive (PQ4).

## Decisions of the owner

| # | Decision |
| --- | --- |
| OD1 | **The lab machine** (20 November 2026). The owner provides, when a sprint asks for it, a Windows machine with 1C:Enterprise 8.3.17.1851 and a copy of a database, and builds the executor `.epf` from its XML sources with the Designer of that version. No CI job needs 1C. |
| OD2 | **Waiting for a busy worker** (20 November 2026, with the merge of the concept). A command for a base whose workers are all busy waits up to its take deadline, 30 seconds by default, and then ends as Expired (rule 7, D10). |
| OD3 | **The built executor in Git** (20 November 2026). The `.epf` is committed with its XML sources, because only the Designer builds it. The departure from the working rules is exception X1 of the [register](exceptions.md). |
| OD4 | The answers to PQ1–PQ6 below (20 November 2026). |

## Requirements

Levels are those of the [work README](README.md). In this epic **Release** is part of the
acceptance: the level only says that the sprints before it can be developed and tested
without it. "Today" is the state on 20 November 2026: no code exists.

| # | Requirement | From | Level | Today | Sprints |
| --- | --- | --- | --- | --- | --- |
| R1 | Read and Write with the script contract of concept §3, from a call of the application to a result in JSON | Concept §3, D2 | Must | Absent | S1, S5, S6, S9 |
| R2 | The agent's checks, its audit log first and a command run once on its agent (rules 1–3) | Concept §3, D4, D7 | Must | Absent | S2, S6 |
| R3 | A caller key makes a command run once, and Failed means nothing was written (rules 4, 5) | Concept §3, D8 | Must | Absent | S3, S5, S9 |
| R4 | Liveness, take deadlines and expiry, numbered polls, a taken command never lost or doubled, revocation, base keys (rules 6, 7, 10, 11, 13, 14) | Concept §3, §4, D5, D10, D17 | Must | Absent | S3, S4, S5, S6, S7 |
| R5 | Workers and run limits (rules 8, 9) | Concept §3, D11 | Must | Absent | S6, S9 |
| R6 | The caller decides how long it waits (rule 12) | Concept §3 | Must | Absent | S5 |
| R7 | Pairing with the agent's key pair, the HMAC under a secret that never travels, and the fingerprint of the server key | Concept §3 Pairing, D4, D5 | Must | Absent | S2, S4, S6, S10 |
| R8 | The protocol v1, written exactly in `docs/protocol.md`; a command signed over its exact bytes; every agent request signed | Concept §6, D4, D5, D15 | Must | Absent | S2 |
| R9 | The store: an EF Core context on PostgreSQL in a schema the application chooses; migrations as EF Core migrations and as one SQL script per migration; removal after retention | Concept §5, D9 | Must | Absent | S3, S4 |
| R10 | The wake-ups through the `ISignals` the application provides: keys under a prefix, a group of its own for every waiting poll and call, every wait reading the store again when it rejoins | Concept §5, D9 | Must | Absent | S5, S7 |
| R11 | Several nodes, a stopped node, a lost poll answer, an older or replayed poll, a lost outcome and an outage of the signals behave as the rules say | Concept §4, §11 | Must | Absent | S7 |
| R12 | The agent as a Windows service under its own virtual account, with the data folder that only it and administrators can use, the local policy, the secrets protected with DPAPI for the machine, and the audit log with its retention | Concept §5, D6, D13 | Must | Absent | S1, S6, S10 |
| R13 | The COM worker and the executor: the external connection under the account of the mode, a fresh executor per command in safe mode, the JSON rules, the user messages, the Write transaction with its checks before the commit, the connector's bitness, the workers ending with the agent, a worker ended at its run limit | Concept §3, §5, D3, D11, D12, D14 | Must | Absent | S9 |
| R14 | The console: install, uninstall, pair, write a secret, check, write a metadata report | Concept §5 | Must | Absent | S10 |
| R15 | The shell: a server of its own, pairing tokens, Read and Write by hand | Concept §2, §5 | Must | Absent | S11 |
| R16 | Every timeout and limit that an application or an administrator can observe is an option with a default, validated when it is set; time comes from `TimeProvider` | `AGENTS.md`, principle 10 | Must | Absent | S1–S11 |
| R17 | Tests as concept §11 and `AGENTS.md` require: a deterministic test of every numbered rule, the transport over loopback HTTP, the store against a real PostgreSQL, a fake clock, no sleeps, no 1C | Concept §11; `AGENTS.md` | Must | Absent | S1–S11 |
| R18 | The answers to open items O1–O8, from the lab check, written into the concept; the manual acceptance of S9, S10 and S11 on the lab machine | Concept §11, §12; OD1 | Must | Absent | L1, S9, S10, S11 |
| R19 | The measurement program for the latency target of concept §11, with the figures under [Measured results](#measured-results) | Concept §11 | Release | Absent | S7, S12 |
| R20 | The explanations E1 "The path of a command" (animated), E2 "Why a command never runs twice" (animated), E3 "How the library is built" (a still figure), each as Markdown and HTML | `AGENTS.md`, "Documentation" | Release | Absent | S7, S12 |
| R21 | The packages of PQ6, published from a version tag through nuget.org Trusted Publishing; the archive of the agent on GitHub Releases with its SHA-256 checksum | Concept D15, D16; PQ4 | Release | The workflows of the packages exist; no package | S1, S12 |
| R22 | `README.md`, the concept, `docs/protocol.md` and the explanations say what exists after every sprint | `AGENTS.md`, "Documentation" | Release | Holds: nothing exists and they say so | S1–S12 |
| R23 | The public contract of stage 1 in code, every public member with its XML documentation | `AGENTS.md`, "The Contract Comes First" | Must | Absent | S1 |

## Acceptance

The epic is done when all of the following hold.

1. Every requirement above is delivered. No entry point throws `NotSupportedException`
   and no test is skipped.
2. The tests written in each sprint pass with their assertions unchanged, except
   corrections of a wrong test made by the rule of the
   [development process](process.md#a-sprint-on-the-full-track), each with its reason.
3. The public surface in code is the one written in [The public contract](#the-public-contract)
   and in the contracts of stages 2 and 3, with the changes the owner agreed to during
   the epic and nothing else.
4. `dotnet build` and `dotnet test` pass from `src` on both CI jobs, and
   `dotnet format --verify-no-changes` on the `build` job. The samples and the shell run
   to the end, and the code in `README.md` is the code of the samples.
5. Every figure of [Measured results](#measured-results) is recorded on the final build,
   and the target of concept §11 is met, or its miss was handled by the rule of the
   [development process](process.md#when-to-stop-and-ask-the-owner) and decided by the
   owner.
6. Every scenario of the manual acceptance of S9, S10 and S11 passed on the lab machine.
7. The three explanations exist in both forms, agree with each other and with what was
   delivered, and their status lines say "built".
8. Everything [The first release](#the-first-release) lists holds, so that the owner can
   tag the release.

### Measured results

Filled by the coordinator when a sprint closes. The program prints the processor, the
operating system and the .NET version with every figure, never a host name; raw output is
not committed. S12 moves the final figures into `README.md`.

| Sprint | Scenario | Figure | Target | Machine | Date |
| --- | --- | --- | --- | --- | --- |
| — | — | — | — | — | — |

## Open questions

The questions of this plan are numbered PQ1, PQ2, … so that they are not taken for the
entries Q1, Q2, … of the [open questions](open-questions.md) register, or for the open items
O1–O8 of the concept. Each answer is written where it outlives this plan, which is removed
after the release.

### Open

None.

### Answered

#### PQ6. One package or two

The protocol assembly holds only internal types, which the server reaches through
`InternalsVisibleTo`. Published as a package of its own, it would have to match the server
package's version exactly: NuGet takes any version at or above the one named, and a
different build of the internals fails at run time with `MissingMethodException`.

**Answer** (the owner, 20 November 2026): **one package.** `Moltaro.OnecOutpost.Server`
carries the protocol assembly inside it, so nobody can mix versions.

#### PQ1. Standing permission for the coordinator

**Answer** (the owner, 20 November 2026): **yes.** The coordinator has, for this epic, the
standing permission that [branches and releases](branches.md#who-does-what) describes,
within the limits it names there.

#### PQ2. Who approves an internal contract

**Answer** (the owner, 20 November 2026): **as the process says.** The owner approves the
public contract:
- the server API and the protocol;
- the commands of the console and the shell;
- the formats of `policy.json` and of the audit log.

The coordinator approves internal contracts, and the owner sees each one in the sprint's
pull request.

#### PQ3. The clock that decides the server's deadlines

**Answer** (the owner, 20 November 2026): **the clock of the node.** Every time decision
of the server takes its time from the `TimeProvider` of its options: leases, take
deadlines, the expiry of a command that no poll took, retention, and the lifetime of
tokens. The safety of taking and expiring comes from one conditional change of the
command's state: the first to commit wins, whatever the clocks say. The expiry of a
handed-out command stays with the agent's signed time (rule 7).

Two consequences of this answer go into the concept and `AGENTS.md` with this plan:
- a node whose clock is behind by Δ reports a gone agent as connected for its lease plus
  Δ;
- a token lasts up to Δ longer, or shorter, on such a node.

Nodes are expected to keep their clocks synchronized.

#### PQ4. Signing the agent archive

**Answer** (the owner, 20 November 2026): **no signing.** The archive ships unsigned, with
its SHA-256 checksum on the release page and a sentence in `README.md` that the programs
are not signed and that an antivirus program may ask for an exception. `README.md` and
[branches and releases](branches.md) are corrected with this plan.

#### PQ5. The metadata report of the console

**Answer** (the owner, 20 November 2026): **bring the owner's collector in, and rewrite
it.** S10 takes the existing collector of the owner's private repository as its starting
point. It rewrites it to the rules of this repository: the code style, the boundaries, the
tests and the documentation. Nothing of a customer or another product comes with it
(D16). The collector's experience with real databases is kept; its code is not copied as
it is.

## The public contract

The public surface of stage 1: the server library and the files the agent reads. S1 puts
it into code exactly as written here. A name, a signature or a behavior that is not here
goes back to the planner and, as a change of the contract, to the owner. The public
contracts of stages 2 and 3 are written into their briefs before those stages start
([Stages 2 and 3](#the-contracts-of-stages-2-and-3)). Concept §7 is a sketch. This contract
adds `CommandId` to an outcome, so that a caller can find the command in the agent's
audit log, and `MessagesTrimmed`, the marker of rule 5.

### Rules for every member

- **Thread safety.** Every public member may be called from any thread at any time.
- **Errors.**
  - An argument error (`ArgumentNullException`, `ArgumentException`,
    `ArgumentOutOfRangeException`), and `InvalidOperationException` for a misuse, are
    thrown by the call itself, before anything happens.
  - A failure of the operation is reported by the returned task. A failure of the store or
    of the signals is an exception of the task, never an outcome.
  - An exception of `ReadAsync` or `WriteAsync` after the call began does not mean that the
    command did not run. A caller finds out by repeating the call with the same caller key
    (rule 4); a new key could run a Write twice.
- **Cancellation.** A token that is already cancelled when a method is called gives a
  cancelled task, and the method changes nothing. A cancelled Read or Write ends with
  `OperationCanceledException` and does not stop the command (rule 12).
- **Results and exceptions.** The expected outcomes of a command are `OutpostOutcome`
  values; exceptions are for failures and mistakes. The default value of an enum of this
  contract never claims a success.
- **Names.** These rules are checked by the server and by the agent, from one place
  ([Internal types of S1](#internal-types-of-s1)). `null` gives `ArgumentNullException`;
  any other value that breaks its rule gives `ArgumentException`. Its `ParamName` is the
  name of the parameter, or of the property for a property of a request or of the options.

  | Name | Rule |
  | --- | --- |
  | Base key | 1 to 64 characters, each one of `a–z 0–9 - _ .`, the first a letter or a digit; compared exactly |
  | Caller key | 1 to 200 characters, each from U+0021 to U+007E (printable ASCII without the space); compared exactly |
  | Agent name | 1 to 100 characters, no control character, no white space at the start or the end |
  | Script | At least one character that is not white space, and no U+0000 |
  | Parameters | No string in them holds U+0000, which PostgreSQL cannot store |
  | Schema | 1 to 63 characters, each one of `a–z 0–9 _`, the first a letter |
  | Endpoint prefix | `/` followed by one or more segments of `A–Z a–z 0–9 - _ .` separated by `/`, not ending with `/`; no segment is `.` or `..` |
- **Sizes.** Every size is counted on the bytes that the protocol carries: UTF-8, with
  characters outside ASCII written as they are, not escaped.
  - The size of a command is that of its script plus that of its parameters written as
    JSON. A call whose command is larger than `CommandSizeLimit` is a caller's mistake
    and gives `ArgumentException`.
  - The size of an outcome is that of its result as the executor wrote it, plus its
    messages and its error text (rule 5).
- **Options** are a plain class with settable properties and defaults. `AddOutpostServer`
  runs the delegate on a new options object, validates it and keeps a copy: a later change
  of that object has no effect.
  - A number or a `TimeSpan` outside the range its row gives yields
    `ArgumentOutOfRangeException`.
  - Any other wrong value, such as a required property left `null`, gives
    `ArgumentException`.
  - `ParamName` is the name of the property.
- **Time.** Every time decision of the server takes its time from the `TimeProvider` of
  its options (PQ3).
- **JSON.** The parameters of a request are written as JSON when the call is made; a
  later change of the `JsonObject` has no effect. A result is a `JsonElement` that the
  outcome owns and that stays valid for as long as the outcome does.

### Namespace `Moltaro.OnecOutpost.Server` (package `Moltaro.OnecOutpost.Server`)

The package carries the protocol assembly inside it (PQ6). It depends on the ASP.NET Core
shared framework, EF Core with Npgsql, and `Moltaro.Signals`.

#### `OutpostServiceCollectionExtensions`

```csharp
public static class OutpostServiceCollectionExtensions
{
    public static IServiceCollection AddOutpostServer(
        this IServiceCollection services,
        Action<OutpostServerOptions> configure);
}
```

- Registers `IOutpostServer` as a singleton, the store, and a hosted service that removes
  what has outlived its retention every `PurgeInterval`.
- Validates the options as "Rules for every member" says. The signing key is checked to be
  a PKCS#8 PEM of an ECDSA P-256 private key.
- A second call on the same collection gives `InvalidOperationException`.
- Requires an `ISignals` of Moltaro.Signals in the container. The library never registers
  one. When `IOutpostServer` is resolved and there is none, the resolution gives
  `InvalidOperationException` that says so.

#### `OutpostEndpointRouteBuilderExtensions`

```csharp
public static class OutpostEndpointRouteBuilderExtensions
{
    public static RouteGroupBuilder MapOutpostAgents(
        this IEndpointRouteBuilder endpoints,
        string prefix);
}
```

- Maps the requests of the agent as `POST {prefix}/{version}/pair`,
  `POST {prefix}/{version}/poll` and `POST {prefix}/{version}/outcome`.
  - `v1` is served.
  - Any other version is answered as `docs/protocol.md` says, with the code
    `protocol-version`, never with a routing 404.
- The endpoints allow anonymous access, because the library authenticates agents itself;
  the application's authorization never applies to them.
- Each endpoint has the body size limit that `docs/protocol.md` gives it. The limit of the
  `outcome` request follows `OutcomeSizeLimit`, so that Kestrel's own default never refuses
  an outcome within the limit.
- Returns the group, so that the application can add its own conventions, such as rate
  limiting.
- `prefix` follows the rule of names. Without `AddOutpostServer` the call gives
  `InvalidOperationException`.

#### `OutpostServerOptions`

```csharp
public sealed class OutpostServerOptions
{
    public string? ConnectionString { get; set; }
    public string Schema { get; set; } = "outpost";
    public string? SigningKeyPem { get; set; }
    public string SignalKeyPrefix { get; set; } = "outpost";
    public TimeSpan PollTimeout { get; set; } = TimeSpan.FromSeconds(25);
    public TimeSpan LeaseGrace { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan TakeTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(2);
    public int CommandSizeLimit { get; set; } = 1024 * 1024;
    public int OutcomeSizeLimit { get; set; } = 16 * 1024 * 1024;
    public TimeSpan OutcomeRetention { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan PairingTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
```

| Option | Meaning | Range |
| --- | --- | --- |
| `ConnectionString` | The PostgreSQL connection of the store | Required |
| `Schema` | The schema of the store | The rule of names |
| `SigningKeyPem` | The server's signing key, as PKCS#8 PEM of an ECDSA P-256 private key. The application keeps it in its own secret storage; one line of .NET creates one: `ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem()` | Required |
| `SignalKeyPrefix` | The prefix of every key the library sets or subscribes to | A name of Moltaro.Signals of at most 64 characters |
| `PollTimeout` | How long a poll waits for a command | 1 second to 5 minutes |
| `LeaseGrace` | What an agent's lease adds to the poll timeout (rule 6) | 1 second to 5 minutes |
| `TakeTimeout` | The take deadline after a command is created (rule 7) | 1 second to 1 hour |
| `ClockSkew` | The skew that every command carries (rule 1) | 1 second to 1 hour |
| `CommandSizeLimit` | The largest command, in bytes | 4 KiB to 16 MiB |
| `OutcomeSizeLimit` | The largest outcome, in bytes, carried in every command (rule 5) | 64 KiB to 64 MiB |
| `OutcomeRetention` | How long an outcome, and with it its caller key, is kept (rule 4) | 1 hour to 365 days |
| `PairingTokenLifetime` | How long a pairing token can be used | 1 minute to 1 day |
| `PurgeInterval` | How often a node removes what outlived its retention | 1 minute to 1 day |
| `TimeProvider` | The clock of every time decision of the server | Required |

#### `IOutpostServer`

```csharp
public interface IOutpostServer
{
    Task<OutpostPairingToken> CreatePairingTokenAsync(string agentName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutpostAgent>> GetAgentsAsync(CancellationToken cancellationToken = default);
    Task<bool> RevokeAgentAsync(Guid agentId, CancellationToken cancellationToken = default);
    Task<OutpostReleaseResultEnum> ReleaseBaseAsync(string baseKey, CancellationToken cancellationToken = default);
    Task<OutpostOutcome> ReadAsync(OutpostReadRequest request, CancellationToken cancellationToken = default);
    Task<OutpostOutcome> WriteAsync(OutpostWriteRequest request, CancellationToken cancellationToken = default);
}
```

- **`CreatePairingTokenAsync`** creates a pending agent with the name and a pairing token
  for it (concept §3, Pairing). The token works once, until `PairingTokenLifetime` has
  passed. Its secret is deleted in the same change that redeems it. A token that expired
  unused is removed by the next purge, with its pending agent. Names need not be unique.
- **`GetAgentsAsync`** lists every agent that is not removed: pending, paired and revoked,
  with the bases each declared in its newest poll.
- **`RevokeAgentAsync`** revokes an agent as rule 13 says and frees its base keys (rule 14).
  It answers `true` when this call revoked it, and `false` when there is no such agent or
  it was revoked already. Revoking a pending agent also ends its token.
- **`ReleaseBaseAsync`** releases a base key as rule 14 says:
  - `Released`: the key is free;
  - `StillDeclared`: the agent that holds the key still declares it, and nothing changes;
  - `NotHeld`: no agent holds the key.
- **`ReadAsync`** runs a Read and **`WriteAsync`** a Write, and each returns when the outcome
  is final (rule 12). The checks run in this order:
  1. the arguments, by the rules of names and sizes;
  2. the caller key (rule 4): a key that names an existing command of the base follows
     rule 4 from here on. Two concurrent first calls with one base and one key create one
     command, and the other call follows rule 4;
  3. the base: a key that no agent holds is Refused with `unknown-base`, and a mode that
     the holding agent does not open is Refused with `mode-closed`;
  4. the agent: one that is not connected gives Unavailable, and no command is created
     (rule 6).

  Then the command is created, signed and handed out, and the call waits for its outcome.

#### `OutpostReadRequest` and `OutpostWriteRequest`

```csharp
public sealed class OutpostReadRequest
{
    public required string BaseKey { get; init; }
    public required string Script { get; init; }
    public JsonObject? Parameters { get; init; }
    public string? CallerKey { get; init; }
}

public sealed class OutpostWriteRequest
{
    public required string BaseKey { get; init; }
    public required string CallerKey { get; init; }
    public required string Script { get; init; }
    public JsonObject? Parameters { get; init; }
}
```

- `Parameters` reaches the script as `Parameters`. `null` means no parameters, an empty
  `Map` in the script. `CallerKey` reaches it as `CallerKey`.
- A Write without a caller key cannot be written: the property is required, and `null`
  gives `ArgumentNullException`.

#### `OutpostOutcome` and `OutpostOutcomeKindEnum`

```csharp
public enum OutpostOutcomeKindEnum
{
    Unknown = 0,
    Succeeded,
    Failed,
    Refused,
    Unavailable,
    Expired,
}

public sealed class OutpostOutcome
{
    public OutpostOutcomeKindEnum Kind { get; }
    public Guid? CommandId { get; }
    public JsonElement? Result { get; }
    public string? Code { get; }
    public string? ErrorText { get; }
    public IReadOnlyList<string> Messages { get; }
    public bool MessagesTrimmed { get; }

    public static OutpostOutcome Succeeded(Guid commandId, JsonElement result, IReadOnlyList<string> messages, bool messagesTrimmed);
    public static OutpostOutcome Failed(Guid commandId, string code, string? errorText, IReadOnlyList<string> messages, bool messagesTrimmed);
    public static OutpostOutcome Refused(Guid? commandId, string code);
    public static OutpostOutcome Unavailable();
    public static OutpostOutcome Expired(Guid commandId);
    public static OutpostOutcome Unknown(Guid commandId, string code);
}
```

- The kinds are those of concept §3. `Unknown` is the default, since it claims nothing.
- `CommandId` is `null` only for Unavailable, and for a Refused that no command was created
  for.
- `Result` holds a value only for Succeeded. A script that set no result gives a
  `JsonElement` of the kind `Null`, never a missing value.
- `Code` is one of [`OutpostCodes`](#outpostcodes) for Failed, Refused and Unknown, and
  `null` otherwise.
- `ErrorText` is the text of the error that 1C raised, for Failed with `script-error`.
- `Messages` holds the user messages of 1C for Succeeded and Failed, and is empty
  otherwise.
- `MessagesTrimmed` says that the executor trimmed the messages or the error text to fit
  the size limit (rule 5).
- The factories exist so that an application can fake `IOutpostServer` in its own tests.
  They check that a code is one of `OutpostCodes` and fits its kind, and copy the messages.

#### `OutpostCodes`

```csharp
public static class OutpostCodes
{
    public const string ScriptError = "script-error";
    public const string ResultType = "result-type";
    public const string ResultTooLarge = "result-too-large";
    public const string RunLimit = "run-limit";
    public const string Lost = "lost";
    public const string CallerKeyConflict = "caller-key-conflict";
    public const string UnknownBase = "unknown-base";
    public const string ModeClosed = "mode-closed";
    public const string NotServed = "not-served";
    public const string NotAddressed = "not-addressed";
    public const string ProtocolVersion = "protocol-version";
    public const string Expired = "expired";
    public const string ClockSkew = "clock-skew";
    public const string Revoked = "revoked";
    public const string Disconnected = "disconnected";
    public const string AgentStopped = "agent-stopped";
    public const string TransactionEnded = "transaction-ended";
}
```

| Code | Kinds | Meaning |
| --- | --- | --- |
| `script-error` | Failed | 1C raised an error; `ErrorText` holds its text. A Write rolled back |
| `result-type` | Failed | The result holds a value that cannot be written as JSON (rule 5) |
| `result-too-large` | Failed, Unknown | The outcome is larger than its limit (rule 5). Unknown for a Write whose outcome was refused for its size, by the server or by something on the path; S5 and S6 state it |
| `run-limit` | Failed (Read), Unknown (Write) | The script exceeded its run limit (rule 9) |
| `lost` | Failed | A Read whose agent or worker stopped, whose agent stayed away, or whose agent was revoked (rules 3, 11, 13) |
| `caller-key-conflict` | Refused | The caller key names another command (rule 4) |
| `unknown-base` | Refused | No agent holds the base key |
| `mode-closed` | Refused | The agent that holds the base does not open the mode |
| `not-served` | Refused | The agent's local policy does not serve the base or the mode (rule 1) |
| `not-addressed` | Refused | The command names another agent (rule 1) |
| `protocol-version` | Refused | The agent does not support the protocol version of the command (rule 1) |
| `expired` | Refused | By the agent's clock, the take deadline passed longer than the skew ago (rule 1) |
| `clock-skew` | Refused | By the agent's clock, the command was issued further ahead than the skew (rule 1) |
| `revoked` | Refused, Unknown | Refused: no poll had taken the command when its agent was revoked. Unknown: a Write that a poll had taken (rule 13) |
| `disconnected` | Unknown | A Write whose agent stayed away longer than its run limit (rule 11) |
| `agent-stopped` | Unknown | A Write whose agent or worker stopped while it ran (rule 3) |
| `transaction-ended` | Unknown | The script ended the transaction of its Write itself (rule 5) |

- Every Refused, like Expired, means that nothing ran. A command that ended as Refused or
  Expired frees its caller key (rule 4); a refusal with `caller-key-conflict` created no
  command and leaves the key with the command that holds it.
- There is no code for a signature that does not verify. The agent cannot trust the
  identity of such a command, so it only records it in the audit log and reports nothing.
  The server ends the command by rules 7 and 11 (concept rule 1).
- A failure of the connection to 1C during a command, such as a restarted cluster or a
  missing licence, gets its codes in stage 2, through the owner, after L1.

#### `OutpostPairingToken`

```csharp
public sealed class OutpostPairingToken
{
    public Guid AgentId { get; }
    public string Token { get; }
    public DateTimeOffset ExpiresAt { get; }

    public OutpostPairingToken(Guid agentId, string token, DateTimeOffset expiresAt);

    public override string ToString();
}
```

- The constructor exists, like the factories of `OutpostOutcome`, so that an application
  can fake `IOutpostServer`. It refuses an empty `agentId` and a `token` that is `null`,
  empty or white space.

- `Token` is the one string that the administrator gives the console. It holds the
  identity of the token, its secret and the fingerprint of the server key; its exact form
  is part of `docs/protocol.md` (S2).
- `AgentId` is the identity the agent will have once it is paired.
- `ToString` gives the agent identity and the expiry, never the token: an application that
  logs the object does not log a credential (principle 9).

#### `OutpostAgent`, `OutpostAgentStateEnum` and `OutpostBase`

```csharp
public enum OutpostAgentStateEnum
{
    Pending = 0,
    Paired,
    Revoked,
}

public sealed record OutpostAgent(
    Guid Id,
    string Name,
    OutpostAgentStateEnum State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PairedAt,
    DateTimeOffset? LastPollAt,
    bool Connected,
    string? AgentVersion,
    IReadOnlyList<OutpostBase> Bases);

public sealed record OutpostBase(string Key, bool ReadOpen, bool WriteOpen, bool Held);
```

- `Connected` follows rule 6 at the moment of the call, by the clock of the node that
  answers (PQ3).
- `Bases` lists what the agent declared in its newest poll. `Held` is `false` for a key that
  another agent holds; the server refused that base for this agent (rule 14).

#### `OutpostReleaseResultEnum`

```csharp
public enum OutpostReleaseResultEnum
{
    NotHeld = 0,
    Released,
    StillDeclared,
}
```

#### `OutpostMigrations` and `OutpostMigrationScript`

```csharp
public static class OutpostMigrations
{
    public static Task MigrateAsync(string connectionString, string schema, CancellationToken cancellationToken = default);
    public static IReadOnlyList<OutpostMigrationScript> GetScripts(string schema);
}

public sealed record OutpostMigrationScript(string Name, string Sql);
```

- `MigrateAsync` applies the EF Core migrations of the store that the schema lacks. It
  creates the schema when it does not exist.
- `GetScripts` returns one SQL script per migration, in order, for an application that
  applies migrations itself. Applying them in order to an empty schema gives the same
  tables, and the same record of applied migrations, as `MigrateAsync` does. `Name` is the
  identifier of the EF Core migration.
- `schema` follows the rule of names.

### The files of the agent

The agent keeps everything in one folder, `%ProgramData%\Moltaro.OnecOutpost`: the data
folder.

- **The data folder admits nobody else.** The console's `install` creates it with
  inheritance off:
  - Administrators and SYSTEM have full control;
  - the agent's account may read the folder, and may modify only `audit\` and
    `outcomes\`;
  - nobody else has any right. `%ProgramData%` lets every local user create
  files, so without this a local user could plant a policy, a pinned key or an outcome.
- **The agent refuses a folder that admits anybody else.** When it starts, it checks the
  access lists of the folder and of every file it reads, since a file moved into the
  folder keeps its own. If one of them admits anyone else, it records `started` with an
  error and stops.
- **The agent's account** is the virtual service account `NT SERVICE\MoltaroOnecOutpost`,
  which needs no password.

| File | Written by | Public | Content |
| --- | --- | --- | --- |
| `policy.json` | The administrator | Yes | The local policy, below |
| `audit\YYYY-MM-DD.jsonl` | The agent | Yes | The audit log, one file per UTC day, below |
| `agent.json` | The console, when it pairs | No | The server's address, the agent's identity, the pinned server key |
| `secrets.dat` | The console | No | The agent's private key and the 1C passwords, encrypted with DPAPI for the machine |
| `outcomes\` | The agent | No | The outbox: outcomes not yet confirmed by the server, one file per command |

#### `policy.json`

```json
{
  "auditRetentionDays": 90,
  "proxy": "http://proxy.local:3128",
  "retryDelayMax": "00:00:30",
  "bases": [
    {
      "key": "accounting",
      "server": "srv-1c:1541",
      "database": "accounting",
      "read": { "account": "outpost-read", "workers": 1, "runLimit": "00:10:00" },
      "write": { "account": "outpost-write", "workers": 1, "runLimit": "00:10:00" }
    },
    {
      "key": "warehouse-file",
      "file": "D:\\1C\\Warehouse",
      "read": { "account": "outpost-read" }
    }
  ]
}
```

| Member | Rule |
| --- | --- |
| `auditRetentionDays` | 1 to 3650; 90 when absent |
| `proxy` | The HTTP proxy through which the agent reaches the server, as an absolute `http` URL. When absent, the agent connects directly. A proxy that asks for authentication gets the Windows credentials of the agent's account, which on the network are those of the computer |
| `retryDelayMax` | The longest delay between attempts after an error, for the connection to the server and for starting a COM worker again. The first delay is a tenth of it. In the form of `runLimit`, from 1 second to 1 hour; 30 seconds when absent |
| `bases` | At least one base; the keys are unique |
| `key` | The rule of base keys |
| `server`, `database` | The 1C cluster, with its port when it is not the default, and the database name. Either both, or `file` |
| `file` | The folder of a file database. Either this, or `server` and `database` |
| `read` | Required |
| `write` | Present only when the base may be written |
| `account` | The 1C account of the mode; its password is a secret that the console writes |
| `workers` | 1 to 8; 1 when absent |
| `runLimit` | A .NET `TimeSpan` in its constant form `[d.]hh:mm:ss`, from 1 second to 1 day (`1.00:00:00`); 10 minutes when absent |

- **An invalid file serves nothing.** An unknown member, a wrong type or a value out of
  its rule makes the whole file invalid. The agent then serves nothing and records why in
  the audit log. The error names the path of the member, such as `bases[1].read.workers`.
  JSON comments are not allowed.
- **The policy is read when the agent starts.** A change takes effect when the service is
  restarted.

#### The audit log

Each line is one JSON object in UTF-8, written and flushed before the agent goes on (rule
2). Every entry has `time`, the UTC time with milliseconds in ISO 8601, and `entry`:

| `entry` | Further members | When |
| --- | --- | --- |
| `started` | `version`, `error` (when it stops at once) | The agent started |
| `policy` | `valid`, `error` (when not valid) | The agent read its policy |
| `connection` | `state` (`connected` or `lost`), `reason` (when lost) | The agent's connection to the server changed |
| `bases` | `held` (the keys the server granted), `claimed` (the keys another agent holds) | The server's answer changed what this agent holds (rule 14) |
| `received` | `command`, `base`, `mode` (`read` or `write`), `callerKey` (or `null`), `script`, `parameters` (an object or `null`), `issued`, `deadline` | A command passed the checks of rule 1, before it runs |
| `refused` | `command` (or `null`), `base` (or `null`), `mode` (or `null`), `code` | A command failed a check of rule 1. For a signature that did not verify, `code` is `signature` and the other members are `null`, since nothing in the command can be trusted |
| `ended` | `command`, `outcome` (`succeeded`, `failed` or `unknown`), `code` (or `null`), `durationMs` | A command ended (rules 3, 5, 9). A second `ended` of the same command replaces the first; it is written only when the outcome was refused for its size, as below |
| `confirmed` | `command` | The server confirmed the outcome (rule 11) |

- **The outbox.** The result of a command is not in the audit log. Before the agent records
  `ended`, it writes and flushes the outcome to `outcomes\`. The file is removed after
  `confirmed`.
- **At a restart.** A file without its `ended` is recorded as `ended` and sent. A
  `received` without a file or an `ended` becomes Unknown or `lost` (rule 3). The audit log
  stays the one record of what happened (D7); the outbox only holds what must still be
  sent.
- **An outcome refused for its size.** The server refuses it with its protocol code, or
  something on the path refuses it with HTTP 413. The agent then replaces the outbox file
  with Unknown `result-too-large` for a Write, or Failed `result-too-large` for a Read,
  records a second `ended` with it, and sends it.
- **Retention.** Files older than `auditRetentionDays` are removed, but never one younger
  than a day.

### Internal types of S1

Two places hold the checks that both sides need, so that each rule lives once:

- In `Moltaro.OnecOutpost.Protocol`, namespace `Moltaro.OnecOutpost.Protocol`:

  ```csharp
  internal static class OutpostNames
  {
      public static void CheckBaseKey(string value, string paramName);
      public static void CheckCallerKey(string value, string paramName);
      public static void CheckAgentName(string value, string paramName);
      public static void CheckScript(string value, string paramName);
      public static bool IsBaseKey(string value);
  }

  internal static class OutpostProtocol
  {
      public const int Version = 1;
  }
  ```

  The assembly gives `InternalsVisibleTo` to the server, the agent, the console, the shell
  and their test projects.
- In `Moltaro.OnecOutpost.Agent`, namespace `Moltaro.OnecOutpost.Agent.Policy`:

  ```csharp
  internal sealed record AgentPolicy(
      int AuditRetentionDays,
      Uri? Proxy,
      TimeSpan RetryDelayMax,
      IReadOnlyList<AgentBasePolicy> Bases);

  internal sealed record AgentBasePolicy(
      string Key,
      string? Server,
      string? Database,
      string? File,
      AgentModePolicy Read,
      AgentModePolicy? Write);

  internal sealed record AgentModePolicy(string Account, int Workers, TimeSpan RunLimit);

  internal sealed class AgentPolicyException : Exception
  {
      public AgentPolicyException(string path, string reason);
      public string Path { get; }
      public string Reason { get; }
  }

  internal static class AgentPolicyReader
  {
      public static AgentPolicy Read(string json);
  }
  ```

  `Read` applies the defaults of the table and throws `AgentPolicyException` for an
  invalid file, with `Path` in the form of the table's example.

### What S1 puts into code

- Every public type and member above, with its XML documentation.
- Working:
  - the options checks in `AddOutpostServer`, and the second-call and missing-`ISignals`
    checks;
  - the prefix check of `MapOutpostAgents`;
  - the argument checks of every member of `IOutpostServer`, by the rules of names and
    sizes;
  - the checks of the factories of `OutpostOutcome`, the checks of the constructor of
    `OutpostPairingToken`, and its `ToString`;
  - the argument checks of `OutpostMigrations`;
  - `OutpostNames` and `AgentPolicyReader`.
- Refusing with `NotSupportedException`, after its argument checks: every member of
  `IOutpostServer`, `OutpostMigrations` and the endpoints.
- The agent program starts and stops, and does nothing else.

## The protocol

The protocol is public contract (concept §6, D15). It is designed in S2, after its study,
by the planner, not by the contract author:

1. The planner writes `docs/protocol.md` exactly.
2. An independent reviewer reviews it until no P1, P2 or P3 finding is left.
3. The owner approves it.
4. The contract author commits it in S2's contract step.

It has to say:
- the requests `pair`, `poll` and `outcome` under `{prefix}/{version}/`, and the answer to
  an unsupported version;
- the JSON of each body and answer, and the body limit of each request; the limit of
  `outcome` follows `OutcomeSizeLimit`;
- the HTTP status of each refusal, with its code;
- how a command travels as exact bytes with its signature, and how the agent's requests are
  signed: over the bytes of the body, the kind of request and the agent's identity;
- the form of the pairing token, and the pairing proof;
- the fingerprint of a key;
- **What every poll answer carries:**
  - the commands;
  - the bases the agent holds and those that another agent holds (rule 14);
  - the server's time;
  - the poll timeout and the lease grace. The agent waits for an answer for no longer than
    their sum, and then counts the poll as lost. The pairing answer carries them too, and
    the console writes them into `agent.json` when it pairs, so that the first poll is
    bounded as well; later answers bring the current values.
- **The answer to an older poll** comes at once, not at the end of the poll timeout. It has
  no commands, carries the other members of a poll answer, and names the newest poll number
  that the server recorded for the agent (rule 10).
- **What every other answer carries:** the server's time.
- **Every answer is signed** by the server over its exact bytes and bound to the request
  it answers by the SHA-256 of that request's exact body, which carries the poll number
  and the agent's time. The agent verifies it before it acts on any of it, and treats one
  that does not verify as a lost answer. The one exception is a bare HTTP 413 to an
  `outcome` request, as concept §6 says.
- **The type of a poll number:** a signed 64-bit integer. A named newest number that
  leaves no room above it is a protocol violation.
- **How sizes are counted**, exactly: which bytes, and whether the escapes of JSON such as
  `\"`, `\\` and those of control characters count, so that the executor, the agent and
  the server count the same number. The refusal of an outcome for its size carries the
  code `result-too-large`.
- **A poll is older when its number is not greater** than the newest that the server
  recorded for its agent, so an exact replay of the newest poll is older too (rule 10).
- **How the console checks the server without polling.** The console never polls: a poll
  of its own would become the newest and take the service's place. It sends an `outcome`
  for a command identity it has just made up. The server confirms it and keeps nothing
  (rule 11), and the answer shows that the server is reachable, that the agent's key is
  accepted, and the server's time.
- **Addresses.** The agent accepts an `http://` address of the server only on a loopback
  address; anywhere else it requires `https://`.

The concept fixes the principles; S2 fixes the bytes.

## Sprints

### Rules for every sprint

- **Tracks.** The [development process](process.md) applies.
  - S8 is on the refactoring track.
  - L1 and S12 are on the light track.
  - Every other sprint is on the full track, because each delivers behavior of the
    contract: contract, then tests by another agent, then implementation, each its own
    commit.
  - A sprint whose brief says it has no contract step or no implementation step says why.
- **Branches.** One sprint at a time, on a branch `epic-1/sprint-NN-name` cut from `main`,
  as [branches and releases](branches.md) says. L1 may run next to a sprint, on its own
  light branch. PQ1 covers the push, the pull request and the merge of a closed sprint.
- **To read.** Every document under **To read** is read at its state of 20 November 2026,
  with the edits this plan makes, and this plan at the state the owner approved.
  `docs/protocol.md` is read at the state S2 committed. If a decision or a rule in one of
  them changes, the coordinator stops and has the plan corrected, as the process says.
- **The public contract is fixed by this plan** for stage 1, and by the briefs of stages 2
  and 3 once the owner approved them. No sprint adds, renames or changes any of these
  without the owner:
  - a public member;
  - a member of `policy.json` or of the audit log;
  - a code of `OutpostCodes`;
  - the protocol.

  A sprint that finds the contract should change stops, and the change goes to the owner.
- **Internal contracts after the study.** S2, S3, S4, S5, S6 and S9 build mechanisms.
  - Each begins with the study that the
    [development process](process.md#a-study-comes-before-a-design) requires, answering
    the questions its brief names.
  - The planner then writes into the brief the internal types that its tests address,
    with what the study taught. An independent reviewer reviews them until no P1, P2 or
    P3 finding is left, and the coordinator approves them (PQ2), before the contract step.
  - The study never reopens the public contract.
- **Studies.** The implementer of the sprint carries out its study, before any code.
  - It reads two to four established systems in their source at pinned commits, with the
    licence checked first by the rule of the process. The PostgreSQL License of Npgsql is
    a permissive licence of the BSD kind.
  - The report is brief and given in the conversation, not written to a file. The
    planner and the coordinator read it before the contract step ends.
- **Tests come with their sprint.** Each sprint writes the tests of its own behavior in its
  tests step, against the contract its contract step committed. S1 writes skipped tests
  only for what the public API reaches by itself.
- **The test lane.**
  - The framework is xUnit.net v3 (`xunit.v3`) on Microsoft.Testing.Platform. The test
    project that S1 creates names that runner in `src/global.json`.
  - The fake clock is `FakeTimeProvider` of `Microsoft.Extensions.TimeProvider.Testing`.
    PostgreSQL comes from Testcontainers (`Testcontainers.PostgreSql`).
  - A test that needs PostgreSQL has the trait `Requires=PostgreSQL`, and one that needs
    Windows has `Requires=Windows`. No test needs both.
  - The `build` job and the Publish workflow run every test without `Requires=Windows`.
    The `windows` job runs every test without `Requires=PostgreSQL`, because GitHub's
    Windows runners have no Linux containers. A developer's machine with Docker runs them
    all. S1 writes the exact commands into `ci.yml` and `publish.yml`.
  - Loopback tests start Kestrel on port `0` of `127.0.0.1`.
  - A test waits for an observed event and never sleeps.
  - A test with the fake clock waits until the code under test has armed its timer before
    it advances time. It does so through a wrapper of the fake clock in
    `Moltaro.OnecOutpost.TestSupport`, which reports every timer created or changed.
  - The server's waits take their deadlines from its own `TimeProvider`, never from the
    deadline of `ISignalSubscription.WaitAsync`, whose clock is that of Moltaro.Signals.
  - A test of a behavior that is not implemented yet is skipped with the one shared reason,
    which lives in `Moltaro.OnecOutpost.TestSupport`.
- **Projects.** Every project targets `net10.0`, so that the agent's logic is also tested
  on Linux. Code that runs on Windows only is marked `[SupportedOSPlatform("windows")]`,
  and the analyzer's warnings are errors. The DPAPI calls, the access list of the data
  folder, the Windows service, the job object and COM are behind boundaries that tests
  replace.
- **Measurements** run by hand, never in CI and never in the test lane, from the program
  `src/Moltaro.OnecOutpost.Measurements`, which the test author of S7 creates in its tests
  step. It is a console program in the solution with `IsPackable` set to `false`, not a
  test project. The coordinator runs it and enters each figure under
  [Measured results](#measured-results).
- **Explanations.** An explanation is documentation, not code or tests. One agent, assigned
  by the coordinator and with no other role in the sprint, writes it on the sprint's
  branch after the implementation step and before the independent review, so that it
  describes the rule and not the code its author wrote. The independent reviewer of the
  sprint reviews it with the rest. Its status line says what is built and what is
  intended.
- **The lab machine.** A sprint whose acceptance needs 1C asks the owner for the lab
  machine (OD1) when its implementation is ready. The coordinator runs the scenarios of
  the brief with the owner, on a copy of a database. It records one line per scenario,
  passed or failed, in the sprint's status. No raw output is committed.
- **Documents.** In the documents step of every sprint the coordinator brings `README.md`,
  the concept, `docs/protocol.md` and the explanations that exist up to what exists, both
  forms of an explanation together, as `AGENTS.md` and the process say.

### Order and why these splits

```text
Stage 1 — the tunnel without 1C
  S1 contract ── S2 protocol ── S3 store ── S4 pairing ── S5 commands ── S6 agent ── S7 nodes, losses
  S8 refactoring
Stage 2 — 1C and the tools           (L1, the lab check, best before S6, at the latest before stage 2)
  S9 executor, COM worker ── S10 console ── S11 shell
Stage 3 — the release
  S12 documents, packages, archive
```

- **Stage 1 without 1C.** Every guarantee of the tunnel, from the signature to the cases
  of a doubled run, is built and tested in CI, with a fake behind the COM worker's
  boundary. Stage 2 then adds what only a lab machine can show, with nothing else open.
- **S2, the protocol, alone.** It is the public wire contract and the security core:
  signatures over exact bytes, the agent's signed requests and the pairing proof. It is
  reviewed alone, as the zone of `AGENTS.md` with the strictest priorities.
- **S3, the store, apart from the server.** Every guarantee of the server is one
  conditional change in the store. They are tested against PostgreSQL without HTTP, so a
  failure points at one place.
- **S4, pairing, apart from S5, commands.** Pairing, authentication, leases and base keys
  are needed by commands but not the other way round. Splitting them keeps each review to
  one mechanism.
- **S6, the agent, after the server.** Its tests run against the real server of S5 over
  loopback, so the first end-to-end test exists at the end of S6.
- **S7, several nodes and lost messages,** on its own, because it needs a test proxy that
  drops and holds messages, and because it is where a wrong rule shows.
- **S8, refactoring,** after stage 1, the middle of the epic, before stage 2 builds on the
  agent.
- **L1 early.** Its answers to O1–O8 can change decisions D3, D12 and D14. They can also
  add members to `policy.json`: an account of its own for COM workers after O1, or a
  connector per base after O8. Run before S6, they reach S6's brief through the owner.
  Run later, they are a change of the contract, through the owner.
- **S9 alone.** The executor and the COM worker are one piece of acceptance on the lab
  machine; split, each half could not be accepted.
- **S10 and S11.** The console works on the agent machine, the shell next to a server.
  They share nothing.

### Status

| Sprint | Result | Track | Status |
| --- | --- | --- | --- |
| S1 | The public contract of stage 1, the projects and the test lane | Full | Not started |
| S2 | The protocol, its signatures and the pairing proof | Full | Not started |
| S3 | The store | Full | Not started |
| S4 | Pairing and agents on the server | Full | Not started |
| S5 | Commands on the server | Full | Not started |
| S6 | The agent | Full | Not started |
| S7 | Several nodes and lost messages | Full | Not started |
| S8 | Refactoring of the code base after stage 1 | Refactoring | Not started |
| L1 | The lab check of open items O1–O8 | Light | Not started |
| S9 | The executor and the COM worker | Full | Not started |
| S10 | The console | Full | Not started |
| S11 | The shell | Full | Not started |
| S12 | Documents, packages and the archive | Light | Not started |

### S1 · The public contract of stage 1, the projects and the test lane

**Track:** full · **Branch:** `epic-1/sprint-01-contract` · **Needs:** the owner's
approval of this plan.

- **Goal.** The public contract of stage 1 exists in code, documented and refusing where
  it is not implemented. The checks that need no implementation work. The tests of those
  checks pass, the tests of the migrations are written and skipped, the sample is written,
  and CI runs the tests with both traits.
- **Contract.** Every type and member of [The public contract](#the-public-contract),
  exactly, with XML documentation that says what each member does, guarantees, does not
  do and which errors it reports. It behaves as
  [What S1 puts into code](#what-s1-puts-into-code) says, with the types of
  [Internal types of S1](#internal-types-of-s1).
- **Steps and projects.** Each commit leaves the build and `dotnet test` green.
  - *Contract step*, the contract author:
    - `Moltaro.OnecOutpost.Protocol`, with no package reference and not packed by itself;
    - `Moltaro.OnecOutpost.Server`, packed, carrying the protocol assembly inside its
      package (PQ6). It references the ASP.NET Core shared framework,
      `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` and
      `Moltaro.Signals`, whose versions go into `Directory.Packages.props`;
    - `Moltaro.OnecOutpost.Agent`, a program, not packed.

    The server project has a package description and `GenerateDocumentationFile` set.
    All of them go into `Moltaro.OnecOutpost.slnx`, with one type per file. No test
    project yet.
  - *Tests step*, the test author:
    - `Moltaro.OnecOutpost.TestSupport`, with the shared skip reason, the wrapper of the
      fake clock and the PostgreSQL fixture;
    - `Moltaro.OnecOutpost.Protocol.Tests`, `Moltaro.OnecOutpost.Server.Tests` and
      `Moltaro.OnecOutpost.Agent.Tests`;
    - `Moltaro.OnecOutpost.Samples`;
    - the runner line in `src/global.json`;
    - the package versions of `xunit.v3`, `Microsoft.Extensions.TimeProvider.Testing`
      and `Testcontainers.PostgreSql`;
    - the test commands with their trait filters in `ci.yml` and `publish.yml`.

    None of these projects is packed. Each test project has tests that run and pass in
    this commit, because Microsoft.Testing.Platform fails a test project in which no
    test runs.
  - *No implementation step.* What S1 makes work is written with the contract. The fixes
    of the review are made by the contract author in the contract's files and by the test
    author in the tests and the sample. A finding that changes a check and its test lands
    as one commit with both.
- **To read.**
  - The [concept](../concept.md), §3, §5–§7 and §11 (20 November 2026);
  - `AGENTS.md` (20 November 2026);
  - the [development process](process.md) and [branches and releases](branches.md);
  - this plan, with the owner's answers to its questions.
- **Acceptance.**
  - Tests that pass:
    - every validation rule of the options;
    - the second call of `AddOutpostServer`, and the missing `ISignals`;
    - the prefix rule of `MapOutpostAgents`;
    - the rules of names and sizes in every member of `IOutpostServer`, the size counted
      without escaping non-ASCII characters;
    - the factories of `OutpostOutcome`, including a code that does not fit its kind;
    - the checks of the constructor of `OutpostPairingToken`;
    - `ToString` of `OutpostPairingToken` leaving the token out;
    - the argument checks of `OutpostMigrations`;
    - every rule of `policy.json`, with the path of the member in the error;
    - a test of the built `Moltaro.OnecOutpost.Protocol` that it references only
      assemblies of .NET (principle 12).
  - Tests written and skipped with the shared reason, in their own folder, un-skipped by S3:
    `MigrateAsync` into a new schema; a second `MigrateAsync` changing nothing;
    `GetScripts` applied in order giving what `MigrateAsync` gives.
  - The sample, written by the test author: an ASP.NET Core application of about 40 lines
    that registers an in-process `SignalHost` and calls `AddOutpostServer`. It runs
    `OutpostMigrations.MigrateAsync`, maps the endpoints, creates a pairing token and calls
    `ReadAsync`. It takes the connection string from the environment variable
    `OUTPOST_SAMPLE_POSTGRES`. Without it, or while the features are not implemented, it
    reports where it stops and ends.
  - `dotnet pack -c Release` from `src` produces exactly the server package, with the
    protocol assembly inside it.
  - The documents step:
    - `README.md` says the contract exists and only the checks work;
    - `AGENTS.md`, "Project" and "Layout", say what now exists, and list the samples and
      measurement projects and the single package of PQ6;
    - the bullets of the [work README](README.md) on the repository as it is set up are
      corrected;
    - concept §7 takes the names of the contract.
- **Out of scope.** Any behavior beyond the checks; `docs/protocol.md`; explanations.
- **Small changes during the sprint.** The owner's answer of 20 November 2026 on the
  console programs: they are built the way a professional tool is, with spinners and
  progress indicators, on Spectre.Console — not student work. It adds "Console programs"
  to `AGENTS.md`, "Code Style", and Spectre.Console to principle 12; the lines of the
  console and of the shell in [Stages 2 and 3](#the-contracts-of-stages-2-and-3) point to
  that rule, and the acceptance of S10 and S11 checks it.
- **Corrections from the review.** Round 1 found that the sentence of
  [`OutpostCodes`](#outpostcodes) on Refused said every Refused frees the caller key. As
  concept rule 4 says, only a command that ended as Refused or Expired frees its key; a
  refusal with `caller-key-conflict` created no command and leaves the key with the
  command that holds it. The sentence now says so; no guarantee changed.

### S2 · The protocol, its signatures and the pairing proof

**Track:** full · **Branch:** `epic-1/sprint-02-protocol` · **Needs:** S1.

- **Goal.** The messages of the protocol v1 exist with their codec:
  - a command is signed over its exact bytes and verified before it is parsed;
  - every request of an agent is signed and verified;
  - the pairing proof binds the agent's key to the secret of a token without sending the
    secret;
  - every limit of a message is checked.
- **Contract.**
  - *Public:* `docs/protocol.md`, written by the planner after the study, reviewed and
    approved as [The protocol](#the-protocol) says, and committed in the contract step.
  - *Internal:* the codec and the signing types in `Moltaro.OnecOutpost.Protocol`, written
    into this brief after the study. They have to be able to:
    - build the bytes of a command from its fields and sign them with the server key;
    - verify the signature of command bytes with a pinned public key, and only then parse
      them;
    - sign an agent request (its kind, the agent's identity and the bytes of its body) and
      verify it with the agent's public key;
    - compute and check the pairing proof;
    - read and write the pairing token;
    - compute the fingerprint of a public key;
    - read and write every body and answer of the three requests;
    - sign every answer of the server, bound to the request it answers, and verify it with
      the pinned key;
    - refuse with the codes of the protocol: version, signature, size, form.

    They perform no I/O and know nothing of HTTP or the store.
- **Study.** Sources, for example: the GitHub Actions runner (MIT); the Azure Pipelines agent
  (MIT); Octopus Deploy's Halibut (Apache-2.0). Questions:
  1. How each authenticates the agent to the server and the server to the agent, and what
     a stolen credential gives an attacker.
  2. What exactly is signed: the exact bytes, or a canonical form. How each avoids a
     difference between what was signed and what is read.
  3. How each binds a new agent's key to a one-time credential at registration.
  4. How each refuses a replayed message.
  5. Which key formats and fingerprints each uses, and how a key is pinned.
- **To read.** The [concept](../concept.md), §3 ("Rules" 1, "Pairing", "What protects
  what") and §6 (20 November 2026); `AGENTS.md`, principles 3, 9, 11 and 12; this plan,
  "The protocol".
- **Acceptance.**
  - Tests of the command signature:
    - a command whose bytes changed in any place is refused;
    - one signed with another key is refused;
    - bytes that do not parse are never parsed before their signature is checked.
  - Tests of the request signatures:
    - the same three cases;
    - a body sent as another kind of request is refused;
    - the same bytes under another agent's identity are refused.
  - Tests of pairing:
    - a proof made with another secret is refused;
    - a key swapped after the proof was made is refused;
    - a server key whose fingerprint differs from the token's is refused.
  - Every message round-trips.
  - Every limit of `docs/protocol.md` is refused one byte beyond it and accepted at it,
    with characters outside ASCII counted as they travel, unescaped.
  - Tests of the answer signatures: an answer whose bytes changed, one signed with another
    key, and one bound to another request are refused.
  - An unknown protocol version is refused with `protocol-version`.
  - The documents step: `docs/protocol.md` says that the codec exists and that no endpoint
    serves it yet.
- **Out of scope.** HTTP, the store, the agent's loop.

### S3 · The store

**Track:** full · **Branch:** `epic-1/sprint-03-store` · **Needs:** S2.

- **Goal.** The library's store on PostgreSQL holds pairing tokens, agents, their bases,
  polls, commands and outcomes. It makes every change that a rule needs as one conditional
  change, safe across nodes. `OutpostMigrations` works.
- **Contract.**
  - *Public, from S1, now working:* `OutpostMigrations`.
  - *Internal:* the EF Core context, its entities and the store's operations, in the
    server's assembly, written into this brief after the study. They take the time from
    the server's `TimeProvider` (PQ3). They have to be able to:
    - create a token with its pending agent;
    - redeem a token once, before its expiry, registering the agent's key and deleting the
      token's secret in the same change;
    - record a poll:
      - its number, treating one that is not greater than the newest as older and
        changing nothing else for it (rule 10);
      - the lease (rule 6);
      - the declared bases with their claims (rule 14);
      - the list of received commands, handing out again those missing from it (rule 11);
    - take the oldest commands for the modes with free workers (rules 7, 8, 10, 13): only
      for the newest poll, only for an agent that is not revoked, and only before their
      take deadline;
    - expire a command that no poll took, and a handed-out command on the evidence of the
      agent's time, which also turns a server-set Unknown or `lost` into Expired (rules 7
      and 11);
    - end a taken command whose agent has been away longer than its run limit: Unknown
      with `disconnected` for a Write, Failed with `lost` for a Read (rule 11);
    - accept the first outcome of a command from the agent it was handed to, with the
      replacements of rule 11, and confirm an outcome for a command it does not know;
    - create a command, or find the one that its base and caller key name, in one change
      (rule 4): the script is compared exactly, the parameters as JSON values;
    - end the commands of a revoked agent (rule 13);
    - release a base key (rule 14);
    - remove ended commands after their retention, and expired tokens with their pending
      agents.

    Every change is one statement or one short transaction under ReadCommitted, and
    holds no lock while it waits for anything.
- **Study.** Sources, for example: the PostgreSQL transport of Rebus (MIT); the durable
  inbox and outbox of Wolverine (MIT); the ADO.NET job store of Quartz.NET (Apache-2.0);
  for migrations with a schema chosen at run time, EF Core (MIT) and Npgsql (PostgreSQL
  License). Questions:
  1. How a row is claimed by one of several nodes in one statement, without long locks:
     `UPDATE … WHERE state … RETURNING`, `FOR UPDATE SKIP LOCKED`, or a version column.
  2. How a row is created or found by a unique key in one statement, so that two
     concurrent first calls never create two.
  3. How the oldest rows are taken first without a hot spot.
  4. How parameters are kept so that two JSON values compare as equal regardless of the
     order of keys and the notation of numbers. A `jsonb` column compared with `=` is
     the candidate.
  5. How removal after retention runs on every node without harm.
  6. How EF Core migrations take a schema chosen at run time, and how one SQL script per
     migration is produced for that schema.
- **To read.** The [concept](../concept.md), §3 "Rules" and §5 (20 November 2026);
  `AGENTS.md`, principles 5–7, 10 and 11; this plan, "The public contract" and PQ3.
- **Acceptance.**
  - A test against PostgreSQL for every operation above. Each case of a rule has its own
    test, named for the rule.
  - The exact replay of the newest poll changes nothing.
  - Races, each driven to a known order, for example by holding the first transaction open
    until the second is observed waiting. In each one side wins and the other changes
    nothing:
    - two takes of one command;
    - a take against an expiry;
    - two outcomes of one command;
    - two claims of one base key;
    - two redemptions of one token;
    - two first calls with one base and one caller key: one command is created, and the
      other call finds it;
    - two repeats racing to create a new command after the first ended as Expired or
      Refused: one command is created.
  - The skipped tests of the migrations from S1 pass unskipped.
  - The documents step: `README.md` says the store exists.
- **Out of scope.** HTTP, the signals, the agent.

### S4 · Pairing and agents on the server

**Track:** full · **Branch:** `epic-1/sprint-04-pairing` · **Needs:** S3.

- **Goal.** An application registers the server, maps the endpoints, creates pairing
  tokens, lists, revokes and releases.
  - An agent pairs over HTTP with a token.
  - Its signed requests are authenticated.
  - A poll records the agent's lease, bases and claims, and answers without commands when
    its timeout ends.
- **Contract.**
  - *Public, from S1, now working:*
    - `AddOutpostServer` in full;
    - `MapOutpostAgents` with `pair`, `poll` without commands, the answer to an
      unsupported version, and the body limits;
    - `CreatePairingTokenAsync`, `GetAgentsAsync`, `RevokeAgentAsync`, `ReleaseBaseAsync`;
    - the purge.
  - *Still refusing:* `ReadAsync`, `WriteAsync` and the `outcome` request.
  - *Internal:* the handlers of the endpoints and the holding of a poll, written into this
    brief after the study.
- **Study.** Sources, for example: the message long poll of the GitHub Actions runner
  service (MIT); the long polling transport of ASP.NET Core SignalR (MIT). Questions:
  1. How a long poll is held and ended: its timeout, a client that went away,
     cancellation, and a node that stops.
  2. How the server learns that the client of a waiting poll is gone.
- **To read.** The [concept](../concept.md), §3 "Pairing", rules 6, 10, 13 and 14, §4 and
  §5 (20 November 2026); `docs/protocol.md` (S2); this plan.
- **Acceptance.**
  - Over loopback HTTP, with a test agent built from the protocol's internal types:
    - pairing succeeds once, and the token's secret is gone after it;
    - a second use, an expired token, a wrong proof and a swapped key are refused;
    - the answer is signed and repeats the registered key;
    - a request with a changed body or another agent's key is refused;
    - a revoked agent's requests are refused;
    - an older poll, and an exact replay of the newest, change nothing; the answer to an
      older poll comes at once, signed, and names the newest number recorded;
    - a poll ends without commands at its timeout, with the fake clock;
    - `Connected` follows the lease, with the fake clock;
    - base keys follow rule 14, release included, and the poll answer reports them;
    - an unsupported version is answered with `protocol-version`;
    - an `outcome` request up to its limit passes Kestrel;
    - the purge removes an unused token and its pending agent.
  - The documents step: `README.md` says that agents can pair and poll.
- **Out of scope.** Commands; the signals, which commands need first.

### S5 · Commands on the server

**Track:** full · **Branch:** `epic-1/sprint-05-commands` · **Needs:** S4.

- **Goal.** Read and Write work on the server, end to end against an agent that speaks
  the protocol:
  1. a call creates a signed command;
  2. it wakes the agent's poll through the signals, and the poll hands the command out;
  3. the `outcome` request takes the agent's outcome;
  4. the outcome wakes the caller, and the call returns it.

  Rules 4–7 and 10–13 hold on the server.
- **Contract.**
  - *Public, from S1, now working:* `ReadAsync`, `WriteAsync`, the `outcome` request and
    polls with commands.
  - *Internal:* the waiting of polls and calls on the signals, written into this brief
    after the study. The rules for it:
    - the keys are under `SignalKeyPrefix`;
    - every waiting poll and every waiting call subscribes with a group of its own, so that
      a set wakes every waiter of its key;
    - each waits with `WaitAsync(null, …)`, and its deadline comes from the server's
      `TimeProvider`;
    - after a rejoin it reads the store again.
- **Study.** Sources, for example: the message acknowledgement of the GitHub Actions
  runner service client (MIT); the polling queue of Halibut (Apache-2.0). Questions:
  1. How a message handed to a poll whose answer was lost is found and handed out again.
  2. How a wake-up reaches a poll that waits on another node.
  3. How a waiting call ends cleanly when its caller cancels.
- **To read.** The [concept](../concept.md), §3, §4 and §5 (20 November 2026);
  `docs/protocol.md`; the `ISignals` and `ISignalSubscription` of
  [Moltaro.Signals](https://github.com/andrton/Moltaro.Signals) 0.1.0; this plan.
- **Acceptance.** Over loopback HTTP, with the test agent of S4:
  - Read and Write round trips, and each check of the order in `ReadAsync` and
    `WriteAsync`.
  - Rule 4:
    - a repeat with the caller key waits for the first outcome and returns it;
    - a different script or parameters is refused, with parameters compared as JSON
      values;
    - Expired and every Refused free the key;
    - a call whose key names a command never gets Unavailable;
    - two concurrent first calls with one key create one command, and both return its
      outcome.
  - Rule 6: Unavailable creates no command.
  - Rule 7: a command not taken in time expires; a handed-out command expires only on the
    agent's time. Both with the fake clock.
  - Rule 11:
    - only the first outcome counts, and only from the agent the command was handed to;
    - an outcome replaces a server-set Unknown or `lost`;
    - a poll that proves a command never reached the agent turns a server-set Unknown into
      Expired;
    - a taken command whose agent stays away longer than its run limit ends as Unknown
      `disconnected` or Failed `lost`, with the fake clock;
    - an outcome for an unknown command is confirmed and nothing is kept.
  - Rule 5: an outcome larger than its limit is refused by the server, and the Write it
    belongs to never becomes Failed. The agent then sends Unknown `result-too-large`.
  - Rule 12: a cancelled call leaves its command running, and its repeat returns the
    outcome.
  - Rule 13: revoking ends commands, with the codes of `OutpostCodes`.
  - The signals:
    - the keys carry the prefix;
    - two calls waiting on one command both return;
    - the newest poll is woken while an older poll of the same agent still waits;
    - a wait reads the store again after its subscription rejoins a restarted Signals host.
  - The documents step: `README.md` says that commands work against an agent that speaks
    the protocol.
- **Out of scope.** The agent program; several server nodes.

### S6 · The agent

**Track:** full · **Branch:** `epic-1/sprint-06-agent` · **Needs:** S5; L1 when it has run,
as [Order](#order-and-why-these-splits) says.

- **Goal.** The agent works end to end against the server of S5, with a fake behind the
  COM worker's boundary:
  - it checks its data folder, reads its policy, and polls with numbered polls through
    its proxy;
  - it checks each command (rule 1) and records it first (rule 2);
  - it runs each command once (rule 3), bounded by workers and run limits (rules 8, 9);
  - it sends outcomes until they are confirmed, through its outbox.

  It runs as a Windows service, and in tests as an in-process host.
- **Contract.**
  - *Public:* the files of S1, now read and written.
  - *Internal:* written into this brief after the study. They have to be able to:
    - run a command in a base and mode through a boundary that the COM worker of S9
      implements, and a fake implements in tests;
    - keep the secrets, and check the access list of the data folder, through boundaries
      that Windows implements, with fakes elsewhere;
    - find a command in the audit log and its outcome in the outbox, after a restart
      included;
    - number polls so that the numbers grow across restarts, as concept rule 10 says: from
      the agent's clock, and above the newest number that an answer to an older poll names.
      Nothing is stored for it, since the agent's account may not write outside `audit\`
      and `outcomes\`;
    - pair with a token, which the console of S10 calls (principle 12: the console depends
      on the agent).
- **Study.** Sources, for example: the message loop and job dispatcher of the GitHub
  Actions runner (MIT); the agent and worker of the Azure Pipelines agent (MIT). Questions:
  1. How the agent records a job before it runs, and what it does after a restart with a
     job that has no result.
  2. How a result is sent until the service acknowledges it.
  3. How a job's time limit is enforced, and what is reported when it is exceeded.
  4. How the agent hosts itself as a Windows service and as a console program, under a
     virtual service account.
- **To read.** The [concept](../concept.md), §3, §4 and §5 (20 November 2026);
  `docs/protocol.md`; this plan, "The files of the agent".
- **Acceptance.**
  - Every check of rule 1, each with its code in the outcome and in the audit log. A
    signature that does not verify is only recorded, and nothing is reported.
  - The order of the entries in the audit log:
    - `received`, then the outbox file, then `ended`, then `confirmed`;
    - every command of a poll answer recorded before the next poll opens (rule 10).
  - A command that cannot be recorded does not run.
  - After a restart:
    - a received command without an outcome is Unknown `agent-stopped` for a Write and
      `lost` for a Read;
    - an outbox file without its `ended` is recorded and sent.
  - Outcomes are sent until confirmed.
  - An outcome refused for its size, by the server's code and by a bare HTTP 413 of the
    test proxy: the outbox file and a second `ended` hold Unknown `result-too-large` for a
    Write and Failed `result-too-large` for a Read, and that is what is sent.
  - Workers bound what runs. A fake run that exceeds its limit is ended, giving `run-limit`.
  - Poll numbers grow across a restart, with the fake clock. After a restart with the clock
    set behind the last number, the first poll is answered as older, and the next one takes
    commands.
  - An answer whose signature does not verify is treated as a lost answer, and nothing in it
    is acted on.
  - An invalid policy serves nothing and is recorded.
  - The entries `connection` and `bases`.
  - The retention of the audit log.
  - Over loopback against the server of S5: a Read and a Write from `ReadAsync` and
    `WriteAsync` to the fake and back, through a test proxy named in `policy.json`.
  - With `Requires=Windows`:
    - the secrets file is encrypted with DPAPI for the machine;
    - a data folder, or a file in it, whose access list admits anybody else makes the
      agent stop, recorded;
    - the program runs as a Windows service.
  - The documents step: `README.md` says that the agent works with a fake in place of 1C.
- **Out of scope.** COM, the executor, the console.

### S7 · Several nodes and lost messages

**Track:** full · **Branch:** `epic-1/sprint-07-nodes` · **Needs:** S6.

- **Goal.** Several server nodes and an agent keep every rule when messages are lost or
  held, and when a node or the signals host stops. The latency target is measured.
- **Contract.** No public change. In `Moltaro.OnecOutpost.TestSupport`, a loopback proxy
  that can drop or hold a chosen request or answer, written in the contract step.
- **To read.** The [concept](../concept.md), §3 rules 7, 10 and 11, §4 and §11 (20
  November 2026); this plan.
- **Acceptance.**
  - With two server nodes on one PostgreSQL, the signals host in one node and a client in
    the other, an agent, and the proxy between them:
    - a call on one node and a poll on the other;
    - a node stopped while a poll waits on it;
    - a lost poll answer: the command is handed out again and runs once;
    - an older poll held and released, and an exact replay of the newest: they take
      nothing;
    - a lost outcome: it is sent again and counted once;
    - a command whose poll answer the proxy dropped, then delivered to the agent in a later
      answer while the agent's next poll is held back. It stays taken, and it expires only
      when a poll whose agent time is past the take deadline plus the skew does not list
      it (rule 7);
    - the signals host stopped: waits end at their deadlines, polls at their timeouts, and
      nothing is lost.
  - *Reported*, by the new program `Moltaro.OnecOutpost.Measurements`, its scenarios
    written by the test author. On loopback without 1C, with one node and with two:
    - the time from a call to the command reaching a waiting poll;
    - the time from an outcome to the waiting caller.

    Each is given as p50, p95 and maximum. The target of concept §11 is 100 ms at p95.
    The figures stay the same with a `PollTimeout` of 5 minutes, which shows that no hop
    waits for a fixed interval.
  - **Explanation E1**, "The path of a command", animated: a call, the store, the signal,
    the poll, the agent's checks and audit log, the worker, the outcome, the caller.
  - **Explanation E2**, "Why a command never runs twice", animated: a repeated call with
    its key, a lost poll answer, an older poll, a lost outcome, and the expiry decided by
    the agent's time.
  - The documents step: `README.md` says the tunnel works across nodes, with the measured
    figures.
- **Out of scope.** 1C.

### S8 · Refactoring

**Track:** refactoring · **Branch:** `epic-1/sprint-08-refactoring` · **Needs:** S7.

- **Goal.** The code base after stage 1 has no technical debt worth removing before stage
  2 builds on the agent.
- **Contract.** None; nothing public or asserted changes.
- **To read.** `AGENTS.md`; the [development process](process.md#a-refactoring-sprint), "A
  refactoring sprint"; the whole code base; the briefs of S9–S12.
- **Acceptance.** Build, format and every test green after every commit. No public member
  and no assertion changed. Each change makes the code simpler to read or to change. The
  documents and the explanations still agree with the code.
- **Out of scope.** Behavior, the contract, anything for a future need.
- KISS (`AGENTS.md`, "Keep It Simple") holds here as everywhere. The task is to remove
  technical debt where it is worth it, so that the code stays clean, easy to understand,
  easy to change and free of duplication. It is always a balance: a change that makes the
  code larger, more abstract or more general than today's case needs is not a
  refactoring.

### L1 · The lab check of open items O1–O8

**Track:** light · **Branch:** `docs/lab-check` · **Needs:** the owner's lab machine (OD1).
Best run before S6, and in any case before the contract of stage 2 is written.

- **Goal.** Each open item of concept §12 has an answer from a real 1C:Enterprise
  8.3.17.1851, so that the contracts of stage 2 rest on facts.
- **How.**
  1. An implementer, assigned by the coordinator, writes a minimal executor as XML sources,
     and the steps of the check for each open item.
  2. The owner builds the `.epf` and runs the check on a copy of a database, with that
     implementer.
  3. The planner writes each answer into concept §12. An answer that changes a decision,
     such as D3, D12 or D14, or a member of `policy.json`, goes to the owner and is written
     on this branch.
  4. An independent reviewer reviews the edit of the concept, until no P1, P2 or P3 finding
     is left.

  The minimal executor is not committed: S9 writes the executor from its contract. No
  report file is written.
- **To read.** The [concept](../concept.md), §3 "The two commands" and §12; this plan,
  "The files of the agent".
- **Done when** O1–O8 are answered in the concept and the change is merged.

### S9 · The executor and the COM worker

**Track:** full · **Branch:** `epic-1/sprint-09-com-worker` · **Needs:** S8, L1, and
the contract of stage 2 approved.

- **Goal.** A real base runs scripts. For each command, the COM worker:
  - holds the external connection under the account of its mode;
  - opens a fresh executor in safe mode;
  - runs the script with `Parameters`, `CallerKey` and `Result`;
  - runs a Write in one transaction, with the checks of rule 5 before the commit;
  - collects the user messages and reports the outcome.

  The agent starts the workers of each base and mode with the connector's bitness, inside
  a job object that ends them with the agent. It ends a worker at its run limit, and
  starts it again with a growing delay, up to `retryDelayMax`.
- **Contract.** As [Stages 2 and 3](#the-contracts-of-stages-2-and-3) says, completed with
  L1's answers before stage 2 starts. Exception X1 of the [register](exceptions.md) comes
  into force with the commit of the `.epf`.
- **Study.** Sources, for example: the worker process model of the Azure Pipelines agent
  (MIT); the job objects of a process-supervision library under a permissive licence.
  Questions: how a child process is tied to its parent's lifetime; how a hung child is
  ended; how parent and child exchange messages, and how either side learns that the other
  is gone.
- **To read.** The [concept](../concept.md), §3 "The two commands", rules 3, 5 and 9, §5
  and §12 with L1's answers; `AGENTS.md`, principles 8 and 9; this plan, "The files of the
  agent" and the contract of stage 2.
- **Acceptance.**
  - Automated, with `Requires=Windows`:
    - the worker program against a fake of the COM connection, behind the worker's own
      boundary;
    - the job object;
    - ending at the run limit;
    - starting again with a growing delay;
    - bitness chosen from the registered connector.
  - Manual, on the lab machine (OD1), one line per scenario:
    - a Read query that returns rows;
    - a Write that creates and posts a typical document, with the same movements as by
      hand;
    - a Write whose script raises an error rolls back;
    - a result of a wrong type, and one too large, roll back before the commit;
    - a script that calls `CommitTransaction` or `RollbackTransaction` gives Unknown
      `transaction-ended`;
    - a script beyond its run limit: a Read fails and a Write is Unknown;
    - stopping the agent during a Write gives Unknown;
    - the script contract:
      - `Parameters` maps JSON as concept §3 says: an object to a `Map`, an array to an
        `Array`, `null` to `Undefined`, no parameters to an empty `Map`, and a string that
        looks like a date stays a string;
      - `CallerKey` is visible to the script;
      - a date in `Result` is written without an offset, and `Undefined` and `Null` as
        `null`;
      - a reference in the `Result` of a Read fails with `result-type`;
      - a script that declares a procedure is refused;
      - the session of a worker lives on between commands;
    - user messages arrive and are trimmed to the limit;
    - safe mode refuses a file operation;
    - the read account cannot write;
    - a connector of another version is reported.
  - The executor's XML sources and its `.epf`, built by the owner with the Designer of
    8.3.17.1851, are committed in `src/Executor`.
  - The documents step: `README.md` says the agent works with 1C.
- **Out of scope.** The console, the shell.

### S10 · The console

**Track:** full · **Branch:** `epic-1/sprint-10-console` · **Needs:** S9.

- **Goal.** The administrator does everything for an agent from the console:
  - installs the service under its virtual account, with the data folder and its access
    list, and uninstalls it;
  - pairs it and writes its secrets;
  - checks the agent machine;
  - writes a metadata report of a base.
- **Contract.** As [Stages 2 and 3](#the-contracts-of-stages-2-and-3) says. The metadata
  report starts from the owner's collector and rewrites it to the rules of this repository
  (PQ5).
- **To read.** The [concept](../concept.md), §3 "Pairing" and §5 (20 November 2026);
  `docs/protocol.md`; `AGENTS.md`, principles 9 and 12; this plan, "The files of the agent"
  and the contract of stage 2.
- **Acceptance.**
  - Automated, with `Requires=Windows` where Windows is needed:
    - `install` and `uninstall` register and remove the service under
      `NT SERVICE\MoltaroOnecOutpost`, and create the data folder with its access list;
    - `pair` against a stub of the pair endpoint, which needs no store, writes
      `agent.json` and `secrets.dat`;
    - `secret` writes a password, encrypted for the machine;
    - `check` reports each case its contract names, against fakes. It checks the server
      without polling, as [The protocol](#the-protocol) says, and a test proves that the
      service's newest poll is not replaced;
    - every command's exit codes and errors;
    - with the output redirected, every command writes plain text, tested.
  - Manual, on the lab machine: `check` against a real base, the rights of the read
    account included; `metadata` writes a report of a real base; the owner runs every
    command in a terminal on the lab machine and accepts how it looks.
  - The documents step: `README.md` says how an agent is installed and paired.
- **Out of scope.** The shell.

### S11 · The shell

**Track:** full · **Branch:** `epic-1/sprint-11-shell` · **Needs:** S10.

- **Goal.** A person tries the tunnel by hand. The shell:
  - runs a server of its own, with Kestrel, an in-process `SignalHost`, a PostgreSQL
    connection and a signing key in a local file;
  - creates pairing tokens;
  - lists agents and bases;
  - sends Read and Write, and shows their outcomes.
- **Contract.** As [Stages 2 and 3](#the-contracts-of-stages-2-and-3) says.
- **To read.** The [concept](../concept.md), §2 and §5 (20 November 2026); this plan, "The
  public contract" and the contract of stage 2.
- **Acceptance.**
  - Automated: a session of shell commands read from its input against an agent with the
    fake worker, ending when the input ends, so that the shell runs to the end with
    `dotnet run`; with the output redirected, every command writes plain text, tested.
  - Manual, on the lab machine: a Read and a Write of S9's scenarios typed in the shell,
    with the agent on another machine reaching the shell over HTTPS; the owner runs every
    command in a terminal on the lab machine and accepts how it looks.
  - The documents step: `README.md` shows a session.
- **Out of scope.** The release.

### S12 · Documents, packages and the archive

**Track:** light · **Branch:** `chore/release-0.1.0` · **Needs:** S11.

- **Goal.** Everything is ready for the owner to tag the first release:
  - the documents say what exists;
  - E3 exists;
  - the package packs;
  - the Publish workflow also builds the archive of the agent and attaches it to the
    GitHub release of the tag, with its checksum.
- **Contract.** The archive, as [Stages 2 and 3](#the-contracts-of-stages-2-and-3) says:
  its name, its folders and its checksum. It is not signed (PQ4).
- **To read.** `AGENTS.md`; [branches and releases](branches.md#releases); this plan, "The
  first release" and the contract of stage 3.
- **Acceptance.**
  - `dotnet pack -c Release` produces exactly the server package, with the protocol
    assembly inside it. It carries the repository README, its XML documentation, the
    licence expression, the repository URL and its description.
  - The Publish workflow:
    - builds the archive before it pushes to nuget.org, so that a run on a test tag of a
      fork shows the archive even though its push to nuget.org fails;
    - has `contents: write`, so that it can create the release.
  - **Explanation E3**, "How the library is built", a still figure of the parts and their
    dependencies.
  - The final figures of [Measured results](#measured-results) in `README.md`.
  - The documents step over the whole product documentation, as the process says for the
    last sprint of an epic.
- **Out of scope.** The tag itself, which is the owner's.

## The contracts of stages 2 and 3

The planner writes these contracts out exactly before stage 2 starts, with L1's answers.
An independent reviewer reviews them until no P1, P2 or P3 finding is left, and the
owner approves them. Until then this is what they have to be able to do.

- **The executor**, an external data processor:
  - one exported function that runs a script in a mode with its parameters, caller key and
    outcome limit, and returns the outcome as JSON;
  - safe mode;
  - the transaction of a Write and the checks of rule 5 before its commit;
  - the user messages, trimmed with a marker.
- **The COM worker**, a program:
  - its command line names the base and the mode;
  - it receives the connection, with its password, from the agent over its standard input,
    never on the command line;
  - it exchanges one JSON message per line with the agent over standard input and output:
    a command in, an outcome out. The outcome's JSON from the executor is forwarded as it
    is, so that its size is what the executor measured;
  - it ends when the agent closes its input.
- **The console**, a program that references the agent, the protocol and Spectre.Console
  (principle 12), built as `AGENTS.md`, "Console programs", says, with these commands and
  their exit codes:
  - `install` and `uninstall`;
  - `pair <server address> <token>`;
  - `secret <base key> <read|write>`, which asks for the password without echoing it;
  - `check`, which reports, each as passed or failed with its reason:
    - the COM connector and its version;
    - the connection to each base under each account;
    - the rights to change data that each read account has;
    - the server;
    - the clock skew;
  - `metadata <base key> <file>`.
- **The shell**, a program built as `AGENTS.md`, "Console programs", says:
  - its options:
    - the PostgreSQL connection;
    - the address it listens on, `http` on loopback by default;
    - a certificate for an `https` address that an agent on another machine can reach;
    - the folder of its signing key;
  - its commands, one per line:
    - `token <agent name>`;
    - `agents`;
    - `read <base key>` and `write <base key> <caller key>`, each followed by the script
      and, optionally, parameters as JSON;
    - `revoke <agent id>` and `release <base key>`;
  - each outcome shown as its kind, its code, its result as indented JSON, and its
    messages.
- **The archive** of a release:
  - its name is `Moltaro.OnecOutpost-<version>-win.zip`;
  - it holds the agent, the COM worker for x64 and for x86, the console, the executor
    `.epf`, the licence and the notices;
  - every program in it is self-contained, so that no .NET runtime of either bitness has to
    be installed;
  - its SHA-256 checksum is on the release page;
  - it runs on the Windows versions that .NET 10 supports.
- **Outcome codes of stage 2:** the codes for a connection to 1C that fails or breaks
  during a command, after L1.

## The first release

The coordinator never creates a release tag; the release is the owner's
([branches and releases](branches.md#releases)). Before the owner tags it, all of the
following hold:

1. S12 is closed and merged; `main` is green on both CI jobs.
2. No entry point throws `NotSupportedException` and no test is skipped in the Release
   run of CI. The tests pass with their assertions unchanged, except corrections of a
   wrong test made by the rule of the process, each with its reason.
3. `dotnet pack -c Release` from `src` produces exactly `Moltaro.OnecOutpost.Server`, with
   the protocol assembly inside it. It depends only on the ASP.NET Core shared framework,
   EF Core with Npgsql and `Moltaro.Signals`.
4. The figures of [Measured results](#measured-results) are recorded on the final build,
   and the target is met or its miss was decided by the owner.
5. `README.md`, the concept, `docs/protocol.md` and the three explanations say what
   exists; `THIRD-PARTY-NOTICES.md` lists everything a sprint adapted from another project.
6. Every entry of the [open questions](open-questions.md) is answered, or carried into
   another epic by the owner's decision.
7. The owner's steps:
   - the nuget.org Trusted Publishing policy for this repository exists, as
     [branches and releases](branches.md#setting-it-up-on-github) says;
   - the owner merges the release change below, tags its merge commit on `main` as
     `v0.1.0`, and pushes the tag;
   - the Publish workflow builds, tests and packs, attaches the archive with its checksum
     to the release, and pushes the package.

**The release change.** `README.md` on `main` says what works today (`AGENTS.md`), and
the README packed into a package cannot change after it is published. So the section
"Packages and downloads" is turned to the published state by one small light-track change
after S12 is merged: the package on nuget.org from 0.1.0, the command that installs it, the
archive on the release page. The change touches nothing else and has its independent
review. The owner merges it and tags its merge commit right away.

After the release, a light-track change removes this document from `docs/work`, as the
[work README](README.md) says of a finished epic.
