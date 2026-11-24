# Epic 1: First release

**Status:** approved by the owner on 20 November 2026, with the public contract of stage
1; the owner answered PQ1–PQ6 the same day. [Status](#status) says where each sprint
stands.

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
- the user guides: how to install an agent, how to write scripts and how to embed the
  server ([The user guides](#the-user-guides));
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
| OD1 | **The lab machine** (20 November 2026). The owner provides, when a sprint asks for it, a Windows machine with 1C:Enterprise 8.3.17.1851 and its Designer, and builds the executor `.epf` from its XML sources with the Designer of that version. No CI job needs 1C. By the owner's answer of the same day on tests with 1C, the machine holds the repository's test base, built from `src/OneC.TestConfiguration`, and at least one copy of a typical base on its 1C cluster, and runs the tests that need 1C there. |
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
| R5 | Workers and run limits (rules 8, 9) | Concept §3, D11 | Must | Absent | S6, S9b |
| R6 | The caller decides how long it waits (rule 12) | Concept §3 | Must | Absent | S5 |
| R7 | Pairing with the agent's key pair, the HMAC under a secret that never travels, and the fingerprint of the server key | Concept §3 Pairing, D4, D5 | Must | Absent | S2, S4, S6, S10 |
| R8 | The protocol v1, written exactly in `docs/protocol.md`; a command signed over its exact bytes; every agent request signed | Concept §6, D4, D5, D15 | Must | Absent | S2 |
| R9 | The store: an EF Core context on PostgreSQL in a schema the application chooses; migrations as EF Core migrations and as one SQL script per migration; removal after retention | Concept §5, D9 | Must | Absent | S3, S4 |
| R10 | The wake-ups through the `ISignals` the application provides: keys under a prefix, a group of its own for every waiting poll and call, every wait reading the store again when it rejoins | Concept §5, D9 | Must | Absent | S5, S7 |
| R11 | Several nodes, a stopped node, a lost poll answer, an older or replayed poll, a lost outcome and an outage of the signals behave as the rules say | Concept §4, §11 | Must | Absent | S7 |
| R12 | The agent as a Windows service under its own virtual account, with the data folder that only it and administrators can use, the local policy, the secrets protected with DPAPI for the machine, and the audit log with its retention | Concept §5, D6, D13 | Must | Absent | S1, S6, S10 |
| R13 | The COM worker and the executor: the external connection under the account of the mode, a fresh executor per command in safe mode unless D12 turns it off for the Writes of a base, the JSON rules, the user messages, the Write transaction with its checks before the commit, the connector's bitness, the workers ending with the agent, a worker ended at its run limit | Concept §3, §5, D3, D11, D12, D14 | Must | Absent | S9, S9b, S9c |
| R14 | The console: install, uninstall, pair, write a secret, check, write a metadata report | Concept §5 | Must | Absent | S10 |
| R15 | The shell: a server of its own, pairing tokens, Read and Write by hand | Concept §2, §5 | Must | Absent | S11 |
| R16 | Every timeout and limit that an application or an administrator can observe is an option with a default, validated when it is set; time comes from `TimeProvider` | `AGENTS.md`, principle 10 | Must | Absent | S1–S11 |
| R17 | Tests as concept §11 and `AGENTS.md` require: a deterministic test of every numbered rule, the transport over loopback HTTP, the store against a real PostgreSQL, a fake clock, no sleeps; 1C only in the tests of `Moltaro.OnecOutpost.OneC.Tests` on the lab machine, never in CI | Concept §11; `AGENTS.md` | Must | Absent | S1–S11 |
| R18 | The answers to open items O1–O8, from the lab check, written into the concept; the tests that need 1C and the manual acceptance of S9, S10 and S11 on the lab machine, on the test base and on each copy of a typical base | Concept §11, §12; OD1 | Must | Absent | L1, S9, S9b, S9c, S10, S11 |
| R19 | The measurement program for the latency target of concept §11, with the figures under [Measured results](#measured-results) | Concept §11 | Release | Absent | S7, S12 |
| R20 | The explanations E1 "The path of a command" (animated), E2 "Why a command never runs twice" (animated), E3 "How the library is built" (a still figure), each as Markdown and HTML | `AGENTS.md`, "Documentation" | Release | Absent | S7, S12 |
| R21 | The packages of PQ6, published from a version tag through nuget.org Trusted Publishing; the archive of the agent on GitHub Releases with its SHA-256 checksum | Concept D15, D16; PQ4 | Release | The workflows of the packages exist; no package | S1, S12 |
| R22 | `README.md`, the concept, `docs/protocol.md` and the explanations say what exists after every sprint | `AGENTS.md`, "Documentation" | Release | Holds: nothing exists and they say so | S1–S12 |
| R23 | The public contract of stage 1 in code, every public member with its XML documentation | `AGENTS.md`, "The Contract Comes First" | Must | Absent | S1 |
| R24 | The user guides G1 "Installing and running an agent", G2 "Writing scripts" and G3 "Embedding the server", in `docs/guides`, linked from `README.md` and from the release page, each checked as [The user guides](#the-user-guides) says | The owner, 21 November 2026 | Release | Absent | S9b, S10, S11, S12 |

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
   `dotnet format --verify-no-changes` on the `build` job, with the build of
   `Moltaro.OnecOutpost.OneC.Tests` on both jobs and its format on the `build` job. The
   samples and the shell run to the end, and the code in `README.md` is the code of the
   samples.
5. Every figure of [Measured results](#measured-results) is recorded on the final build,
   and the target of concept §11 is met, or its miss was handled by the rule of the
   [development process](process.md#when-to-stop-and-ask-the-owner) and decided by the
   owner.
6. Every test of `Moltaro.OnecOutpost.OneC.Tests` passed on the lab machine, on the final
   build, and every scenario of the manual acceptance of S9, S10 and S11 passed there.
7. The three explanations exist in both forms, agree with each other and with what was
   delivered, and their status lines say "built".
8. The three user guides exist, `README.md` and the release page lead to them, every
   script and every command they show ran as [The user guides](#the-user-guides) says, and
   a person who did not write them installed an agent from the archive by G1 alone.
9. Everything [The first release](#the-first-release) lists holds, so that the owner can
   tag the release.

### Measured results

Filled by the coordinator when a sprint closes. The program prints the processor, the
operating system and the .NET version with every figure, never a host name; raw output is
not committed. S12 moves the final figures into `README.md`.

| Sprint | Scenario | Figure | Target | Machine | Date |
| --- | --- | --- | --- | --- | --- |
| S7b | One node, `PollTimeout` 25 s, call → waiting poll | p50 15.4 ms, p95 30.6 ms, max 189.3 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S7b | One node, `PollTimeout` 25 s, outcome → waiting caller | p50 13.3 ms, p95 29.1 ms, max 229.4 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S7b | Two nodes, `PollTimeout` 25 s, call → waiting poll | p50 14.6 ms, p95 23.7 ms, max 52.1 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S7b | Two nodes, `PollTimeout` 25 s, outcome → waiting caller | p50 12.5 ms, p95 21.9 ms, max 244.7 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S7b | One node, `PollTimeout` 5 min, call → waiting poll | p50 15.0 ms, p95 24.8 ms, max 41.3 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S7b | One node, `PollTimeout` 5 min, outcome → waiting caller | p50 12.8 ms, p95 21.1 ms, max 29.5 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S7b | Two nodes, `PollTimeout` 5 min, call → waiting poll | p50 15.1 ms, p95 25.0 ms, max 35.1 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S7b | Two nodes, `PollTimeout` 5 min, outcome → waiting caller | p50 12.5 ms, p95 21.0 ms, max 33.8 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S10aR | One node, `PollTimeout` 25 s, call → waiting poll | p50 16.9 ms, p95 22.4 ms, max 29.4 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S10aR | One node, `PollTimeout` 25 s, outcome → waiting caller | p50 14.5 ms, p95 19.9 ms, max 25.9 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S10aR | Two nodes, `PollTimeout` 25 s, call → waiting poll | p50 17.1 ms, p95 22.4 ms, max 31.5 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S10aR | Two nodes, `PollTimeout` 25 s, outcome → waiting caller | p50 14.7 ms, p95 20.1 ms, max 31.1 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S10aR | One node, `PollTimeout` 5 min, call → waiting poll | p50 17.1 ms, p95 23.0 ms, max 33.6 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S10aR | One node, `PollTimeout` 5 min, outcome → waiting caller | p50 14.4 ms, p95 19.6 ms, max 26.5 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S10aR | Two nodes, `PollTimeout` 5 min, call → waiting poll | p50 18.1 ms, p95 25.0 ms, max 108.4 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |
| S10aR | Two nodes, `PollTimeout` 5 min, outcome → waiting caller | p50 15.5 ms, p95 20.3 ms, max 68.5 ms | 100 ms at p95 | i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15 | 23 November 2026 |

On loopback without 1C, 500 Reads per case after 50 of warm-up, with a test agent that
flushes nothing to the disk, the server's nodes on the fake clock of the tests, which never
moves: every round of every case ended with that clock standing still, which shows that no
hop waits for a fixed interval. Two earlier runs the same day, on the machine loaded by
other builds and tests, gave the same p50 and a p95 of up to 131 ms in one case each time,
a different case each run; the figures above are of a run on the quiet machine.

S10aR measured the same cases again after its store took one transaction frame (review 1,
entry 8), on the quiet machine, the build of a5a6fcd: every p95 stays within the figure of S7b, the two nodes' poll at 5 minutes level
with it at 25.0 ms.

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

## The user guides

The owner's requirement of 21 November 2026: a person who downloads the agent has to
learn from the repository how to install it and how to use it, not only what the library
guarantees. The concept says why and what; the guides say how. They are product
documentation, in English, in `docs/guides`, as Markdown that GitHub renders, and
`README.md` leads to each of them in its first screen.

**Rules for all three.**
- **A guide is written for its reader, who knows nothing of this repository.** It starts
  with what the reader needs before the first step, goes step by step, shows for each
  step the command to paste and what a success looks like, and ends with what to do when
  it fails: each error the reader can meet, in the words the program prints, with its
  cause and its remedy.
- **Nothing in a guide is untried.** Every command of the console and the shell that a
  guide shows is run by a test of that program, and every script by a test of
  `Moltaro.OnecOutpost.OneC.Tests` against the test base. The source of a script is the
  code block of the guide's Markdown: the test reads it from there, and no copy of it is
  kept elsewhere, so that a guide cannot drift from what works. C# shown in a guide is
  the code of a sample.
- **A guide says what the library does not do** where the reader would expect it, in one
  sentence with the reason, instead of leaving it to be found out.
- **A guide names no customer, base or configuration of anybody.** Its examples use the
  repository's own test configuration.
- A guide is written by one agent with no other role in its sprint, after the
  implementation and before the independent review, and reviewed with the sprint, like an
  explanation. Its status line says what of it works today.

**G1. Installing and running an agent** (`docs/guides/agent.md`), for the administrator
of the back office. Written in S10, when the console exists.
- What the machine needs: Windows, the 1C platform of the cluster's version, a route to
  the server over HTTPS, through a proxy or not.
- The COM connector: that installing 1C does not register it, how `check` finds it, and
  `connector register`.
- The two 1C accounts: which rights the read account and the write account need, that
  both need the right of external connection, and that the read account gets no right to
  change data.
- Unpacking the archive and checking its checksum; `install`; where the data folder is and
  who may read it.
- `policy.json`, member by member, with a whole example for a client-server base and one
  for a file base.
- `secret`, `pair`, and what the application's side gives the administrator for it.
- `check`, line by line: what each line means and what to do when it fails.
- Safe mode: what it protects, when the Writes of a base need it off (`safeMode`), and
  what the separate Windows account of its workers then protects.
- A configuration with modules of its own: the message of `check` about a common module
  that is not available to an external connection, and the one flag to ask the
  developer of that configuration for.
- Licences: each worker holds one client licence; how many workers to configure.
- The audit log: where it is, how to read an entry, how long it is kept.
- Updating and uninstalling. What happens when the machine is cloned.

**G2. Writing scripts** (`docs/guides/scripts.md`), for the developer of the application
that calls Read and Write. Written in S9b, when a command first reaches 1C, from what S9
and L1 established.
- The contract of a script: `Parameters`, `CallerKey`, `Result`; how JSON becomes 1C
  values and back; a date; `Undefined` and `Null`.
- What a script may not do and why: declare a procedure, return a reference, end its
  transaction, reach files in safe mode, use a function that the compatibility mode of
  the base lacks.
- Read and Write: the account, the transaction, what Failed, Refused and Unknown mean for
  the caller, and what to do after an Unknown Write with the caller key.
- Sizes and limits: the limit of an outcome, reading a large list in pages, the run
  limit.
- User messages and the error text, and that the wording of 1C depends on the machine.
- **Recipes,** each a whole script with its parameters and its result:
  - the catalogs, documents and registers of a base, from `Metadata`;
  - the attributes and tabular sections of one object, with their types;
  - a page of the items of a catalog, with code and description;
  - a search by description, and the children of an item of a hierarchical catalog;
  - one item by its unique identifier, and how to carry a reference as that identifier;
  - a query with parameters;
  - creating and posting a document, made idempotent with `CallerKey`;
  - finding out what an Unknown Write did.

**G3. Embedding the server** (`docs/guides/server.md`), for the developer of the
application. Its first part, trying the tunnel by hand with the shell, is written in S11;
the rest in S12, from the sample.
- A first session with the shell: its options, a token, pairing an agent, a Read and a
  Write by hand.
- The package, `AddOutpostServer` and `MapOutpostAgents`, the options one by one with
  their defaults and what each one costs or protects.
- What the application provides: PostgreSQL and the migrations, both ways of applying
  them; Moltaro.Signals; the signing key and where to keep it; who may call Read and
  Write.
- Pairing from the application: creating a token, giving it to the administrator,
  listing agents, revoking one, releasing a base key.
- Calling Read and Write: the requests, the outcome and its kinds, the codes one by one
  with what the caller should do for each, cancelling a wait, repeating with the caller
  key.
- Several nodes and the ingress: what a poll and an outcome need from it.
- What to watch in production, and what the library does not report.

## The public contract

The public surface of stage 1: the server library and the files the agent reads. S1 puts
it into code exactly as written here. A name, a signature or a behavior that is not here
goes back to the planner and, as a change of the contract, to the owner. The public
contracts of stages 2 and 3 are written into their briefs before those stages start
([Stages 2 and 3](#the-contracts-of-stages-2-and-3)). Concept §7 names these types. Beyond
the outline the concept had before S1, this contract adds `CommandId` to an outcome, so
that a caller can find the command in the agent's audit log, and `MessagesTrimmed`, the
marker of rule 5.

Text marked *(S2)* was changed in S2 together with `docs/protocol.md`. It waits for the
owner's approval of the protocol, with the changes that S2's brief lists, and holds only
after it.

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
  | Agent name | A display label: 1 to 100 characters of any kind but control characters, counted as UTF-16 code units (`string.Length`), with no white space at the start or the end, and no lone surrogate, a code unit from U+D800 to U+DFFF without its pair (Q2, Q6) |
  | Script | At least one character that is not white space, no U+0000, and no lone surrogate (Q6) |
  | Parameters | No string in them, names included, holds U+0000, which PostgreSQL cannot store, or a lone surrogate, which UTF-8 cannot carry (Q6); no object in them, at any depth, gives a name twice, the names compared after their escapes are read (S5); they nest at most 64 levels, the outer object being level 1, a limit of the protocol (Q5) |
  | Schema | 1 to 63 characters, each one of `a–z 0–9 _`, the first a letter |
  | Endpoint prefix | `/` followed by one or more segments of `A–Z a–z 0–9 - _ .` separated by `/`, not ending with `/`; no segment is `.` or `..` |
- **Sizes.** Every size is counted on the bytes that the protocol carries: UTF-8, with
  characters outside ASCII written as they are, not escaped.
  - *(S2)* The size of a command is the length of the text of its script between its
    quotes plus the text of its parameters, `{}` when there are none, both as the command
    carries them, every escape counted as it is written (`docs/protocol.md` §7). A call
    whose command is larger than `CommandSizeLimit` is a caller's mistake and gives
    `ArgumentException`.
  - *(S2)* The size of an outcome is the length of the UTF-8 text of its outcome object as
    it travels, with its result, its messages and its error text, every escape counted as
    it is written (rule 5; `docs/protocol.md` §8).
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
| `SigningKeyPem` | The server's signing key, as PKCS#8 PEM of an ECDSA P-256 private key. The application keeps it in its own secret storage; one line of .NET creates one: `ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem()` | Required: exactly one PEM block, the unencrypted PKCS#8 `PRIVATE KEY` of an ECDSA P-256 key, with nothing but white space before or after it; any other text, a second block included, is refused (Q4) |
| `SignalKeyPrefix` | The prefix of every key the library sets or subscribes to | A name of Moltaro.Signals of at most 64 characters |
| `PollTimeout` | How long a poll waits for a command at most: a poll is held for the smaller of the poll timeout it carries and this option (§6.2 of the protocol). It also bounds each wait of a call for its outcome, after which the call reads the store again, so that a lost wake-up of the signals delays an outcome by at most this option | 1 second to 5 minutes |
| `LeaseGrace` | What the lease of a poll adds to its hold. The lease ends at the earlier of the moment the poll is recorded and its signed time plus `ClockSkew`, plus the hold, plus the grace (rule 6, §6.2 of the protocol) | 1 second to 5 minutes |
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
- `ErrorText` is the text of the error that 1C raised, for Failed with `script-error`, or
  the executor's own sentence for a transaction the script left open, or for a Write whose
  base refused a query before the commit *(S9)*.
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
    public const string Malformed = "malformed";
    public const string Expired = "expired";
    public const string ClockSkew = "clock-skew";
    public const string Revoked = "revoked";
    public const string Disconnected = "disconnected";
    public const string AgentStopped = "agent-stopped";
    public const string TransactionEnded = "transaction-ended";
    public const string BaseUnreachable = "base-unreachable";
    public const string ConnectionLost = "connection-lost";
}
```

| Code | Kinds | Meaning |
| --- | --- | --- |
| `script-error` | Failed | 1C raised an error; `ErrorText` holds its text. *(S9)* Also a script that left a transaction of its own open, which the executor rolled back, and a Write whose base refused a query before the commit, as it does after a nested rollback; `ErrorText` is then the executor's own sentence. A Write rolled back |
| `result-type` | Failed, Unknown | The result holds a value that cannot be written as JSON (rule 5). *(S2)* Also a result deeper than 64 levels, and an outcome of the executor that the agent cannot accept: Failed for a Read, and for a Write that 1C rolled back; Unknown for a Write whose outcome the agent could not accept (`docs/protocol.md` §8) |
| `result-too-large` | Failed, Unknown | The outcome is larger than its limit (rule 5). Unknown for a Write whose outcome was refused for its size, by the server or by something on the path; S5 and S6 state it |
| `run-limit` | Failed (Read), Unknown (Write) | The script exceeded its run limit (rule 9) |
| `lost` | Failed | A Read whose agent or worker stopped, whose agent stayed away, or whose agent was revoked (rules 3, 11, 13) |
| `caller-key-conflict` | Refused | The caller key names another command (rule 4) |
| `unknown-base` | Refused | No agent holds the base key |
| `mode-closed` | Refused | The agent that holds the base does not open the mode |
| `not-served` | Refused | The agent's local policy does not serve the base or the mode (rule 1) |
| `not-addressed` | Refused | The command names another agent (rule 1) |
| `protocol-version` | Refused | The agent does not support the protocol version of the command (rule 1) |
| `malformed` | Refused | *(S2)* A verified command of this protocol version breaks §7 of `docs/protocol.md` (rule 1) |
| `expired` | Refused | By the agent's clock, the take deadline passed longer than the skew ago (rule 1) |
| `clock-skew` | Refused | By the agent's clock, the command was issued further ahead than the skew (rule 1) |
| `revoked` | Refused, Unknown | Refused: no poll had taken the command when its agent was revoked. Unknown: a Write that a poll had taken (rule 13) |
| `disconnected` | Unknown | A Write whose agent stayed away longer than its run limit (rule 11) |
| `agent-stopped` | Unknown | A Write whose agent or worker stopped while it ran (rule 3) |
| `transaction-ended` | Unknown | The script ended the transaction of its Write itself (rule 5). *(S9)* Also a commit of the executor that raised, so it cannot tell whether the Write was written |
| `base-unreachable` | Failed | *(S9)* For a Read and a Write: the COM worker has no session in the base, or could not open the executor, so the script did not start. `ErrorText` is `null` |
| `connection-lost` | Failed (Read), Unknown (Write) | *(S9)* The session of the COM worker ended while the script ran |

- Every Refused, like Expired, means that nothing ran. A command that ended as Refused or
  Expired frees its caller key (rule 4); a refusal with `caller-key-conflict` created no
  command and leaves the key with the command that holds it.
- There is no code for a signature that does not verify. The agent cannot trust the
  identity of such a command, so it only records it in the audit log and reports nothing.
  The server ends the command by rules 7 and 11 (concept rule 1).
- A failure of the connection to 1C got two codes in S9, `base-unreachable` and
  `connection-lost`, a draft of the contract for the owner (the planner, 21 November 2026,
  after L1). Examples of `base-unreachable` are a wrong password, a cluster that does not
  answer, no free licence, a COM connector that is not registered; its `ErrorText` is
  `null`, since the text of 1C may name an account or a path and goes to the audit log
  only. An example of `connection-lost` is a cluster that restarted while the script ran.

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
  this agent does not hold: another agent holds it, or, after a release or a revocation,
  no agent holds it until this agent's next poll claims it (rule 14; a draft of S4).

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
  creates the schema when it does not exist, and the database when it does not exist and
  the role of the connection may create one. Calls for one schema, on one node or on
  several, wait for each other on a session advisory lock, so the connection keeps its
  session for the whole call: a direct connection or a pooler in session mode, not one
  that pools by transaction. A failure of the database that may pass, such as a refused
  connection, is reported as `InvalidOperationException` with the `NpgsqlException`
  inside; one that does not pass, such as a connection string without a password, as
  `NpgsqlException`, and a refusal by the server, such as a wrong password, as the
  `PostgresException` that derives from it.
- `GetScripts` returns one SQL script per migration, in order, for an application that
  applies migrations itself. Applying them in order to an empty schema gives the same
  tables, and the same record of applied migrations, as `MigrateAsync` does. `Name` is the
  identifier of the EF Core migration. Each script carries its own transaction, so a tool
  does not wrap it in another.
- `schema` follows the rule of names.

### The files of the agent

The agent keeps everything in one folder, `%ProgramData%\Moltaro.OnecOutpost`: the data
folder.

Text marked *(S6)* holds as a draft of the contract, until the owner overturns it: the
changes "Carried over from S2" that S6 builds, and the list "What S6 changes in the public
contract", both in [S6's brief](#s6--the-agent). Text marked *(S9b)* holds the same way:
the list "What S9b changes in the public contract, as a draft for the owner" in
[S9b's brief](#s9b--the-agent-runs-the-com-workers). Text marked *(S9c)* holds the same way:
the list "What S9c changes in the public contract, as a draft for the owner" in
[S9c's brief](#s9c--the-account-of-the-workers-without-safe-mode). Text marked *(S10a)*
holds the same way: the list "What S10a changes in the public contract, as a draft for the
owner" in [S10's brief](#s10--the-console).

- **The data folder admits nobody else.** The console's `install` creates it with
  inheritance off:
  - Administrators and SYSTEM have full control;
  - the agent's account may read the folder, and may modify only `audit\` and
    `outcomes\`;
  - nobody else has any right. `%ProgramData%` lets every local user create
  files, so without this a local user could plant a policy, a pinned key or an outcome.

  *(S10a)* A data folder that exists already is given these lists again only when neither
  it, `audit\` nor `outcomes\` is a reparse point or admits anyone else; otherwise
  `install` refuses it and repairs nothing. `pair` and `secret` refuse a data folder that is
  a reparse point or admits anyone else; they judge the folder itself. An owner
  other than Administrators, SYSTEM or the agent's account counts as somebody else, since an
  owner can always change the access list (Q14 in [S10's brief](#s10--the-console)), so
  `install`, `pair` and `secret` make Administrators the owner of the folder and of every
  file they write.
- **The agent refuses a folder that admits anybody else.** When it starts, it checks the
  access lists of the folder and of every file it reads, since a file moved into the
  folder keeps its own. If one of them admits anyone else, it records `started` with an
  error and stops.
- *(S9c)* **The agent refuses a program folder that others may change**, since an account
  that could replace the code the agent's account runs, its own program, the worker or the
  executor, would get its secrets. When it starts, it checks the access lists of the folder
  of its program, of everything in it, and of the folders above it up to the volume's
  root, as "The program folder" in S9c's brief says. If anyone but Administrators, SYSTEM,
  `NT SERVICE\TrustedInstaller` and the agent's account could change them, it records
  `started` with an error that names the entry and the accounts, and stops. The console's
  `install` puts the program under `%ProgramFiles%` with inheritance off, full control for
  Administrators and SYSTEM, read and run for Users, and Administrators owning every entry
  it writes. *(S10a)* That folder is `%ProgramFiles%\Moltaro.OnecOutpost`: `install`, run
  from outside it, replaces its content with that of the unpacked archive, or, run from
  that folder itself, sets its lists alone; it refuses a run from inside it or from a
  folder that holds it. Then it judges the folder by the agent's rule before it registers
  or reconfigures the service, and refuses, without repairing, a folder that someone else
  may change. The folder of the COM connector is judged by the same rule by the console's
  `check`, not by the agent.
- *(S10a)* **The COM connector** is the one that the classes of HKLM register, in the
  64-bit view and in the 32-bit one, never HKCR, which merges in the classes of the user
  who runs the console, unseen by the service; a registration counts only when its file
  exists. The agent and the console read it through one finder. The agent starts its
  workers with the bitness of the first registered connector, 64 bits first, so a
  registration whose file is gone picks no bitness. `connector register` registers a
  connector only where exactly one version of a bitness is installed, since the connector
  has to match the version of the 1C cluster.
- **The agent's account** is the virtual service account `NT SERVICE\MoltaroOnecOutpost`,
  which needs no password. *(S9c)* The console's `install` grants it "Replace a process
  level token" and "Adjust memory quotas for a process", which it needs to start a worker
  under a worker account; they take effect from its next start. *(S10a)* `install`
  registers the service `MoltaroOnecOutpost` under that account with an automatic start,
  and does not start it; it reconfigures a service that exists, never deletes it, and
  writes the failure actions, a restart after a minute at most three times, the count
  reset after a day, only when it creates it. `uninstall` takes the two rights back,
  then removes the service, and leaves both folders.
- *(S9c)* **A worker account** runs every COM worker of a base whose `write` has
  `safeMode: false`, Reads included. It is a local user outside Administrators, with no
  privilege beyond those of Users, and "Log on as a batch job", which *(S10a)* the
  console's `secret <base key> worker` grants, not `install`, since the policy names the
  account only after `install`; `uninstall` leaves it that right. The agent logs it on at
  its start, before any worker starts, and stops with an error when it cannot. Its profile
  is not loaded. A script without safe mode does what that account can, can leave a
  process outside the job object through Task Scheduler or WMI, and can read and forge
  what the other workers of its base do.
- *(S6)* **The agent program** takes one argument, `service`, and runs only as the
  Windows service. It exits with 0 when stopped; 1 when it stops on its own, recorded in
  the audit log when the log can be written; 2 when the log was not opened, as for a
  second copy, with nothing recorded; 3 when started outside the service controller, with
  nothing in the data folder changed; 4 for any other arguments; 5 when not on Windows.
  As a service it reports a code other than 0 as `ERROR_SERVICE_SPECIFIC_ERROR` with
  that code.

| File | Written by | Public | Content |
| --- | --- | --- | --- |
| `policy.json` | The administrator | Yes | The local policy, below |
| `audit\YYYY-MM-DD.jsonl` | The agent | Yes | The audit log, one file per UTC day, below |
| `agent.json` | The console, when it pairs | No | The server's address, the agent's identity, the pinned server key, and the poll timeout and lease grace of the pairing answer |
| `secrets.dat` | The console | No | The agent's private key and the 1C passwords, *(S9c)* and the password of the worker account of each base that names one, encrypted with DPAPI for the machine. *(S10a)* `pair` writes it with the key alone, and `secret` writes each password, keeping the others |
| `outcomes\` | The agent | No | The outbox: outcomes not yet confirmed by the server, one file per command |

#### `policy.json`

```json
{
  "auditRetentionDays": 90,
  "proxy": "http://proxy.example.com:3128",
  "retryDelayMax": "00:00:30",
  "requestTimeout": "00:05:00",
  "bases": [
    {
      "key": "accounting",
      "server": "srv-1c:1541",
      "database": "accounting",
      "read": { "account": "outpost-read", "workers": 1, "runLimit": "00:10:00" },
      "write": { "account": "outpost-write", "workers": 1, "runLimit": "00:10:00", "safeMode": false, "workerAccount": "outpost-worker" }
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
| `proxy` | The HTTP proxy through which the agent reaches the server, as an absolute `http` URL. When absent, the agent connects directly. *(S6)* A URL with a user or a password is refused (Q7). The agent offers the proxy no credentials, by no scheme, so never NTLM: a proxy that asks for authentication answers 407, and the agent is not connected through it (`status-407`). The proxy is used only for an `https` server. Offering the computer's credentials by Kerberos alone is planned |
| `retryDelayMax` | The longest delay between attempts after an error, for the connection to the server and for starting a COM worker again. The first delay is a tenth of it. *(S6)* A tenth of it is also the wait after an answer to an older poll (§6.2 of `docs/protocol.md`). In the form of `runLimit`, from 1 second to 1 hour; 30 seconds when absent |
| `requestTimeout` | *(S6)* The agent's wait for an answer to `pair` or `outcome`, and for the body of a poll answer. In the form of `runLimit`, from 10 seconds to 1 hour; 5 minutes when absent |
| `bases` | At least one base, *(S6)* at most 256; the keys are unique |
| `key` | The rule of base keys |
| `server`, `database` | The 1C cluster, with its port when it is not the default, and the database name. Either both, or `file` |
| `file` | The folder of a file database. Either this, or `server` and `database` |
| `read` | Required |
| `write` | Present only when the base may be written |
| `account` | The 1C account of the mode; its password is a secret that the console writes |
| `workers` | 1 to 8; 1 when absent |
| `safeMode` | In `write` only: whether the Writes of the base run in the 1C safe mode; `true` when absent. With `false`, the COM workers of the base run under a Windows account of their own (D12), *(S9c)* which `workerAccount` names. A script without safe mode does what that account can, can leave a process outside the job object through Task Scheduler or WMI, and can read and forge what the other workers of its base do. In `read` it is an unknown member: a Read always runs in safe mode |
| `workerAccount` | *(S9c)* In `write` only: the local Windows account under which every COM worker of the base runs, Reads included; its password is a secret that the console writes. Required when `safeMode` is false, refused when it is true or absent. 1 to 20 characters, none of `" / \ [ ] : ; \| = , + * ? < > @` or a control character, and not only periods and spaces, so never a domain account or a UPN; unique across bases, ignoring case, so that the scripts of one base cannot reach the workers of another. In `read` it is an unknown member |
| `runLimit` | A .NET `TimeSpan` in its constant form `[d.]hh:mm:ss`, from 1 second to 1 day (`1.00:00:00`); 10 minutes when absent. *(S6)* It is also the deadline for a worker of the mode to open its session: a worker that has not opened by then is ended and never runs a command |

- **An invalid file serves nothing.** An unknown member, a member given twice, a wrong
  type, a member set to `null`, or a value out of its rule makes the whole file invalid;
  an empty or white-space string is out of the rule of `server`, `database`, `file` and
  `account` (Q1), and a lone surrogate, escaped in a string or in a member name, is out of
  the rule of every string (Q6). JSON comments are not allowed. The agent then serves
  nothing and records why in the audit log. *(S6)* It records `started` with an error and
  `policy` with `valid` false, and stops; nothing is polled until a start with a valid
  policy, and the outbox waits on the disk meanwhile. A mode that the policy serves without
  its password in `secrets.dat` stops the start too, *(S9c)* as does a `workerAccount`
  without its password there.
- **The error says clearly where and what** (Q1): the path of the member, such as
  `bases[1].read.workers`, what is wrong with it, and what is expected there. For example:
  `bases[0].server: the value is empty; expected the 1C cluster address such as
  "srv-1c:1541", or remove "server" and "database" and give "file"`. A file that is not
  JSON is reported by the line and the column of the fault, counted from 1, the column in
  characters, each Unicode scalar value counting as one. The error never repeats a string
  value of the file, which may hold a secret such as a password in the URL of the proxy
  (principle 9); it names the kind of a wrong value instead, such as empty or a number.
  *(S6)* The file is read as UTF-8, a byte order mark skipped and not counted in the
  columns; bytes that are not UTF-8 make it invalid, with an empty path.
- **The policy is read when the agent starts.** A change takes effect when the service is
  restarted.

#### The audit log

Each line is one JSON object in UTF-8, written and flushed before the agent goes on (rule
2). Every entry has `time`, the UTC time with milliseconds in ISO 8601, and `entry`:

| `entry` | Further members | When |
| --- | --- | --- |
| `started` | `version`, *(S6)* `withoutSafeMode` (*(S9b)* the keys of the bases whose policy turns safe mode off for Writes, an array, empty when there is none and when there is an `error`), `error` (when it stops at once) | The agent started |
| `policy` | `valid`, `error` (when not valid) | The agent read its policy |
| `connection` | `state` (`connected` or `lost`), `reason` (when lost) | The agent's connection to the server changed. *(S6)* Once per change of the state of its polls, which answers to outcomes do not change, with the `reason` of §9 of `docs/protocol.md`: the code of a verified refusal, `protocol-version`, `status-` and the HTTP status, `status-407` from a proxy included, or `unreachable` |
| `bases` | `held` (the keys the server granted), `claimed` (the keys another agent holds) | The server's answer changed what this agent holds (rule 14) |
| `received` | `command`, `base`, `mode` (`read` or `write`), `callerKey` (or `null`), `script`, `parameters` (an object, *(S6)* always), `issued`, `deadline` | A command passed the checks of rule 1, before it runs |
| `refused` | `command` (or `null`), `base` (or `null`), `mode` (or `null`), `code` | A command failed a check of rule 1. For a signature that did not verify, `code` is `signature` and the other members are `null`, since nothing in the command can be trusted. *(S6)* The same holds for `form`, verified bytes that name no command. A `refused` of a command ends it as `ended` does |
| `ended` | `command`, `outcome` (`succeeded`, `failed` or `unknown`), `code` (or `null`), `durationMs` | A command ended (rules 3, 5, 9). *(S6)* A later `ended` of the same command replaces an earlier one; it is written only after the outcome was refused for its size, as below, and at a start, when the outbox file no longer matches the last one. `durationMs` is `null` in an `ended` that a start writes; a second `ended` for the size carries the duration of the first |
| `confirmed` | `command` | The server confirmed the outcome (rule 11) |
| `reservation` | *(S6)* `agent`, `below` | Before the agent sends a poll number at or above the `below` of its last reservation, or any number while it knows none of its identity: the bound below which it sends its numbers (§6.2 of `docs/protocol.md`) |

- *(S6)* **Writing.** The agent alone writes the log, and holds `audit\lock` open without
  sharing for its whole run. It writes one entry at a time, with the writer of §2 of
  `docs/protocol.md`: no white space between tokens, text outside ASCII as it is, and
  `parameters` written again rather than copied. Each entry ends with LF, counts as
  written only with it, and is on the disk before the next is written and before the
  agent goes on. At a start and after a failed write, the agent treats its log as §13 of
  `docs/protocol.md` says.
- **The outbox.** The result of a command is not in the audit log. Before the agent records
  `ended`, it writes and flushes the outcome to `outcomes\`. The file is removed after
  `confirmed`. *(S6)* A `refused` of a command is written before the file of its Refused
  outcome, which is kept until `confirmed` too.
  - *(S6)* A file of the outbox is written under a temporary name and renamed over the
    old one, both through to the disk, before the agent goes on, and `confirmed` is on
    the disk before its file is removed. A temporary file left at a start is removed.
  - *(S6)* The agent reads a file of the outbox with `ReadOutcome` of §8 for the mode of
    its command, which accepts everything its writer writes. A file it cannot read, a file
    of a command that the audit log does not name, and a command with an `ended` but
    neither its file nor its `confirmed`, make it record `started` with an error and stop,
    as a line of the log it cannot read does. A failed read, write or removal of a file of
    the outbox stops the agent as a failed write of the log does.
- **At a restart.** A file without its `ended` is recorded as `ended` and sent. A
  `received` without a file or an `ended` becomes Unknown or `lost` (rule 3). The audit log
  stays the one record of what happened (D7); the outbox only holds what must still be
  sent. *(S6)* A file whose kind and code differ from the last `ended` or `refused` of its
  command, as after a stop between the replacement of a file and its second `ended`, gets
  an `ended` that matches it. The file of a confirmed command is removed, and a `refused`
  of a command, not confirmed, without its file gets its file again from its code.
- **An outcome refused for its size.** The server refuses it with its protocol code, or
  something on the path refuses it with HTTP 413. The agent then replaces the outbox file
  with Unknown `result-too-large` for a Write, or Failed `result-too-large` for a Read,
  records a second `ended` with it, and sends it.
- **Retention.** Files older than `auditRetentionDays` are removed, but never one younger
  than a day, *(S6)* by the agent's clock when it wrote them:
  - *(S6)* a file's age counts from its last entry, and a file is removed once it is older
    than `auditRetentionDays` plus a day, so that with that day of tolerance it is still
    older than `auditRetentionDays` by the server's clock;
  - *(S6)* no file is removed while it holds an entry of a command whose outcome is not
    confirmed, or the highest `reservation` of the agent's identity, nor while it holds
    the `confirmed` of a command whose other entries are in a file that stays. So the
    files chained to a file that holds an unconfirmed command are kept past
    `auditRetentionDays` while that command stays unconfirmed, and, after
    the clock was set back, two files whose entries confirm each other's commands are
    kept for good;
  - *(S6)* retention runs only right after a verified poll answer of the current run, and
    only when the agent's clock is within a day of that answer's `time`. An agent that has
    had no such answer since it started, or whose clock is further off, removes nothing,
    so a clock moved ahead wipes nothing out;
  - *(S6)* entries written while the agent's clock was behind count as older by that
    much, and may be removed early once the clock is right.

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
      public static bool HasLoneSurrogate(string value);
  }

  internal static class OutpostProtocol
  {
      public const int Version = 1;
  }
  ```

  The assembly gives `InternalsVisibleTo` to the server, the agent, the console and their
  test projects, and to the shell's tests; not to the shell, which depends on the server
  (principle 12; S10aR).
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
2. An independent reviewer reviews it, within the limit the
   [development process](process.md#review) sets for a document.
3. The owner approves it.
4. The contract author commits it in S2's contract step.

It has to say what follows. The bullets marked *(S2)* were changed in S2 together with
`docs/protocol.md`. They wait for the owner's approval of it, with the changes that S2's
brief lists, and hold only after it.
- the requests `pair`, `poll` and `outcome` under `{prefix}/{version}/`, and the answer to
  an unsupported version;
- the JSON of each body and answer, and the body limit of each request; the limit of
  `outcome` follows `OutcomeSizeLimit`;
- the HTTP status of each refusal, with its code;
- how a command travels as exact bytes with its signature, and how the agent's requests are
  signed: over the bytes of the body, the kind of request and the agent's identity;
- the form of the pairing token, and the pairing proof;
- the fingerprint of a key;
- the nesting limit of the JSON of the protocol, a fixed limit and not an option (Q5).
  The parameters of a command and the result of an outcome count
  their 64 levels from their own outer value, as the rule of names says; the envelope of
  the message that carries them does not count against them. S2 fixes how the whole
  message is checked, so that a command that `ReadAsync` or `WriteAsync` accepted is never
  refused for its nesting, and what an outcome whose result nests deeper becomes. The
  limit lives in the protocol assembly, which both sides check it from;
- **What every poll answer carries** *(S2)*:
  - the commands;
  - the bases the agent holds and the other keys it declared, which it does not hold
    (rule 14; a draft of S4);
  - the server's time;
  - the poll timeout and the lease grace. Every poll carries the poll timeout the agent
    last learnt, and the server holds it for no longer than the smaller of that and its
    option. The agent waits for an answer to begin for no longer than the poll timeout it
    sent plus the lease grace, and then counts the poll as lost. The pairing answer carries
    both too, and the console writes them into `agent.json` when it pairs, so that the
    first poll is bounded as well; later answers bring the current values.
- **The answer to an older poll** *(S2)* comes at once, not at the end of the poll timeout.
  It has no commands, carries the other members of a poll answer, and names the newest
  poll number that the server recorded for the agent (rule 10). The agent waits a tenth of
  its `retryDelayMax` before it polls again.
- **What every other signed answer carries** *(S2)*: the server's time.
- **Every answer the agent acts on is signed** *(S2)* by the server over its exact bytes,
  and bound to the request it answers by the SHA-256 of that request's exact body, which
  carries the poll number and the agent's time. The agent verifies it before it acts on any
  of it.
  - The one exception is a bare HTTP 413 to an `outcome` request, as concept §6 says,
    acted on whatever its headers and body.
  - Every other unsigned answer to a poll or an outcome counts as lost.
- **The type of a poll number:** a signed 64-bit integer, from 1 to 2⁶², as
  `docs/protocol.md` fixes it.
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
  - After each of S10a, S10b, S10c, S11 and S12 is merged, the architect reviews what the
    system has become, as the [development process](process.md#the-architects-review)
    says; a refactoring sprint S10aR, S10bR, S11R or S12R follows only when an entry of
    its list is taken (the owner, 23 November 2026). The first architect's review reads
    the whole code base after S8, alongside S10a.
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

  From the first release on, a sprint that finds the contract should change stops, and
  the change goes to the owner. Before it, the sprint makes the change, lists it for the
  owner and goes on, as the [development process](process.md#the-plan) says.
- **Internal contracts after the study.** S2, S3, S4, S5, S6, S9, S9b, whose study is S9's,
  and S9c build mechanisms.
  - Each begins with the study that the
    [development process](process.md#a-study-comes-before-a-design) requires, answering
    the questions its brief names.
  - The planner then writes into the brief the internal types that its tests address,
    with what the study taught. An independent reviewer reviews them, and the
    coordinator approves them (PQ2), before the contract step.
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
    Windows has `Requires=Windows`. No test needs both. The tests that need 1C have no
    trait: they live apart, as below.
  - The `build` job and the Publish workflow run every test without `Requires=Windows`.
    The `windows` job runs every test without `Requires=PostgreSQL`, because GitHub's
    Windows runners have no Linux containers. A developer's machine with Docker runs all
    of the solution's tests. S1 writes the exact commands into `ci.yml` and `publish.yml`.
  - The tests that need 1C live in `Moltaro.OnecOutpost.OneC.Tests`, as `AGENTS.md` says.
    - S9 creates the project, outside the solution, so that `dotnet test` from `src`, in
      CI and on a developer's machine, never runs it. S9 adds
      `dotnet build Moltaro.OnecOutpost.OneC.Tests --configuration Release` to both jobs of
      `ci.yml`, and
      `dotnet format Moltaro.OnecOutpost.OneC.Tests/Moltaro.OnecOutpost.OneC.Tests.csproj --verify-no-changes`
      to the `build` job, so that the project keeps compiling and keeps its form.
    - On the lab machine it runs alone, from `src`, with
      `dotnet test --project Moltaro.OnecOutpost.OneC.Tests`, after the test base is built
      from `src/OneC.TestConfiguration`.
    - Its tests need Windows and 1C only. None needs PostgreSQL, Docker or a server's
      store.
    - The bases, the accounts and the objects of a typical configuration that a test uses
      come from `src/OneC.TestConfiguration/lab.local.json`, which L1 creates and Git
      ignores (`*.local.json`). It names the test base with the passwords of its users,
      and for each copy of a typical base its cluster and database, its configuration and
      version, its accounts with their passwords and the document type to post. It never
      leaves the lab machine. Data comes from the base while a test runs, never as a
      literal.
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
  machine (OD1) when its implementation is ready. The owner runs the tests that need 1C
  there, with the command of the test lane, and the coordinator runs the manual scenarios
  of the brief with the owner. Both use the test base and the copies of typical bases,
  never a customer's working database. In the sprint's status the coordinator records the
  number of 1C tests passed and failed with the name of each failed one, and one line per
  manual scenario, passed or failed. No raw output is committed.
- **Documents.** In the documents step of every sprint the coordinator brings `README.md`,
  the concept, `docs/protocol.md` and the explanations that exist up to what exists, both
  forms of an explanation together, as `AGENTS.md` and the process say.

### Order and why these splits

```text
The contract and the wire
  S1 contract ── S2 protocol
The riskiest unknown: a script in a real 1C
  L1 lab check ── S9 executor, COM worker
The tunnel, with a fake in place of the worker
  S3 store ── S4 pairing ── S5a commands in the store ── S5b calls ── S6a agent's files ── S6b agent checks, workers ── S6c agent loop
The agent with 1C
  S9b the agent runs the workers ── lab ── S9c the account of the workers ── lab
Several nodes, and the refactoring
  S7a nodes, losses ── S7b measurements, explanations ── S8 refactoring ── lab
The tools
  S10a console setup ── architect ── S10aR refactoring ── S10b console check ── architect ── S10c metadata, G1 ── lab ── architect ── S11 shell ── architect
The release
  S12 documents, packages, archive ── architect
```

- **1C first** (the owner, 21 November 2026). Everything rests on a script that runs in a
  real 1C through COM, in safe mode, under both accounts, and on the answers to O1–O8,
  which can still change D3, D12 and D14. So L1 and S9 follow S2 at once: if 1C does not
  behave as the concept expects, it is known before the server and the agent are built.
  The sprints keep their numbers and branches; only their order changes.
- **1C first, again** (the owner, 22 November 2026; written by the planner). After S6c come
  S9b, the lab, S9c, the lab, then S7 and S8, and after S8 the tests of
  `Moltaro.OnecOutpost.OneC.Tests` run once more on the lab, since the refactoring touches
  code already proven on 1C. So the agent meets a real 1C as soon as it exists, before the
  nodes and the refactoring build on it.
- **The tunnel without 1C.** Every guarantee of the tunnel, from the signature to the
  cases of a doubled run, is still built and tested in CI, with a fake behind the COM
  worker's boundary.
- **S2, the protocol, alone.** It is the public wire contract and the security core:
  signatures over exact bytes, the agent's signed requests and the pairing proof. It is
  reviewed alone, as the zone of `AGENTS.md` with the strictest priorities.
- **S3, the store, apart from the server.** Every guarantee of the server is one
  conditional change in the store, tested against PostgreSQL without HTTP, so a failure
  points at one place. S3 builds the store, its migrations and the changes of pairing
  tokens; S4 and S5 add the changes their own behavior needs, each tested the same way
  before its HTTP.
- **S4, pairing, apart from S5, commands.** Pairing, authentication, leases and base keys
  are needed by commands but not the other way round. Splitting them keeps each review to
  one mechanism.
- **S5a, commands in the store, apart from S5b, the calls** (the planner, 22 November
  2026). Ten store operations with their races, and the calls, the hand-out and the
  signals, all in strict zones, are too much for one review; a review of the store alone
  can be exhaustive.
- **S6, the agent, after the server.** Its tests run against the real server of S5 over
  loopback, so the first end-to-end test exists at the end of S6c.
- **S6a, the agent's files, apart from the loop** (the planner, 22 November 2026). The
  audit log as journal, the outbox, the restart rules and the poll numbers are a strict
  zone that fakes of the disk test without HTTP; the transport, the checks, the workers
  and the end-to-end tests are a second one. Together they are too much for one review,
  and a review of the agent's files alone can be exhaustive.
- **S6b, the checks and the workers, apart from S6c, the loop** (the coordinator, 22
  November 2026, under the planner's standing instruction). The checks of rule 1 and of a
  poll answer, and the workers with their run limits, are tested through internal types
  without HTTP; the transport, pairing, the loop and the service are a strict zone of
  their own. Each review can then be exhaustive.
- **S7, several nodes and lost messages,** on its own, because it needs a test proxy that
  drops and holds messages, and because it is where a wrong rule shows. It runs as two
  sprints (the coordinator, 23 November 2026, under the planner's standing instruction to
  cut a sprint too large for one exhaustive review): S7a, the proxy and the scenarios of
  several nodes and lost messages; S7b, the measurements and the explanations E1 and E2,
  which describe what S7a proved. S7a and S7b do not wait for S9c, which needs nothing of them and
  waits for its own lab run; S9c is merged after it (the planner, 23 November 2026).
- **S8, refactoring,** after S7, the middle of the epic, before the console and the shell
  build on the agent.
- **L1 right after S2.** Its answers to O1–O8 can change decisions D3, D12 and D14. They
  can also add members to `policy.json`: an account of its own for COM workers after O1,
  or a connector per base after O8. They reach the briefs of S9 and S6 through the owner.
- **S9, the executor and the COM worker together.** They are one piece of acceptance on
  the lab machine; split, each half could not be accepted. Its tests drive the worker
  program directly, so it needs no agent.
- **S9b, the agent runs the workers,** right after S6c: what of the old S9 needs the agent
  of S6. It is where a command first travels from `ReadAsync` to a real 1C and back.
- **S9c, the account of the workers without safe mode, apart from S9b** (the planner, 22
  November 2026). Starting a worker under a Windows account of its own, whose password is
  a secret, is a strict zone of its own: secrets and the local policy. Meanwhile S9b
  serves no Write that runs without safe mode, so nothing runs under the service account
  without it.
- **S10 and S11.** The console works on the agent machine, the shell next to a server.
  They share nothing.
- **S10a, the console's setup, apart from S10b, its check** (the coordinator, 23 November
  2026, under the planner's standing instruction to cut a sprint too large for one
  exhaustive review). `install` with the service, the rights and the access lists,
  `pair` and `secret` are strict zones of their own; `check`, with a probe of every base
  and every account, the server and the worker accounts, and `metadata` are a second
  one, which needs the lab. Each review can then be exhaustive.

### Status

| Sprint | Result | Track | Status |
| --- | --- | --- | --- |
| S1 | The public contract of stage 1, the projects and the test lane | Full | Done |
| S2 | The protocol, its signatures and the pairing proof | Full | Done, 21 November 2026: the codec, the signatures and the pairing proof with their tests; the code review closed in two rounds, after the ten rounds of `docs/protocol.md`. What it changed in the public contract is listed in its brief for the owner |
| S3 | The store | Full | Done, 21 November 2026: the store on PostgreSQL in the schema the application chooses, its first migration, `OutpostMigrations` with `MigrateAsync` and `GetScripts`, and the pairing tokens with their pending agents: created, found, redeemed once and purged, which no endpoint uses yet. The internal contract was reviewed in two rounds; the code review took three, and its last finding, a word of the XML documentation of `MigrateAsync`, was fixed on the owner's decision without a fourth round. What it changed in the public contract is listed in its brief for the owner |
| S4 | Pairing and agents on the server | Full | Done, 22 November 2026: the store's polls, leases and base keys with their second migration; the endpoints `pair` and `poll`, with the unsigned answers of §9 before the body and the hold of a poll; `AddOutpostServer` in full, `CreatePairingTokenAsync`, `GetAgentsAsync`, `RevokeAgentAsync`, `ReleaseBaseAsync` and the purge. Read, Write and `outcome` still refuse. The internal contract was reviewed in two rounds; the code review closed in one, with four P4 left for the coordinator; the documents were reviewed once. What it changed in the public contract is listed in its brief for the owner |
| S5a | Commands in the store | Full | Done, 22 November 2026: the commands table with the third migration `Commands`, the store operations of commands and outcomes with their tests and races against PostgreSQL, revocation ending the agent's commands in its transaction, and parameters with a name twice refused at the call. Read, Write and `outcome` still refuse. The study read actions/runner and Halibut; the internal contract was reviewed in two rounds and approved under PQ2; the code review took two rounds and left P4s, listed for S5b in its brief. Of the drafts of the public contract that the S5 brief lists for the owner, S5a makes two: parameters with a name twice refused, and a store database in UTF8 |
| S5b | The calls, the hand-out, `outcome` and the signals | Full | Done, 22 November 2026: `ReadAsync` and `WriteAsync` create a signed command, wake the agent's poll through the signals and wait for its outcome; the poll hands commands out up to `free` and 64 MiB; the `outcome` request takes the agent's outcome, with the signed 413 `result-too-large`; the purge removes ended commands. All tested over loopback HTTP with the test agent. The tests ran on the internal contract approved for S5; the code review took three rounds, its P3 of round 1 (a second §2 read of an outcome, 464 → 336 MiB at the peak for 64 MiB) and a P2 of round 2 (sets that did not honor cancellation) fixed; the P4s left are listed in the S5 brief. The other two drafts of the public contract that the S5 brief lists for the owner are S5b's |
| S6a | The agent's files | Full | Done, 22 November 2026: the policy with the changes carried over from S2, `agent.json` and the secrets read through DPAPI for the machine, the access lists of the data folder, the audit log written in order to the disk with its lock, torn entries, start and retention, the outbox, the order of a command's records, the reservation of poll numbers and the start after a restart; the agent program still runs nothing. The internal contract of S6 was reviewed in two rounds and approved under PQ2; the code review took three rounds (a P1 and a P2, then a P2 of retention, then a P3 moved to S8 by the planner). What it changes in the public contract is listed in the S6 brief for the owner |
| S6b | The agent's checks of commands and poll answers, its workers and run limits | Full | Done, 22 November 2026: `CommandChecks` with the checks of rule 1 in the order of §7 and the checks of a poll answer, the worker boundary with its fake, and `WorkerPool` with workers, the deadline to open, run limits, the size comparison of S9 and the executor's object; no HTTP, and the agent program still runs nothing. The contract of S6b and S6c was reviewed in two rounds and approved under PQ2; the code review took two rounds (a P3 on what `Kill` promises, fixed), with P4s left in the S6 brief |
| S6c | The agent's loop: the transport, pairing, polls, outcomes, the host and the service | Full | Done, 22 November 2026: the transport with the addresses of §11, the waits and read limits, pairing, numbered polls through the proxy, the loop that records every command before it runs and sends outcomes until confirmed, the replacement for size, `connection` and `bases`, retention and the reservation called after verified answers, the in-process host and the Windows service by the `service` argument; end to end against the server of S5 over loopback with a fake in place of the COM worker, through a test proxy. The code review took two rounds (three P3s of round 1 fixed). The manual acceptance of the service is the owner's, on a lab or test machine. What it changes in the public contract is listed in the S6 brief for the owner |
| S7a | Several nodes and lost messages | Full | Done, 23 November 2026, merged before S9c, which waits for its lab run (the planner, 23 November 2026): the proxy that drops and holds requests and answers and the host of two nodes in TestSupport, and a test of every line under "With two server nodes", all of which passed against the code as it was, so no implementation step; the code review closed in one round, with P4s left in the S7 brief |
| S7b | The measurements and the explanations E1 and E2 | Full | Done, 23 November 2026: the program `Moltaro.OnecOutpost.Measurements`, run by hand, with the figures under Measured results (every p95 within 100 ms); explanations E1 "The path of a command" and E2 "Why a command never runs twice", animated, each as Markdown and HTML. The code review took two rounds (three P3s of documents, fixed) |
| S8 | Refactoring of the code base after S7 | Refactoring | Done, 23 November 2026: 37 of the 42 items of the survey, one commit each with every test green, behavior, the public contract and the assertions unchanged; the code review took one round. On the lab, 23 November 2026, from `main` at 877fd9a: 76 of 76 tests of `Moltaro.OnecOutpost.OneC.Tests` passed, in 8 min 23 s, which also proves the refactorings 39 to 41 of the lab tests |
| L1 | The lab check of open items O1–O8 | Light | Done, 21 November 2026: the concept has the answers to O1–O8, with D12 as the owner changed it after O1, and the test configuration builds into a base on the lab's cluster. A file base is not tried on the lab, which gives no licence for one |
| S9 | The executor and the COM worker, run after L1 and before S3 | Full | Done, 21 November 2026: the executor's sources and its `.epf`, which the owner built on the lab machine (X1), the COM worker with its tests against a fake of the COM connection, and the tests that need 1C. The code review took three rounds, and its last finding, confirmed on the lab, was fixed by a query of the executor before the commit of a Write, reviewed once more. On the lab, 1C:Enterprise 8.3.17.1851, the first run gave 43 of 44 tests that need 1C passed; the failed one, `A_write_whose_commit_raises_gives_unknown_transaction_ended`, was removed, since 8.3.17 gave no way to provoke the case. The second run, with the `.epf` rebuilt after the fix, gave 45 of 45 passed, on the test base and one copy of a typical base, which ran with one account for both modes. No manual scenario, since every line of the Acceptance has a test. What it changed in the public contract is listed in its brief for the owner |
| S9b | The agent runs the COM workers | Full | Done, 23 November 2026: the agent runs the COM workers of each base and mode with the bitness of the registered connector, inside a job object, ends a worker at its run limit and starts it again with a growing delay, and serves a base whose Writes run without safe mode for Read only until S9c; guide G2 with a lab test of every script. The internal contract was reviewed in two rounds and approved under PQ2; the code review took three rounds (six P3s of round 1, two of round 2, all fixed). 2430 tests passed here. On the lab, 23 November 2026, 1C:Enterprise 8.3.17.1851, the test base built again: 73 of 73 tests of `Moltaro.OnecOutpost.OneC.Tests` passed (S9's 45, G2's 25, the agent's 3), in 7 min 05 s. The agent, the COM worker and 1C were real, the server the test's; the real server against the agent is proven in CI over loopback (S6c) |
| S9c | The account of the workers without safe mode | Full | Done, 23 November 2026: `write.workerAccount` in `policy.json` and its password in `secrets.dat`; the agent logs each such account on once at the start and runs every COM worker of its base, Reads included, under it (`LogonUserW`, `CreateProcessAsUserW` suspended, in the job, only its two pipe ends inherited); the program folder and the folders above it checked at the start. The internal contract was reviewed in two rounds and approved under PQ2; the code review took two rounds (a P2 of the job test, fixed). The lab run of 106e58f gave 71 of 76 (the lab agent removed its program folder while workers ran, and gave no reason when a worker did not open), that of bc53229 gave 73 of 76 (the worker under its account ended 0xC0000142: it could not connect to the window station or desktop that `""` names); after a study the worker gets a desktop of the agent's own on its non-interactive station, never WinSta0, reviewed in two rounds (a P2 of the tests, fixed). The third lab run, of 849574f, gave 76 of 76 |
| S10a | The console's setup: `install`, `uninstall`, `pair`, `secret`, `connector register` | Full | Done, 23 November 2026: the console on Spectre.Console.Cli with `install`, `uninstall`, `pair`, `secret` and `connector register`, and one finder of the COM connector in the agent, which its launcher and the console use. The study read actions/runner and Octopus Tentacle; the internal contract was reviewed in two rounds and approved under PQ2, round 2's fixes (a P2 of the contract step and six P3) taken without a third round. The code review took three rounds: three P1 of round 1 (the lists of `audit` and `outcomes`, tests that expected an order of entries, colour forced on in CI), five P3 of round 2, none in round 3. 2697 tests passed here; CI passed with the tests of `Requires=Administrator` on its elevated `windows` job. The manual run of its commands is on the lab with S10c. What it changes in the public contract is listed in the S10 brief for the owner |
| S10aR | Refactoring after S10a: the entries 8 to 11 of the architect's review 1 and entry 1 of review 2 | Refactoring | Done, 23 November 2026: one transaction frame in the store, measured again with every p95 within S7b's; one growing delay of a retry; the forms of §2 in the protocol alone; four small cleanups; the console's install test running the program's own parts. One commit per entry, every test green after each, no public member, assertion or test changed; the review took one round (five P4, two fixed in this plan) |
| S10b | The console's check | Full | Not started |
| S10c | The console's metadata report, guide G1 and the lab run of S10 | Full | Not started |
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
      `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational` at the
      same version as `Microsoft.EntityFrameworkCore` (Q3),
      `Npgsql.EntityFrameworkCore.PostgreSQL` and `Moltaro.Signals`, whose versions go into
      `Directory.Packages.props`;
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
    - every rule of `policy.json`, with an error that gives the path of the member, what
      is wrong and what is expected (Q1);
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
  that rule, and the acceptance of S10 and S11 checks it. The rule names
  Spectre.Console.Cli for the commands, so principle 12 and those lines name it as well.
- **The owner's answer of 20 November 2026 to Q1** of the
  [open questions](open-questions.md): a member given twice, a member set to `null`, and
  an empty or white-space string for `server`, `database`, `file` or `account` make the
  whole `policy.json` invalid, as the reader of S1 does; and the error says clearly where
  and what, as [`policy.json`](#policyjson) now says. The error texts of
  `AgentPolicyReader` and their tests follow it.
- **The owner's answer of 20 November 2026 to Q2:** the agent name is a display label, so
  it takes arbitrary characters; the rule of names stays as S1 has it, counted as UTF-16
  code units. The XML documentation of `agentName` and of `OutpostAgent.Name` says so.
- **The planner's answer and proposals of 20 November 2026** to the
  [open questions](open-questions.md) Q3–Q5 that the sprint found: an answer to Q3, and
  proposals for Q4 and Q5, each with its test where it changes a check:
  - Q3: the server also references `Microsoft.EntityFrameworkCore.Relational`, at the
    version of `Microsoft.EntityFrameworkCore`, so that every EF Core assembly of the
    package is of one version;
  - Q4: `SigningKeyPem` accepts exactly one PEM block with nothing but white space around
    it, as its row in the options table says, so that the import of S2 reads exactly what
    the check accepted;
  - Q5: the parameters of a command nest at most 64 levels, counted from their own outer
    value, as the rule of names says. It is a fixed limit of the protocol, not an option,
    as `AGENTS.md` principle 10 now says; how a whole message and the result of an
    outcome are counted is in [The protocol](#the-protocol), and `docs/protocol.md`
    states it when S2 writes it. The fixes of review round 2 narrowed
    the sentence of principle 10 to a limit that a side needs before it can read what the
    other side sent, since a limit that travels in a signed message, such as the size of
    an outcome, stays an option. The limit lives in the protocol assembly, as
    [The protocol](#the-protocol) says.

  Q4 and Q5 change the public contract, so they went to the owner, who approved both on
  20 November 2026, as the [open questions](open-questions.md) say.
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
    here after the study. They perform no I/O and know nothing of HTTP or the store. Every
    rule they apply is a rule of `docs/protocol.md`, and their documentation names its
    section.

    In the namespace `Moltaro.OnecOutpost.Protocol`:

    ```csharp
    internal static class OutpostProtocol
    {
        public const int Version = 1;
        public const int MaxMessageDepth = 72;
        public const int MaxValueDepth = 64;
        public const int PairBodyLimit = 16 * 1024;
        public const int PollBodyLimit = 1024 * 1024;
        public const int PollAnswerLimit = 64 * 1024 * 1024;
        public const int AnswerLimit = 64 * 1024;
        public const int OutcomeEnvelopeAllowance = 4 * 1024;
        public const int MaxBases = 256;
        public const int MaxReceived = 10_000;
        public const long MaxPollNumber = 1L << 62;

        // Ranges that the options, `policy.json` and the codec check alike (§2, "Ranges").
        public static readonly TimeSpan MinClockSkew = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan MaxClockSkew = TimeSpan.FromHours(1);
        public static readonly TimeSpan MinTakeTimeout = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan MaxTakeTimeout = TimeSpan.FromHours(1);
        public static readonly TimeSpan MinPollTimeout = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan MaxPollTimeout = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan MinLeaseGrace = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan MaxLeaseGrace = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan MinRunLimit = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan MaxRunLimit = TimeSpan.FromDays(1);
        public const int MinOutcomeSizeLimit = 64 * 1024;
        public const int MaxOutcomeSizeLimit = 64 * 1024 * 1024;
        public const int MinCommandSizeLimit = 4 * 1024;
        public const int MaxCommandSizeLimit = 16 * 1024 * 1024;
        public const int MaxWorkers = 8;

        public const string AgentHeader = "Outpost-Agent";
        public const string SignatureHeader = "Outpost-Signature";
    }

    internal enum OutpostRequestKindEnum { Pair, Poll, Outcome }

    internal static class OutpostProtocolCodes
    {
        public const string Form = "form";
        public const string Signature = "signature";
        public const string ResultTooLarge = OutpostOutcomeCodes.ResultTooLarge;
        public const string ProtocolVersion = OutpostOutcomeCodes.ProtocolVersion;

        // `ServerKey` alone is a code of the codec only, never sent (§6.1).
        public const string ServerKey = "server-key";
    }

    internal static class OutpostForms
    {
        public static string WriteBase64Url(ReadOnlySpan<byte> bytes);
        public static bool TryReadBase64Url(ReadOnlySpan<char> text, [NotNullWhen(true)] out byte[]? bytes);
        public static string WriteId(Guid id);
        public static bool TryReadId(ReadOnlySpan<char> text, out Guid id);
    }

    internal sealed class OutpostProtocolException : Exception
    {
        public OutpostProtocolException(string code, string reason, Guid? commandId = null);
        public string Code { get; }
        public Guid? CommandId { get; }
    }

    internal static class OutpostHeaders
    {
        public static string FormatAgent(Guid agent);
        public static bool TryParseAgent(string? value, out Guid agent);
        public static string FormatSignature(ReadOnlySpan<byte> signature);
        public static bool TryParseSignature(string? value, [NotNullWhen(true)] out byte[]? signature);
    }
    ```

    - `OutpostProtocolException.CommandId` is set for the two refusals of a verified command
      that name it: `protocol-version` and `malformed` (§7).
    - `TryParseSignature` accepts exactly 86 base64url characters that decode to 64 bytes.
    - `OutpostForms` is the one home of the forms of §2 that the headers, the token and the
      codec all need: canonical base64url and the 36-character lowercase identity. It sits
      in the root, which `.Signing` and `.Codec` reference already, so that neither needs
      the other for it. `OutpostHeaders` uses it.
    - `OutpostModeEnum` stays in `.Outcomes`: in the root it would make a cycle with
      `OutpostProtocolCodes`, whose constants come from `OutpostOutcomeCodes`.

    In `Moltaro.OnecOutpost.Protocol.Outcomes`, the one place of every outcome code and of
    which code fits which kind:

    ```csharp
    internal enum OutpostAgentOutcomeKindEnum { Unknown = 0, Succeeded, Failed, Refused }
    internal enum OutpostModeEnum { Read, Write }

    internal static class OutpostOutcomeCodes
    {
        public const string ScriptError = "script-error";
        // … one constant for each code of `OutpostCodes`, `malformed` included, with the same names and values.

        public static bool FitsFailed(string code);
        public static bool FitsRefused(string code);
        public static bool FitsUnknown(string code);
        public static bool IsAgentOutcome(OutpostModeEnum mode, OutpostAgentOutcomeKindEnum kind, string? code);
        public static bool IsExecutorOutcome(OutpostModeEnum mode, OutpostAgentOutcomeKindEnum kind, string? code);
    }
    ```

    - The three `Fits…` answer the kinds column of the table of `OutpostCodes`. The public
      factories of `OutpostOutcome` use them, so that the fit of a code to a kind lives once.
    - `IsAgentOutcome` answers the table "The outcomes an agent may send, by mode" of §8.
      `IsExecutorOutcome` answers the paragraph on the executor. Both take a `null` code for
      `succeeded`.
    - The public `OutpostCodes` of the server defines each of its constants as the constant
      here, so that every code string lives once.

    In `Moltaro.OnecOutpost.Protocol.Signing`, the keys, the signatures, the pairing proof and
    the token of §2, §3 and §5:

    ```csharp
    internal static class OutpostKeys
    {
        public static byte[] ExportPublicKey(ECDsa key);
        public static ECDsa ImportPublicKey(ReadOnlySpan<byte> subjectPublicKeyInfo);
        public static string Fingerprint(ReadOnlySpan<byte> subjectPublicKeyInfo);
    }

    internal static class OutpostSignatures
    {
        public static byte[] SignRequest(ECDsa agentKey, OutpostRequestKindEnum kind, Guid agent, ReadOnlySpan<byte> body);
        public static byte[] SignAnswer(ECDsa serverKey, OutpostRequestKindEnum kind, Guid agent, int status, ReadOnlySpan<byte> requestBody, ReadOnlySpan<byte> answerBody);
        public static byte[] SignCommand(ECDsa serverKey, ReadOnlySpan<byte> commandBytes);
        public static bool VerifyRequest(ECDsa agentKey, OutpostRequestKindEnum kind, Guid agent, ReadOnlySpan<byte> body, ReadOnlySpan<byte> signature);
        public static bool VerifyAnswer(ECDsa serverKey, OutpostRequestKindEnum kind, Guid agent, int status, ReadOnlySpan<byte> requestBody, ReadOnlySpan<byte> answerBody, ReadOnlySpan<byte> signature);
        public static bool VerifyCommand(ECDsa serverKey, ReadOnlySpan<byte> commandBytes, ReadOnlySpan<byte> signature);
    }

    internal static class OutpostPairingProof
    {
        public static byte[] Compute(ReadOnlySpan<byte> secret, Guid agent, ReadOnlySpan<byte> agentKey);
        public static bool Check(ReadOnlySpan<byte> secret, Guid agent, ReadOnlySpan<byte> agentKey, ReadOnlySpan<byte> proof);
    }

    internal sealed record OutpostPairingTokenText(Guid Agent, byte[] Secret, string Fingerprint)
    {
        public string Write();
        public static bool TryRead(string text, [NotNullWhen(true)] out OutpostPairingTokenText? token);
    }
    ```

    - `ImportPublicKey` accepts exactly the 91-byte form of §2, and refuses anything else
      with `OutpostProtocolException` and `form`.
    - `Fingerprint` gives the 64 lowercase hexadecimal digits of §2.
    - `Check` compares in constant time.
    - `ToString` of `OutpostPairingTokenText` leaves the secret out.
    - The signing inputs of §3 are built only in `OutpostSignatures`, for signing and for
      verifying alike. The readers of the codec call the `Verify…` methods before they
      parse, and nothing else parses signed bytes, so no caller parses unverified bytes.

    In `Moltaro.OnecOutpost.Protocol.Messages`, one record per message of §6 to §9, with
    the members of its JSON:

    ```csharp
    internal sealed record OutpostPairRequest(string AgentVersion, byte[] Key, byte[] Proof);
    internal sealed record OutpostPairAnswer(DateTimeOffset Time, Guid Agent, byte[] Key, byte[] ServerKey, TimeSpan PollTimeout, TimeSpan LeaseGrace);
    internal sealed record OutpostPollMode(int Free, TimeSpan RunLimit);
    internal sealed record OutpostPollBase(string Key, OutpostPollMode Read, OutpostPollMode? Write);
    internal sealed record OutpostPollRequest(long Number, DateTimeOffset Time, string AgentVersion, TimeSpan PollTimeout, IReadOnlyList<OutpostPollBase> Bases, IReadOnlyList<Guid> Received);
    internal sealed record OutpostSignedCommand(byte[] Bytes, byte[] Signature);
    internal sealed record OutpostPollAnswer(DateTimeOffset Time, long? Newest, TimeSpan PollTimeout, TimeSpan LeaseGrace, IReadOnlyList<string> Held, IReadOnlyList<string> Claimed, IReadOnlyList<OutpostSignedCommand> Commands);
    internal sealed record OutpostCommandMessage(int Protocol, Guid Id, Guid Agent, string Base, OutpostModeEnum Mode, string? CallerKey, string Script, JsonElement Parameters, DateTimeOffset Issued, DateTimeOffset Deadline, TimeSpan Skew, int OutcomeLimit);
    internal sealed record OutpostOutcomeMessage(OutpostAgentOutcomeKindEnum Kind, string? Code, JsonElement? Result, string? ErrorText, IReadOnlyList<string> Messages, bool MessagesTrimmed);
    internal sealed record OutpostOutcomeRequest(Guid Command, ReadOnlyMemory<byte> Outcome);
    internal sealed record OutpostOutcomeAnswer(DateTimeOffset Time, Guid Confirmed);
    internal sealed record OutpostRefusal(DateTimeOffset Time, string Code);
    ```

    - `OutpostOutcomeRequest.Outcome` holds the exact bytes of the outcome object (§6.3,
      §8); its size is their length.
    - `OutpostPollBase.Write` is `null` when the policy does not open writing, and is then
      written as `"write":null` (§6.2).
    - `OutpostOutcomeMessage.Result` is a `JsonElement` for `succeeded`, of kind `Null` when
      the result is `null`, and a C# `null` for every other kind.

    In `Moltaro.OnecOutpost.Protocol.Codec`:

    ```csharp
    internal static class OutpostCodec
    {
        // Writers, by §2.
        public static byte[] Write(OutpostPairRequest message);
        public static byte[] Write(OutpostPairAnswer message);
        public static byte[] Write(OutpostPollRequest message);
        public static byte[] Write(OutpostPollAnswer message);
        public static byte[] Write(OutpostCommandMessage message);
        public static byte[] Write(OutpostOutcomeMessage message);
        public static byte[] Write(OutpostOutcomeRequest message);
        public static byte[] Write(OutpostOutcomeAnswer message);
        public static byte[] Write(OutpostRefusal message);
        public static byte[] WriteUnsupportedVersion();
        public static long CommandSize(string script, JsonElement parameters);

        // Readers of the server: they verify the request, then parse it; `pair` the other way round (§3).
        public static OutpostPairRequest ReadPairRequest(Guid agent, ReadOnlySpan<byte> body, ReadOnlySpan<byte> signature);
        public static OutpostPollRequest ReadPollRequest(ECDsa agentKey, Guid agent, ReadOnlySpan<byte> body, ReadOnlySpan<byte> signature);
        public static OutpostOutcomeRequest ReadOutcomeRequest(ECDsa agentKey, Guid agent, ReadOnlyMemory<byte> body, ReadOnlySpan<byte> signature);

        // Readers of the agent: they verify the answer, then parse it; `pair` checks the fingerprint first (§3).
        public static OutpostPairAnswer ReadPairAnswer(string fingerprint, Guid agent, ReadOnlySpan<byte> sentKey, ReadOnlySpan<byte> requestBody, ReadOnlySpan<byte> body, ReadOnlySpan<byte> signature);
        public static OutpostPollAnswer ReadPollAnswer(ECDsa serverKey, Guid agent, int totalFree, ReadOnlySpan<byte> requestBody, ReadOnlySpan<byte> body, ReadOnlySpan<byte> signature);
        public static OutpostOutcomeAnswer ReadOutcomeAnswer(ECDsa serverKey, Guid agent, Guid command, ReadOnlySpan<byte> requestBody, ReadOnlySpan<byte> body, ReadOnlySpan<byte> signature);
        public static OutpostRefusal ReadRefusal(ECDsa serverKey, OutpostRequestKindEnum kind, Guid agent, int status, ReadOnlySpan<byte> requestBody, ReadOnlySpan<byte> body, ReadOnlySpan<byte> signature);
        public static OutpostCommandMessage ReadCommand(ECDsa serverKey, OutpostSignedCommand command);

        // Unsigned: the one unsigned body of §9, recognized for a reason and the console.
        public static bool IsUnsupportedVersion(ReadOnlySpan<byte> body);

        // Unsigned: the outcome object inside a verified request, or in a file of the outbox.
        public static OutpostOutcomeMessage ReadOutcome(OutpostModeEnum? mode, ReadOnlySpan<byte> outcome);

        public static int ValueDepth(JsonElement value);
    }
    ```

    - **The readers verify first.** Every signed reader verifies the signature of §3 before
      it parses anything. On failure it throws `OutpostProtocolException` with `signature`,
      and with `form` for a verified body that breaks §2 or the rules of its message.
    - **The two exceptions of §3.** `ReadPairRequest` parses the body, then verifies it with
      the key it carries. `ReadPairAnswer` checks the fingerprint of the server key in the
      body against `fingerprint` and throws `server-key` on a mismatch, then verifies with
      that key and throws `signature` when it fails. `server-key` is a code of the codec
      only, never sent: it lets the console report the mismatch as its own reason (§6.1).
      It then checks that the answer's `agent` is `agent` and its `key` is `sentKey`. It
      throws `form` for that, and for a body from which no server key can be read; the
      console reports `form` as an answer that is not the pairing answer it expected. A
      refusal of `pair` is a bare 403 without a body, and has no reader.
    - **The answers' own rules** (§9, "verified"). Each reader of an answer takes what it
      compares as parameters, and only hashes `requestBody`:
      - `ReadPollAnswer` throws `form` for more commands than `totalFree`, the sum of
        `free` in the poll, and for commands in an answer whose `newest` is not `null`.
        The count by base and mode needs the commands checked, so the agent makes it
        (§6.2);
      - `ReadOutcomeAnswer` throws `form` when `confirmed` is not `command`;
      - `ReadRefusal` throws `form` for a code that is not one of the signed refusals of
        §9 under `status`.
    - **`IsUnsupportedVersion`** tells whether an unsigned body is exactly the body that
      `WriteUnsupportedVersion` writes, for reason 2 of §9 and the console. It verifies
      nothing, and nothing is acted on because of it.
    - **`ReadCommand`** verifies, then reads `protocol` and `id`.
      - A command of another version throws `protocol-version` with `CommandId`.
      - Bytes that do not yield both throw `form` without it.
      - A verified command of this version that breaks §7 throws `malformed` with
        `CommandId`.
    - **`ReadOutcomeRequest`** reads the whole body by §2, the `outcome` member included
      (step 1 of §6.3), and keeps that member as a slice of `body`, without a copy. The size
      check and `ReadOutcome` follow it, in the order of §6.3.
    - **`ReadOutcome`** applies the member rules of §8 for the mode, the outcomes an agent
      may send included, and throws `form`. A `null` mode, for a command the server does not
      know, accepts what either mode allows.
    - **The executor's object** (§8) is checked by the agent with `ReadOutcome` for its
      mode and then `IsExecutorOutcome`. Nothing changes it: the agent forwards the bytes as
      they are, or, when either check fails, sends `result-type` instead.
    - **`CommandSize`** is the size of §7, computed by the same writer as
      `Write(OutpostCommandMessage)`, `{}` included, as a `long`; `CommandChecks` of the
      server uses it.
    - **`WriteUnsupportedVersion`** writes the one unsigned body of §9.
    - **Size checks.** The checks of size belong to the callers: the server checks the
      outcome member's length against its command's limit, and the endpoints check the body
      limits of §10.
    - **`ValueDepth`** counts the levels of §2: a scalar is 0, and an object or an array is
      1 plus the deepest of its members.
- **Study.** Sources, for example: the GitHub Actions runner (MIT); the Azure Pipelines agent
  (MIT); Octopus Deploy's Halibut (Apache-2.0). Questions:
  1. How each authenticates the agent to the server and the server to the agent, and what
     a stolen credential gives an attacker.
  2. What exactly is signed: the exact bytes, or a canonical form. How each avoids a
     difference between what was signed and what is read.
  3. How each binds a new agent's key to a one-time credential at registration.
  4. How each refuses a replayed message.
  5. Which key formats and fingerprints each uses, and how a key is pinned.
- **To read.**
  - The [concept](../concept.md), §3 ("Rules" 1, 5, 7, 10, 11 and 14, "Pairing", "What
    protects what") and §6 (20 November 2026);
  - `AGENTS.md`, principles 3, 9, 10, 11 and 12;
  - this plan, "The public contract", "The files of the agent" and "The protocol";
  - `docs/protocol.md`, as the owner approved it.
- **Acceptance.**
  - Tests of the command signature:
    - a command whose bytes changed in any place is refused with `signature`;
    - one signed with another key is refused;
    - bytes that do not parse and carry no valid signature are refused with `signature`,
      never with `form`, which shows that nothing parsed them;
    - verified bytes that yield no `protocol` or no `id` are refused with `form` and no
      `CommandId`;
    - a verified command of another version is refused with `protocol-version`, and a
      verified command of this version that breaks §7 with `malformed`, each with its
      `CommandId`.
  - Tests of the request signatures:
    - the same cases;
    - a body sent as another kind of request is refused;
    - the same bytes under another agent's identity are refused.
  - Tests of the answer signatures: an answer whose bytes changed, one signed with another
    key, and one bound to another request, agent, status or kind are refused. Also refused
    with `form`: a poll answer with more commands than the sum of `free`, one with
    commands and a `newest`, an outcome answer whose `confirmed` is another command, a
    refusal whose code does not fit its status, and a pairing answer without a readable
    server key. `IsUnsupportedVersion` is true only for the exact body of
    `WriteUnsupportedVersion`.
  - Known values of §3, computed outside the codec from the strings of §3 written out in
    the test:
    - the output of each `Sign…` verifies with `ECDsa.VerifyData` over the documented
      signing input;
    - one pairing proof equals a fixed HMAC-SHA256 over the documented input;
    - one fingerprint equals the fixed SHA-256 of a fixed key.
  - Tests of pairing:
    - a proof made with another secret is refused;
    - a signed answer whose `agent` or `key` is not what was sent is refused with `form`;
    - a key swapped after the proof was made is refused;
    - an answer whose server key does not match the token's fingerprint is refused with
      `server-key`, even when it is signed with that key, and one with the right key and a
      wrong signature with `signature`;
    - a token round-trips, and a token of another form is not read;
    - `ToString` of `OutpostPairingTokenText` leaves the secret out.
  - Tests of §2:
    - the escapes a writer writes, and no white space;
    - a reader that accepts white space and every escape;
    - U+0000 and lone surrogates refused;
    - duplicated members refused at every depth, names compared after unescaping;
    - non-canonical base64url, a key that is not the 91-byte form, and a proof that is not
      32 bytes, each refused;
    - times and durations truncated to milliseconds;
    - refused: a missing member, an unknown one, `null` where it is not allowed, a value of
      the wrong type, an integer with a fraction or an exponent, a byte order mark, and a
      time or an identity in any other form;
    - `"write":null` read as a base without writing;
    - `ValueDepth` at 64 and 65, and a scalar at 0;
    - the forms of `OutpostForms` and `OutpostHeaders`, a signature of exactly 64 bytes.
  - Tests of §8:
    - for each mode, every outcome an agent may send is accepted, and every other
      combination is refused;
    - with a `null` mode, `ReadOutcome` accepts what either mode allows and refuses what
      neither does;
    - an outcome member that breaks §2 gets `form` from `ReadOutcomeRequest`, before any
      check of its size.
  - Tests of `IsExecutorOutcome`: every outcome the executor may hand over is accepted, and
    every other one refused; `ReadOutcome` refuses U+0000 and a lone surrogate anywhere in
    the object.
  - `CommandSize` counts exactly what `Write(OutpostCommandMessage)` writes for the script
    and the parameters, the 2 bytes of `{}` included.
  - Every message round-trips. Every limit that the codec owns, and every range of a
    member, is refused one step beyond it and accepted at it. The body limits of §10 are
    the endpoints', and S4 and S5 state them.
  - The documents step: `docs/protocol.md` says that the codec exists and that no endpoint
    serves it yet.
- **Out of scope.** HTTP, the store, the agent's loop.
- **Small changes during the sprint.**
  - The owner's answer of 20 November 2026 to Q6 of the
    [open questions](open-questions.md): a lone surrogate is refused at once, with a clear
    argument error, in the agent name, the script and the parameters of `ReadAsync` and
    `WriteAsync`. It makes `policy.json` invalid, reported at its path as Q1 requires. The
    rule of names and [`policy.json`](#policyjson) say so; the checks of `OutpostNames`, of
    the parameters and of `AgentPolicyReader` follow it, each with its tests.
  - The owner's answer of 20 November 2026 on tests with 1C: both kinds. Tests that need
    1C live in `Moltaro.OnecOutpost.OneC.Tests`, outside the solution, and run on the lab
    machine, never in CI, against a base built from the repository's own test
    configuration and against copies of real typical databases. On 21 November 2026 the
    owner made that base a client-server one on the lab's cluster: the lab has no licence
    for a file base. The library still serves both kinds. `AGENTS.md`, "Tests And
    Samples", "Layout" and "Working Rules", concept §3 "Rules" and §11, `process.md`,
    OD1, the epic's Acceptance, the test lane, "The lab machine", L1, S9, S10, S11, R17
    and R18 say so.
  - **What S2 makes of this list, and what later sprints make** (the planner, 21 November
    2026, by the owner's rules of that day). The list holds as a draft of the contract,
    without waiting for the owner, who may overturn any item; the coordinator lists it for
    the owner when S2 closes. S2 makes only what its codec makes true: **Sizes**, **Codes**,
    the two "Edits that state nothing new", and in the concept the two sentences of rule 1
    and of rule 5 on the size of an outcome. The items on `policy.json`, on the audit log
    and the exception to principle 10 are in the brief of S6, which builds them. Every
    other item under "The concept and `AGENTS.md`" and "The XML documentation" is made by
    the sprint that builds the behavior it describes, S4 for the lease and S6 for the
    agent; the planner writes it into that brief when the sprint is prepared.
  - **Changes of the public contract that come with `docs/protocol.md`,** for the owner's
    approval together with it, made in the contract step after that approval:
    - **Sizes.**
      - The size of a command counts the text of its script between its quotes and the
        text of its parameters, as written in the command with their escapes, and `{}`
        when there are none (§7).
        - `CommandChecks` of S1 uses `OutpostCodec.CommandSize`, and its tests follow.
        - `Server.Tests/Commands/CommandSizeTests.A_script_of_exactly_the_limit_in_UTF8_passes_the_checks`
          then fails by the 2 bytes of `{}`. The test author changes it to a script of the
          limit less those 2 bytes, with the reason in the commit.
        - "Rules for every member", Sizes, and the XML documentation of
          `OutpostServerOptions.CommandSizeLimit` follow it.
      - The size of an outcome is its outcome object as it travels (§8). "Rules for every
        member", Sizes, already says so; the XML documentation of
        `OutpostServerOptions.OutcomeSizeLimit` follows it.
    - **Codes.**
      - `result-type` also covers a result deeper than 64 levels and an outcome of the
        executor that the agent cannot accept. For a Write it is Unknown (§8).
      - A new Refused code, `malformed`, for a verified command of this version that breaks
        §7.
      - The rows of `OutpostCodes` follow this, and its constants take their values from
        `OutpostOutcomeCodes`. The fit of a code to a kind moves from `OutpostCodes` to
        `OutpostOutcomeCodes.Fits…`, and the test author changes
        `Server.Tests/Outcomes/OutcomeCodeTests.cs` to match, with the reason in the commit.
      - The XML documentation of `OutpostCodes.ResultType`, and the lists of codes in that
        of `OutpostOutcome.Refused` and `OutpostOutcome.Unknown`, follow both.
    - **The concept and `AGENTS.md`:**
      - rule 1 says that verified bytes without a `protocol` and an `id` are only recorded,
        with `form`, since they name no command, and that its checks run in the order §7
        of the protocol gives;
      - rule 5 says what the size of an outcome is, and that the executor replaces U+0000
        and lone surrogates in its messages and error text with U+FFFD, then trims them,
        never inside a surrogate pair;
        the XML documentation of `OutpostOutcome.Messages` and `OutpostOutcome.ErrorText`
        follows;
      - rule 6, and the correctness item of `AGENTS.md` on an agent that is gone, say the
        lease of §6.2 exactly:
        - the lease of a poll is its hold, the smaller of the poll timeout it carries and
          `PollTimeout`, plus `LeaseGrace`, and ends no later than the poll's signed time
          plus `ClockSkew` plus that hold and grace;
        - so polls held back on the path keep an agent that has gone connected, together,
          for at most `ClockSkew` plus the lead the agent's clock had over the node's when
          it sent them;
        - an agent whose clock is behind by more than `ClockSkew` plus `LeaseGrace` shows
          as connected only for part of each poll, and by more than `ClockSkew` plus the
          hold plus `LeaseGrace` never. Calls for it may get Unavailable where they got
          Refused `clock-skew`, which `OutpostAgent.Connected`, `ReadAsync` and
          `WriteAsync` show;
        - a live agent that reads an answer for longer than `LeaseGrace` shows as not
          connected until its next poll;
      - rule 2 adds rule 10 to what the audit log is the journal for;
      - rule 10 adds the reservation of §6.2: the agent reserves its poll numbers in its
        audit log and, after a restart, numbers above the highest reservation, so that a
        poll captured while its clock was moved ahead can never become the newest again.
        An agent with no reservation numbers its first poll 1, and an answer that it is
        older stops the agent, since its log was cleared; this is the one older answer
        after which the agent does not continue above `newest`.
        The correctness item of `AGENTS.md` on a Write that runs twice adds the condition:
        also after a restart with the agent's clock set back, while the audit log is kept;
      - rule 7 drops "an agent whose clock jumps back voids this rule". A command reaches
        the agent only in the answer to its open poll, every command it received is listed
        before its next poll opens, and the reservation keeps every later poll newer than
        any captured one, so a clock set back no longer lets a command run after the
        server ended it. It says instead, in one sentence, that this rests on the audit
        log, and that a log cleared, cut short or restored acts as a clone does;
      - §4 adds the poll timeout to what every poll carries, and no longer says that the
        server's time in its answers is only for the console: the agent's retention also
        acts on it;
      - rule 14 says that a claimed key is reported in `bases.claimed`;
      - D4, §6 and this plan's "The protocol" say that every answer the agent acts on is
        signed and bound to the request's body, with the one exception of a bare 413 to an
        outcome, acted on whatever its headers and body, and that the other unsigned
        answers are acted on by nobody;
      - §6 says that a bare 413 comes from an ingress, from the web server, or from the
        library's own check of the declared length, all before the body is read;
      - "Pairing" says that a refusal of `pair` is a bare 403 without a body, and only ends
        the pairing;
      - the threat table says what a hop on the path can still do (§13 of the protocol),
        that a hop that also controls the agent's time service is stopped from running a
        Write twice by the reservation of rule 10, which lives as long as the audit log,
        that a compromised application that also controls the agent's time service can
        make retention remove the confirmed history, that a cleared audit log stops the
        agent at its first poll, that removing only some audit files brings lower
        reservations back and can forget a received Write, which may then run twice,
        and that a compromised application can fill the disk of the agent machine through
        the audit log, with scripts and parameters of up to `CommandSizeLimit` each. They
        are kept for `auditRetentionDays` and a day, and for as long as the server's signed
        `time` is more than a day away from the agent's clock, which a compromised
        application can keep it, so nothing else bounds them.
    - **The XML documentation** of `OutpostServerOptions.LeaseGrace` and of
      `OutpostAgent.Connected`, which says that the lease is `PollTimeout` plus
      `LeaseGrace` and that a gone agent shows as connected at most for its lease, follows
      rule 6 as above. The rows of `PollTimeout` and `LeaseGrace` in the table of the
      options follow it too.
    - **The bullets marked *(S2)*** in "The public contract" and "The protocol" of this
      plan hold once the owner approves.
  - **Edits that state nothing new**, in the contract step:
    - the ranges of the options and of `policy.json` that the protocol mirrors move into
      constants of `OutpostProtocol`, which the options check, `AgentPolicyReader` and the
      codec all use;
    - the depth check of S1 moves into `OutpostCodec.ValueDepth`.
- **What S2 changed in the public contract, as a draft for the owner** (the coordinator,
  21 November 2026). Each holds until the owner overturns it:
  - a new Refused code, `OutpostCodes.Malformed`, for a verified command of this version
    that breaks §7;
  - `result-type` is also an Unknown code, for a Write whose outcome the agent could not
    accept, and also covers a result deeper than 64 levels. `OutpostOutcome.Unknown`
    accepts it. The XML documentation of `OutpostCodes.ResultType`, `OutpostOutcome.Refused`
    and `OutpostOutcome.Unknown` says so;
  - the size of a command counts `{}` when there are no parameters, and every escape as
    the writer of §2 writes it. A script of exactly `CommandSizeLimit` without parameters
    is now 2 bytes over the limit and refused. The XML documentation of `CommandSizeLimit`
    and of `OutcomeSizeLimit` says what each size is;
  - parameters deeper than 64 levels still give `ArgumentException` with `ParamName`
    `Parameters`, and its message now names the 64 levels;
  - in the concept, the two sentences of rule 1 on verified bytes without a `protocol` and
    an `id` and on the order of its checks, and the sentence of rule 5 on the size of an
    outcome;
  - the readings Q8 and Q9 of the [open questions](open-questions.md), taken as
    recommended.

### S3 · The store

**Track:** full · **Branch:** `epic-1/sprint-03-store` · **Needs:** S2.

- **Goal.** The library's store exists on PostgreSQL in a schema the application chooses,
  `OutpostMigrations` works, and the store keeps pairing tokens with their pending agents
  and redeems each once.
- **Cut to what it builds** (the planner, 21 November 2026, by the owner's rule that a
  design document covers what its sprint builds). The plan had S3 design every operation
  of the store ahead of the sprints that call them. Now S3 builds the store itself and the
  operations of pairing tokens, which prove its way of making one conditional change. The
  operations of polls, leases and base keys are in the brief of S4, and those of commands
  and outcomes in the brief of S5; each of those sprints writes them with its tests
  against PostgreSQL, as here, before its HTTP. The study of S3 covered all of them, and
  what it found is carried into those briefs.
- **Contract.**
  - *Public, from S1, now working:* `OutpostMigrations`.
  - *Internal, after the study.* In the server's assembly, folder and namespace
    `Moltaro.OnecOutpost.Server.Store`; its migrations in
    `Moltaro.OnecOutpost.Server.Store.Migrations`.

    ```csharp
    internal sealed class StoreContext : DbContext
    {
        public StoreContext(DbContextOptions<StoreContext> options, string schema);
        public string Schema { get; }
        public DbSet<AgentRow> Agents { get; }
        public DbSet<PairingTokenRow> PairingTokens { get; }
    }

    internal static class StoreContextFactory
    {
        public static StoreContext Create(string? connectionString, string schema);
    }

    internal sealed class AgentRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public OutpostAgentStateEnum State { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? PairedAt { get; set; }
        public byte[]? PublicKey { get; set; }
    }

    internal sealed class PairingTokenRow
    {
        public Guid AgentId { get; set; }
        public byte[] Secret { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }

    internal sealed class OutpostStore
    {
        public OutpostStore(string connectionString, string schema, TimeProvider timeProvider);
        public Task CreatePairingTokenAsync(Guid agentId, string agentName, byte[] secret, DateTimeOffset expiresAt, CancellationToken cancellationToken);
        public Task<byte[]?> FindPairingSecretAsync(Guid agentId, CancellationToken cancellationToken);
        public Task<bool> RedeemPairingTokenAsync(Guid agentId, byte[] publicKey, CancellationToken cancellationToken);
        public Task<int> PurgePairingTokensAsync(CancellationToken cancellationToken);
    }
    ```

    - **The schema is chosen at run time.** `StoreContextFactory.Create` is the one place
      that builds the options, with no connection string for `GetScripts`: Npgsql, the
      default schema of the model, the history table of the migrations in that schema, a model cache key that includes the schema, and
      the library's own `IMigrationsAssembly` with an explicit list of migrations, each
      created with the schema, and a snapshot that takes the schema too. The check of
      pending model changes stays on. No retries.
    - **`OutpostMigrations.MigrateAsync`** migrates through that context.
      **`GetScripts`** gives, for each migration, the script of the migrator from the one
      before it, the first from nothing; it opens no connection. The name of a script is
      the identifier of its migration.
    - **A token is the identity of its agent.** `CreatePairingTokenAsync` writes the agent
      as Pending, with its name and the node's time, and the token with its secret and
      expiry, in one short transaction.
    - **`FindPairingSecretAsync`** gives the secret of a token that has not expired by the
      store's `TimeProvider` and whose agent is Pending, and `null` otherwise. S4 checks
      the proof of a pairing with it before it redeems.
    - **`RedeemPairingTokenAsync`** is one statement: it deletes the token when it has not
      expired, and in the same statement makes its agent, while Pending, Paired with the
      key and the time. It gives `true` to the one call that made the agent Paired. The
      secret is gone with the token (concept, "Pairing").
    - **`PurgePairingTokensAsync`** deletes, in one statement, the tokens that expired and
      their agents that are still Pending, and gives the number of tokens. Tokens are few,
      so it takes no batches.
    - Each operation opens a context of its own, runs under ReadCommitted and holds no
      lock while it waits for anything. What EF Core cannot say, such as `RETURNING` in a
      statement that changes two tables, is raw SQL with the table names taken from the
      model.
    - **The model.** Tables `agents` and `pairing_tokens`, columns in snake case, each
      named in the model, with no package for it. The key
      of `pairing_tokens` is `agent_id`, a foreign key to `agents` that deletes the token
      with its agent. Every time is a `timestamptz` that the store writes in UTC from its
      `TimeProvider`, never from the clock of the database (PQ3).
    - **The contract step** puts the types and the model into code.
      `StoreContextFactory.Create`, `OutpostMigrations` and the four operations of
      `OutpostStore` throw `NotSupportedException`: the options, the migrations assembly,
      the snapshot and the first migration are the implementer's.
    - The store uses `OutpostAgentStateEnum` of the root namespace, and `OutpostMigrations`
      of the root uses the store: the root is the public contract, the store depends on
      its types, and only the entry points of the root call down.
    - **Effort:** the test author and the implementer on `xhigh`, the store being a strict
      zone.
- **What S3 changed in the public contract, as a draft for the owner** (the coordinator,
  21 November 2026, after the code review). Each holds until the owner overturns it; the
  section on [`OutpostMigrations`](#outpostmigrations-and-outpostmigrationscript) and the
  XML documentation say so:
  - `MigrateAsync` also creates the database when it does not exist and the role may
    create one, as EF Core does;
  - calls of `MigrateAsync` for one schema wait for each other on a session advisory lock,
    because four or more nodes migrating a new schema at once failed without it; the
    connection keeps its session for the whole call;
  - `MigrateAsync` reports a failure of the database that may pass as
    `InvalidOperationException` with the `NpgsqlException` inside, and one that does not
    as `NpgsqlException`, a refusal by the server being its `PostgresException`;
  - each script of `GetScripts` carries its own transaction.
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
- **To read.** The [concept](../concept.md), §3 "Pairing" and §5; `docs/protocol.md`, §6.1
  `pair`; `AGENTS.md`, principles 6, 9 and 10; this plan, "The public contract" and PQ3.
- **Acceptance.**
  - Migrations, against PostgreSQL:
    - the model has no pending changes against its snapshot;
    - two `MigrateAsync` running at once into one new schema both complete, and the schema
      is what one gives;
    - two schemas in one database each get their own tables, and a context of one never
      reads the other's;
    - `GetScripts` works with no database, and the name of each script is the identifier
      of its migration.
  - Pairing tokens, against PostgreSQL, with the fake clock:
    - a created token has a Pending agent and its secret is found;
    - the secret is found until its expiry and not from it on;
    - a redemption makes the agent Paired with its key and time and leaves no token, so a
      second one gives `false`;
    - a redemption from the expiry on gives `false` and leaves the agent Pending;
    - a redemption of an identity the store does not know gives `false`, and its secret
      is not found;
    - the purge removes an expired token with its Pending agent, gives their number, and
      leaves a token that has not expired and an agent that is Paired.
  - One race, driven to a known order: another connection holds the row of a token locked
    until two redemptions are both observed waiting for it; then one gives `true`, the
    other `false`, and the agent has the key of the one that won.
  - The skipped tests of the migrations from S1 pass unskipped.
  - The documents step: `README.md` says that the migrations work and that the store keeps
    pairing tokens, which no endpoint uses yet.
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
  - *Internal, after the study* (the contract author, 21 November 2026; reviewed in two
    rounds and approved by the coordinator under PQ2, 21 November 2026). The store keeps
    the operations of polls, leases and base keys moved here from S3 (the planner, 21
    November 2026), tested against PostgreSQL before the endpoints use them. Folders and
    namespaces of the server's assembly: `Store` for the store; `Endpoints` for the
    handlers of the agents' requests; `Signing` for the server's key; `Purge` for the
    hosted service of the purge.

    ```csharp
    // Moltaro.OnecOutpost.Server.Store
    internal sealed class AgentRow // gains, all null until the agent's first poll
    {
        public long? NewestPoll { get; set; }
        public DateTimeOffset? LastPollAt { get; set; }
        public DateTimeOffset? LeaseEndsAt { get; set; }
        public string? AgentVersion { get; set; }
    }

    internal sealed class AgentBaseRow
    {
        public Guid AgentId { get; set; }
        public string Key { get; set; }
        public bool WriteOpen { get; set; }
    }

    internal sealed class BaseClaimRow
    {
        public string Key { get; set; }
        public Guid AgentId { get; set; }
    }

    internal sealed record PollState(bool Revoked, long? Newest, IReadOnlyList<string> Held, IReadOnlyList<string> Claimed);

    internal sealed class StoreContext // gains
    {
        public DbSet<AgentBaseRow> AgentBases { get; }
        public DbSet<BaseClaimRow> BaseClaims { get; }
    }

    internal sealed class OutpostStore // gains
    {
        public Task<AgentRow?> FindAgentAsync(Guid agentId, CancellationToken cancellationToken);
        public Task<PollState> RecordPollAsync(Guid agentId, OutpostPollRequest poll, TimeSpan hold, TimeSpan leaseGrace, TimeSpan clockSkew, CancellationToken cancellationToken);
        public Task<PollState> ReadPollStateAsync(Guid agentId, long number, CancellationToken cancellationToken);
        public Task<IReadOnlyList<OutpostAgent>> ListAgentsAsync(CancellationToken cancellationToken);
        public Task<bool> RevokeAgentAsync(Guid agentId, CancellationToken cancellationToken);
        public Task<OutpostReleaseResultEnum> ReleaseBaseAsync(string baseKey, CancellationToken cancellationToken);
    }

    // Moltaro.OnecOutpost.Server.Signing
    internal sealed class ServerSigningKey
    {
        public ServerSigningKey(string pkcs8Pem);
        public byte[] PublicKey { get; }
        public string Fingerprint { get; }
        public byte[] SignAnswer(OutpostRequestKindEnum kind, Guid agent, int status, ReadOnlySpan<byte> requestBody, ReadOnlySpan<byte> answerBody);
    }

    // Moltaro.OnecOutpost.Server.Endpoints: PairHandler, PollHandler and OutcomeHandler, each with
    // public Task HandleAsync(HttpContext context); AgentRequests, the checks of §9 before the body and
    // the reading of the body; AgentAnswers, the bare, unsigned and signed answers.
    // Moltaro.OnecOutpost.Server.Purge: PurgeService : BackgroundService.
    ```

    - **The model.** `agents` gains `newest_poll` (`bigint`), `last_poll_at` and
      `lease_ends_at` (`timestamptz`) and `agent_version`. `agent_bases` holds the bases
      of an agent's newest poll, keyed by `agent_id` and `key`. `base_claims` holds one row
      per claimed key, keyed by `key`, with an index on `agent_id`. Both have a foreign key
      to `agents` that deletes with the agent. The second migration, `PollsAndBaseClaims`,
      creates them; `StoreMigrationsAssembly` lists it and the snapshot follows.
    - **Held and claimed.** Of the keys the newest poll declared, `Held` lists those whose
      claim names the agent and `Claimed` every other, both in key order: ordinal, by
      `COLLATE "C"` or a sort in .NET, here and wherever this brief says it. A key the agent
      holds and no longer declares is in neither list.
    - **The order of locks** is a pairing token, then an agent row, then claims, in every
      operation. The agent row orders the agent's polls and the releases of its keys.
      Claims are inserted in key order, so two polls that claim the same keys never wait
      on each other in a cycle. `RedeemPairingTokenAsync` and the purge already take
      token before agent.
    - **`FindAgentAsync`** reads the row, or gives `null`.
    - **`RecordPollAsync`** is one short transaction. `now` is read once: it is
      `LastPollAt`, and the lease ends at the earlier of `now + hold + leaseGrace` and
      `poll.Time + clockSkew + hold + leaseGrace` (§6.2).
      1. A conditional `UPDATE` of the agent row, while it is Paired and its `newest_poll`
         is null or below `poll.Number`, sets the number, the times and the version, with
         `RETURNING`.
      2. When no row was updated, one statement reads the answer and nothing changes (rule
         10). `Revoked` is true when the agent is not Paired. Otherwise `Newest` is the
         recorded number, and the lists are those of the newest poll.
      3. Otherwise the transaction replaces the agent's `agent_bases` with the declared
         bases. It inserts a claim for each declared key, `ON CONFLICT (key) DO NOTHING`
         (rule 14), reads the lists and commits. `Newest` is `null`.
    - **`ReadPollStateAsync`** is one statement that changes nothing and takes no lock, so
      that the number and the lists come from one snapshot. `Revoked` is true when the
      agent is not Paired. `Newest` is the recorded number when it is not `number`, and
      `null` when it is.
    - **`ListAgentsAsync`** is one statement: the agents by `created_at` and identity,
      their bases by key, and the claims of those keys. `Connected` is true when the agent
      is Paired and `lease_ends_at` is later than the store's now. `ReadOpen` is always
      true, since every declared base opens Read (§6.2).
    - **`RevokeAgentAsync`** is one short transaction, in the order of locks. It deletes
      the agent's token, then updates the agent to Revoked when it is not already, then
      deletes its claims. It gives `true` when the update changed the row. The bases rows
      stay, so `GetAgentsAsync` still shows them, none held. S5 adds the end of the
      agent's commands to this transaction.
    - **`ReleaseBaseAsync`** is one short transaction.
      1. It reads the holder of the claim: `NotHeld` when there is none.
      2. It locks the holder's agent row `FOR NO KEY UPDATE`, so that a poll of the holder
         that is committing is seen whole. That lock conflicts with the poll's `UPDATE`,
         but not with the key-share locks that S5's inserts take.
      3. It deletes the claim when its holder is still that agent and the holder's
         `agent_bases` do not declare the key, with `RETURNING`: `Released`.
      4. Otherwise it reads the claim again: `NotHeld` when it is gone, and
         `StillDeclared` when it is not.

      *Limitation.* Another agent may claim the key between steps 1 and 2, after a
      release or a revocation. The call then answers `StillDeclared` and changes nothing,
      and the next call decides.
    - Every operation is as S3 says: a context of its own, ReadCommitted, no lock held
      while it waits for anything, time from the store's `TimeProvider` (PQ3), and raw SQL
      with the table names from the model where EF Core cannot say it.
    - **`MapOutpostAgents`** keeps its checks. It maps a group at `prefix`, with
      `AllowAnonymous` and `DisableRequestTimeout`, and in it `POST v1/pair`, `v1/poll`
      and `v1/outcome`. Each is a `RequestDelegate` of the handler that `AddOutpostServer`
      registered and takes `HttpContext` only, so that nothing binds the body. One more
      `POST {version}/{request}`, whose internal route constraint refuses `v1` compared
      without regard to case, as routing compares the literal, answers every other
      version. This keeps the public promise of no routing 404, and it never matches a
      path under `v1`, which stays the application's (§9). Every other
      `POST {prefix}/{version}/{request}` belongs to the library.
    - **Before the body** (§9):
      1. a version other than `v1` gets the unsigned 400 of `WriteUnsupportedVersion`;
      2. a header of §4 that is missing, repeated or in another form, and a
         `Content-Encoding`, get a bare 400;
      3. the handler sets `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` to the limit
         of §10. For an `outcome` that is `OutcomeSizeLimit` plus
         `OutcomeEnvelopeAllowance`. A declared `Content-Length` beyond the limit gets a
         bare 413, unread;
      4. `poll` and `outcome` then look up the agent: none or Pending gets a bare 401, and
         Revoked a bare 403.

      Only then is the body read, whole, into one array. Kestrel's
      `BadHttpRequestException` with status 413, for a body without a declared length,
      becomes a bare 413 while the response has not started, so that the application's
      exception handler never decides it. "Bare" means a status alone, without a body or
      `Outpost-Signature`. *Limitation.* When the feature is missing or read-only, as
      after a middleware buffered the body, the request fails loudly with a 500 and no
      guarantee is lost.
    - **`pair`** gives a bare 403 to every failure of §6.1, in this order:
      `ReadPairRequest`, `FindPairingSecretAsync`, `OutpostPairingProof.Check`, then
      `RedeemPairingTokenAsync`, whose `false` is the lost race. Its 200 is
      `OutpostPairAnswer` with the key it registered, the server's key, `PollTimeout` and
      `LeaseGrace`, signed with `SignAnswer`.
    - **`poll`.**
      - `ReadPollRequest` gives the signed refusals of §9: `signature` with 401 and `form`
        with 400, each an `OutpostRefusal` signed as the answer to that request.
      - The hold is the smaller of the poll's `PollTimeout` and the option.
        `RecordPollAsync` then decides. `Revoked` gets a bare 403. An older poll gets its
        signed 200 at once, with `newest` and no commands.
      - The newest poll is held on one linked token: a `CancellationTokenSource` of the
        hold on the options' `TimeProvider`, started once the poll is recorded,
        `RequestAborted`, and `ApplicationStopping`.
      - When the request is aborted, the handler writes nothing and changes nothing more:
        the lease stays as recorded.
      - When the hold ends or the node stops, the handler calls `ReadPollStateAsync` with
        `RequestAborted` and answers by its result. `Revoked` gets a bare 403. A
        `Newest` that is not `null` means a newer poll replaced this one while it was
        held, which only the store tells, since nodes share nothing: the answer is the one
        of an older poll. Otherwise it is the ordinary 200 without commands.
      - Every 200 carries the node's time, the options `PollTimeout` and `LeaseGrace`, and
        the lists of the `PollState`.
    - **`outcome`** runs the checks of §9 and reads its body within its limit. It then
      throws `NotSupportedException`, which ASP.NET Core answers with a 500; S5 replaces
      it.
    - **`ServerSigningKey`** is made once by `AddOutpostServer` from the validated PEM.
      `PublicKey` is the 91 bytes of §2, and `Fingerprint` comes from
      `OutpostKeys.Fingerprint`. Each signature imports the key into an `ECDsa` of its own
      and disposes it, since .NET does not document an `ECDsa` as safe for concurrent
      use. No lock is taken. S5 adds the signing of commands here.
    - **`AddOutpostServer` in full** adds singletons to the checks of S1: the
      `OutpostStore` with the options' connection string, schema and `TimeProvider`; the
      `ServerSigningKey`; the three handlers; and `PurgeService` as a hosted service.
      Resolving `IOutpostServer` still needs `ISignals`, which S5 uses.
    - **The members of `IOutpostServer`** check their arguments, then their token: a
      cancelled one gives a cancelled task before the store is opened.
      - `CreatePairingTokenAsync` makes the identity with `Guid.NewGuid()` and 16 bytes
        of `RandomNumberGenerator`. The expiry is now plus `PairingTokenLifetime`,
        truncated to whole microseconds, so that `ExpiresAt` is what the store keeps.
        `Token` is the text of `OutpostPairingTokenText` with the key's fingerprint.
      - `GetAgentsAsync`, `RevokeAgentAsync` and `ReleaseBaseAsync` give what the store
        operation of the same name gives; `GetAgentsAsync` uses `ListAgentsAsync`.
    - **The purge.** `PurgeService` runs on every node: once when the host starts, then
      after every `PurgeInterval`, waiting with `Task.Delay` on the options'
      `TimeProvider`. It ends with the host's stopping token. In S4 it calls
      `PurgePairingTokensAsync`, and S5 adds the commands past their retention. A failure
      other than stopping is logged as one warning, with its exception, under the
      category `Moltaro.OnecOutpost.Server`, and the next round comes after the interval.
      Otherwise a store that is down would stop the host: that is the default of
      `BackgroundService`.
    - **The test agent** of the Acceptance is test code of
      `Moltaro.OnecOutpost.Server.Tests`, built from the protocol's internal types. S5
      reuses it.
    - **The contract step** puts into code the rows, the two `DbSet`s, the model, the
      migration `PollsAndBaseClaims` and its snapshot, and `PollState`. The test of pending
      model changes of S3 then stays green after every commit. The six operations throw
      `NotSupportedException`. The types of `Signing`, `Endpoints` and `Purge` are the
      implementer's, since the tests reach them only through the public API and HTTP.
    - **Settled: what a poll stores** (the contract author, 21 November 2026, as the study
      recommends).
      - It stores its number, the node's moment of recording as `LastPollAt`, and the end
        of its lease, computed once, so that `Connected` is `lease_ends_at > now` and the
        hold and the grace need not be kept. It also stores the agent version.
      - It replaces its bases whole, as rows with `WriteOpen`, and claims its keys as rows
        of their own, which settles the race of two claims in one statement.
      - Left out, since nothing in S4 reads them: the agent's time, which S5 adds with its
        own migration if its contract needs it; `ReadOpen`, always true in v1; and
        `free`, `runLimitMs` and `received`, which belong to S5.
    - **Settled: "still declares" means the newest poll**, whatever its lease (the
      contract author, 21 November 2026, as the study recommends).
      - Rule 14 frees a key only by revocation or release, so that the scripts of one
        database never reach another. A holder that is down for maintenance and comes back
        declaring the key finds it still its own.
      - The decision depends on no clock, which a live lease would.
      - It matches `OutpostAgent.Bases`, what the newest poll declared.
      - A holder gone for good is revoked, which frees its keys.
    - **Edits that state nothing new:** none on tests or samples.
      - The argument tests of S1 call with a cancelled token and a database nobody
        listens on, and stay green because every member checks its token before the store.
      - `MapOutpostAgentsTests` never starts its application, and stays green now that
        mapping works.
      - `StoreMigrationsAssembly` lists and creates the second migration, in the contract
        step.
      - No test of an earlier sprint is skipped, so S4 unskips none.
    - **Deferred to S4 by S2's list** ("What S2 makes of this list"). Whoever writes the
      code that each item describes makes it, and the documents step writes the prose of
      `README.md`, `AGENTS.md`, the concept and the protocol:
      - concept rule 6, and the correctness item of `AGENTS.md` on an agent that is gone,
        state the lease of §6.2 exactly, polls held back on the path included;
      - the XML documentation of `OutpostServerOptions.LeaseGrace` and of
        `OutpostAgent.Connected`, which today says that the lease is `PollTimeout` plus
        `LeaseGrace`, follows §6.2, and so do the rows of `PollTimeout` and `LeaseGrace`
        in the options table. This is in the contract step (the coordinator's decision);
      - rule 14 says that a claimed key is reported in `bases.claimed`;
      - concept §4 says that every poll carries its poll timeout;
      - D4, §6 and this plan's "The protocol" say that every answer the agent acts on is
        signed and bound to the request's body, except a bare 413 to an outcome. Concept
        §6 adds that the library's own check of the declared length sends a bare 413 too;
      - "Pairing" says that a refusal of `pair` is a bare 403 without a body.

      The other items of that list are the agent's, for S6.
    - **Effort:** the test author and the implementer on `xhigh`, since pairing, the
      transport and the store are strict zones.
- **What S4 changes in the public contract, as a draft for the owner** (the contract
  author, 21 November 2026). Each holds until the owner overturns it. The XML
  documentation it touches says so, written by whoever writes the code it describes:
  - the log category `Moltaro.OnecOutpost.Server` holds one warning, for a purge that
    failed;
  - `OutpostAgent.Connected` is `false` for an agent that is not Paired, whatever its
    lease, so a revoked agent is never shown as connected; concept rule 6 says so too;
  - a poll whose agent was revoked after it was looked up gets the bare 403 of
    §9 after its body was read. This happens when it is recorded, or when its hold ends;
  - a declared key that no agent holds at that moment is in `bases.claimed` of a poll
    answer, and has `Held` `false` in `OutpostBase`. That is a key freed during a hold,
    by a release or a revocation, and the agent's next poll claims it. Both lists of a
    poll answer hold declared keys only.

  The same change makes `docs/protocol.md` say so, in §6.2 `bases`, in §9 and in its
  Status, and the XML documentation of `OutpostBase.Held` too.
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
    - a second use, an expired token, a wrong proof, a swapped key and a body that breaks
      §2 all get the same bare `403` without a body;
    - two pairings racing on one token: one wins and the other gets the `403`;
    - the answer is signed and repeats the registered key;
    - a request with a changed body or another agent's key gets the signed `signature`;
    - the unsigned answers of §9 of `docs/protocol.md`, each one sent before the body is
      read:
      - an unsupported version gets `protocol-version` in the form that later versions
        keep;
      - a required header of §4 that is missing, repeated or in another form, and a
        `Content-Encoding`, each get a bare 400. An `outcome` without `Expect` or without
        `Content-Length` is not answered with a bare 400 or 413;
      - an unknown agent and a pending one get a bare 401, and a revoked one a bare 403;
      - a `pair`, a `poll` and an `outcome` body at their limits are not answered with a
        bare 413, and one byte beyond with a declared length gets one. The test client
        sends these with `Expect: 100-continue` and an `Expect100ContinueTimeout` longer
        than the test's own wait, so that the 413 comes before the body and no reset can
        wipe it out;
      - a chunked body one byte beyond its limit, which declares no length, ends in a 413
        or a reset of the connection, either of which passes, and the store is unchanged;
    - an older poll, and an exact replay of the newest, change nothing. The answer to an
      older poll comes at once, signed, and names the newest number recorded;
    - a poll is held for the smaller of the poll timeout it carries and the option, and
      ends without commands at that time, with the fake clock;
    - the end of a hold, with the fake clock:
      - a held poll that a newer one replaced is answered as older when its hold ends;
      - a revocation during the hold ends the poll in the bare 403 when its hold ends;
      - stopping the node ends the hold at once with the ordinary 200;
      - an aborted poll gets no answer written, and its lease stays as recorded;
    - `Connected` follows the lease of §6.2, with the fake clock: a poll recorded on time
      grants its hold plus the lease grace; a poll whose signed time is behind the node's
      by more than `ClockSkew` grants less by the part of its lag beyond `ClockSkew`; and
      an agent whose clock is behind
      by more than `ClockSkew` plus the hold plus the lease grace never shows as
      connected;
    - base keys follow rule 14, release included, and the poll answer reports them;
    - the purge removes an unused token and its pending agent.
  - Against PostgreSQL, without HTTP: a test for each operation of the store above, each
    case of a rule named for the rule; the exact replay of the newest poll changes
    nothing; and one race driven to a known order, two claims of one base key, in which
    one side wins and the other changes nothing.
  - The documents step: `README.md` says that agents can pair and poll.
- **Out of scope.** Commands; the signals, which commands need first.

### S5 · Commands on the server

**Track:** full · **Needs:** S4. One brief, run as two sprints (the planner, 22 November
2026):

- **S5a, commands in the store.** Branch `epic-1/sprint-05-commands`. The commands table
  and its migration, the store operations of commands and outcomes, their tests against
  PostgreSQL with the races of the Acceptance, and revocation ending the agent's commands
  in its transaction. `ReadAsync`, `WriteAsync` and `outcome` still refuse. It closes with
  its own code review, pull request and merge under PQ1.
- **S5b, the calls.** Branch `epic-1/sprint-05b-calls`, cut from `main` after S5a merges.
  The calls, the hand-out in the poll, the `outcome` request and the signals over loopback
  HTTP, and the documents step that says commands work. The internal contract stays as
  approved; the steps from the tests on run again for it.

The Acceptance "Against PostgreSQL, without HTTP" is S5a's; every other line of it is
S5b's.

**Left for S5b by S5a's review** (P4, the coordinator, 22 November 2026). S5b looks at
each where its own code meets it:

- a take skips a Waiting command whose row a create that found it holds, and nothing then
  wakes the poll before its hold ends;
- a number in the parameters that PostgreSQL's `numeric` cannot hold, such as `1e-20000`,
  makes every call with a caller key fail in the store; the limitation is stated nowhere;
- `PassCommandAsync` throws `InvalidOperationException` for a command the purge removed;
- the room of a poll answer is not tested at its exact boundary;
- deleting a Pending agent scans `commands` through its foreign key;
- the comment on the order of locks in `OutpostStore` reads as if two creates for one
  agent wait for each other, which `FOR SHARE` locks do not.

S5b closed the first, the fourth and the last; it stated the second in the XML
documentation of `CallerKey`. **Left by S5b's review** (P4, the coordinator, 22 November
2026), for S8 unless an earlier sprint meets them:

- a repeat that finds its command as the purge removes it gets `InvalidOperationException`
  from `PassCommandAsync`; deleting a Pending agent scans `commands`;
- the poll answer is written through UTF-16 base64 strings and a growing buffer, about
  352 MiB at the peak for a full 64 MiB answer; a waiting call reads a stored outcome with
  a copy and a clone;
- a cancellation during the sets of a revocation or of a poll's Expired skips the sets
  after the one in flight, so those callers learn of the end up to `PollTimeout` later;
- a signal that arrives as a hold ends gets no take, and the next poll takes the command;
- at the lease boundary a call can get Unavailable while another call's command for the
  same key is committing; nothing runs twice;
- the 4-argument `ReadOutcomeRequest` is called only by the tests of the protocol.

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
  - *The store, moved here from S3* (the planner, 21 November 2026): the operations of
    commands and outcomes, added to `OutpostStore` with a migration, and tested against
    PostgreSQL before the calls and the endpoints use them. They have to be able to:
    - create a command, or find the one that its base and caller key name, in one change
      (rule 4): the script is compared exactly, the parameters as JSON values;
    - record the list of received commands of the newest poll, and of no older one (rule
      10), handing out again those missing from it (rule 11);
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
    - end the commands of a revoked agent (rule 13);
    - remove ended commands after their retention.

    Every change is one statement or one short transaction under ReadCommitted, holds no
    lock while it waits for anything, and takes its time from the server's `TimeProvider`
    (PQ3). From the study of S3, to start from:
    - the take is one statement: a CTE that updates the row of the agent only while the
      poll is its newest and it is not revoked, and commands picked per base and mode,
      oldest first by an identity column, with `FOR UPDATE SKIP LOCKED`; the other changes
      of one command are `UPDATE … WHERE state = … RETURNING`, the state being the version;
    - create-or-find is a partial unique index on the base and the caller key over the
      commands that hold their key, and `INSERT … ON CONFLICT … DO UPDATE … RETURNING`
      with an identity the server generates, which tells a created command from a found
      one; a call for an agent that is not connected reads by the key before it answers
      Unavailable;
    - the parameters are kept as the exact text that was signed and compared as `jsonb`
      only there, which needs a database in UTF-8;
    - oldest first is the order of insertion, not of commit;
    - removal runs on every node in bounded batches with `SKIP LOCKED`.

    To settle when this contract is written: the states of a hand-out and whether a
    command once listed and then left out is handed out again; what is stored with a
    command for its expiry on the agent's time; whether a command is signed once and its
    bytes kept; whether a command the server itself refuses is stored; and that duplicate
    names inside the parameters are refused at the call, since `jsonb` would compare
    another value than the one signed — a draft of the public contract.
  - *Internal, after the study* (the contract author, 22 November 2026, from the study's
    recommendations A–H as the coordinator took them; reviewed in two rounds and approved
    by the coordinator under PQ2, 22 November 2026). Folders as in S4; the commands of
    the store are in `Store`.

    ```csharp
    // Moltaro.OnecOutpost.Server.Store
    internal enum CommandPhaseEnum { Waiting = 0, HandedOut, Received }

    internal sealed class CommandRow
    {
        public Guid Id { get; set; }
        public long Sequence { get; set; }
        public Guid AgentId { get; set; }
        public string BaseKey { get; set; }
        public OutpostModeEnum Mode { get; set; }
        public string? CallerKey { get; set; }
        public byte[] Bytes { get; set; }
        public byte[] Signature { get; set; }
        public DateTimeOffset Deadline { get; set; }
        public TimeSpan Skew { get; set; }
        public int OutcomeLimit { get; set; }
        public CommandPhaseEnum Phase { get; set; }
        public TimeSpan? RunLimit { get; set; }
        public OutpostOutcomeKindEnum? OutcomeKind { get; set; }
        public string? OutcomeCode { get; set; }
        public byte[]? Outcome { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
    }

    internal sealed record PollRecord(PollState State, IReadOnlyList<Guid> Expired);
    internal sealed record PollTake(PollState State, IReadOnlyList<OutpostSignedCommand> Commands);
    internal sealed record BaseHolder(Guid AgentId, bool Declared, bool WriteOpen, bool Connected);
    internal sealed record CommandMatch(Guid Id, bool Created, bool Same);
    internal sealed record CommandPass(OutpostOutcome? Outcome, DateTimeOffset Due);
    internal sealed record CommandLimits(OutpostModeEnum Mode, int OutcomeLimit);
    internal sealed record Revocation(bool Revoked, IReadOnlyList<Guid> Ended);

    internal sealed class StoreContext // gains
    {
        public DbSet<CommandRow> Commands { get; }
    }

    internal sealed class OutpostStore // changes two operations of S4 and gains eight
    {
        public Task<PollRecord> RecordPollAsync(Guid agentId, OutpostPollRequest poll, TimeSpan hold, TimeSpan leaseGrace, TimeSpan clockSkew, CancellationToken cancellationToken);
        public Task<Revocation> RevokeAgentAsync(Guid agentId, CancellationToken cancellationToken);
        public Task<PollTake> TakeCommandsAsync(Guid agentId, OutpostPollRequest poll, long room, CancellationToken cancellationToken);
        public Task<CommandMatch?> FindCallerKeyAsync(string baseKey, string callerKey, OutpostModeEnum mode, string script, string parameters, CancellationToken cancellationToken);
        public Task<BaseHolder?> FindBaseAsync(string baseKey, CancellationToken cancellationToken);
        public Task<CommandMatch?> CreateCommandAsync(CommandRow command, CancellationToken cancellationToken);
        public Task<CommandPass> PassCommandAsync(Guid commandId, CancellationToken cancellationToken);
        public Task<CommandLimits?> FindCommandLimitsAsync(Guid commandId, CancellationToken cancellationToken);
        public Task<bool> KeepOutcomeAsync(Guid agentId, Guid commandId, OutpostOutcomeMessage message, ReadOnlyMemory<byte> outcome, CancellationToken cancellationToken);
        public Task<int> PurgeCommandsAsync(TimeSpan retention, CancellationToken cancellationToken);
    }
    ```

    - **The model.** The third migration, `Commands`, creates `commands`: the key `id`,
      `sequence` as `bigint` generated as identity, a foreign key to `agents` that deletes
      with the agent, `bytes`, `signature` and `outcome` as `bytea`, `deadline` and
      `ended_at` as `timestamptz`, `skew` and `run_limit` as `interval`, the enums as
      integers. Its indexes: unique on `base_key` and `caller_key` where the caller key is
      not null and the outcome is neither Expired nor Refused, the commands that hold their
      key (rule 4); `agent_id` with `sequence` where there is no outcome, for the take; and
      `ended_at`, for the purge. `StoreMigrationsAssembly` lists it and the snapshot
      follows. A fourth index, `IX_commands_agent_id`, on `agent_id` over the commands whose
      outcome is none or one the server set of the kind Unknown or Failed, serves the expiry
      that a newest poll's list makes, which without it scans the whole table while the poll
      holds its agent row (the coordinator, 22 November 2026, from S5a's review).
    - **The facts of a command.** `Phase` only moves forward: Waiting, never handed out;
      HandedOut, in at least one poll answer; Received, listed by a newest poll. An outcome
      the agent sent does not change the phase, a departure from the study: a kept outcome
      already keeps the command from the take and from Expired. The outcome is none
      (`OutcomeKind` null), the agent's (`Outcome` holds its object as it came, §8), or one
      the server set (`Outcome` null). Only an outcome the server set of the kind Unknown
      or Failed is *replaceable* (rule 11). `RunLimit` is that of the mode in the poll
      that last handed the command out. Each change is a conditional `UPDATE … WHERE …
      RETURNING`; every change that sets an outcome sets `EndedAt` to its now.
    - **The order of locks** is a pairing token, then an agent row, then claims, then
      commands. An operation that changes several commands holds their agent row first,
      as the record, the take and the revocation do, or skips locked rows, as the purge
      does; every other changes one command and then waits for nothing.
    - **`RecordPollAsync`** keeps S4's steps. When the poll is the newest, after its
      claims, two statements reconcile `received` (rules 7, 10, 11): the agent's HandedOut
      commands in the list become Received; those not in it whose outcome is none or
      replaceable become Expired when `poll.Time` is later than `deadline + skew`. Ids of
      no command of this agent are ignored. `Expired` lists what the second changed, and
      is empty for an older poll.
    - **`TakeCommandsAsync`** is one short transaction. It locks the agent row `FOR NO KEY
      UPDATE` while the agent is Paired and `poll.Number` is its newest. Then one statement
      picks, per base and mode of the poll with a `free` above 0, at most `free` commands
      of the agent oldest first by `sequence`, `FOR UPDATE SKIP LOCKED`: Waiting or
      HandedOut, with no outcome, a deadline later than now, a mode the poll declares, and
      a key the agent holds (rule 14). It keeps them in `sequence` order while their
      entries in the answer, the bytes in base64url with the signature, fit `room`, the
      first always (§10), and makes those HandedOut with their `RunLimit`. It reads the
      `PollState` in the same transaction, `Newest` as `ReadPollStateAsync` gives it, and
      commits. When the lock found no row, it takes nothing and the state says why:
      `Revoked`, or a `Newest` that is not `null`. Two takes of one agent wait on its row,
      so the Acceptance's race "two takes of one command" is a take that finds the
      command's row held by another transaction: it skips it and changes nothing.
    - **`FindCallerKeyAsync`** is one short transaction: it makes the command that
      holds the key Expired when it is Waiting with no outcome and its deadline has passed,
      then reads the command that
      holds the key. `Same` compares the mode, the script exactly, and the parameters as
      `jsonb` (rule 4), the stored ones read from `bytes`. `null` when no command holds the
      key.
    - **`FindBaseAsync`** is one statement: the holder of the key's claim, whether its
      newest poll declares the key and opens Write there, and `Connected` as
      `ListAgentsAsync` judges it. `null` when no agent holds the key.
    - **`CreateCommandAsync`** is one short transaction. It locks the agent row `FOR
      SHARE` while the agent is Paired and its lease ends later than now, so that a
      revocation ends every command made before it (rule 13) and none is made for an
      agent not connected (rule 6): `null` otherwise. Then `INSERT … ON CONFLICT` on the
      unique index `DO UPDATE … RETURNING` gives the command: `Created` when its id is the
      new one, otherwise the one found, with `Same` as above.
    - **`PassCommandAsync`** is one short transaction: one statement ends the command when
      time made it due, Expired when it is Waiting with no outcome and its deadline has
      passed, or, when it is taken with no outcome and now is at or after its agent's
      `lease_ends_at` plus `RunLimit`, Unknown `disconnected` for a Write and Failed
      `lost` for a Read (rule 11).
      A second reads it. `Outcome` is the command's outcome, the agent's read with
      `OutpostCodec.ReadOutcome` for its mode. `Due` is when time can next end it: the
      deadline while Waiting, `lease_ends_at` plus `RunLimit` once taken.
    - **`FindCommandLimitsAsync`** reads the mode and the outcome limit, or gives `null`.
    - **`KeepOutcomeAsync`** is one statement: it keeps the outcome when the command is
      this agent's, not Waiting, and its outcome is none or replaceable, and gives `true`
      when it changed the row (rule 11).
    - **`RevokeAgentAsync`** adds, after the claims, two statements: the agent's Waiting
      commands with no outcome become Refused `revoked`, and its taken ones with no outcome
      Unknown `revoked` for a Write and Failed `lost` for a Read (rule 13). `Ended` lists
      them. They run only when the call revoked the agent.
    - **`PurgeCommandsAsync`** deletes, in statements of at most 1 000 rows with `SKIP
      LOCKED` until one deletes fewer, every command whose `EndedAt` is a retention ago or
      more, and every one with no outcome whose end by time, as `PassCommandAsync` judges
      it, was due that long ago: the same as ending it when due and removing it a
      retention later, without an update. It gives the number deleted.
    - Every operation is as S3 and S4 say, its now read once from the store's
      `TimeProvider` (PQ3).
    - **The signals.** The keys are `{SignalKeyPrefix}.{id}`, with the id of an agent or a
      command in the form of §2. Every waiter subscribes with the group
      `Guid.NewGuid().ToString("N")`, and disposes its subscription in a `finally`. A set
      comes after the commit it announces; `Unavailable` is ignored. The agent's key is set
      when a call created a command; a command's key when its outcome was kept, when a
      poll's record made it Expired, and when a revocation ended it. A failure of the
      signals is a failure of the poll, a 500, or of the call.
    - **A call** (`ReadAsync`, `WriteAsync`), after its checks and its token:
      1. with a caller key, `FindCallerKeyAsync`: a `Same` command is waited for, another
         is Refused `caller-key-conflict` with no `CommandId`;
      2. `FindBaseAsync`: `null` is Refused `unknown-base`; a key not declared, or Write
         not open, `mode-closed`; not `Connected` is Unavailable;
      3. it writes the command with `OutpostCodec.Write`: a new id, `issued` the now
         truncated to the millisecond, `deadline` that plus `TakeTimeout` truncated alike,
         `ClockSkew` and `OutcomeSizeLimit`. It signs the bytes once, with the new
         `ServerSigningKey.SignCommand`, and `CreateCommandAsync` stores them as they are,
         with the deadline that was signed. `null` is Unavailable; `Created` sets the agent's
         key; otherwise it goes on as step 1 does. Before any Unavailable, a call with a
         caller key runs `FindCallerKeyAsync` once more and follows it when it finds one;
      4. the wait: subscribe to the command's key, then pass, wait, pass. A pass is
         `PassCommandAsync`, which returns the outcome or the next `Due`. Each wait is
         `WaitAsync(null, …)` on a linked token: the caller's, and a source on the options'
         `TimeProvider` at the earlier of `Due` and `PollTimeout` after the wait starts,
         the coordinator's choice for the study's Q2. After any wait the caller's token is
         checked first (rule 12).
    - **A poll**, after S4's checks: it subscribes to its agent's key before
      `RecordPollAsync`, sets the keys of `Expired`, and starts the hold as S4 does. Then
      it passes and waits until the hold ends. A pass is `TakeCommandsAsync`, with `room`
      the limit of a poll answer (§10) less the length of the answer without commands:
      `Revoked` gets the bare 403, a `Newest` the answer to an older poll, commands the
      ordinary answer with them, and nothing another `WaitAsync(null, hold token)`. The
      end of a hold is S4's.
    - **`outcome`**, after its body: `ReadOutcomeRequest` with the signed refusals of §9;
      `FindCommandLimitsAsync`, `OutcomeSizeLimit` and either mode for an unknown
      command; an `outcome` larger than the limit gets the signed 413 `result-too-large`,
      which `WriteRefusalAsync` gains; `ReadOutcome` with `form`; then `KeepOutcomeAsync`,
      the command's key set when it kept, and the signed 200 with `confirmed`.
    - **The purge** calls `PurgeCommandsAsync` with `OutcomeRetention` after the tokens,
      in the same round. Its catch logs every failure but an `OperationCanceledException`
      after the stop was requested, so that no failure leaves the loop to be logged
      under the host's category (S4's review, P4 3).
    - **Settled: the states of a hand-out** (as the study's A). A poll that takes anything
      answers, so it takes once, and every HandedOut command that its later pass finds
      was handed out by an older poll and missing from this one's list: it is handed out
      again until its deadline by the node's clock (rule 11). A Received command is never
      handed out again and never Expired: it reached the agent (rule 3), and Expired would
      free a key whose Write may still run. A replaceable outcome is not handed out again;
      it waits for the agent's outcome or for the poll that makes it Expired.
      *Limitation.* A connected agent whose audit log was restored (§13 of
      `docs/protocol.md`) may leave a Received command out of its lists; that command ends
      only when the agent is revoked, as §13 has it.
    - **Settled, as the study's B, C, D and F:** expiry stores the signed `deadline` and
      `skew`, never the agent's time; the signed bytes are kept and handed out as they are;
      a refusal of the server stores nothing; `free` is a parameter of the take,
      `runLimitMs` is stored at each hand-out, `received` becomes the phase, and `agents`
      gains no column.
    - **Settled: duplicate names are refused at the call** (E): `CommandChecks` runs
      `OutpostJsonReader.CheckText` on the written parameters after their depth, in place
      of its check of U+0000, as `ArgumentException` for `Parameters`.
    - **Settled: a change that time makes due sets no key**, a departure from the study:
      every waiter of the command ends its own wait by that time, `Due` being read from
      the same row, and every wait ends by `PollTimeout`.
    - **Settled: the take hands out only for keys the agent holds**, so that a command
      made before a release never reaches its agent after another claimed the key (rule
      14). *Limitation.* A base, mode or claim that changes between `FindBaseAsync` and the
      creation leaves a command that no poll takes, and it ends as Expired.
    - **Settled: the body is read as S4 reads it** (S4's review, P4 1), a departure from
      the study: `GetBuffer` saves one copy, but not the peak of the stream's growth, about
      three times the body, which §9 leaves to rate limiting. `WriteBare` (P4 2) and
      `HoldTests` (P4 4) are not touched and stay P4.
    - **The contract step** is S5a's and puts into code all of this contract, for both
      halves: `CommandPhaseEnum`, `CommandRow`, the seven records, the `DbSet`, the model,
      the migration `Commands` and its snapshot, and the check of duplicate names with its
      XML documentation, which then works and gets its test in S5a's tests step. The eight
      new operations throw `NotSupportedException`; `RecordPollAsync` and
      `RevokeAgentAsync` give their new records and keep working, with `Expired` and
      `Ended` empty. S5a's implementer makes the store operations; the calls, the
      handlers, `SignCommand` and the purge's call are S5b's.
    - **Edits that state nothing new:**
      - in the contract step, `PollHandler` reads `.State` of the `PollRecord`,
        `OutpostServer.RevokeAgentAsync` gives `.Revoked` of the `Revocation`, and the
        store tests of S4 and their helper `StoreAgent` read the same;
      - the comment of `ArgumentChecks`, which names `NotSupportedException`, follows the
        implementation;
      - the argument tests of S1 and the outcome tests of `RequestCheckTests` stay green;
        no test asserts that Read, Write or `outcome` refuse, and none is skipped;
      - the documents step corrects `docs/work/README.md`, whose list of projects says
        "the test projects of the first three", to the list of `AGENTS.md`.
    - **Effort:** the test author and the implementer on `xhigh`, since the store and the
      transport are strict zones.
- **What S5 changes in the public contract, as a draft for the owner** (the contract
  author, 22 November 2026). Each holds until the owner overturns it; whoever writes the
  code writes its XML documentation:
  - the parameters of `ReadAsync` and `WriteAsync` with a name twice in one object, at any
    depth, give `ArgumentException` for `Parameters`, and the rule of names says so;
  - a key whose holder's newest poll no longer declares it opens neither mode: a call gets
    Refused `mode-closed`;
  - the store's database has the encoding UTF8, which the XML documentation of
    `ConnectionString` and concept §5 state;
  - a held poll whose agent was revoked gets the bare 403 of §9 also when it wakes, and
    `PollTimeout` also bounds each wait of a call, which §9, its XML documentation and its
    row in the options table say.
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
  - The hand-out (§6.2 and §10 of `docs/protocol.md`):
    - a poll takes at most `free` commands per mode, oldest first, and a command handed out
      again counts against `free`;
    - an answer is filled up to 64 MiB, and always holds one command when one is due, the
      largest one included;
    - an outcome is checked by §8 for the mode of its command.
  - The signals:
    - the keys carry the prefix;
    - two calls waiting on one command both return;
    - the newest poll is woken while an older poll of the same agent still waits, and the
      older one takes nothing and is answered as older when it wakes or its hold ends;
    - a wait reads the store again after its subscription rejoins a restarted Signals host.
  - Against PostgreSQL, without HTTP: a test for each operation of the store above, each
    case of a rule named for the rule, and these races, each driven to a known order, in
    which one side wins and the other changes nothing. A statement with `SKIP LOCKED`
    never waits, so such a race holds the first change open and shows that the second
    changed nothing:
    - two takes of one command;
    - a take against an expiry;
    - two outcomes of one command;
    - two first calls with one base and one caller key: one command is created, and the
      other call finds it;
    - two repeats racing to create a new command after the first ended as Expired or
      Refused: one command is created.
  - The documents step: `README.md` says that commands work against an agent that speaks
    the protocol.
- **Out of scope.** The agent program; several server nodes.

### S6 · The agent

**Track:** full · **Needs:** S5; L1 when it has run, as
[Order](#order-and-why-these-splits) says. One brief, run as three sprints (the planner, 22
November 2026; the coordinator cut the loop in two, 22 November 2026, under the planner's
standing instruction to cut a sprint too large for one exhaustive review):

- **S6a, the agent's files.** Branch `epic-1/sprint-06a-agent-files`. What the agent keeps
  on its machine, without HTTP: the policy with the changes carried over from S2;
  `agent.json` read, the identity and the pinned key, which the console writes in S10;
  the audit log, written in order through to the disk, its lock, torn entries, reading
  and cutting at a start, and retention as a rule given the time of a verified answer;
  the outbox; the order of the records of a command, in one place; the secrets and the
  access list of the data folder behind their boundaries; the reservation of poll
  numbers, given the `newest` of an older answer as an input; and what a start finds after
  a restart. The agent program still does nothing.
- **S6b, the checks and the workers.** Branch `epic-1/sprint-06b-agent-checks`, cut from
  `main` after S6a merges. The checks of every command (rule 1, §7) and of a poll answer;
  the worker boundary with its fake; `WorkerPool` with workers and run limits (rules 8,
  9) and what turns an executor's object into the outcome sent. No HTTP: tested through
  the internal types and S6a's files in memory. The agent program still runs nothing.
- **S6c, the loop.** Branch `epic-1/sprint-06c-agent-loop`, cut from `main` after S6b
  merges. The transport, the addresses of §11, the waits and read limits; pairing;
  numbered polls; outcomes sent until confirmed; the call sites of retention and of the
  reservation; `connection` and `bases`; the end-to-end tests against the server of S5;
  the in-process host; the Windows service; and the documents step.

Each closes with its own tests, implementation, code review, pull request and merge under
PQ1. S6a's part of the internal contract below was approved once; S6b's and S6c's part is
approved once, before S6b's tests. Each contract step puts its own part into code.

**Which lines of the Acceptance are S6a's.** In the order of the Acceptance:
- of the order of the entries, `received`, then the outbox file, then `ended`, then
  `confirmed`, through `CommandRecords`;
- after a restart, both lines as far as `Recover` records them; that the outbox file is
  sent is S6c's;
- of an outcome refused for its size, the outbox file and the second `ended` that
  `ReplaceForSize` writes; what triggers it is S6c's;
- of the poll numbers, the first four lines, through `PollNumbers` and starts of
  `AgentStart`, and the last, `newest` 2⁶², through `Answered` and `Next`;
- of the audit log, every line but "nothing runs before its `received` is on the disk";
  of the second copy, a second `AgentStart.Open` that gets `IOException` and records
  nothing; that the program then exits with an error is S6c's;
- of the outbox, every line;
- an invalid policy serves nothing and is recorded;
- of the retention, the first two lines, and of the third an answer whose `time` is more
  than a day away, through `RemoveExpired`;
- with `Requires=Windows`, DPAPI for the machine, and the access list that admits anybody
  else;
- the changes carried over from S2 to `AgentPolicy`, `AgentPolicyReader` and the audit
  log.

**Which are S6b's**, through `CommandChecks` and `WorkerPool` with the fake worker: of
rule 1, each check's code, the first of §7 when several fail, and no identity for
`signature` and `form`; of the poll answers, the verdicts of `CheckAnswer`; the
executor's object and its `size`; and workers and run limits. That the codes reach the
outcome and the log, and that a lost answer's commands are neither recorded nor run, is
S6c's. Every other line is S6c's, among them "a command that cannot be recorded does not
run", "nothing runs before its `received` is on the disk", the sum of `free`, the second
copy's exit, "nothing removed before the first verified poll answer of a run", the
cleared log end to end, the clock set behind the server's newest number, and the
documents step.

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
    - write the audit log and the outbox through a boundary that tests replace, so that the
      order of writes, a torn entry, a failed write and a torn outbox file can be tested;
    - keep the secrets, and check the access list of the data folder, through boundaries
      that Windows implements, with fakes elsewhere;
    - find a command in the audit log and its outcome in the outbox, after a restart
      included;
    - number polls so that the numbers grow across restarts, as concept rule 10 says: from
      the agent's clock, above the newest number that an answer to an older poll names,
      and within the reservation of §6.2 of `docs/protocol.md`, which the agent keeps in its
      audit log, the one place outside `outcomes\` that its account may write. The day of
      ticks that a reservation adds is a constant of the agent;
    - pair with a token, which the console of S10 calls (principle 12: the console depends
      on the agent).
  - *Internal, after the study* (the contract author, 22 November 2026, from the study's
    proposal A and cut B, with the coordinator's decisions under PQ2 written in as
    "Settled"; reviewed in two rounds and approved by the coordinator under PQ2, 22 November
    2026; the P1 of round 2 was closed by bringing back the counting of polls of round 1,
    which round 1 had reviewed, without a third round). A folder is a namespace
    `Moltaro.OnecOutpost.Agent.<Folder>` and one responsibility; every type is `internal`.
    Interfaces stand only at the boundaries the brief names: the files, the access lists,
    the protection of the secrets and the workers; the transport's boundary is a given
    `HttpMessageHandler`. Folders depend one way: `Audit` and `Outcomes` on `Files`;
    `Records` on both; `Polls` on `Audit`; `Start` on all of S6a; in S6b `Checks` on
    `Policy` and `Audit`, `Workers` on `Policy` and `Secrets`; in S6c `Pairing` on
    `Transport`, `Loop` on all of S6a and on `Transport`, `Checks` and `Workers`, and
    `Host` on all.

    ```csharp
    // ---- S6a ----

    // Moltaro.OnecOutpost.Agent
    internal sealed class AgentStopException : Exception
    {
        public AgentStopException(string reason);
    }

    // Moltaro.OnecOutpost.Agent.Policy: S1's records gain one member each
    internal sealed record AgentPolicy(int AuditRetentionDays, Uri? Proxy, TimeSpan RetryDelayMax, TimeSpan RequestTimeout, IReadOnlyList<AgentBasePolicy> Bases);
    internal sealed record AgentModePolicy(string Account, int Workers, TimeSpan RunLimit, bool SafeMode);

    // Moltaro.OnecOutpost.Agent.Files
    internal interface IAgentFolder
    {
        IReadOnlyList<string> Names();
        Stream OpenRead(string name);
        void Append(string name, ReadOnlySpan<byte> bytes);
        void Cut(string name, long length);
        void Replace(string name, ReadOnlySpan<byte> bytes);
        void Delete(string name);
        IDisposable Lock(string name);
    }
    [SupportedOSPlatform("windows")]
    internal sealed class DiskFolder : IAgentFolder
    {
        public DiskFolder(string path);
    }

    // Moltaro.OnecOutpost.Agent.Access
    internal interface IAccessLists
    {
        IReadOnlyList<string> Others(string path);
    }
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsAccessLists : IAccessLists
    {
        public WindowsAccessLists(string dataFolder);
    }

    // Moltaro.OnecOutpost.Agent.Secrets
    internal interface IMachineProtection
    {
        byte[] Protect(byte[] data);
        byte[] Unprotect(byte[] data);
    }
    [SupportedOSPlatform("windows")]
    internal sealed class MachineDpapi : IMachineProtection;
    internal sealed class AgentSecrets
    {
        public AgentSecrets(byte[] privateKey, IReadOnlyDictionary<(string Base, OutpostModeEnum Mode), string> passwords);
        public byte[] PrivateKey { get; }
        public IReadOnlyDictionary<(string Base, OutpostModeEnum Mode), string> Passwords { get; }
    }
    internal static class SecretsFile
    {
        public static AgentSecrets Read(byte[] file, IMachineProtection protection);
        public static byte[] Write(AgentSecrets secrets, IMachineProtection protection);
    }

    // Moltaro.OnecOutpost.Agent.Pairing: the file in S6a, the request in S6c
    internal sealed record AgentPairingSettings(Uri Server, Guid Agent, byte[] ServerKey, TimeSpan PollTimeout, TimeSpan LeaseGrace);
    internal static class AgentPairingFile
    {
        public static AgentPairingSettings Read(byte[] file);
        public static byte[] Write(AgentPairingSettings settings);
    }

    // Moltaro.OnecOutpost.Agent.Audit
    internal sealed record AuditCommand(Guid Id, OutpostModeEnum? Mode, bool Received, OutpostAgentOutcomeKindEnum? Kind, string? Code, long? DurationMs, bool Confirmed);
    internal sealed class AuditLog : IDisposable
    {
        public static AuditLog Open(IAgentFolder audit, TimeProvider clock);
        public string? Fault { get; }
        public IReadOnlyList<string> Files { get; }
        public IReadOnlyCollection<AuditCommand> Unconfirmed { get; }
        public AuditCommand? Find(Guid command);
        public long? HighestReservation(Guid agent);
        public void Started(string version, IReadOnlyList<string> withoutSafeMode, string? error);
        public void Policy(string? error);
        public void Connection(bool connected, string? reason);
        public void Bases(IReadOnlyList<string> held, IReadOnlyList<string> claimed);
        public void Received(OutpostCommandMessage command);
        public void Refused(Guid? command, string? baseKey, OutpostModeEnum? mode, string code);
        public void Ended(Guid command, OutpostAgentOutcomeKindEnum kind, string? code, long? durationMs);
        public void Confirmed(Guid command);
        public void Reservation(Guid agent, long below);
        public void RemoveExpired(Guid agent, DateTimeOffset answerTime, int retentionDays);
        public void Dispose();
    }

    // Moltaro.OnecOutpost.Agent.Outcomes: the outbox
    internal sealed class Outbox
    {
        public static Outbox Open(IAgentFolder outcomes);
        public IReadOnlyCollection<Guid> Commands { get; }
        public byte[] Read(Guid command);
        public void Put(Guid command, ReadOnlySpan<byte> outcome);
        public void Remove(Guid command);
    }

    // Moltaro.OnecOutpost.Agent.Records
    internal sealed class CommandRecords
    {
        public static CommandRecords Open(AuditLog log, Outbox outbox);
        public void Recover();
        public void Refuse(Guid? command, string? baseKey, OutpostModeEnum? mode, string code);
        public void End(Guid command, ReadOnlySpan<byte> outcome, long durationMs);
        public void ReplaceForSize(Guid command);
        public void Confirm(Guid command);
    }

    // Moltaro.OnecOutpost.Agent.Polls
    internal sealed class PollNumbers
    {
        public const long ReservationSpan = 864_000_000_000;
        public PollNumbers(AuditLog log, Guid agent, TimeProvider clock);
        public long Next();
        public void Answered(long? newest);
    }

    // Moltaro.OnecOutpost.Agent.Start
    internal sealed record AgentFolders(IAgentFolder Root, IAgentFolder Audit, IAgentFolder Outcomes);
    internal sealed record AgentRun(AgentPolicy Policy, AgentPairingSettings Pairing, AgentSecrets Secrets, AuditLog Log, Outbox Outbox, CommandRecords Records, PollNumbers Numbers);
    internal static class AgentStart
    {
        public static AgentRun Open(AgentFolders folders, IAccessLists access, IMachineProtection protection, TimeProvider clock, string version);
    }

    // ---- S6b ----

    // Moltaro.OnecOutpost.Agent.Checks: §7 and §6.2, concept rule 1
    internal enum CommandVerdictEnum { Runs, Refused, Known }
    internal sealed record CommandCheck(CommandVerdictEnum Verdict, Guid? Id, string? BaseKey, OutpostModeEnum? Mode, string? Code, OutpostCommandMessage? Command);
    internal sealed class CommandChecks
    {
        public CommandChecks(Guid agent, ECDsa serverKey, AgentPolicy policy, TimeProvider clock);
        public CommandCheck Check(OutpostSignedCommand command, AuditLog log);
        public IReadOnlyList<CommandCheck>? CheckAnswer(OutpostPollRequest poll, OutpostPollAnswer answer, AuditLog log);
    }

    // Moltaro.OnecOutpost.Agent.Workers: the lines of S9, concept rules 8 and 9
    internal sealed class WorkerConnectionLine // a class: it holds a password
    {
        public required string Location { get; init; }
        public required string User { get; init; }
        public required string Password { get; init; }
        public required bool SafeMode { get; init; }
        public required string Executor { get; init; }
    }
    internal sealed record WorkerCommandLine(string Script, string ParametersJson, string? CallerKey, long OutcomeLimit);
    internal interface IWorkerLauncher { IWorkerProcess Start(string baseKey, OutpostModeEnum mode); }
    internal interface IWorkerProcess : IDisposable
    {
        Task<bool> OpenAsync(WorkerConnectionLine connection, CancellationToken cancellationToken);
        Task<(long Size, byte[] Outcome)?> RunAsync(WorkerCommandLine command, CancellationToken cancellationToken);
        void Kill();
    }
    internal sealed record WorkerRun(byte[] Outcome, long DurationMs);
    internal sealed class WorkerPool : IAsyncDisposable
    {
        public WorkerPool(AgentPolicy policy, AgentSecrets secrets, IWorkerLauncher launcher, string executor, TimeProvider clock);
        public void Open();
        public int Free(string baseKey, OutpostModeEnum mode);
        public Task<WorkerRun> RunAsync(OutpostCommandMessage command, CancellationToken cancellationToken);
        public void Release(Guid command);
        public ValueTask DisposeAsync();
    }

    // ---- S6c ----

    // Moltaro.OnecOutpost.Agent.Transport: §4, §6, §9, §10, §11 and the `proxy` row
    internal static class ServerRoute
    {
        public static bool IsAllowed(Uri server);
        public static SocketsHttpHandler CreateHandler(Uri server, Uri? proxy);
    }
    internal sealed record PollResult(OutpostPollAnswer? Answer, string? Reason);
    internal enum OutcomeResultEnum { Confirmed, TooLarge, Lost }
    internal sealed class AgentConnection : IDisposable
    {
        public AgentConnection(Uri server, Guid agent, ECDsa agentKey, ECDsa serverKey, HttpMessageHandler handler, TimeProvider clock);
        public Task<PollResult> PollAsync(OutpostPollRequest poll, TimeSpan leaseGrace, TimeSpan requestTimeout, CancellationToken cancellationToken);
        public Task<OutcomeResultEnum> SendOutcomeAsync(Guid command, ReadOnlyMemory<byte> outcome, TimeSpan requestTimeout, CancellationToken cancellationToken);
        public void Dispose();
    }

    // Moltaro.OnecOutpost.Agent.Pairing: §6.1; it writes no file, the console of S10 writes agent.json and secrets.dat
    internal enum AgentPairingFailureEnum { Unreachable, Refused, ProtocolVersion, ServerKey, Signature, Answer }
    internal sealed class AgentPairingResult // a class: it holds the private key
    {
        public AgentPairingSettings? Settings { get; init; }
        public byte[]? PrivateKey { get; init; }
        public int? Status { get; init; }
        public AgentPairingFailureEnum? Failure { get; init; }
    }
    internal static class AgentPairing { public static Task<AgentPairingResult> PairAsync(Uri server, OutpostPairingTokenText token, string agentVersion, HttpMessageHandler handler, TimeSpan requestTimeout, TimeProvider clock, CancellationToken cancellationToken); }

    // Moltaro.OnecOutpost.Agent.Workers: the program's launcher, which S9b implements
    internal sealed class ComWorkerLauncher : IWorkerLauncher;

    // Moltaro.OnecOutpost.Agent.Loop: §6.2, §6.3, §9 and the Acceptance
    internal sealed class AgentLoop { public AgentLoop(AgentRun run, AgentConnection connection, CommandChecks checks, WorkerPool workers, TimeProvider clock, string version); public Task RunAsync(CancellationToken cancellationToken); }

    // Moltaro.OnecOutpost.Agent.Host
    internal sealed record AgentParts(AgentFolders Folders, IAccessLists Access, IMachineProtection Protection, IWorkerLauncher Workers, string Executor, Func<Uri, Uri?, HttpMessageHandler> Handlers, TimeProvider Clock, string Version);
    internal static class AgentExitCodes { public const int Stopped = 0, Error = 1, NotStarted = 2, NotService = 3, Usage = 4, NotWindows = 5; }
    internal static class AgentHost { public static Task<int> RunAsync(AgentParts parts, CancellationToken cancellationToken); }
    [SupportedOSPlatform("windows")]
    internal static class AgentService { public static int Run(Func<CancellationToken, Task<int>> run); }
    ```

    **S6a**, the behavior its tests state:

    - **One caller at a time.** `AuditLog`, `Outbox`, `CommandRecords` and `PollNumbers` are
      not thread-safe: in S6c one task makes every change of the agent's files, so that no
      lock is held across their I/O (principle 10). Each change is on the disk before its
      method returns, unless a bullet says otherwise.
    - **`AgentStopException`** carries the reason that `started` records, and lives in the
      root because every folder throws it. The reason names the cause and, for a file, its
      path in the data folder, such as `outcomes\{id}.json`; it never quotes a value read
      from a file.
    - **The policy.** `AgentPolicyReader` reads `requestTimeout` and `safeMode` of `write`
      (`true` when absent; `AgentModePolicy.SafeMode` of `read` is always `true`, and
      `safeMode` in `read` stays an unknown member), refuses more than 256 bases and a
      `proxy` whose URL has a user or a password, without repeating either, and names
      `http://proxy.example.com:3128` as its example, as "Carried over from S2" says. The
      start decodes `policy.json` as UTF-8, skipping a byte order mark; bytes that are not
      UTF-8 make the file invalid, with an empty path.
    - **`IAgentFolder`** is one folder of the data folder, by file name, without subfolders.
      `Names` lists its files. `OpenRead` reads a file from its start, sharing reading and
      writing. `Append` adds the bytes at the end, creating the file when it is absent.
      `Cut` sets the file's length. `Replace` writes the bytes to `{name}.tmp`, flushes it to
      the disk and renames it over `name`, the rename through to the disk. `Delete` removes
      a file, absent or not, and need not reach the disk. `Lock` opens the file, creating
      it, without sharing until it is disposed. Every failure is an `IOException`;
      `DiskFolder` turns `UnauthorizedAccessException` into one, and nothing retries.
    - **`DiskFolder`** runs on Windows only. It writes through `FileStream` with
      `Flush(true)`, opens the files it writes sharing reading only, and renames with
      `MoveFileExW` and `MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH` through
      `LibraryImport`. Its tests have `Requires=Windows` and run in the `windows` job; the
      agent's logic is tested on every platform with the folder in memory. *Limitations:*
      a same-volume rename rests on the journal of NTFS, and no test can show a loss of
      power; a program that holds an audit file open without sharing writing fails the
      agent's next write, and the agent stops (§13).
    - **`IAccessLists.Others(path)`** takes a path relative to the data folder, `""` for the
      folder itself, such as `audit` or `audit\2026-11-22.jsonl`. It gives the accounts,
      other than Administrators, SYSTEM and the account the agent runs under, that own the
      entry or that an allow entry applying to the entry itself gives any right; empty when
      none; `IOException` when it cannot read the list. `WindowsAccessLists` reads them with
      `FileSystemAclExtensions`, names each account as Windows translates its SID, or by the
      SID, and takes the agent's account from `WindowsIdentity.GetCurrent()`:
      `NT SERVICE\MoltaroOnecOutpost` as a service. *Limitation:* an administrator's own
      account, as the owner of a file or in an entry, counts as somebody else, so a file that
      an administrator's account owns stops the agent until Administrators own it.
    - **`IMachineProtection`.** `Protect` encrypts for the machine and `Unprotect` reverses
      it; both throw `CryptographicException` when they cannot, such as for a file copied
      from another machine. `MachineDpapi` calls `CryptProtectData` and `CryptUnprotectData`
      with `CRYPTPROTECT_LOCAL_MACHINE | CRYPTPROTECT_UI_FORBIDDEN`, without entropy, and
      frees every blob the system returns.
    - **`AgentSecrets`** is a class, not a record, so that nothing prints a password.
      `PrivateKey` is the PKCS#8 form of the agent's ECDSA P-256 key; `Passwords` are by
      base key and mode, as the console's `secret` writes them, an empty password being a
      password. `SecretsFile.Write` gives `Protect` over
      `{"key":…,"passwords":[{"base":…,"mode":"read","password":…},…]}`, written by the
      writer of §2 with the key in base64url; `Read` reverses it. Its
      `CryptographicException` passes; content that breaks this form, a base key against
      its rule or a base and mode given twice is `FormatException`, quoting nothing.
    - **`AgentPairingFile`.** `agent.json` is
      `{"server":…,"agent":…,"serverKey":…,"pollTimeoutMs":…,"leaseGraceMs":…}` in the forms
      of §2: an absolute `http` or `https` URL, an identity, the 91-byte key in base64url,
      and the two durations in the ranges of `PollTimeout` and `LeaseGrace`. `Write` writes
      it with the writer of §2; `Read` takes exactly these members and throws
      `FormatException`, quoting nothing, for anything else. The rule of §11 for the
      address is the transport's (S6c).
    - **`AuditLog.Open`** takes `lock` of the folder, and its `IOException` passes: a second
      copy, or a missing `audit\`, records nothing. It then reads every file named
      `YYYY-MM-DD.jsonl`, in the order of their names, line by line to each LF; it ignores
      other names.
      - First, in every file, a last line without its LF, or one with it that is not JSON,
        is cut off with `Cut`, whose `IOException` passes; only then are lines parsed.
      - A line is parsed with the depth of a message, 72 (`OutpostProtocol.MaxMessageDepth`),
        not the default 64, so that a `received` whose parameters nest 64 levels, 65 inside
        the entry, is read, also as the last line. No line has a limit of length.
      - Any other line that is not an object with `time` and an `entry` of the table whose
        members below are in their forms, and a file that `OpenRead` cannot open, set
        `Fault` to a reason naming the file and the line, and reading stops there. The log
        reads `time` of every line; `command` and `mode` of `received`; `command`, `mode`
        and `code` of `refused`; `command`, `outcome`, `code` and `durationMs` of `ended`;
        `command` of `confirmed`; `agent` and `below` of `reservation`. It reads no other
        member.
      - `Files` lists the names it read.
    - **Writing the log.** Each writer adds one entry: one JSON object by the writer of §2,
      `time` from the clock in the form of §2, `entry`, its members as the table and the
      drafts below give them, and LF last, with one `Append` to the file of the UTC day of
      that `time`. `received.parameters` is written again with `OutpostJsonWriter.Value`;
      `policy.valid` is `true` exactly when `error` is null; `connection.state` is
      `connected` or `lost`; `ended.outcome` is `succeeded`, `failed` or `unknown`.
      - An `IOException` of `Append` makes the log cut the file back to its length before
        that write, set `Fault`, and throw `AgentStopException`. An exception of that cut is
        not reported: every entry is safe to find written at the next start, which cuts a
        torn one.
      - With `Fault` set, every writer but `Started` writes nothing and throws
        `AgentStopException`; after a failed write, `Started` too.
      - Each write updates what `Find`, `Unconfirmed` and `HighestReservation` give.
    - **`Find`** gives the facts of a command that any entry names, `received`, `refused`
      with a command, `ended` or `confirmed`, and null for any other: `Mode` from its
      `received` or `refused`; `Received`; `Kind` and `Code` of its last `ended` or `refused`,
      last in the order of file names and lines, with `DurationMs` of that `ended`; and
      `Confirmed`. Only that last one depends on the order of the files, which the clock can
      upset, and nothing decides by it but the start's reconciliation with the outbox file;
      every other fact is a set or a maximum. `Unconfirmed` gives every command that an
      entry names and no `confirmed` does: the `received` list of a poll (§6.2).
    - **`HighestReservation(agent)`** is the greatest `below` of the reservations of that
      identity, in whichever files they are, and null when there is none.
    - **`RemoveExpired(agent, answerTime, retentionDays)`** holds the whole rule of retention;
      S6c calls it after each verified poll answer, with that answer's `time`:
      - it removes nothing when the clock is more than a day away from `answerTime`, either
        way;
      - otherwise it deletes, in the order of their names, every file whose last entry's
        `time` is more than `retentionDays` plus a day before the clock, and every file
        that holds no entry, except a file that holds an entry of a command `Unconfirmed`
        gives, and the file that holds the highest reservation of `agent`;
      - no command is left with its `received` or `ended` and without its `confirmed`: a
        file stays while it holds the `confirmed` of a command that has an entry in a file
        that stays, so a `confirmed` is deleted after the other entries of its command; a
        `received` or `ended` may go while its `confirmed` stays (the coordinator, 22
        November 2026, from S6a's code review, rounds 1 and 2);
      - a file it cannot delete stays until a later call, and nothing is recorded;
      - afterwards `Find` no longer knows a command none of whose entries is left.
    - **`Outbox.Open`** deletes every file whose name ends with `.tmp`, torn or left by a
      stop, and lists the files named `{id}.json`, the identity in the form of §2. Any other
      name throws `AgentStopException` naming the file. `Read` gives a file's bytes, `Put`
      writes `{id}.json` with `Replace`, and `Remove` deletes it. Any `IOException` of the
      outbox throws `AgentStopException`: a failed read, write or removal of the outbox
      stops the agent as a failed write of the log does.
    - **`CommandRecords`** is the one place for the order of the records that touch both
      the log and the outbox (rules 2, 3 and 11); the loop writes `received` with
      `AuditLog.Received`. An outcome object that the agent writes itself is
      `OutpostCodec.Write` of one with no result, no error text and no messages.
      - `Refuse` writes `refused`, then, for a command with an identity, the file of its
        Refused outcome with the code. `signature` and `form` have no identity and write
        only the entry.
      - `End` writes the file, then `ended` with the kind and the code that
        `OutpostCodec.ReadOutcome` reads from the bytes for the command's mode, and
        `durationMs`. The bytes are an outcome that the loop checked by §8; a `form` from
        the reader is a defect and passes.
      - `ReplaceForSize`, for a command whose outbox file is of kind `succeeded` or
        `failed`, writes the file of Unknown `result-too-large` for a Write or Failed
        `result-too-large` for a Read, then a second `ended` with it and the `durationMs`
        that `Find` gives.
      - `Confirm` writes `confirmed`, then removes the file.
      - `Open` checks what a restart left and writes nothing. It throws
        `AgentStopException` for an outbox file that `ReadOutcome` refuses for the mode of
        its command, either mode when no entry names one; for a file of a command that no
        entry names; for a file of kind `refused` that does not match a `refused` of its
        command, or of another kind for a command that only a `refused` names; and for a
        command, not confirmed, that has an `ended` and no file.
      - `Recover`, called after `started` and `policy`, writes for each command what the
        restart rules need: the file of a confirmed command is removed; a file whose kind
        and code differ from the last `ended` or `refused` of its command, or that has
        neither, gets an `ended` that matches it, with the `durationMs` of that last `ended`
        or null; a `refused` with an identity, not confirmed, without its file, gets its
        file again from its code; a `received` with neither file nor `ended` gets the file
        of Unknown `agent-stopped` for a Write or Failed `lost` for a Read, then its `ended`
        with `durationMs` null (rule 3). A stop in the middle leaves what the next start
        finishes. Afterwards every outbox file belongs to a command that is not confirmed
        and matches its last entry, and S6c sends each.
    - **`PollNumbers`** (rule 10, §6.2 of `docs/protocol.md`):
      - The constructor reads `HighestReservation(agent)`; one above 2⁶² throws
        `AgentStopException` with a reason that names both causes, a cleared log that a
        first poll found and numbers used up.
      - The run is *counting* when its log holds no reservation of the identity, until
        `Answered(null)`. `Next` then gives 1, and then one above the last. Otherwise it
        gives the greatest of the clock's UTC ticks, one above the last number it gave, one
        above the greatest `newest` given to `Answered`, and, for the first number of the
        run, the highest reservation.
      - Before it gives a number at or above the `below` of the last reservation, or any
        number while no reservation of the identity is known, `Next` records `reservation`
        with `below` that number plus `ReservationSpan`. When the number is above 2⁶², it
        records that reservation and throws `AgentStopException`.
      - `Answered(newest)` comes with every verified poll answer that is not lost: null for
        an ordinary answer, which ends counting, and the `newest` of an answer to an older
        poll. Such an answer while counting is a cleared log, since one poll is open at a
        time and the last number given is a counted one: it records a reservation with
        `below` 2⁶² + 1 and throws `AgentStopException`. Otherwise the numbers after it are
        above `newest`.
      - *Costs:* a poll that a hop retries or duplicates while counting is answered as older
        and stops the agent until it is paired anew, a needless stop and never a double
        run; a restart before any answer is not lost numbers from the clock, since the log
        then holds a reservation, and a cleared log then goes unseen.
    - **`AgentStart.Open`** runs these steps, and the first that fails decides:
      1. `AuditLog.Open`; its `IOException` passes, and nothing is recorded;
      2. a `Fault` of the log;
      3. the access lists of `""`, `audit`, `outcomes`, each of `policy.json`, `agent.json`
         and `secrets.dat` that the root lists, every file in `Files`, and every file that
         the outcomes folder lists. One that admits somebody else fails with the path and
         the accounts; an `IOException` of the list fails too;
      4. `agent.json` with `AgentPairingFile.Read`, and `secrets.dat` with
         `SecretsFile.Read`: an absent file, its `IOException`, `FormatException` or
         `CryptographicException` fails, naming the file; an absent `agent.json` says that
         the agent is not paired;
      5. `Outbox.Open`, `CommandRecords.Open` and the `PollNumbers` of the identity in
         `agent.json`, and their `AgentStopException`;
      6. `policy.json`: one that cannot be read, or that `AgentPolicyReader` refuses, fails
         and is recorded as `policy` with `valid` false and its error after `started`; a mode
         that the policy serves with no password in the secrets fails, naming the base and
         the mode.

      A failed step after the first records `started` with the error and `withoutSafeMode`
      empty, disposes the log and throws `AgentStopException` with the same reason. When
      every step passes, the start records `started` with `withoutSafeMode` the keys of the
      bases whose `write` has `SafeMode` false, in the order of the policy, records `policy`
      as valid, calls `Recover` and returns the run. `version` is the agent's version as
      given; the caller disposes `Log`, which releases the lock.
    - **The fakes of S6a's tests**, in `Moltaro.OnecOutpost.Agent.Tests`: a folder in memory,
      seeded by the test, whose folders of one data folder share one list of operations, in
      which each change is a line, so that a test asserts the order of writes; that can
      fail its next write after writing some of its bytes, which gives a torn entry and a
      failed write at once; that can fail a `Cut`, a `Replace` or a `Delete`, and an
      `OpenRead` of one name; and whose lock fails while another holds it. Access lists that
      answer by path; a protection that changes the bytes reversibly, so that no plaintext
      is in the file, and that can refuse to unprotect. `MachineDpapi` and
      `WindowsAccessLists` are tested with `Requires=Windows`. `FakeClock` gains a
      constructor that takes its start time, since a restart with the clock set back is a
      new start with a clock of its own.

    **S6c**, decided with S6a's contract:

    - **Free 0 until S9b.** The program's launcher starts no process until S9b, and its
      `OpenAsync` gives `false`, so every mode declares `free` 0.
    - **`TooLarge`.** A verified `result-too-large` or a bare 413 to an `outcome` gives
      `TooLarge`, which replaces the outcome once with `ReplaceForSize` when its outbox file
      is of kind `succeeded` or `failed` and its code is not `result-too-large`; an outcome
      with that code counts as already replaced, and for it any answer but a verified `200`
      is lost (§9). *Limitation:* an executor's own Failed `result-too-large` refused by a
      hop's 413 is sent again until the hop lets it through.
    - **One task writes the files.** Every change of the audit log and the outbox comes from
      one task of the loop, while requests and runs may wait meanwhile (principle 10).
    - **`SERVICE_STOPPED` with the exit code.** `AgentService` reports it on every stop, so
      that the failure actions, which restart only a process that ends without reporting,
      leave a deliberate stop with an error stopped.
    - **The loopback server of S5's tests** moves to `Moltaro.OnecOutpost.TestSupport`, since
      two test projects then need it.

    **S6b and S6c**, settled in S6b's contract step (the contract author, 22 November 2026,
    from S6b's study, with the coordinator's decisions under PQ2 of that day; reviewed in
    two rounds and approved by the coordinator under PQ2, 22 November 2026). The behavior
    their tests state:

    - **`CommandChecks.Check`** (S6b) follows §7: `ReadCommand`, whose `signature` and
      `form` give `Refused` without an identity; with an identity, `Known` when `Find`
      knows it, whatever the rest of the bytes; then the codec's `protocol-version` and
      `malformed`, with the identity alone; then `not-addressed`, `not-served` (no base of
      that key, or a Write where `write` is absent), `clock-skew` (`issued` later than the
      clock plus `skewMs`) and `expired` (the clock later than `deadline` plus `skewMs`),
      with identity, base and mode; otherwise `Runs` with the command. It records nothing.
    - **`CheckAnswer`** (S6b) checks every command of an answer that `ReadPollAnswer`
      read, and gives null, the answer lost, when two commands with an identity share it,
      whatever their verdicts, or when those that `Runs` exceed, for a base and mode, the
      `free` that `poll` declared; otherwise the checks in the order of the answer. Reason
      for "whatever": the server never sends two, and one comparison keeps "never twice
      from one answer" (§6.2).
    - **The lines** (S6b) are S9's. `Location` is `Srvr="{server}";Ref="{database}";` or
      `File="{file}";`, each `"` in a value doubled; `User` the mode's `Account`;
      `Password` from `Passwords`; `SafeMode` the mode's; `Executor` the pool's path.
      `ParametersJson` is `Parameters.GetRawText()`, `OutcomeLimit` the command's.
    - **`IWorkerProcess`** reports failures as results, by no exception but
      `OperationCanceledException`: `OpenAsync` gives `ready`; `RunAsync` the executor's
      `size` with the exact bytes of its `outcome`, or null when the process ended or broke
      its line first; `Kill` ends the process, completes a pending `OpenAsync` with false
      and a pending `RunAsync` with null, may run beside either and beside another `Kill`,
      from any thread, and is idempotent (the coordinator, 22 November 2026, from S6b's
      code review, round 1).
    - **`WorkerPool`** (S6b; rules 8, 9):
      - `Open` starts the opens of the `workers` of every mode and returns at once. Each
        races `Task.Delay(runLimit, clock)`: the mode's run limit is its deadline
        (principle 10, concept §5), and a late worker is killed and, like one that gives
        false, never free. So the loop starts at once, and a slow base holds up no other.
      - `Free` counts a mode's workers whose open ended with true and that hold no
        command. A command holds one from `RunAsync`, called right after its `received`,
        to `Release`, called after `End`: it counts from its acceptance until its outcome
        is recorded.
      - `RunAsync` takes such a worker before it returns. With none, it gives Failed
        `base-unreachable` at once, in either mode, as the script never started (§8); in
        S6b only a test reaches this, from S9b a worker that ends while idle too.
      - A run races `RunAsync` with `Task.Delay(runLimit, clock)`. When the delay wins:
        `Kill`, a wait for the run's end, and `run-limit`, whatever the run then gives.
        Otherwise null gives `connection-lost`; a `size` other than the length of the bytes
        (S9's requirement), bytes that `ReadOutcome` refuses for the mode, and a kind and
        code that `IsExecutorOutcome` refuses give `result-type`; anything else goes byte
        for byte. Each code is Failed for a Read and Unknown for a Write, written as
        `CommandRecords` writes its own; S9b relaxes `IsExecutorOutcome` for the worker's
        codes. `DurationMs` is the clock's time from the call to the outcome.
      - A worker is dropped after null, after a kill, and after it gave `base-unreachable`
        or `connection-lost`, after which S9's worker ends; until S9b starts it again with a
        growing delay (concept §5), its mode has one worker fewer.
      - Cancellation kills the worker, waits for its run and throws, recording nothing
        (rule 3 at the next start). `DisposeAsync` kills and disposes every worker.
      - Not thread-safe, and no lock: the loop's task calls it, a run's task changes only
        its own worker, and `Release` reads what that left after the loop awaited the run.
        An open's task sets only its worker's opened flag, with `Volatile.Write`, and the
        loop reads it with `Volatile.Read`: the flag only goes from false to true, so a
        late read under-declares `free` and is safe.
    - **The fake of S6b**, in `Moltaro.OnecOutpost.Agent.Tests`: a launcher whose processes
      write each `Start`, `Open`, `Run` and `Kill` as a line of the operation list of the
      folders in memory; whose `OpenAsync` gives true, false or never answers; and whose
      runs give an outcome with its true size or one off, null, or wait for `Kill`.
    - **`ServerRoute`** (S6c), renamed from `AgentHandler`, which named the mechanism: it
      is the way to the server (`AGENTS.md`, naming).
      - `IsAllowed` (§11) accepts an absolute `https` address, and an `http` one whose
        `Uri.Host` is `localhost`, an address in `127.0.0.0/8` with `HostNameType` `IPv4`,
        or `[::1]` with `HostNameType` `IPv6`; nothing is resolved. So `loopback`,
        `127.1` and `2130706433` count, and `localhost.` and `foo.localhost` do not. Reason
        (PQ2): `Uri` normalises before anyone reads the host, each form it maps there is
        loopback, and one rule on its result keeps §11.
        `AgentPairingFile` now refuses any other address, so a start records it.
      - `CreateHandler` (PQ2, as the study measured) gives a `SocketsHttpHandler` with
        `AllowAutoRedirect` false, no decompression, `MaxResponseDrainSize` 0, no cookies,
        no check of revocation, the 1-second `Expect100ContinueTimeout` of .NET (X2),
        `UseProxy` only for an `https` server with a proxy named, so never the system's,
        and no credentials for the proxy or the server; `ArgumentException` for an address
        that `IsAllowed` refuses.
    - **`AgentConnection`** (S6c) owns its handler through `HttpMessageInvoker` and sends
      HTTP/1.1 only, the headers of §4, `Accept-Encoding: identity`, and on `outcome`
      `Expect: 100-continue`.
      - A poll waits for its answer to begin for its `pollTimeout` plus `leaseGrace`, then
        reads the body within `requestTimeout` and 64 MiB; `outcome` and `pair` wait
        `requestTimeout` over both and read within 64 KiB. Each wait is a
        `CancellationTokenSource` on the clock; a body beyond its limit is lost unread.
      - A poll answer verified by §9 and `ReadPollAnswer`, with the sum of the poll's
        `free`, is `Answer`; any other gives the reason of §9. A tunnel the proxy
        refuses throws `HttpRequestException` with its status, a status that came:
        `status-407`.
      - `SendOutcomeAsync` gives `Confirmed` for a verified `200`; `TooLarge` for a verified
        `result-too-large` and for a 413 without an `Outpost-Signature` that verifies,
        whatever its body; `Lost` otherwise.
    - **`AgentPairing.PairAsync`** (S6c) makes a P-256 key, sends `pair` with the proof and
      reads the answer with `ReadPairAnswer`: `Settings` and the key in PKCS#8 for a
      verified `200`; otherwise the status when one came, and `Unreachable` without one,
      `Refused` for a bare 403, `ProtocolVersion` for the unsigned body of §9, `ServerKey`
      and `Signature` for the codec's codes, `Answer` for anything else.
    - **`AgentLoop.RunAsync`** (S6c) ends by `OperationCanceledException` or
      `AgentStopException`. Its one task awaits whichever of the poll, the one outcome
      request, the runs and the delays ends first.
      - A poll has `Next`, the poll timeout last learnt, the policy's bases with `Free` and
        `runLimit`, and `Unconfirmed` as `received`. Where the sum of `free` would pass the
        room left, the modes keep theirs in the policy's order, read before write, and the
        rest declare 0. It opens once the last answer's commands are recorded (rule 10).
      - A lost answer, one that `CheckAnswer` refuses included (`status-200`), records
        `connection` `lost` when the state is not, and nothing else: no `Answered`, no
        command, no `bases`, no retention. Then a growing delay: a tenth of
        `retryDelayMax`, doubled up to it, reset by an answer that is not lost.
      - Any other answer: `connection` `connected` when needed; `Answered(newest)`; its
        `pollTimeoutMs` and `leaseGraceMs` kept; each command in order, `Refuse`, or
        `Received` then `RunAsync`, or nothing for `Known`; `bases` when it differs from the
        run's last; `RemoveExpired` with its `time`. After an older poll's answer, a tenth
        of `retryDelayMax`. A run that ends gets `End`, then `Release`.
      - Outcomes go one at a time, the files in turn, the start's first. `Confirmed` calls
        `Confirm`. `TooLarge` calls `ReplaceForSize` when the outbox file is of kind
        `succeeded` or `failed` with a code other than `result-too-large`, and the
        replacement goes next; for any other it is lost. After a lost answer the next file
        waits a growing delay of its own, so an outcome that stays lost holds the others up
        one delay per turn.
    - **`AgentHost.RunAsync`** (S6c): `AgentStart.Open`, whose `IOException` gives
      `NotStarted` and `AgentStopException` `Error`; the keys; the pool, its opens
      started; the connection over `Handlers(server, proxy)`; the checks; the loop, which
      gives `Stopped` for the token and `Error` for `AgentStopException`. It disposes the
      pool, the connection and the log, in that order. It meets a P4 of S6a: `SecretsFile.Read`
      refuses a key that is not P-256 in PKCS#8 (`FormatException`).
    - **`AgentExitCodes`** (S6c): `Stopped`, a cancellation while workers open included;
      `Error`, a stop of the agent's own (`AgentStopException`), recorded when the log can
      be written; `NotStarted`, the log not opened, a second copy included, nothing
      recorded; `NotService`, `service` outside the service controller; `Usage`, any
      arguments but `service`; `NotWindows`.
    - **`Program`** (S6c; PQ2) gives `NotWindows` unless `OperatingSystem.IsWindows()`,
      which satisfies CA1416 and keeps the assembly testable everywhere; then `Usage` unless
      its one argument is `service`, so a console run never waits on the service
      controller; then `AgentService.Run` of `AgentHost.RunAsync` with the machine's parts,
      `ComWorkerLauncher`, `OutpostExecutor.epf` beside the program and `CreateHandler`.
    - **`AgentService.Run`** (S6c; PQ2): the dispatcher, the handler and `SetServiceStatus`
      by `LibraryImport`, the entry points `static` and `[UnmanagedCallersOnly]`, catching
      everything, so no delegate lives. Error 1063 gives `NotService` before the data folder
      is touched. `ServiceMain` reports `SERVICE_RUNNING`, accepting stop and shutdown, and
      runs `run` on its thread; stop and shutdown report `STOP_PENDING` and cancel from the
      thread pool; `SERVICE_STOPPED` comes last, with 0 or `ERROR_SERVICE_SPECIFIC_ERROR`
      and the code. The wait hint of `STOP_PENDING` is no wait of the agent: no option.
    - **S6c's tests** supply `AgentParts.Handlers`: `CreateHandler` that accepts the test
      certificate. `agent.json` names `https://outpost.test:{port}/outpost/` (RFC 6761,
      never resolved), `policy.json` the test proxy at `http://127.0.0.1:{port}`. The proxy,
      in `TestSupport` beside the loopback server, answers CONNECT with `200`, ends TLS for
      `outpost.test` with a certificate made by `CertificateRequest` and pipes to Kestrel,
      opening TLS as a hop does; asked to, it answers 407 with `Negotiate` and `NTLM` and
      records every `Proxy-Authorization`. The loopback server can answer `outcome` with a
      bare 413 and an HTML body before its endpoints. A fake handler gives the rest.
    - **Manual acceptance of the service** (S6c), on a lab or test machine, as
      administrator, nothing to Git: `sc create` with `service` under a virtual account
      granted the data folder; `sc start` gives `RUNNING` and `started`, `sc stop` `STOPPED`
      with 0; without `agent.json`, `STOPPED` with 1066 and `Error`, kept stopped by the
      failure actions; `taskkill /f` is restarted by them; `sc delete`.
    - **Planned, when a domain lab exists:** the proxy offered the computer's credentials
      by Kerberos alone, by a CONNECT of the agent's own in a `ConnectCallback`, with one
      token from `NegotiateAuthentication` of package `Kerberos` (study of S6b, §1).

    **Settled** (the coordinator, 22 November 2026, under PQ2, with their reasons):

    - **DPAPI and the Windows service without new packages.** `ProtectedData` and
      `ServiceBase` are not in the shared framework of .NET 10, and principle 12 gives the
      agent only the protocol. The agent calls `CryptProtectData` and `CryptUnprotectData`
      behind `IMachineProtection` (S6a), and the service control dispatcher behind
      `AgentService` (S6c), through `LibraryImport`, which generates its marshalling, so no
      reflection; both are marked `[SupportedOSPlatform("windows")]`, and the agent's project
      allows unsafe code for the generated marshalling. The access lists use
      `System.Security.AccessControl` of the shared framework. Reason: principle 12 holds
      with no change of a rule.
    - **`started` lists the bases that write without safe mode** in a new member
      `withoutSafeMode`. Reason: "Carried over from S2" asks for it and names no member.
    - **`refused` first, then its outbox file**, and a start that finds the entry without its
      file and without its `confirmed` writes the file again. Reason: a Refused outcome is
      wholly given by its code and identity, so the start can finish it rather than stop.
    - **The study's three points**: the log is read with the depth of a message; nothing
      but the start's reconciliation depends on the order of the audit files; the outbox
      renames through to the disk. Reason: a reader that took a deep `received` for a torn
      line would cut a valid entry; the clock can put an `ended` in a file named before its
      `received`; and a start that finds an `ended` with neither file nor `confirmed` stops
      until the agent is paired anew, which a rename lost to a loss of power would cause.
    - **An invalid policy stops the agent** (left open for S6b, settled here because the
      start is S6a's): `started` with an error, then `policy` with `valid` false. Its outbox
      waits on the disk for a start with a valid policy; the server meanwhile ends its taken
      commands by the lease and the run limit, and the outcomes sent later replace those
      (rule 11). Reason: the simplest answer that keeps the concept. An invalid file names
      no proxy, so a poll would reach the server by a path the administrator did not
      choose; a poll that declared no bases would let the application release the agent's
      keys (rule 14) over a fault of a file; and a service that stops is what the
      administrator sees.
    - **A served mode without its password stops the start.** Reason: the console's
      `secret` writes the passwords after `pair`, and a start that runs without one would
      only fail later, in every command of that mode.
    - **A failed read, write or removal of the outbox stops the agent**, like one of the
      log; the start removes the file of a confirmed command, and stops on a file of a
      command the log does not name. Reason: one rule for every change of the agent's
      journal, and no file can then outlive its command's entries.
    - **`durationMs` is null in an `ended` that a start writes.** Reason: the start did not
      see the run, and a made-up duration would be an approximate record.
    - **Counting polls until an answer is not lost** (§6.2, a draft). Reason: correctness
      over simplicity; with the first answer lost, a poll from the clock would hide a cleared
      log, and a handed-out Write not yet listed would be handed out again or expire and
      free its key. The guarantee of rule 3 under §13 outweighs a needless stop.
    - **The exception to principle 10**, the one-second wait for `100 Continue`, stays as
      "Carried over from S2" has it, as X2 of the register.
    - **The proxy gets no credentials** (a draft below). Reason: S6b's study showed that
      `SocketsHttpHandler` cannot offer Kerberos without NTLM inside Negotiate, and a
      CONNECT of the agent's own with a Kerberos token is a strict-zone mechanism that only
      a domain lab can prove. With no credentials, "never NTLM" holds; a proxy that asks
      for authentication answers 407, and the agent is not connected through it. The
      Kerberos design is planned, above.
    - **The contract step of S6a** puts into code every type of S6a above, each member
      throwing `NotSupportedException` but `AgentPolicyReader`, which keeps working and gives
      the defaults of the new members. **The contract steps of S6b and S6c** add their
      types, as the code block marks them, the same way. The agent program still does
      nothing until S6c's implementation.
    - **Edits that state nothing new** in S6a's contract step: `AgentPolicyReader` passes
      `RequestTimeout` 5 minutes and `SafeMode` `true`; the expected values of
      `PolicyValuesTests` construct `AgentModePolicy` with `SafeMode: true`. In S6a's tests
      step: `FakeClock` gains its constructor, and the agent's test project references
      `Moltaro.OnecOutpost.TestSupport`. In S6b's contract step: three comments of S6a's
      tests that give the loop's work to S6b give it to S6c. In S6c's tests step: the
      loopback server and its helpers move to `Moltaro.OnecOutpost.TestSupport`, and the
      server's tests use them from there.
    - **The documents steps.** S6a's: `README.md` says that the agent keeps its files and
      still runs nothing; "The files of the agent" takes the changes carried over from S2
      and the drafts below; §6.2 and §13 of `docs/protocol.md` take the drafts on counting,
      on a failed write and on moving `outcomes\` aside, §13's "is found at the first poll"
      included. S6b's: nothing that a reader sees changes, so the check alone. S6c's: the
      line of the Acceptance; §9 and §11 of `docs/protocol.md` take the drafts of S6c
      below, which the `proxy` row has already; `README.md` says that a proxy that asks for
      authentication is not supported yet.
    - **Effort:** the test author and the implementer on `xhigh` in every part, since the
      audit log, the secrets, the command checks, pairing and the transport are strict
      zones.
- **What S6 changes in the public contract, as a draft for the owner** (the contract
  author, 22 November 2026). Each holds until the owner overturns it:
  - `started` gains `withoutSafeMode`: the keys of the bases whose Writes run without safe
    mode, an array, empty when there is none and when `started` has an error;
  - `refused` is written before its outbox file, and a start that finds a `refused` of a
    command, not confirmed, without its file writes the file again from its code;
  - `durationMs` of `ended` is `null` in an `ended` that a start writes, for a received
    command that becomes Unknown `agent-stopped` or Failed `lost` and for an outbox file
    without its `ended`; a second `ended` for the size carries the duration of the first;
  - at a start, the outbox file of a confirmed command is removed, and a file of a command
    that the audit log does not name stops the agent as a file it cannot read does; a
    failed read, write or removal of an outbox file stops the agent as a failed write of
    the log does;
  - an invalid `policy.json` stops the agent, recorded as `started` with an error and
    `policy` with `valid` false, and nothing is polled until a start with a valid policy; a
    mode that the policy serves without its password in `secrets.dat` stops it too;
  - `policy.json` is read as UTF-8, a byte order mark skipped and not counted in the
    columns of an error; bytes that are not UTF-8 make it invalid, with an empty path;
  - §13: after a failed write the next start finds the log as it was before that write, or
    with that entry whole when cutting it back failed too; every entry is safe to find
    written;
  - §13: the administrator who moves `audit\` aside to pair the agent anew moves
    `outcomes\` aside with it, since an outbox file that no entry names stops the start;
  - §6.2 and §13: an agent with no reservation of its identity numbers its polls 1, 2, 3, …
    until an answer is not lost, and an answer to an older poll in that time is the cleared
    log. A poll that a hop retries or duplicates while counting is answered as older and
    stops the agent until it is paired anew: a needless stop, never a double run. A restart
    before any answer is not lost still leaves a cleared log unseen;
  - S6c (the coordinator, 22 November 2026, under PQ2): the `proxy` row and §11: the agent
    offers its proxy no credentials, by no scheme, so never NTLM; a proxy that asks for
    authentication answers 407, the poll is lost with `status-407` (§9: the status of a
    proxy's answer to CONNECT is a status that came), and the agent is not connected
    through it. Kerberos alone is planned for when a domain lab can prove it;
  - S6c, §11: the host of an `http` address is judged as .NET's `Uri` writes it, so
    `loopback`, `127.1` and `2130706433` count as loopback, and `localhost.` and
    `foo.localhost` do not;
  - S6c: the agent program takes one argument, `service`, and exits with `AgentExitCodes`:
    0 stopped, 1 a stop of the agent's own, recorded when the log can be written, 2 the log
    not opened (a second copy), 3 not started by the service controller, 4 other
    arguments, 5 not Windows; as a service it
    reports a code other than 0 as `ERROR_SERVICE_SPECIFIC_ERROR` with that code;
  - S6c, the `runLimit` row: the run limit of a mode is also the deadline for its workers
    to open, and a worker that opens later is ended and never runs a command.
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
  - Every check of rule 1, each with its code in the outcome and in the audit log, and,
    when several fail, the code of the first in the order of §7 of `docs/protocol.md`. A
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
  - The table of §9 of `docs/protocol.md`, one test per row, and a lost answer for each case
    of its list. For a replacement, any answer but a verified `200` counts as lost, and an
    outcome of kind `refused` or `unknown` is never replaced.
  - The `connection` entries: one per change of the state of the polls, with each kind of
    `reason` of §9; an outcome whose answers keep being lost while polls go through
    records no entry.
  - The waits and the read limits:
    - a poll answer beyond 64 MiB, and any other answer beyond 64 KiB, are lost unread;
    - the wait for a poll answer to begin, and the reading of its body within
      `requestTimeout`;
    - the waits for an outcome and a pair, with the fake clock;
    - every poll carries the poll timeout the agent last learnt.
  - After an answer to an older poll, the agent waits a tenth of `retryDelayMax` before it
    polls again.
  - The sum of `free` never exceeds the room left in `received`.
  - The executor's object goes through `OutpostCodec.ReadOutcome` and `IsExecutorOutcome`:
    one that §8 does not allow becomes Unknown or Failed `result-type`, and any other is
    sent as it came. So does a `size` that is not the length of its bytes.
  - An outcome refused for its size, by the server's code and by a bare HTTP 413 with an
    HTML body on the path: the outbox file and a second `ended` hold Unknown
    `result-too-large` for a Write and Failed `result-too-large` for a Read, and that is
    what is sent.
  - Workers bound what runs. A fake run that exceeds its limit is ended, giving `run-limit`.
    A worker that does not open within the run limit is ended and never free; a run with
    no free worker is Failed `base-unreachable`; a worker that ends without an outcome
    gives `connection-lost`, Failed for a Read and Unknown for a Write.
  - Poll numbers grow across a restart, with the fake clock:
    - a reservation is recorded and flushed before a poll at or above the last bound, and
      at every start;
    - after a restart with the clock set behind the last number, every poll is above every
      poll sent before;
    - two periods with the clock set ahead by different amounts, the larger first, each
      followed by setting it back and a restart: every poll after the last restart is
      above every poll sent before, whatever the names and times of the audit files;
    - a reservation of another identity does not count;
    - a cleared log, end to end against the server: an agent with no reservation of its
      identity numbers its first poll 1. After a new pairing that poll is the newest and
      takes commands. After a Write was received, the log and the outbox cleared and the
      agent started again, that poll is answered as older; the agent stops without running
      anything, the Write is not handed out again, and every later start records `started`
      with an error;
    - with its reservation and the clock set behind the server's newest number, the first
      poll is answered as older, and the next one takes commands;
    - an answer to an older poll whose `newest` is 2⁶² stops the agent after it records the
      reservation of the next number, and every later start records `started` with an
      error.
  - The audit log (§13 of `docs/protocol.md`):
    - nothing runs before its `received` is on the disk, and no entry is written before the
      one before it is on the disk;
    - a second copy of the agent exits with an error while the first runs;
    - a last line without its LF, and a last line that is not JSON, are cut at a start, and
      the agent then starts normally twice in a row;
    - a torn last line, then a start that stops on an access list that admits someone
      else, then a start with the access lists fixed: the last start runs normally;
    - after a failed write the file is cut back to its length before that write, nothing
      more is written, and the agent stops;
    - a `received` whose parameters nest 64 levels is read back, also as the last line;
    - any other line it cannot read, and an audit file it cannot open, stop the agent with
      `started` and an error.
  - The outbox:
    - a torn temporary file is removed at a start;
    - a file it cannot read, and an `ended` with neither its file nor its `confirmed`,
      stop the agent with `started` and an error;
    - a file whose kind and code differ from its last `ended` gets a matching `ended` at a
      start.
  - A poll answer whose commands that pass §7 exceed `free` for a base and mode is lost, and
    none of its commands is recorded or run. So is an answer with two commands of one
    `id`. A command for a base or mode that the local policy does not serve is refused with
    `not-served`, and the others run.
  - An answer whose signature does not verify is treated as a lost answer, and nothing in it
    is acted on.
  - Addresses (§11 of `docs/protocol.md`): `http://` only on a loopback address written
    as such, as `Uri` writes it, never through the proxy; no redirect followed;
    `Accept-Encoding: identity` on every request; `Expect: 100-continue` on every
    `outcome`.
  - An invalid policy serves nothing and is recorded.
  - The entries `connection` and `bases`.
  - The retention of the audit log, with the fake clock:
    - a file that holds an entry of a command whose outcome is not confirmed, and the file
      that holds the highest reservation, are kept past `auditRetentionDays`;
    - other files go once they are older than `auditRetentionDays` plus a day;
    - nothing is removed before the first verified poll answer of a run, nor after an
      answer whose `time` is more than a day away from the agent's clock.
  - Over loopback against the server of S5: a Read and a Write from `ReadAsync` and
    `WriteAsync` to the fake and back, through a test proxy named in `policy.json`.
  - The proxy is offered no credentials, and an NTLM or Negotiate challenge gets none: the
    poll is lost with `status-407`.
  - With `Requires=Windows`:
    - the secrets file is encrypted with DPAPI for the machine;
    - a data folder, or a file in it, whose access list admits anybody else makes the
      agent stop, recorded;
    - the program started with `service` outside the service controller exits with
      `NotService` and changes nothing in its data folder. A real service is the manual
      acceptance of the contract.
  - The documents step: `README.md` says that the agent works with a fake in place of 1C.
- **Carried over from S2** (the planner, 21 November 2026): the changes of the public
  contract that came with `docs/protocol.md` and that this sprint builds. They hold as a
  draft; `AgentPolicy`, `AgentPolicyReader`, the audit log and their tests follow them.
  - **`safeMode` of `write`** (the owner's change of D12, 21 November 2026), as the table
    of `policy.json` gives it; the agent records in the `started` entry of the audit log
    which bases write without safe mode.
  - **`policy.json`:**
    - at most 256 bases;
    - a new member `requestTimeout`: the agent's wait for an answer to `pair` or `outcome`,
      and for the body of a poll answer, in the form of `runLimit`, from 10 seconds to 1
      hour, 5 minutes when absent;
    - the `proxy` row:
      - the agent offers the proxy no credentials, so never NTLM, and reaches only a proxy
        that asks for none (changed from Kerberos only by the coordinator, 22 November
        2026, under PQ2: "Settled" and the drafts above);
      - a URL with a user or a password is refused, which answers Q7;
      - the example host is not a `.local` name;
    - `retryDelayMax` gains a second meaning: a tenth of it is also the wait after an
      answer to an older poll (§6.2).

    `AgentPolicy`, `AgentPolicyReader` and their tests follow all four.
  - **The audit log:**
    - `connection` is recorded once per change of the state of the polls, which answers
      to outcomes do not change, with the `reason` of §9: the code of a verified refusal,
      `protocol-version`, `status-` and the HTTP status, or `unreachable`;
    - the agent alone writes the log, holding `audit\lock` without sharing, one entry at a
      time with the writer of §2, each ending with LF and on the disk before the next;
      outbox files are written under a temporary name and renamed, through to the disk,
      and read with `ReadOutcome`; a file that differs from its last `ended` gets a
      matching one at a restart. The bullets marked *(S2)* in "The audit log" say so;
    - a new entry `reservation`, with the members `agent` and `below`: the bound below
      which the agent sends its poll numbers, recorded before it sends a number at or
      above the last bound. The agent starts at or above the highest one of its own
      identity. With none, it numbers its first poll 1; when the server answers that
      poll as older, the log was cleared, and the agent records a reservation that
      leaves no number and stops (§6.2). This is the owner's answer of 21 November 2026
      on a cleared audit log;
    - torn entries (§13): an entry counts as written only with its LF. At a start the
      agent reads and cuts its log before it writes any entry, and cuts the last line of
      a file when it lacks its LF or is not JSON. After a failed write it cuts the file
      back to its length before that write, writes nothing more and stops. Any other line
      it cannot read, an audit file it cannot open, an outbox file it cannot read, a
      command with an `ended` but neither its file nor its `confirmed`, and a start with
      no poll number left at or below 2⁶², make it record `started` with an error and
      stop. The administrator pairs it anew, after moving `audit\` aside when the log
      cannot be read, and the application revokes the old identity. The readers of the
      log and of the outbox accept every depth and length their writer writes;
    - the row of `ended` says that a later `ended` replaces an earlier one, and is
      written after a refusal for size and at a restart, when the outbox file no longer
      matches the last one;
    - `refused` is recorded with the codes `signature` and `form` for commands that cannot
      be trusted;
    - a `refused` entry ends its command as `ended` does, and its Refused outcome is kept
      in the outbox until it is confirmed;
    - `received.parameters` is always an object;
    - retention:
      - a file's age counts from its last entry;
      - no file is removed while it holds an entry of a command whose outcome is not
        confirmed, or the highest `reservation` of the agent's identity;
      - retention runs only right after a verified poll answer of the current run, and
        only when the agent's clock is within a day of that answer's `time`. An agent
        that has had no such answer since it started, or whose clock is further off,
        removes nothing. So a clock moved ahead, by a hop that controls its time service
        or by a fault, wipes nothing out;
      - a file is removed when it is older than `auditRetentionDays` plus one day, so that
        with the day of tolerance it is still older than `auditRetentionDays` by the
        server's clock;
      - entries written while the agent's clock was behind count as older by that much,
        and may be removed early once the clock is right;
      - so rule 2's "for at least a day", concept §5's "never for less than a day" and
        the "Retention" bullet of "The audit log" ("never one younger than a day") each
        add "by the agent's clock when it wrote them". D7 rests on the same.
  - **An exception to principle 10**, for the [register](exceptions.md): the agent's
    wait of one second for `100 Continue` (§9) is a constant of the agent, the default of
    .NET, not a member of `policy.json`. Its only effect is a second added to an outcome
    sent through a hop that strips `Expect`, and nobody has a reason to change it. Since
    S6, until permanent. The coordinator enters it under "Approved, not in force yet"
    once the owner approves.
- **Left by S6c's review** (P4, the coordinator, 22 November 2026), for S8 or the sprint
  named:
  - a poll answer is copied once after it is buffered, so a 64 MiB answer peaks near
    128 MiB, not measured;
  - `Program.Version()` reads the version resource of the `.exe` and no test reaches it;
    S12 checks that the archive's `.exe` keeps its product version;
  - X2 is built and waits for the owner in `docs/work/exceptions.md`.
- **Left by S6b's review** (P4, the coordinator, 22 November 2026), for S6c where its
  code meets them, otherwise S8:
  - untested behaviors whose code is right: `run-limit` whatever the run then gives; a
    late open that answers true never becomes free; a Write worker's Unknown
    `connection-lost` drops it; the executor's Failed `result-type` and `result-too-large`
    sent byte for byte; a synchronous `OperationCanceledException` of `RunAsync`;
  - the agent's own outcome of a mode (Failed for a Read, Unknown for a Write) is chosen in
    both `WorkerPool` and `CommandRecords`;
  - `race.Cancel()` after a completed open or run also cancels the token the process was
    given;
  - the rethrown cancellation of a synchronous `RunAsync` carries the linked token, not
    the caller's.
- **Left by S6a's review** (22 November 2026). The code review took three rounds. Its
  last finding, a P3, was moved to S8 by the planner, 22 November 2026: in
  `AuditLog.RemoveExpired` the rule that a `confirmed` stays while its command has
  entries in a file that stays is coded twice, by `KeepConfirmed` with `ConfirmsAny` and
  by `WaitsForOthers`; removing the first pair gives the same deletions. The P4s left for
  the coordinator, for S8 unless S6b or S6c meets them:
  - while the clock is behind the file of a command's last `ended`, every start appends
    another `ended` that never takes effect;
  - `SecretsFile` accepts any bytes as the agent's key, where `AgentPairingFile` checks
    the server's key, so a bad key fails only when S6c first signs (S6c meets it);
  - the root of `Moltaro.OnecOutpost.Agent.Tests` holds eight shared helpers, as the
    server's tests do;
  - the reason of a stop after `started` is recorded nowhere.
- **Out of scope.** COM, the executor, the console.

### S7 · Several nodes and lost messages

**Track:** full · **Needs:** S6, and S9c by the order. One brief, run as two sprints (the
coordinator, 23 November 2026), each with its own tests, implementation, code review, pull
request and merge under PQ1:

- **S7a, several nodes and lost messages.** Branch `epic-1/sprint-07a-nodes`. The proxy of
  the contract step and every line of the Acceptance under "With two server nodes".
- **Left by S7a's review** (P4, the coordinator, 23 November 2026), for S7b or S8: the
  proxy answers `100 Continue` itself, so the refusal for size before the body no longer
  runs end to end through it; its comment's reason for `Connection: close` is not what .NET
  10 does; two signature lines make it throw; the `RestartAsync` branch for the second node
  and some hop headers are unused; `FirstDelay` and a wait on the signals' state are
  duplicated.
- **S7b, the measurements and the explanations.** Branch `epic-1/sprint-07b-measurements`,
  cut from `main` after S7a merges. The program `Moltaro.OnecOutpost.Measurements` and the
  lines *Reported*, explanations E1 and E2, and the documents step with the figures.

- **Goal.** Several server nodes and an agent keep every rule when messages are lost or
  held, and when a node or the signals host stops. The latency target is measured.
- **Contract.** No public change. In `Moltaro.OnecOutpost.TestSupport`, a loopback proxy
  that can drop or hold a chosen request or answer, written in the contract step.
  - *Internal* (the contract author, 23 November 2026). In folder and namespace
    `Moltaro.OnecOutpost.TestSupport.Loopback`, every type `internal`, working from this
    step, with no tests of its own (`AGENTS.md`). S6c's `TestProxy` grows into the proxy,
    and a second node starts from `LoopbackServer`; no interface is added.

    ```csharp
    internal sealed partial class TestProxy // its CONNECT, 407 and TLS for outpost.test unchanged
    {
        public LoopbackServer? Node { get; set; }               // where each request read from then on goes; null: the port of the CONNECT
        public IReadOnlyList<ProxiedRequest> Requests { get; }  // every request read, in order, whatever was done with it
        public Task<ProxiedRequest> DropNextRequestAsync(Func<ProxiedRequest, bool> match, CancellationToken cancellationToken);
        public Task<AgentAnswer> DropNextAnswerAsync(Func<ProxiedRequest, bool> match, CancellationToken cancellationToken);
        public Task<HeldRequest> HoldNextAsync(Func<ProxiedRequest, bool> match, CancellationToken cancellationToken);
        public Task<AgentAnswer> ReplayAsync(ProxiedRequest request, LoopbackServer node, CancellationToken cancellationToken);
    }
    internal sealed record ProxiedRequest(OutpostRequestKindEnum Kind, string Path, IReadOnlyList<(string Name, string Value)> Headers, byte[] Body)
    {
        public byte[] Signature { get; } // of its Outpost-Signature; empty for none
    }
    internal sealed class HeldRequest
    {
        public ProxiedRequest Request { get; }
        public Task<AgentAnswer> ReleaseAsync(LoopbackServer node, CancellationToken cancellationToken);
    }
    internal sealed class TwoNodes : IAsyncDisposable
    {
        public static Task<TwoNodes> StartAsync(PostgreSqlFixture postgres, string schemaName, Action<OutpostServerOptions> configure, CancellationToken cancellationToken);
        public LoopbackServer First { get; }  // embeds the Signals host
        public LoopbackServer Second { get; } // a SignalClient of that host
        public FakeClock Clock { get; }       // of both
        public Task StopAsync(LoopbackServer node, CancellationToken cancellationToken);
        public Task<LoopbackServer> RestartFirstAsync(CancellationToken cancellationToken);
    }
    // LoopbackServer: + StartAnotherAsync(ISignals signals, CancellationToken cancellationToken); its overload with
    // RemoteSignals takes an ISignals. RemoteSignals: + SignalHost? Host, the host that runs.
    ```

    - **The relay.** Inside the tunnel the proxy reads each request whole, its head and
      its body by `Content-Length`, and answers `Expect: 100-continue` with `100 Continue`
      itself, as a hop that reads a body before it forwards it does. It forwards the
      request over HTTP to the node with its header lines, less those of one hop, and the
      exact bytes of its body. It writes back what the agent reads of an answer: the
      status, `Content-Type`, `Outpost-Signature` and body. Every answer carries
      `Connection: close`, so each request comes on a tunnel of its own, which keeps the
      relay simple. A dropped request never reaches a node later: .NET sends a request
      again by itself only while its body has not gone, and the proxy reads every body
      whole before a rule takes it (the refactorer, S8). An agent that closes its
      connection while its request is at the node ends that request there too, as the
      byte pipe of S6c did.
    - **Choosing a request.** Each of the three methods sets a rule at once and gives a
      task that completes when the rule took effect, within `LoopbackServer.Deadline`.
      The rule takes the next request read after it that `match` accepts, once. Rules are
      tried in the order they were set. A test chooses by `Kind`, `pair`, `poll` or
      `outcome`, and by order: the next one, or the number or command it reads from
      `Body`.
    - **Drop the request:** it reaches no node, and the proxy closes the agent's connection
      without an answer. The task gives the request.
    - **Drop the answer:** the node handles the request, and the proxy closes the agent's
      connection in place of the answer. The task gives the node's answer, and fails when
      the node gave none.
    - **Hold:** the request reaches no node, and the agent's connection stays open until
      the agent closes it, as when its wait ends or it stops. `ReleaseAsync` sends it to
      the node given and gives the answer. The answer also reaches the agent while the
      agent still waits on that connection, and reaches only the test otherwise. A hold
      never released never reaches a node.
    - **Replay:** `ReplayAsync` sends a request of `Requests` again to a node, exact bytes
      and headers, and gives the answer to the test alone. The poll that §13 captures
      while the agent's clock is ahead is such a request. The newest poll replayed exactly
      is one too.
    - **Two nodes.** They share one schema, one signing key, the same options and one fake
      clock, on which both arm their timers. The Signals host of `RemoteSignals` is the
      `ISignals` of `First`, and its client that of `Second`, so a set on either node
      wakes the other. `Node` of the proxy says which node takes each request; a call is
      made on either node's `Server`. `StopAsync` stops a node as its host stops. For
      `First` the Signals host stops with it, as when its process ends: `Second`'s client
      loses the host and tries again by itself.
      `RestartFirstAsync` starts `First` again, stopping it first if it runs. The new node
      is a new `LoopbackServer` at a new port, which `First` gives from then on, and a new
      host starts on the old port, which the client reaches on its next attempt.
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
      nothing is lost;
    - the replay of §13 of `docs/protocol.md`: a poll captured while the agent's clock was
      set ahead is replayed after the clock was set back, the agent restarted and ran a
      Write, and the proxy held back the Write's outcome. The poll is older, and the Write
      is not Expired.
  - *Reported*, by the new program `Moltaro.OnecOutpost.Measurements`, its scenarios
    written by the test author. On loopback without 1C, with one node and with two:
    - the time from a call to the command reaching a waiting poll;
    - the time from an outcome to the waiting caller.

    Neither includes the agent's flushes to the disk, which come between the two: the
    `received` before a command runs, and the outbox file and `ended` before its outcome
    is sent. Each is given as p50, p95 and maximum. The target of concept §11 is 100 ms at
    p95. The figures stay the same with a `PollTimeout` of 5 minutes, which shows that no
    hop waits for a fixed interval.
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

- **Goal.** The code base after S7 has no technical debt worth removing before the console
  and the shell build on the agent.
- **Contract.** None; nothing public or asserted changes.
- **To read.** `AGENTS.md`; the [development process](process.md) as it stood for S8, "A
  refactoring sprint"; the whole code base; the briefs of S9–S12.
- **Acceptance.** Build, format and every test green after every commit. No public member
  and no assertion changed. Each change makes the code simpler to read or to change. The
  documents and the explanations still agree with the code. After S8 the tests of
  `Moltaro.OnecOutpost.OneC.Tests` run again on the lab (OD1), every one passed, since the
  refactoring touches code already proven on 1C (the owner, 22 November 2026).
- **Out of scope.** Behavior, the contract, anything for a future need.
- KISS (`AGENTS.md`, "Keep It Simple") holds here as everywhere. The task is to remove
  technical debt where it is worth it, so that the code stays clean, easy to understand,
  easy to change and free of duplication. It is always a balance: a change that makes the
  code larger, more abstract or more general than today's case needs is not a
  refactoring.
- **Carried in** (the planner, 22 November 2026): the last finding of S6a's code review,
  as "Left by S6a's review" in the S6 brief gives it: `KeepConfirmed` and `ConfirmsAny`
  repeat in `AuditLog.RemoveExpired` the check that `WaitsForOthers` makes, and are
  removed, with a review, the retention tests unchanged. The P4s left there and in the S5
  brief are looked at too.
- **The survey** (the refactorer, agreed by the coordinator, 23 November 2026; the planner's
  order of that day): 42 items of debt, in the coordinator's notes. The 23 that touch no
  file of S9c's branch are built first, on `epic-1/sprint-08-refactoring` from `main`, each
  its own commit with every test green; they are merged only after S9c, and the 19 that
  touch S9c's code are added on top once it is merged. Twin theories are not merged into
  one, so that the names stating a behavior stay. Item 34, one shared 30-second deadline of
  the tests, is left: it would add a type and a `using` to about twenty files to remove
  three one-line constants. The code review took one round (six P4s left: a reader of a
  request's kind that only tests call, two shapes of reader, `ArgumentOutOfRangeException`
  for a value outside an enum where none can reach, a test member keeping the old word,
  two names in the lab tests, the S2 brief's contract naming the removed
  `AgentPolicyException.Reason`).
- **The lab run** (the owner, 23 November 2026), on the lab machine with 1C:Enterprise
  8.3.17.1851, from `main` at 877fd9a: 76 of 76 tests of `Moltaro.OnecOutpost.OneC.Tests`
  passed, in 8 min 23 s. It is the run the refactoring owed, and the only proof of items 39
  to 41, which touch only those tests.
- **Not refactorings, for the owner or a later sprint** (the survey): which outcome
  `PassCommandAsync` gives for a command the purge removed (Q15); an index on
  `commands(agent_id)`, a migration, and the copies of a 64 MiB poll answer, each changed
  only with a measurement; the sets of a revocation and of Expired sent whatever the
  caller's cancellation; an `ended` appended at every start while the clock is behind; the
  reason of a stop after `started`, which the audit log does not record (Q16); tests for
  the behaviors S6b's review found untested; forwarding `Expect: 100-continue` through
  `TestProxy`; `Requires=Windows` on every class of `Moltaro.OnecOutpost.ComWorker.Tests`;
  README's sentence on a node that stops.

### L1 · The lab check of open items O1–O8

**Track:** light · **Branch:** `chore/lab-check` · **Needs:** the owner's lab machine (OD1).
Runs right after S2, before S9 and before the contract of stage 2 is written.

- **Goal.** Each open item of concept §12 has an answer from a real 1C:Enterprise
  8.3.17.1851, so that the contracts of stage 2 rest on facts.
- **How.**
  1. An implementer, assigned by the coordinator, writes a minimal executor as XML sources,
     the steps of the check for each open item, and the test configuration below.
  2. The owner builds the `.epf` and the test base, and runs the check on the test base
     and on a copy of a typical base, with that implementer.
  3. The planner writes each answer into concept §12. An answer that changes a decision,
     such as D3, D12 or D14, or a member of `policy.json`, goes to the owner and is written
     on this branch.
  4. An independent reviewer reviews the edit of the concept and the test configuration.

  The minimal executor is not committed: S9 writes the executor from its contract. No
  report file is written.
- **The 1C test lane.** L1 also sets it up, as `AGENTS.md`, "Tests And Samples", says:
  - `src/OneC.TestConfiguration`: the XML sources of a minimal test configuration, and the
    script that builds a base from them on the lab's 1C cluster, through the Designer's
    command line. No built configuration or base is committed. The configuration has:
    - the compatibility mode 8.2.13, the lowest in use (L1);
    - a catalog, a document that posts to an accumulation register, and a common module;
    - two roles, both with the right of external connection and whatever else O2 finds
      that opening the executor needs: one that reads every object and changes none, and
      one that also writes and posts;
    - two users, `outpost-read` and `outpost-write`, one with each role, created by the
      script with the passwords that `lab.local.json` gives them.

    The test base is enough for O2, O4, O7 and O8. O1, O3, O5 and O6 need a typical
    configuration or a client-server base, so they are answered on the copy.
  - The owner provides the lab machine with 1C:Enterprise 8.3.17.1851 and its Designer,
    the COM connector and the .NET 10 SDK, and at least one copy of a typical base on its
    1C cluster, a client-server base, with a read account and a write account. The tests
    need no Docker and no PostgreSQL.
- **To read.** The [concept](../concept.md), §3 "The two commands" and §12; this plan,
  "The files of the agent".
- **Done when** O1–O8 are answered in the concept, the test configuration builds into a
  base on the lab's cluster, and the change, with `src/OneC.TestConfiguration`, is
  merged.

### S9 · The executor and the COM worker

**Track:** full · **Branch:** `epic-1/sprint-09-com-worker` · **Needs:** S2, L1, and
the contract of the executor and the COM worker approved.

- **Goal.** A real base runs scripts. For each command, the COM worker:
  - holds the external connection under the account of its mode;
  - opens a fresh executor, in safe mode unless the policy turns it off for the Writes of
    the base (D12);
  - runs the script with `Parameters`, `CallerKey` and `Result`;
  - runs a Write in one transaction, with the checks of rule 5 before the commit;
  - collects the user messages and reports the outcome.

  The tests start the worker program themselves and speak to it as the agent will. How
  the agent starts, limits and restarts its workers is S9b.
- **Contract.** As [Stages 2 and 3](#the-contracts-of-stages-2-and-3) says, completed with
  L1's answers before stage 2 starts. Exception X1 of the [register](exceptions.md) comes
  into force with the commit of the `.epf`.
  - The executor replaces each U+0000 and each lone surrogate in its messages and its
    error text with U+FFFD, then trims them, never inside a surrogate pair, before it
    writes and measures its outcome object (§8 of `docs/protocol.md`). The object crosses
    COM as UTF-16 text, and the worker encodes it as UTF-8; since the text is then valid
    Unicode, these are the bytes 1C measured.
  - The executor writes its object with the line breaks of the 1C JSON writer turned off,
    so that it travels as one line between the worker and the agent, and returns the size
    it measured beside it. The agent compares that size with the length of the bytes it
    received; a difference is an outcome it cannot accept: Failed `result-type` for a
    Read, Unknown `result-type` for a Write.
  - *Internal, after the study* (the planner, 21 November 2026). The worker depends on
    nothing outside .NET (principle 12), so it reads and writes its lines with
    `System.Text.Json` and shares no type with the protocol assembly. It reads a line to a
    depth of 72 levels, the protocol's limit of a message, so that parameters of 64 levels
    pass.

    **Places.**
    - `src/Moltaro.OnecOutpost.ComWorker`: the program, `net10.0`, Windows only, built for
      `win-x64` and `win-x86`. One folder, one namespace.
    - `src/Moltaro.OnecOutpost.ComWorker.Tests`: its tests, in the solution, with
      `Requires=Windows`.
    - `src/Executor/OutpostExecutor.xml` with `src/Executor/OutpostExecutor/Ext/ObjectModule.bsl`:
      the sources of the external data processor `OutpostExecutor`.
      `src/Executor/OutpostExecutor.epf` is built from them on the lab machine and committed
      after that (X1).
    - `src/OneC.TestConfiguration/Build-TestBase.ps1` gains one step, after the configuration
      is loaded and before the users are created: the Designer builds the `.epf` from the
      sources with `/LoadExternalDataProcessorOrReportFromFiles`. A base has no users then,
      so no password reaches a command line, and one command builds everything the lab
      tests need.
    - `src/Moltaro.OnecOutpost.OneC.Tests`: the lab tests, outside the solution. They start
      the built worker program and speak to it over its standard streams, with a helper of
      their own.
    - `lab.local.json` gains `typicalBases`, an array of at least one copy: for each its
      `server`, `database`, `configuration` and `version`, the accounts `read` and `write`
      with `user` and `password`, the `documentType` to post again, and `writeSafeMode`.

    **The executor.** One exported function, in the English variant, written only with what
    the compatibility mode 8.2.13 allows:

    ```bsl
    Function Run(Mode, Script, ParametersJson, CallerKey, OutcomeLimit) Export
    ```

    - `Mode` is `"read"` or `"write"`. `ParametersJson` is the text of a JSON object.
      `CallerKey` is a string or `Undefined`. `OutcomeLimit` is a number of bytes.
    - It returns one line of text without a line break:
      `{"size":<number>,"outcome":<the outcome object of docs/protocol.md section 8>}`. `size` is the
      length in bytes of the UTF-8 text of the outcome object alone, as the executor measured
      it before the commit of a Write.
    - Safe mode is not its parameter: the worker opens it in safe mode or without.
    - It never raises: every error of the script is an outcome. How it measures UTF-8 in
      the mode 8.2.13 is its own matter, and a lab test holds the result.
    - After the script of a Read or a Write it rolls back every transaction the script
      left open, and the outcome is Failed `script-error` with the executor's own sentence
      (Q12), unless the script raised, or its result fails the type or size check, which
      keep their code and text. So a Read leaves no transaction to the next command of its
      worker.
    - **Before it commits a Write it asks the base a question that needs no table,** the
      query `SELECT 1`. In the compatibility mode 8.2.13 a nested `BeginTransaction` and
      `RollbackTransaction`, in the script or in the code of the configuration it calls,
      rolls the whole transaction back and leaves it active, and its commit does not
      raise; only the next access to the base does (the lab, 21 November 2026). When the
      query raises, the executor rolls back and the outcome is Failed `script-error` with
      its own sentence: nothing was written. It judges by the raise, never by the wording
      of 1C. A lab test writes an item, begins and rolls back a nested transaction, and
      finds Failed and no item; another does the same without the nested pair and finds
      Succeeded and the item.

    **The worker's command line:** `Moltaro.OnecOutpost.ComWorker.exe <base key> <read|write>`.
    The base key only names the worker in its diagnostics. Anything else ends it with exit
    code 1.

    **The lines.** One JSON object per line in UTF-8, ended with LF, without a BOM, on the
    raw standard streams. The standard output carries these lines and nothing else;
    `Console.Out` is pointed at the standard error. The standard error carries diagnostics
    for a person. The worker never writes its first input line. A text that is not its own,
    such as the failure message of 1C or of .NET, it writes there only when the text holds
    the password neither as it is nor with its quotes doubled; otherwise it writes its own
    sentence that the text was withheld. Replacing the password inside such a text would
    give a short one away. Two limits stay: that a text was withheld says the password is
    part of it, which over many failures can narrow a password of one or two characters,
    and a password that 1C quotes cut or in another case is not seen. An outcome line is
    never changed.

    | Direction | Line | When |
    | --- | --- | --- |
    | in | `{"location":"Srvr=\"srv:1541\";Ref=\"base\";","user":"...","password":"...","safeMode":true,"executor":"C:\\...\\OutpostExecutor.epf"}` | Once, first. `location` is the part of a 1C connection string that names the base, for a cluster or for a file base; the worker adds the user and the password, quoted as 1C wants. A Read worker given `"safeMode":false` ends with exit code 1 (D12) |
    | out | `{"ready":true}` | Once, after the session is open |
    | out | `{"ready":false}` | Instead, when no session opens; the text of the failure goes to the standard error, and the worker ends with exit code 2 |
    | in | `{"script":"...","parameters":{},"callerKey":null,"outcomeLimit":16777216}` | One per command. The worker passes the text of `parameters` to the executor as it received it. It takes the next line only after it wrote the outcome of this one |
    | out | `{"size":123,"outcome":{...}}` | One per command: the text the executor returned, encoded as UTF-8 and written with its LF in one write, unchanged |

    - **A fresh executor per command** (concept section 3): the worker opens it with an
      `UnsafeOperationProtectionDescription` whose warnings are off, calls `Run`, and
      releases it.
    - **The outcomes the worker writes itself,** in the same form, with `errorText` null,
      `messages` empty and `messagesTrimmed` false:
      - the executor does not open: Failed `base-unreachable`, for a Read and a Write, and
        the worker ends with exit code 2;
      - `Run` returns a text with a line break, which would shift every later outcome:
        Failed `result-type` for a Read, Unknown `result-type` for a Write, and the worker
        goes on, since its session is whole;
      - the call of `Run` throws, which the executor itself never causes: Failed
        `connection-lost` for a Read, Unknown `connection-lost` for a Write, and the worker
        ends with exit code 3. The text of the failure goes to the standard error only.
    - **A line the worker cannot read** — not JSON, a member missing, unknown or of a wrong
      type — ends it with exit code 1 and a reason on the standard error. It is not skipped.
    - **The end of the input** ends the worker with exit code 0, once the command it runs
      has its outcome written, and also before the first line. A last line without its LF
      is a line. A worker that ends with exit code 1 writes nothing to the standard output
      for the line that ended it.

    **The types,** in `Moltaro.OnecOutpost.ComWorker`. The loop is synchronous: the worker
    is one thread that owns one COM session and has nothing else to do while a script runs.

    ```csharp
    internal interface IOneCConnector
    {
        IOneCSession Connect(string location, string user, string password);
    }

    internal interface IOneCSession : IDisposable
    {
        IOneCExecutor OpenExecutor(string path, bool safeMode);
    }

    internal interface IOneCExecutor : IDisposable
    {
        string Run(string mode, string script, string parametersJson, string? callerKey, long outcomeLimit);
    }

    internal enum WorkerModeEnum { Read, Write }

    internal static class WorkerExitCodes
    {
        public const int Ended = 0;
        public const int BadInput = 1;
        public const int NoSession = 2;
        public const int SessionLost = 3;
    }

    internal sealed class Worker
    {
        public Worker(IOneCConnector connector, WorkerModeEnum mode);
        public int Run(Stream input, Stream output, TextWriter diagnostics);
    }

    [SupportedOSPlatform("windows")]
    internal sealed class ComConnector : IOneCConnector
    {
        public IOneCSession Connect(string location, string user, string password);
    }
    ```

    - `Worker.Run` is the whole behavior of the lines above and returns the exit code.
      `Program` only reads the command line, points `Console.Out` at the standard error and
      calls it with `ComConnector` and the raw streams.
    - `ComConnector`, with the session and the executor it gives, is the only code that
      touches COM, late-bound through `IDispatch`. Each COM object is released by its owner,
      once.
    - The automated tests run `Worker` in the test process with a fake `IOneCConnector` and
      streams of their own. The shipped program has no test entry point; only the lab tests
      start it as a process.
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
    - every line of the internal contract: the session that does not open, the executor
      that does not open, a `Run` that throws for each mode, a line that cannot be read, a
      Read worker told to run without safe mode, the end of the input, and that the
      standard output carries nothing but the lines;
    - parameters of 64 levels reach the executor;
    - the password appears in no output of the worker: a failure of the fake that quotes
      it, also a short one and one with its quotes doubled, is withheld.

    L1 installed one version of the connector, so it recorded no error of a connector
    whose version differs from the cluster's; nothing is asserted about one.
  - In `Moltaro.OnecOutpost.OneC.Tests`, on the lab machine (OD1), against the test base
    unless a line says otherwise:
    - a Read query that returns rows;
    - a Write that creates and posts a document of the test configuration;
    - on each copy of a typical base: the latest posted document of the type that
      `lab.local.json` names, posted again through the worker with the safe mode that
      `writeSafeMode` gives, has the same register
      movements as it had before, which a person's posting made (O1). The owner names a
      type whose posting depends on no sequence and no later document, in a period that
      no closing date of changes covers;
    - messages and an error text with U+0000, a lone surrogate, and a character outside
      the Basic Multilingual Plane at the point of trimming arrive with U+FFFD where it
      belongs and the pair kept whole, and the size the executor measured equals the
      length of the bytes the agent receives;
    - a Write whose script raises an error rolls back;
    - a result of a wrong type, and one too large, roll back before the commit;
    - a script that calls `CommitTransaction` or `RollbackTransaction` gives Unknown
      `transaction-ended`. A commit of the executor that raises gives it too, but has no lab
      test: the one way of provoking it that was tried does not make the commit raise on
      8.3.17 (the lab, 21 November 2026);
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
    - the read account cannot write.
  - Manual, on the lab machine, one line per scenario: only what no test can make.
  - The executor's XML sources and its `.epf`, built by the owner with the Designer of
    8.3.17.1851, are committed in `src/Executor`.
  - The documents step: `README.md` says that the COM worker runs scripts in 1C and that
    no agent starts it yet.
- **What S9 changed in the public contract, as a draft for the owner** (the coordinator,
  21 November 2026). Each holds until the owner overturns it:
  - two codes, `OutpostCodes.BaseUnreachable` and `OutpostCodes.ConnectionLost`, which an
    agent may send as §8 of `docs/protocol.md` lists them, and which
    `OutpostOutcome.Failed` and `OutpostOutcome.Unknown` accept;
  - `transaction-ended` also covers a commit of the executor that raised, and
    `script-error` a script that left a transaction of its own open, and a Write whose
    base refused a query before the commit, with the executor's own sentence as
    `ErrorText` (Q12 of the [open questions](open-questions.md));
  - in the concept, rule 5 says that a Write also fails when its script never started, and
    names the two cases above, §3 says that a transaction a Read's script leaves open is
    rolled back and the Read fails, and §12 says that a nested rollback is silent in the mode
    8.2.13 and that the executor detects it with a query before the commit;
  - in the Acceptance, the line on a connector of another version gave way to the lines of
    the internal contract, since L1 recorded no error for one;
  - the readings Q10, Q11 and Q12 of the open questions, taken as recommended, with Q12's
    answer from the lab: a commit that raises keeps Unknown `transaction-ended` and has no
    lab test.
- **Small changes made while S9 was open** (process, "Small changes during a sprint"):
  - `docs/work/process.md`, "The model and the effort of each role": the owner's rule of
    21 November 2026, after a run spent its weekly limit in the middle of this sprint;
  - the user guides in this plan and in the Layout of `AGENTS.md`: the owner's instruction
    of 21 November 2026. S9 owes no guide.
- **Out of scope.** The agent's supervision of its workers, the console, the shell.

### S9b · The agent runs the COM workers

**Track:** full · **Branch:** `epic-1/sprint-09b-workers` · **Needs:** S6, S9.

- **Goal.** A command travels from `ReadAsync` and `WriteAsync` to a real 1C and back. The
  agent starts the workers of each base and mode with the connector's bitness, inside a
  job object that ends them with the agent. It ends a worker at its run limit, and starts
  it again with a growing delay, up to `retryDelayMax`.
- **Limitation** (the planner, 22 November 2026). The agent starts the COM workers under
  its own account, for Reads and for Writes in safe mode. A base whose `write` has
  `safeMode: false` is not served for Write: its mode is closed at the start and recorded,
  never run under the service account. S9c serves it.
- **Contract.** As [Stages 2 and 3](#the-contracts-of-stages-2-and-3) says.
  - *Internal* (the contract author, 22 November 2026, from S9's study and lines and the
    code of S6 on `main`; reviewed in two rounds and approved by the coordinator under
    PQ2, 22 November 2026). Every type is `internal`; the new ones are in `Moltaro.OnecOutpost.Agent.Workers`,
    and the folders depend as S6 says. The one interface stays S6's `IWorkerLauncher`: a
    test registers a connector class of its own for the current user, and the job object
    lies behind the launcher, which the pool's tests replace with S6b's fake.

    ```csharp
    // Moltaro.OnecOutpost.Protocol.Outcomes: IsComWorkerOutcome replaces IsExecutorOutcome
    internal static class OutpostOutcomeCodes
    {
        public static bool IsComWorkerOutcome(OutpostModeEnum mode, OutpostAgentOutcomeKindEnum kind, string? code);
    }

    // Moltaro.OnecOutpost.Agent.Workers
    [SupportedOSPlatform("windows")]
    internal sealed partial class WorkerJob : IDisposable // its struct in WorkerJob.ExtendedLimitInformation.cs
    {
        public WorkerJob();
        public void Add(Process process);
        public void Dispose();
    }
    [SupportedOSPlatform("windows")]
    internal sealed partial class ComWorkerLauncher : IWorkerLauncher // S6's type, now real; NoProcess stays
    {
        public const string ComConnector = "V83.COMConnector";
        public const string WorkerFile = "Moltaro.OnecOutpost.ComWorker.exe";
        public ComWorkerLauncher(string x64Program, string x86Program, string connectorProgId, WorkerJob job);
    }
    [SupportedOSPlatform("windows")]
    internal sealed class ComWorkerProcess : IWorkerProcess
    {
        public ComWorkerProcess(Process process);
    }
    // WorkerPool, IWorkerLauncher and IWorkerProcess keep their members; WorkerPool's behavior grows below.
    ```

    - **The lines the pool accepts.** `IsComWorkerOutcome` is true for what the executor
      hands over (§8, "The executor") and for the outcomes of S9's worker itself (S9,
      "The outcomes the worker writes itself"): Failed `base-unreachable` in either mode,
      Failed `connection-lost` for a Read, Unknown `connection-lost` and Unknown
      `result-type` for a Write; false otherwise. The pool calls it where it called
      `IsExecutorOutcome`, its one caller, and the rest of S6b's check stays. So a Write
      whose executor did not open stays Failed `base-unreachable`.
    - **Starting again** (concept §5; the `retryDelayMax` row).
      - `Open` gives each mode `workers` slots, each with a task of its own, holding one
        worker at a time. Each start is one `Start` and its open, raced with the run limit
        as S6b says; every slot's first start happens before `Open` returns, in the
        policy's order.
      - A worker is dropped as S6b says, and also when its open gives false, comes late or
        is cancelled. Its end is one task, completed by its open when the open did not give
        true, and otherwise by the run that dropped it, in a `finally` after the run's last
        use of the process. On it the slot disposes the process, waits a delay on the
        pool's clock, and starts a new worker. The old process was killed and may still be
        closing its 1C session; the new one may be free before the old command's `Release`.
      - The delay is a tenth of `retryDelayMax` after a worker whose open gave true;
        after one whose open did not, twice the delay before it, from a tenth up to
        `retryDelayMax` (3, 6, 12, 24, 30, 30 … seconds by default). So a worker that opens
        and then fails every command comes back after a tenth each time.
      - Lifetimes: the pool holds one `CancellationTokenSource` of its own, which only
        `DisposeAsync` cancels and which its slots pass to their delays and opens; `Open`
        takes no token (the coordinator, 23 November 2026, from S9b's code review, round 2). `DisposeAsync` cancels it first,
        then awaits every slot, and disposes nothing itself. On it a slot kills its current
        worker, waits for its open and for the run that holds it, if any, disposes it and
        ends; a worker it starts after the cancellation it kills and disposes at once. The
        host disposes the pool after the loop ended, so no run begins after that, and a
        slot reads the `Running` task the loop set only after the cancellation.
      - Threads: `Start` may run on several slots' tasks at once. A slot publishes its
        worker with `Volatile.Write`, and `Free` and `RunAsync` read it with
        `Volatile.Read`, so a worker read late is one not yet opened and `free` is only
        under-declared. A slot publishes a worker whose open gave true before it ends that
        open's deadline, so a test sees `Free` count it once that timer is gone (the
        coordinator, 23 November 2026, from S9b's code review, round 1). `IsHeld` stays the
        loop's; a slot reads `IsDropped` only after the worker's end. The pool's class
        comment states this ownership.
      - *Limitation:* a worker whose process ends while it holds no command is seen only
        when a command reaches it, which gets `connection-lost`, Unknown for a Write,
        though its script never started; the worker is then started again. S9's worker ends
        by itself only after a command, so this needs the program to crash while it waits.
    - **The bitness** (concept §5, O8). `Start` reads the registry each time: the x64
      program when the connector is registered for 64 bits, otherwise the x86 program when
      it is for 32 bits, otherwise a `NoProcess`, whose open gives false, so the slot tries
      again after its delay. Registered for a bitness: `HKEY_CLASSES_ROOT`, opened with
      `RegistryKey.OpenBaseKey` in the view `Registry64` or `Registry32`, gives a class
      identifier as the default value of `{connectorProgId}\CLSID`, and
      `CLSID\{that}\InprocServer32` has a default value that is not empty; never 64 bits on
      a 32-bit Windows. A key that cannot be read is not registered. No file or version is
      checked; the console's `check` does (S10). The agent program gives `x64\` and `x86\`
      beside its own file, each holding `WorkerFile` with the files of its build, and
      `ComConnector`.
    - **The start.** `Process.Start` of the program with `ArgumentList` the base key and
      `read` or `write`, standard input and output redirected and standard error not, `UseShellExecute` false and
      `CreateNoWindow` true, then `WorkerJob.Add`. The connection with its password travels
      only in the first input line, never on the command line or in the environment. A
      program that does not start, and a process the job does not take, killed at once,
      give a `NoProcess`: `Start` throws nothing. *Limitation:* a process is in the job
      from just after it starts; if the agent ends in between, it ends at the end of its
      input, as S9's worker does.
    - **`ComWorkerProcess`** speaks S9's lines over the raw streams, as S9's lab helper does.
      - `OpenAsync` writes the connection line and reads one line of at most 1 KiB: true
        only for an object whose one member is `"ready":true`; anything else, the end of
        the output included, kills the process and gives false.
      - `RunAsync` writes the command line, `ParametersJson` as it is, and reads one line of
        at most `OutcomeLimit` plus 64 bytes. An object with exactly `size`, an integer from
        0, then `outcome`, any JSON value, read to `OutpostProtocol.MaxMessageDepth`, gives
        `size` and the exact bytes of `outcome`. The end of the output before a whole
        line, a longer line and any other line kill the process and give null.
      - A failed write gives false or null; cancellation kills and throws
        `OperationCanceledException`. `Kill` is `Process.Kill`, an ended process ignored.
      - The standard error is not redirected: the worker inherits the agent's, which a
        service has none of. *Limitation:* the agent keeps no diagnostics of a worker; the console's `check`
        shows why one does not open (S10).
    - **`WorkerJob`**: `CreateJobObjectW` with null attributes, so its handle is not
      inheritable, then `SetInformationJobObject` with `JobObjectExtendedLimitInformation`
      and `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`; `Add` is `AssignProcessToJobObject`; through
      `LibraryImport`, in a `SafeHandle`, no package (principle 12); a failure is a
      `Win32Exception`. The agent program makes one job and never disposes it, so Windows
      closes it when the agent ends, however it ends. A test disposes its own.
    - **Writes without safe mode.** `AgentHost.RunAsync` gives the pool, the checks and the
      loop the policy that `AgentStart` read without the `write` of each base whose `write`
      has `SafeMode` false: no worker of that mode starts, a poll declares its `write` as
      `null` (§6.2), the server refuses a Write with `mode-closed`, and one that reaches
      the agent is Refused `not-served`. `started.withoutSafeMode` records those bases.
    - **The test program** `src/Moltaro.OnecOutpost.TestWorker`: a console program in the
      solution, `net10.0`, `IsPackable` false, nothing outside .NET, written by the test
      author, referenced by `Moltaro.OnecOutpost.Agent.Tests` as the lab project references
      the worker. It speaks S9's lines, command line and exit codes, each outcome as the
      executor's line with its true size. A `location` of `line <text>` gets `<text>` as its ready
      line, any other `{"ready":true}`. Scripts: `wait` writes nothing until the input ends
      or a kill; `exit <n>` ends with `n` and no outcome; `line <text>` writes `<text>`;
      `pid`, `path`, `arguments` and `connection` succeed with its process identity, its
      `Environment.ProcessPath`, its arguments as an array and its connection line's
      members as an object; any other succeeds with `null`. S9c's tests step adds `job`,
      the process ids of its immediate job when its `Main` began, which `Main` asks first,
      or `null` in no job (S9c's code review, round 1);
      `desktop`, the window station and desktop it runs on as `{station}\{desktop}`;
      `user`, the SID of the account it runs under; and `handle <n>`, the final path of the
      file that its handle of value n names, or `null`.
    - **The tests with `Requires=Windows`**, `Moltaro.OnecOutpost.Agent.Tests`, folder
      `ComWorkers`: the real launcher, a `WorkerJob` of the test, the test program, the pool
      on the fake clock, and a class identifier that the test registers under
      `HKEY_CURRENT_USER\Software\Classes` in the views it needs and removes after:
      - disposing the job ends a running worker, seen by its `pid`;
      - a `wait` past the run limit gives `run-limit` and its process has ended; after the
        slot's delay a new process, another `pid`, runs a command;
      - `arguments` gives the base key and the mode alone, `connection` the line given to
        `OpenAsync`, password included;
      - the program copied into `x64\` and `x86\` of a temporary folder: `path` names the
        x64 copy when the class is in both views, the x86 copy when only in `Registry32`;
        with none, or a program that does not exist, the open gives false;
      - by `line`: a ready line other than `{"ready":true}`, one over 1 KiB, an outcome line
        over `OutcomeLimit` plus 64, one not `size` then `outcome`, and a `size` that is not
        an integer each give false or null, and the process ends.
    - **The tests on every platform**, with S6b's fake launcher and the fake clock: each
      line of the worker's own accepted byte for byte, with its drop; the delays, a tenth,
      doubled, capped and back to a tenth; nothing started during or after `DisposeAsync`;
      through `TestHost`, a base whose `write` has `safeMode: false`: no start of its write
      workers, `write` `null` in the polls, a Write Refused `not-served`. The table test of
      `IsComWorkerOutcome`, in `Moltaro.OnecOutpost.Protocol.Tests`.
    - **The lab tests** (the planner, 22 November 2026), `Moltaro.OnecOutpost.OneC.Tests`,
      folder `Agent`, Windows and 1C only. A test writes `policy.json`, `agent.json` through
      `AgentPairingFile` and `secrets.dat` through `SecretsFile` with `MachineDpapi` into a
      temporary data folder on the lab disk, outside the repository and removed after each
      test, whose access list `AdmitNobodyElse` sets: inheritance off, and full control for
      Administrators, SYSTEM and the account that runs the tests only. The policy names the test base as `test`, one worker per mode, a run limit of a minute
      and a `retryDelayMax` of a second; the 1C passwords come from `lab.local.json`, never
      from a file in Git. It runs `AgentHost.RunAsync` on `FakeClock` with
      `DiskFolder`, `WindowsAccessLists`, `MachineDpapi`, the real `ComWorkerLauncher` with
      the worker beside the tests as both programs (x64 on the lab) and
      `Repository.ExecutorPath`. Its handler is `LabServer`, an in-process server of the
      test, an `HttpMessageHandler` with the real codec and signatures under a server key
      pinned in `agent.json`: it verifies the agent's requests, hands a signed command once
      a poll declares its mode free, answers a held poll at once when a command comes,
      holds an empty one until then, and confirms each outcome, which it gives the test.
      No pairing request, no command of the samples. A launcher of the test wraps the real
      one only to report when a run began, so that a test moves the clock only after it
      observed the script running (`AGENTS.md`: the COM worker's timeouts on a fake clock).
      - A Write that creates a product with a name the test generated, and a Read that
        counts the products of that name: Succeeded, and 1.
      - A loop of ten minutes by `CurrentDate()`: once it runs, the test moves the clock past
        the run limit, and the Read is Failed `run-limit` and the Write Unknown
        `run-limit`; then past the slot's delay, and a command of the same mode succeeds on
        the worker started again, so 1C gives it a session.
      - Once a poll shows the looping Write as received and not yet confirmed, and its run
        began, the test stops the host, which exits with `Stopped`; a second host on the
        folder sends Unknown `agent-stopped`.

      This proves the agent, the worker and 1C real, with the test's server; the real
      server against the agent is proven in CI over loopback (S6c), and from another
      machine in S11. `AccessListEdits` moves to `Moltaro.OnecOutpost.TestSupport`, marked
      `[SupportedOSPlatform("windows")]`; the lab project references TestSupport and the
      agent, which with the protocol and TestSupport give it their internals. No test of the lab project
      uses PostgreSQL, Testcontainers or a server's store.
    - **Guide G2**, `docs/guides/scripts.md`. A script is a fenced block with the info
      string `bsl read` or `bsl write`, followed by the kind and code it gives when it does
      not succeed, such as `bsl read failed script-error`. Its parameters are the next
      `json parameters` block before the next script, `{}` without one; a `json result`
      block is shown, not compared, since the data come from the base. Each script gives
      what its info string says on the test base whatever it holds.
      `Guides/ScriptGuideTests` reads the file through `Repository`, runs each script in a
      worker of the test base as `LabWorkers` starts one, a Write with a new caller key,
      and asserts its kind and code; a `bsl` block without a mode, and a guide without a
      script of each mode, fail it. For its recipes the tests step makes `Products`
      hierarchical and gives `GoodsReceipt` a `Comment` of 200 characters, where an
      idempotent Write keeps its caller key; the lab builds the test base again.
    - **The steps and their edits of existing code.** The guide comes after the
      implementation ([The user guides](#the-user-guides)).
      - Contract step: the new types throwing `NotSupportedException`, and
        `IsComWorkerOutcome` working, being a table; `IsExecutorOutcome` and the pool stay.
        It removes `AgentHostTests.With_the_programs_launcher_every_mode_declares_free_0`,
        which calls the constructor S9b replaces, of a type now Windows only, on every
        platform, and asserts S6c's "free 0 until S9b"; the real launcher's tests replace it.
      - Tests step: the test program, the test configuration, the tests; the table test of
        `IsComWorkerOutcome` replaces `ExecutorOutcomeTests`; S6b's fake launcher takes a
        lock around its list; in `WorkerRunTests.cs` the class comment (line 13) and the test
        of lines 132–137 say that the mode has one worker fewer until its slot starts it
        again, its name too, its assertions unchanged, provided it passes no clock past the
        delay; the comment of `WorkerOutcomeTests.cs`, line 75, says that S9b admits the
        worker's codes.
      - Implementation: the pool calls `IsComWorkerOutcome`, `IsExecutorOutcome` goes, and
        the comments that name it or say "until S9b" are corrected: `OutpostCodec.cs` line
        761, `WorkerPool`, `ComWorkerLauncher`, and `NoProcess`, now the process for no
        registered connector or a program that did not start.
      - Edits that state nothing new: the references and `InternalsVisibleTo` above;
        `Repository` names the guide; the README of `src/OneC.TestConfiguration` lists the
        two changes; the test program in the projects table of `AGENTS.md`.
    - **The documents step:** `README.md` says that the agent runs scripts in 1C, states
      the limitation and leads to G2, which states it too; "The files of the agent" takes
      the drafts below; "What exists today" in `AGENTS.md` says that the agent starts the
      COM worker.
- **What S9b changes in the public contract, as a draft for the owner** (the contract
  author, 22 November 2026). Each holds until the owner overturns it:
  - until S9c, a base whose `write` has `safeMode: false` is served for Read only, as
    "Writes without safe mode" says. The `safeMode` row of `policy.json` says so, and
    `started.withoutSafeMode` becomes "the keys of the bases whose policy turns safe mode
    off for Writes";
  - the archive (stage 3): the COM worker of each bitness lies in `x64\` and `x86\` beside
    the agent's program, each with the files of its build; S12 builds it so;
  - the bitness: 64-bit workers when a 64-bit connector is registered, otherwise 32-bit,
    judged at every start; with neither, the mode is never free. In concept §5 it replaces
    "An agent machine then serves the bases of one platform version per bitness" with: an
    agent machine serves the bases of one platform version, that of its 64-bit connector
    when one is registered, otherwise that of its 32-bit one.
- **Study.** The study of S9 answers its questions; none of its own.
- **To read.** The [concept](../concept.md), rules 3, 8 and 9 and §5; `AGENTS.md`,
  principle 8; the brief of S9.
- **Acceptance.**
  - Automated, with `Requires=Windows`, against a small test program that stands in for
    the worker:
    - the job object;
    - ending at the run limit;
    - starting again: one start after a delay with the test program, and the growth of the
      delay with S6b's fake on every platform;
    - bitness chosen from the registered connector.
  - Automated, with S6b's fake: a base whose `write` has `safeMode: false` starts no Write
    worker, declares its `write` as `null`, and refuses a Write that reaches it with
    `not-served`.
  - In `Moltaro.OnecOutpost.OneC.Tests`, on the lab machine (OD1), against the test base,
    through the real agent and worker with the server of the test:
    - a Read and a Write reach the test base and return their outcomes;
    - a script beyond its run limit: a Read fails and a Write is Unknown;
    - stopping the agent during a Write gives Unknown.
  - No manual line: a Read and a Write from a server on another machine are S11's manual
    line, since S10's console pairs the agent and S11's shell runs the server (the
    planner, 22 November 2026).
  - **Guide G2**, "Writing scripts", as [The user guides](#the-user-guides) lists it. A test
    of `Moltaro.OnecOutpost.OneC.Tests` reads each of its scripts from the guide's
    Markdown and runs it against the test base.
  - The documents step: `README.md` says the agent works with 1C, and leads to G2.
    `README.md` and G2 state the limitation.
- **The lab run** (the owner, 23 November 2026), on the lab machine with 1C:Enterprise
  8.3.17.1851, from the archive of the branch at 45b505f: `Build-TestBase.ps1` built
  `outpost_test` again, then `dotnet test --project Moltaro.OnecOutpost.OneC.Tests` gave 73
  of 73 passed, none failed or skipped, in 7 min 05 s. It proves that the agent, the COM
  worker and 1C work together; the server was the test's in-process one, and the real
  server against the agent is proven in CI over loopback (S6c). A Read and a Write from a
  server on another machine are S11's.
- **Notes of S9b's review** (the coordinator, 23 November 2026):
  - guide G2 was written by the test author in the tests step, before the implementation,
    so that its scripts could be read by their lab test from the start; a documents agent
    with no other role reviewed its prose after the implementation and changed no script,
    and the independent review found that it matches the code. A departure from "The user
    guides", recorded here;
  - concept §5 says an agent machine serves "one platform version per bitness"; the code
    uses the 64-bit connector when both are registered, as S9b's draft for the owner says.
    The concept is the owner's to change with that draft.
- **Out of scope.** The console, the shell, the account of S9c.

### S9c · The account of the workers without safe mode

**Track:** full · **Branch:** `epic-1/sprint-09c-worker-account` · **Needs:** S9b, run on
the lab and merged. A strict zone: secrets and the local policy. The test author and the
implementer work on `xhigh`.

- **Goal** (the planner, 22 November 2026, and the coordinator, 23 November 2026). A base
  whose `write` has `safeMode: false` is served for Write. All its COM workers run under a
  local Windows account of their own, inside the job object, so that a script without
  safe mode can read neither the agent's secrets nor its audit log (D12, principle 9).
- **Contract.** The public part is the drafts below.
  - *Internal* (the contract author, 23 November 2026, from S9c's study, with the
    coordinator's decisions under PQ2; reviewed in two rounds and approved by the
    coordinator under PQ2, 23 November 2026). Every type is `internal`, and the folders depend
    as S6 says. The boundary of the workers stays S6's `IWorkerLauncher`, which gains the
    logon, so that the start is tested on every platform with S6b's fake; the program
    folder is read through S6a's `IAccessLists`, as the data folder is.

    ```csharp
    // Moltaro.OnecOutpost.Agent.Policy
    internal sealed record AgentModePolicy(string Account, int Workers, TimeSpan RunLimit, bool SafeMode, string? WorkerAccount);

    // Moltaro.OnecOutpost.Agent.Secrets: PrivateKey and Passwords stay
    internal sealed class AgentSecrets
    {
        public AgentSecrets(byte[] privateKey, IReadOnlyDictionary<(string Base, OutpostModeEnum Mode), string> passwords, IReadOnlyDictionary<string, string> workerPasswords);
        public IReadOnlyDictionary<string, string> WorkerPasswords { get; } // by base key
    }

    // Moltaro.OnecOutpost.Agent.Access
    internal interface IAccessLists
    {
        IReadOnlyList<string> Others(string path); // S6a's, unchanged
        IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> Writers(string folder);
    }

    // Moltaro.OnecOutpost.Agent.Workers
    internal interface IWorkerLauncher
    {
        void LogOn(string baseKey, string account, string password);
        IWorkerProcess Start(string baseKey, OutpostModeEnum mode); // unchanged
    }
    internal static class WorkerAccountRule
    {
        public static bool IsOrdinary(IReadOnlyCollection<string> groups, IReadOnlyCollection<string> privileges);
    }
    // ComWorkerLauncher keeps its constructor and constants and becomes IDisposable, closing its tokens and desktops;
    // each struct of its calls in ComWorkerLauncher.{Struct}.cs; tokens are SafeAccessTokenHandle; all through LibraryImport.
    [SupportedOSPlatform("windows")]
    internal sealed partial class ComWorkerLauncher : IWorkerLauncher, IDisposable
    {
        public void Use(string baseKey, SafeAccessTokenHandle token, WorkerDesktop desktop); // the test author's seam, PQ2
    }
    [SupportedOSPlatform("windows")]
    internal sealed partial class WorkerDesktop : IDisposable
    {
        public static WorkerDesktop Make(string baseKey, SafeAccessTokenHandle token);
        public string Name { get; } // "{station}\OnecOutpost-{base key}-{logon}"
    }
    [SupportedOSPlatform("windows")]
    internal sealed partial class WorkerJob : IDisposable
    {
        public void Add(SafeProcessHandle process); // replaces Add(Process)
    }
    [SupportedOSPlatform("windows")]
    internal sealed partial class ComWorkerProcess : IWorkerProcess
    {
        public ComWorkerProcess(SafeProcessHandle process, Stream input, Stream output); // replaces (Process)
    }

    // Moltaro.OnecOutpost.Agent.Start, which now uses Workers, an edge that S6's list gains
    internal static class AgentStart
    {
        public static AgentRun Open(AgentFolders folders, IAccessLists access, IMachineProtection protection, IWorkerLauncher workers, string programs, TimeProvider clock, string version);
    }

    // Moltaro.OnecOutpost.Agent.Host: AgentHost.Served goes
    internal sealed record AgentParts(AgentFolders Folders, IAccessLists Access, IMachineProtection Protection, IWorkerLauncher Workers, string Programs, string Executor, Func<Uri, Uri?, HttpMessageHandler> Handlers, TimeProvider Clock, string Version);
    ```

    - **`write.workerAccount`**. `AgentPolicyReader` reads it into `WorkerAccount` of the
      write mode, null otherwise. Required when `safeMode` is false, refused when it is
      true or absent, an unknown member in `read`. 1 to 20 characters, none of
      `" / \ [ ] : ; | = , + * ? < > @` or a control character, not only periods and
      spaces, so never a domain account or a UPN; unique across bases, ignoring case, so
      that the scripts of one base cannot reach the workers of another. Errors as Q1 says,
      quoting no value: `bases[0].write: "safeMode" is false without "workerAccount";
      expected the name of a local Windows account that runs the workers of the base`,
      `bases[0].write.workerAccount: given while "safeMode" is true; remove it, or set
      "safeMode" to false`, `bases[2].write.workerAccount: the account of bases[0] again;
      expected an account of its own for each base`.
    - **`secrets.dat`** gains `"workers":[{"base":…,"password":…},…]` after `passwords`,
      required, empty when unused (0.x), read and written as S6a's `passwords` are, with a
      base key against its rule or given twice a `FormatException`.
    - **`AgentStart.Open`** adds to S6a's steps: after step 3, `access.Writers(programs)`,
      whose first entry fails with `the access list of {path} lets {accounts} change it;
      expected nobody but Administrators, SYSTEM, TrustedInstaller and the agent's account,
      as the console's install sets it`, and its `IOException` with `the access lists of
      the program folder cannot be read`; in step 6, a worker password for each base with
      a `WorkerAccount`, else `secrets.dat holds no password for the worker account of the
      base {key}; write it with the console`; a step 7, `workers.LogOn(key, account,
      password)` for each such base in the policy's order, whose `AgentStopException`
      fails it before any worker starts. `started.withoutSafeMode` is as before; no new
      audit entry.
    - **The program folder** (the coordinator, 23 November 2026), since an account that
      could replace code the agent's account runs would get the secrets. `programs` is
      `AppContext.BaseDirectory` in `Program`. `Writers(folder)` gives, by full path, with
      its accounts named as `Others` names them, each entry that an account other than
      Administrators, SYSTEM, `NT SERVICE\TrustedInstaller` and the agent's account owns,
      or that an allow entry applying to the entry itself gives:
      - on each parent, from the volume's root down, `Delete`,
        `DeleteSubdirectoriesAndFiles`, `ChangePermissions`, `TakeOwnership` or
        `GenericAll`; adding a subfolder, which `C:\` gives Authenticated Users, plants a
        name only after one of these, so it is not judged by itself;
      - on the folder and everything in it, a folder before its entries by ordinal name,
        any right beyond `ReadAndExecute`, `Synchronize` and the generic rights to read
        and to run; a reparse point is judged by its own list and not entered.

      `IOException` when a list cannot be read or a folder listed. S6a's limitation of an
      administrator's own account holds. *Limitations:* the connector's folder is S10's
      `check`; a volume whose root gives Authenticated Users Modify fails the parents.
    - **`ComWorkerLauncher.LogOn`** runs before any `Start`. Each failure closes the token
      and throws `AgentStopException`, never with the password:
      1. `LogonUserW(account, ".", password, LOGON32_LOGON_BATCH, LOGON32_PROVIDER_DEFAULT)`,
         the password in `Marshal.StringToHGlobalUni`, zeroed in a `finally`; BATCH, since
         a service logon adds SERVICE with `SeImpersonatePrivilege`. Failure: `the worker
         account {account} of the base {key} cannot log on: {the Win32 message}`;
      2. the token's groups, whatever their attributes, and privileges, enabled or not,
         judged by `WorkerAccountRule`: `the worker account {account} of the base {key} is
         an administrator or holds a privilege beyond an ordinary user's; expected a local
         user that is not in Administrators, as the console's install prepares it`;
      3. the agent's token holds `SeAssignPrimaryTokenPrivilege` and
         `SeIncreaseQuotaPrivilege`, enabled or not, since `CreateProcessAsUserW` enables
         them: `the agent's account lacks the right "Replace a process level token" or
         "Adjust memory quotas for a process"; install the agent with the console`.

      The logon comes first, so that a missing account gives 1326 whatever the agent
      holds. The token lives as long as the launcher; one logon per start, so no retries
      lock the account out.
    - **The desktop of a worker** (the coordinator, 23 November 2026). Cause: in the lab
      run of 23 November 2026 each worker under the account ended with 0xC0000142 before
      its first line, since a .NET program loads user32 before `Main` and could not
      connect to the station and desktop that `lpDesktop` `""` names. Decision, since a
      desktop of the agent's own admits only that logon and needs no undocumented step:
      after step 3, once per worker logon, `LogOn` calls `WorkerDesktop.Make` and then
      `Use(baseKey, token, desktop)`, which makes every `Start` of the base use that token
      and `desktop.Name`, so that a test reaches the desktop with its own token (the
      coordinator, PQ2). `Make`: `CreateWindowStationW(null, …)` gives the
      agent's own non-interactive station, refused unless its name starts with
      `Service-0x`; it gains one ACE for the worker token's logon SID, 0x123 (enumerate
      desktops and the station, read attributes, global atoms); `CreateDesktopW` makes
      `OnecOutpost-{base key}-{logon}` on it, `{logon}` the worker token's logon LUID in hex
      so that no desktop of an earlier logon is ever reused (the coordinator, 23 November
      2026, from the review of the desktop, round 1), swapped in and back at once, with the DACL
      `D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0xC7;;;{logon SID})`, 0xC7 as measured. A failure,
      such as no `WRITE_DAC` on the station: `the worker account {account} of the base
      {key} cannot get a desktop: {the Win32 message}`. Invariant: the worker account is
      never granted WinSta0 or its `Default` desktop. The launcher owns what `Use` gets
      and closes it in `Dispose`, which every test that makes a launcher calls.
    - **`WorkerAccountRule.IsOrdinary`**, apart so that it is tested on every platform:
      true only when no group SID is Administrators, `S-1-5-32-544`, and each privilege is
      one of Users': `SeChangeNotifyPrivilege`, `SeShutdownPrivilege`, `SeUndockPrivilege`,
      `SeIncreaseWorkingSetPrivilege`, `SeTimeZonePrivilege`.
    - **The account of a worker** (the coordinator, 23 November 2026: concept §3, D12).
      Invariant, in the launcher's comment: every COM worker of a logged-on base runs
      under its token, Reads included; the agent's own token runs only bases without a
      `workerAccount`. `AgentHost` drops `Served` and S9b's "Writes without safe mode".
    - **The start** replaces S9b's bullet; the choice of the program stays. Two
      `AnonymousPipeServerStream`s; `CreateProcessAsUserW` with the worker's token (the
      agent's own opened by the constructor), the full path, `"{program}" {base key}
      {read|write}`, `CREATE_SUSPENDED | CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT |
      EXTENDED_STARTUPINFO_PRESENT`, `CreateEnvironmentBlock(token, false)`, the program's
      folder, `lpDesktop` `"{station}\OnecOutpost-{base key}-{logon}"` for a logged-on base and
      `""` under the agent's own token, the client ends as standard input and output, a null
      standard error, and a `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` of the two client ends
      alone, so that a worker inherits nothing else. Then the client ends close,
      `WorkerJob.Add`, `ResumeThread`. A failure gives a `NoProcess`, after
      `TerminateProcess` of a created process; no lock. A worker is in the job before it
      runs, so S9b's limitation goes; no `JOB_OBJECT_LIMIT_BREAKAWAY_OK`.
      `ComWorkerProcess` owns its streams and handle; `Kill` is `TerminateProcess`.
    - **Limitations**, each in its code's comment: the profile is not loaded (below); a
      script without safe mode can leave a process outside the job through a service the
      account may use, such as Task Scheduler or WMI, so the account's rights are the
      boundary (D12); the workers of one base can read each other's memory, commands and
      1C passwords included, and forge each other's lines through duplicated handles; the
      password string stays in `AgentSecrets`; the station's ACEs stay until the agent's
      logon session ends; a junction or symbolic link inside the
      program folder is judged by its own list, not by its target's, and only an
      administrator can plant one; a worker that does not start keeps no
      diagnostics.
    - **The risk of the profile** (the coordinator, 23 November 2026). The agent cannot
      load the profile, so HKCU is the default hive and TEMP the system's; the first lab
      test shows whether 1C opens and posts so. If not, the coordinator decides then on
      `CreateProcessWithLogonW` with `LOGON_WITH_PROFILE`, and records it here. Session 0,
      and `WRITE_DAC` of the virtual account on its station, are proven only by S10's
      manual acceptance under the real service.
    - **The tests with `Requires=Windows`**, `Moltaro.OnecOutpost.Agent.Tests`:
      - S9b's launcher tests, unchanged, now with the test's own token, needing no right;
      - the test program reports the process ids of its immediate job as the first thing
        its `Main` does, before it reads a line, and its new script `job` gives them: the
        worker's own id and not the test host's, which tells the launcher's job from one
        the test host runs in; the code holds the order (S9c's code review, round 1);
      - `LogOn` of a random account that does not exist: the base, the account and the
        message of 1326; through `AgentHost.RunAsync` with the real launcher, `DiskFolder`
        and `FakeAccessLists` admitting its `Programs`, `started`'s error, the exit
        `Error`, and no data file holding the generated password. Never the current user;
      - with the process's own token: `Make`, `Use` and `Start` give a worker whose new
        script `desktop` gives `Name`; a desktop that does not admit the token's logon SID,
        given to `Use`, gives an open of false; `Name` starts with `Service-0x`;
      - `Writers`: read and run for Users give nothing; a write entry two folders down, an
        owner not admitted and a parent giving Everyone `Delete` are reported; an
        inherit-only entry gives nothing; a junction inside is not entered.
    - **The tests on every platform**, with S6b's fake launcher recording `LogOn` or
      throwing, and `FakeAccessLists` answering `Writers`: the rule of `workerAccount` and
      its errors; `workers` in `secrets.dat`; the start without a worker password, with a
      `LogOn` that throws, a writer in the program folder or an unreadable list, each
      stopping with its reason; `LogOn` for each base with an account, in order, before
      `started`, and for no other; through `TestHost`, in place of `WithoutSafeModeTests`,
      such a base declares its `write` and runs a Write with `SafeMode` false;
      `WorkerAccountRule` as a table.
    - **The lab tests**, `Moltaro.OnecOutpost.OneC.Tests`, folder `Agent`, the in-process
      tests of the worker account and its desktop, through `LabAgent`, with `workerAccount` in `lab.local.json` as a `LabAccount`; `LabAgent`
      copies the worker and the executor into a program folder that admits nobody else and
      lets Users read and run, and gives it as `Programs` and as the launcher's programs:
      - first, the profile's probe: on each typical base with `writeSafeMode` false, its
        latest posted document of `documentType`, posted again under the account, keeps
        its movements, as `RepostingTests` compares them;
      - on the test base with `safeMode: false`, a Write reading `secrets.dat` of the data
        folder gives false, and on a control file the account may read, true.
    - **The steps.** Contract: the new members; `LogOn` and `Writers` of the real types
      throw `NotSupportedException`; `WorkerAccountRule` works, being a table;
      `AgentPolicyReader` gives `WorkerAccount` null, as in S6a; `AgentStart.Open` leaves
      its new parameters unused; `WorkerJob.Add` and `ComWorkerProcess` work at once, as
      S9b's tests use them. Edits that state nothing new, made there (the coordinator, 23
      November 2026): the fakes and `ObservedLauncher` take the new members; every caller,
      test and helper passes the new arguments, `WorkerAccount: null`, empty worker
      passwords and `Programs`; `StartedWorker` and `TestProgram` give the handle and
      streams. Tests step: `lab.local.example.json` names `workerAccount`; `job` and
      `desktop` join S9b's list of scripts. Implementation: `AgentHost`'s "until S9c" goes. Documents: `README.md` and G2 drop S9b's limitation and state
      these; "The files of the agent" takes the drafts; §6.2 of `docs/protocol.md` drops
      "Until sprint S9c".
- **What S9c changes in the public contract, as a draft for the owner** (the planner, 22
  November 2026, and the contract author, 23 November 2026). Each holds until the owner
  overturns it:
  - `policy.json`: the row `workerAccount`, in `write`, the account of all the base's
    workers, with the rule above; the `safeMode` row drops S9b's sentence;
  - concept §5 and D13, `secrets.dat`: the secrets add each base's worker password,
    without which a served base stops the start;
  - S10's `secret <base key> worker` writes that password, asked without echo;
  - S10's `install` grants `NT SERVICE\MoltaroOnecOutpost` "Replace a process level
    token" and "Adjust memory quotas for a process", in effect from its next start, and
    each worker account "Log on as a batch job" (S10a moved that right to `secret <base
    key> worker`); a worker account is a local user outside Administrators with no
    privilege beyond Users'. `install` and G1 put the program under `%ProgramFiles%` with
    its list's inheritance off, full control for Administrators and SYSTEM, read and run
    for Users, and Administrators owning every entry it writes;
  - S10's `check` judges the connector's folder by the rule of the program folder;
  - "The files of the agent": the agent refuses a program folder that others may change,
    by "The program folder" above;
  - the limitations, in `README.md`, G2 and the `safeMode` row: a script without safe
    mode does what the worker account can, can leave a process outside the job through
    Task Scheduler or WMI, and can read and forge what the other workers of its base do.
- **What the owner prepares on the lab machine** (the coordinator, 23 November 2026), since
  it changes the lab machine: a new local account outside Administrators with "Log on as a
  batch job", in `lab.local.json` as `workerAccount`, which never signs in, so it has no
  profile, as one that `install` prepares; "Replace a process level token" and "Adjust
  memory quotas for a process" for the lab user, in effect after a new logon, which an
  administrator's token does not use; and a `%TEMP%` of the lab user whose folders give
  nobody else Delete or full control, since every lab agent judges its program folder
  there (the coordinator, 23 November 2026, from S9c's code review, round 1).
- **Study.** Done, 23 November 2026; it stays in the coordinator's notes.
- **To read.** The [concept](../concept.md), D12, §5 and §12 (O1); `AGENTS.md`, principles
  4, 8 and 9; this plan, "The files of the agent"; the briefs of S9 and S9b.
- **Acceptance.** The tests above, the lab's on a real data folder as S9b's set it. No
  manual line: the real service is S10's (the coordinator, 23 November 2026), whose
  console writes its data folder.
- **The lab runs** (the owner, 23 November 2026), on the lab machine with 1C:Enterprise
  8.3.17.1851, `outpost-worker` in Users with "Log on as a batch job", the two rights for
  the lab user, `workerAccount` in `lab.local.json`:
  - 106e58f: 71 of 76. The lab agent removed its program folder while its workers still
    ran, and a worker that did not open gave no reason; both were faults of the lab tests.
  - bc53229: 73 of 76. The worker under the worker account ended with 0xC0000142: a .NET
    program loads user32 before `Main` and could not connect to the window station that
    `lpDesktop ""` names. Fixed by the desktop of the agent's own (above).
  - 849574f: 76 of 76, in 8 min 24 s. A document of the typical copy was posted again under
    `outpost-worker` with its movements unchanged, a Write without safe mode ran under that
    account, and it could not read `secrets.dat`.
- **Out of scope.** The console's `install`, which prepares the account (S10); a domain
  account; a service account that Windows manages.

### S10 · The console

**Track:** full · **Branch:** `epic-1/sprint-10a-console-setup`, then
`epic-1/sprint-10b-console-check`, then `epic-1/sprint-10c-console-metadata` · **Needs:** S9c,
S8.

- **The cut** (the coordinator, 23 November 2026, under the planner's standing instruction
  to cut a sprint too large for one exhaustive review). S10 runs as two sprints, each with
  its own pull request:
  - **S10a, the setup**: the console project, `install`, `uninstall`, `pair`, `secret` with
    its form for the worker account, and `connector register`, with the finder of the
    connector that `check` uses later. Pairing, secrets and the access lists are strict
    zones, so its test author and implementer work on `xhigh`. Its Acceptance is the
    automated lines of the commands it builds. It has no lab line: the manual run of its
    commands and S9c's manual line run on the lab with S10c, which needs a paired agent
    anyway, so the owner goes to the lab once for S10.
  - **S10b, the check**: `check` with every line, the entries of the architect's reviews
    joined to it, and its tests in `Moltaro.OnecOutpost.OneC.Tests`, written and built but
    run on the lab with S10c. Its internal contract is written after S10a is merged, with
    what S10a taught. Its test author and implementer work on `xhigh`, since `check` reads
    the secrets, logs the worker accounts on and signs an `outcome` with the agent's key.
    It has no lab line of its own, as S10a has none.
  - **S10c, the metadata report and the guide** (the coordinator, 24 November 2026, from
    S10b's study, under the planner's standing instruction to cut a sprint too large for
    one exhaustive review): `metadata`, read in pages through the executor; guide G1; and
    the one lab run of S10, which runs the tests of `check` and of `metadata` in
    `Moltaro.OnecOutpost.OneC.Tests` and the manual lines of the Acceptance, the run of
    every command, S9c's line and the installation by G1 alone. Its branch is
    `epic-1/sprint-10c-console-metadata`.
- **A draft pull request before the close** (the owner, 23 November 2026, for S10a only):
  S10a's tests with `Requires=Administrator` run only in CI's elevated `windows` job, so
  after the implementation step its branch is pushed and its pull request opened as a
  draft; every later fix is pushed to it, never force-pushed, and it is marked ready and
  merged only after the close, under PQ1. A later sprint that needs the same asks again.
- **From the architect's review 1** (the whole code base after S8; the planner, 23
  November 2026):
  - *S10a's documents step:* explanation E1, both forms in one change, still says that a
    Write without safe mode is refused and that a script always runs in safe mode; it is
    brought up to S9c.
  - *S10a's contract, with the fixes of its first review round:* one finder of the COM
    connector, in the agent, which the launcher and the console both use: the classes of
    HKLM in each registry view, never HKCR, a registration counting only when its file
    exists. The agent's behavior changes: a registration whose file is gone no longer
    picks that bitness. It is a draft for the owner and has a test of its own.
  - *S10b, before the code that calls them,* each keeping what the architect's list says
    must stay unchanged:
    - `ComWorkerLauncher`'s three steps, finding the connector, logging a worker account on
      and judging it, and creating the worker's process, get one home each, the first in
      the agent's `Connector` since S10a and the others in `Workers`, which the launcher
      composes and `check` calls alone; the copies in the lab tests go.
      A strict zone: its implementer and reviewer work on `xhigh`;
    - the checks of `AgentStart` become internal and free of the open audit log, whose lock
      the running service holds, so that `check` reports each; the layout of the data
      folder, its path, `audit`, `outcomes` and the three file names, is named once;
    - the answer to an `outcome` becomes a record that carries the answer's time and the
      reason of a refusal, computed once, which the server's line and the skew of `check`
      need;
    - the connection line of a worker and the judgment of its output move out of
      `WorkerPool` into statics, from `WorkerConnectionLine.For`, so that `check` and
      `metadata` run a worker outside a pool.
- **From the architect's review 2** (after S10a; the planner, 23 November 2026), for
  S10b, joined to the entries above:
  - with the answer to an `outcome` as a record: the console's sentences for an answer it
    does not accept, today private to `pair`, are one table that `pair` and the server's
    line of `check` read, and the record names its cause in the same terms; S10b's
    contract author chooses whether pairing's enum or the reason of §9 is the shared
    cause;
  - with the launcher's steps: the agent's two privileges are named once, in the agent's
    `Workers`, for the launcher and the console, and `ServiceAccount.Name` replaces the
    three spellings of `NT SERVICE\{name}`;
  - with the checks of `AgentStart`: `AgentFolders.OnDisk(dataFolder)` and the layout of
    the program folder (`x64\`, `x86\`, the worker's file, the executor) are named once
    too, since the console spells the data folder again and `check` needs `audit\`,
    `outcomes\` and the program folder;
  - one wording, with one remedy that exists, for the reasons of `started` and of
    `check` where they share a step: today the start's reasons for `secrets.dat` say to
    write the secrets with the console, which `secret` refuses then, and the launcher's
    reason for an administrator as the worker account names `install`, which touches no
    worker account. The change of the audit log's text is a draft for the owner.
- **Q14** (the planner, 23 November 2026): the rule stays strict. A file of the data
  folder whose owner is not Administrators, SYSTEM or the agent's account counts as
  somebody else, since an owner can always change the file's access list. `install` makes
  Administrators the owner of the data folder and of every file it writes, and so do `pair`
  and `secret` (S10a); `check` names a file whose owner is someone else and gives the
  remedy (S10b); G1 shows how to edit `policy.json` without changing its owner, and the
  one command that restores it (S10c).

- **Goal.** The administrator does everything for an agent from the console:
  - installs the service under its virtual account, with the data folder and its access
    list, and uninstalls it;
  - pairs it and writes its secrets;
  - checks the agent machine;
  - writes a metadata report of a base.
- **Contract.** As [Stages 2 and 3](#the-contracts-of-stages-2-and-3) says, and `secret`
  gains a form for the password of a base's worker account, with S9c's drafts for
  `install` (the two privileges, the batch right, `%ProgramFiles%`, the folder's access
  list and owner) and for `check` (the connector's folder). The metadata
  report starts from the owner's collector and rewrites it to the rules of this repository
  (PQ5).
  - *Internal of S10a* (the contract author, 23 November 2026, from S10's study, with the
    coordinator's decisions under PQ2; reviewed in two rounds; approved by the coordinator
    under PQ2, 23 November 2026, with round 2's fixes, which no further round reviews).
    Every type is `internal`; `Commands` uses the agent and `Service`, `Rights`, `Folders`
    and `Connector`, which use nothing of each other. `sid` is `ServiceAccount.Sid(ServiceName)`.

    ```csharp
    // Moltaro.OnecOutpost.Agent.Connector, the one finder of the COM connector, which the launcher and the console use
    internal enum ConnectorBitnessEnum { X64, X86 }
    internal sealed record ConnectorFile(string Path, string Version);
    internal sealed record ConnectorFinding(ConnectorBitnessEnum Bitness, ConnectorFile? Registered, IReadOnlyList<ConnectorFile> Unregistered);
    internal interface IConnectorFinder { IReadOnlyList<ConnectorFinding> Find(); }
    // ComConnector, V83.COMConnector, moves here; Machine() is HKLM's SOFTWARE\Classes in both views and both 1cv8 folders.
    [SupportedOSPlatform("windows")]
    internal sealed class ConnectorFinder(RegistryKey classes64, RegistryKey classes32, string progId, IReadOnlyList<string> platformFolders) : IConnectorFinder { }
    internal static class ServiceAccount { public static string Sid(string serviceName); } // Agent.Access; computed without Windows

    // Moltaro.OnecOutpost.Console.Commands. A command takes (IAnsiConsole, ConsoleParts); its own settings in {Command}.Settings.cs.
    internal sealed record ConsoleParts(bool Elevated, string ServiceName, string Source, string ProgramFolder, string DataFolder,
        IAgentFolder Data, IServiceControl Services, IAccountRights Rights, IFolderSetup Folders, Func<string, IAccessLists> AccessLists,
        IMachineProtection Protection, IConnectorFinder Connectors, IConnectorRegistration Registration,
        Func<Uri, Uri?, HttpMessageHandler> Handlers, TextReader? Input, TimeProvider Clock, string Version); // Input: null for a console
    internal static class ConsoleApp { public static void Configure(IConfigurator config, ConsoleParts parts); }
    internal static class ConsoleExitCodes { } // the table below; StopTimeoutConverter : TypeConverter, by TryReadTimeSpan
    internal sealed class ServiceSettings : CommandSettings { public TimeSpan StopTimeout { get; init; } } // --stop-timeout <TIME>
    // InstallCommand and UninstallCommand : AsyncCommand<ServiceSettings>; PairCommand : AsyncCommand<PairCommand.Settings>,
    // <SERVER> <TOKEN>; SecretCommand, <BASE> <KIND>, likewise; ConnectorRegisterCommand : AsyncCommand<EmptyCommandSettings>.

    // Moltaro.OnecOutpost.Console.Service
    internal enum ServiceStopEnum { NotInstalled, Stopped, TimedOut }
    internal interface IServiceControl
    {
        bool Install(string name, string displayName, string binaryPath); // true when created, false when reconfigured
        Task<ServiceStopEnum> StopAsync(string name, TimeSpan timeout, CancellationToken cancellationToken);
        void Delete(string name);
    }
    [SupportedOSPlatform("windows")] internal sealed partial class WindowsServiceControl : IServiceControl { public WindowsServiceControl(TimeProvider clock); }

    // Moltaro.OnecOutpost.Console.Rights
    internal static class AccountRightNames { } // AssignPrimaryToken, IncreaseQuota, BatchLogon: the names LSA gives them
    internal interface IAccountRights
    {
        string? UserSid(string account); // a local user's, by LookupAccountNameW; null for none, a group or an alias
        void Grant(string sid, IReadOnlyList<string> rights);
        void Revoke(string sid, IReadOnlyList<string> rights);
    }
    [SupportedOSPlatform("windows")] internal sealed partial class WindowsAccountRights : IAccountRights { public IReadOnlyList<string> Held(string sid); }

    // Moltaro.OnecOutpost.Console.Folders
    internal enum FolderKindEnum { Absent, Folder, ReparsePoint }
    internal interface IFolderSetup
    {
        FolderKindEnum Kind(string path);
        string FinalPath(string folder); // by GetFinalPathNameByHandleW; the full path of one that does not exist
        void ApplyProgramFolder(string folder, string? source); // null: the lists alone
        void ApplyDataFolder(string folder, string agentSid, bool create);
        void OwnByAdministrators(string file);
    }
    [SupportedOSPlatform("windows")] internal sealed class WindowsFolderSetup : IFolderSetup { }

    // Moltaro.OnecOutpost.Console.Connector
    internal interface IConnectorRegistration
    {
        string CommandOf(ConnectorBitnessEnum bitness, string path);
        Task<int> RegisterAsync(ConnectorBitnessEnum bitness, string path, CancellationToken cancellationToken); // regsvr32's code
    }
    [SupportedOSPlatform("windows")] internal sealed class RegSvr32Registration : IConnectorRegistration { }
    ```

    - **The program** gives `NotWindows` elsewhere, else runs a `CommandApp` with: `Elevated`
      by `WindowsPrincipal.IsInRole`, false for UAC's filtered token; `MoltaroOnecOutpost`;
      `AppContext.BaseDirectory`; `%ProgramFiles%` and `%ProgramData%`'s `Moltaro.OnecOutpost`;
      `AccessLists` making the string a `SecurityIdentifier`; `ConnectorFinder.Machine()`; a
      redirected standard input as UTF-8, a byte order mark skipped; its product version.
      Whether it is interactive, and so prompts, is Spectre's own detection, as `AGENTS.md`
      asks. `RunAsync` gets a token that `Console.CancelKeyPress` cancels, setting `Cancel`,
      which each command checks before each step, so Ctrl+C ends it between two steps.
      `Configure` registers the parts, sets `UseStrictParsing` and the name, and by
      `SetExceptionHandler` gives `Cancelled` for an `OperationCanceledException` whose token
      is cancelled, `Usage` for Spectre's parse and validation errors, `Failure` for any
      other. Waits show a spinner.
    - **Output.** A step done writes one line; any other end writes, in red, `{Kind}
      ({code}): {reason}`, a failed step `Failure (1): the step "{step}" failed: {message}`
      and `(Windows error {code})`. Nothing writes a password, a token or unverified text.

      | Code | Constant | Commands | When |
      | --- | --- | --- | --- |
      | 0 | `Success` | all | Done, `--help` too |
      | 1 | `Failure` | all | A step failed for a reason of Windows or of the disk, or an unexpected exception |
      | 2 | `Usage` | all | An unknown command or option, a missing or invalid argument or password, `install` run from inside the program folder or from a folder that holds it |
      | 3 | `NotElevated` | all | Not run as administrator; nothing was changed |
      | 4 | `NotWindows` | all | Not on Windows |
      | 5 | `Cancelled` | all | Ctrl+C |
      | 6 | `NotInstalled` | `uninstall`, `pair`, `secret` | The service, or the data folder, does not exist |
      | 7 | `StopTimedOut` | `install`, `uninstall` | The service did not stop within `--stop-timeout`; nothing further was done |
      | 8 | `ProgramFolderWriters` | `install` | Someone else may change the program folder |
      | 9 | `DataFolderAdmitsOthers` | `install`, `pair`, `secret` | The data folder admits someone else, or is a reparse point |
      | 10 | `PolicyInvalid` | `pair`, `secret` | `policy.json` is absent, unreadable or invalid |
      | 11 | `AlreadyPaired` | `pair` | `agent.json` exists |
      | 12 | `PairingFailed` | `pair` | Anything but a verified `200` |
      | 13 | `NotPaired` | `secret` | `secrets.dat` is absent |
      | 14 | `NotInPolicy` | `secret` | The policy names no such base, mode or worker account |
      | 15 | `UnknownAccount` | `secret` | The worker account is not a local user |
      | 16 | `NotInteractive` | `secret` | The input is the console, the output redirected |
      | 17 | `ConnectorNotRegistered` | `connector register` | No bitness ends with a registered connector: several installed, or none |
      | 18 | `RegistrationFailed` | `connector register` | regsvr32 failed, or HKLM shows no registration after it |

    - **`install [--stop-timeout <TIME>]`**, `TIME` in `policy.json`'s constant form, `00:00:01`
      to `01:00:00`, `00:01:00` when absent; each step idempotent, named in its failure:
      1. *elevation*: `install needs a console run as administrator; nothing was changed`;
         a `Source` whose `FinalPath` lies inside that of `ProgramFolder` gives `Usage`,
         `unpack the archive outside {folder} and run install from there`; one whose
         `FinalPath` holds that of `ProgramFolder` gives `Usage` too, with a message of its
         own that says to unpack the archive into a folder of its own outside
         `%ProgramFiles%`;
      2. *stop*: `StopAsync`; `TimedOut` gives `the service {name} did not stop within
         {timeout}; run install again once it has, or with a longer --stop-timeout`;
      3. *program folder*: `ApplyProgramFolder(ProgramFolder, source)`, `source` null for
         equal final paths, `Copied the program to {folder} and set its access lists.` or
         `Set …`; then `AccessLists(sid).Writers(ProgramFolder)`, before the service is registered or reconfigured,
         entries giving `ProgramFolderWriters` with a table of them;
      4. *service*: `Install(name, "Moltaro OnecOutpost agent", "\"{ProgramFolder}\Moltaro.OnecOutpost.Agent.exe\" service")`,
         `Registered` or `Reconfigured the service {name} under NT SERVICE\{name}.`; 1072 gives
         `Failure` and `close the Services console and Event Viewer, and run install again`;
      5. *rights*: `Grant(sid, [AssignPrimaryToken, IncreaseQuota])`, `Granted NT SERVICE\{name}
         "Replace a process level token" and "Adjust memory quotas for a process".`;
      6. *data folder*: `Kind` of the folder, `audit` and `outcomes`; a `ReparsePoint`, or
         the accounts of `Others` of a `Folder`, give `DataFolderAdmitsOthers`, `{entry} of
         {folder} is a reparse point | admits {accounts}; install does not repair it: inspect
         it, remove it and run install again`; then `ApplyDataFolder(DataFolder, sid,
         create)`, `create` when `Absent`: `Created` or `Set the access lists of {folder}.`

      Then `Installed; the service is not started. Start it once policy.json, the pairing
      and the passwords are written; an update keeps those written before.`
    - **`uninstall [--stop-timeout <TIME>]`**: elevation; `StopAsync`, `NotInstalled` giving
      `NotInstalled`, `… nothing was changed`, `TimedOut` `StopTimedOut`; `Revoke(sid, [the
      two])`, `Delete`; then `Removed the service {name} and its account's two rights. Left
      {both folders}, with the audit log; the worker accounts keep "Log on as a batch job".`
    - **The steps `pair` and `secret` share**, written once: elevation; the data folder's
      `Kind`, `Absent` giving `NotInstalled`, `… run install first`, and `ReparsePoint`
      `DataFolderAdmitsOthers`; `AccessLists(sid).Others("")`, accounts giving
      `DataFolderAdmitsOthers`, since DPAPI for the machine lets every local process decrypt
      and only the folder's list keeps the secrets (*limitation:* an entry that only the
      folder's content inherits, which only an administrator can add, reaches a file written
      there, which the agent's start then refuses); `policy.json` by `ReadFile`,
      `PolicyInvalid` with the agent's reason. Files go by `Data.Replace`, then `OwnByAdministrators` (Q14).
    - **`pair <SERVER> <TOKEN>`**, `SERVER` absolute and allowed by `ServerRoute.IsAllowed`,
      `TOKEN` read by `OutpostPairingTokenText.TryRead`, else `Usage` naming the rule and
      quoting neither. After the shared steps:
      1. `agent.json` exists: `AlreadyPaired`, `the agent is paired; to pair it anew,
         remove agent.json; what its outbox holds then reaches the server under the new
         identity, which drops it as another agent's (§6.3); pairing anew writes secrets.dat
         with the new key alone, so the passwords are written again with secret`;
      2. `AgentPairing.PairAsync` with `Version`, `Handlers(server, policy.Proxy)` and
         `policy.RequestTimeout`; a failure gives `PairingFailed`, `HTTP {status}: ` when one
         came and the reason by `AgentPairingFailureEnum` in §6.1's words, writing nothing;
      3. `secrets.dat` with the new key alone, replacing one a failed pairing left, then
         `agent.json`, which so exists only with its key; a failure gives `Failure`, `the server
         paired the agent, but {file} could not be written: {message}; the application revokes
         it and gives a new token`. Then `Paired with {server} as {id}; write the passwords with secret.`
    - **`secret <BASE> <KIND>`**, `BASE` by `OutpostNames.IsBaseKey`, `KIND` exactly `read`,
      `write` or `worker`, else `Usage`. After the shared steps: `secrets.dat` absent gives
      `NotPaired`, `… pair the agent first`, one that does not read or decrypt `Failure`;
      `NotInPolicy`: `the policy names no base {key} | does not let it be written | names no
      worker account for it`. For `worker`: `UserSid("{machine}\{workerAccount}")`, null
      giving `UnknownAccount`, `{account} of the base {key} is not a local user of this
      machine`, then `Grant(sid, [BatchLogon])`, `Granted {account} "Log on as a batch job".`
      The password: when `Profile.Capabilities.Interactive`, a `TextPrompt<string>` with
      `Secret(null)`, `Password of {account} for {the read mode | the write mode | the
      workers} of the base {key}:`; else one line of `Input`; else, `Input` being null,
      `NotInteractive`, `run secret in a terminal, or give the password through standard
      input`, reading nothing. An empty password is one for `read` and `write`, a real case
      of 1C, and `Usage` for `worker`, whose blank password Windows refuses outside its
      console (the coordinator, 23 November 2026), as is the end of the input. Every other
      secret is kept: `Wrote the password …; the service uses it from its next start.`
    - **`connector register`**: elevation; then per finding, `{bits}-bit: {path}, {version},
      is registered.`, or without `Registered`: one `Unregistered` is registered and `Find`
      read again, `{bits}-bit: registered {path}, {version}.`, else `RegistrationFailed` with
      regsvr32's code and kind (1 arguments, 2 `OleInitialize`, 3 loading the DLL, 4 no
      `DllRegisterServer`, 5 `DllRegisterServer` failed) or `regsvr32 ended with 0, yet HKLM
      shows no registration`; several, `… none is registered, and the connector has to match
      the version of the 1C cluster, so register that one:` and `{version}: {CommandOf}`
      each; none, `… none is registered or installed`. The exit is `RegistrationFailed` when
      a run of regsvr32 failed, else `ConnectorNotRegistered` when no bitness ends with a
      registered connector, else `Success`.
    - **`WindowsServiceControl`**, by `LibraryImport`, failing with `Win32Exception`, its
      handle with `SERVICE_START`. `Install`: `ChangeServiceConfigW` of an existing service,
      never a delete, keeping its failure actions; else `CreateServiceW` (own process,
      automatic start, `NT SERVICE\{name}`, no password) with the failure actions, class
      constants: restart after 1 minute, three times, reset after a day, the flag for a stop
      with an error false; both set the description and no required privileges. `StopAsync`:
      1060 `NotInstalled`; `SERVICE_CONTROL_STOP`, 1062 stopped, 1061 waiting;
      `QueryServiceStatusEx` every tenth of the wait hint, 1 to 10 seconds, until stopped or
      the timeout by the clock. `Delete`: 1072 counts as done.
    - **`WindowsAccountRights`**: `LsaOpenPolicy(POLICY_LOOKUP_NAMES | POLICY_CREATE_ACCOUNT)`;
      `LsaAddAccountRights`, idempotent; `LsaRemoveAccountRights` without `AllRights`, an
      account unknown to LSA no error; `Held` by `LsaEnumerateAccountRights`.
      **`ServiceAccount.Sid`** is `S-1-5-80-` and the SHA-1 of the upper-cased name in
      UTF-16 as five sub-authorities, as Windows derives it.
    - **`WindowsFolderSetup`** makes Administrators the owner of every entry it writes, as an
      elevated token may. `ApplyDataFolder` creates, when `create`, the folder by
      `CreateDirectoryW` with `O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;FR;;;{agent})`,
      which fails with 183 when the folder exists, giving `Failure`, so that only a folder
      judged by `Others` is ever applied to; otherwise it applies that descriptor; `audit\`
      and `outcomes\` inherit it plus `(A;OICI;0x1301bf;;;{agent})`, created so or applied,
      files untouched. `ApplyProgramFolder` makes or applies
      `O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;0x1200a9;;;BU)`; with a `source`, it
      empties the folder and copies `source`'s content in; then every entry gets
      Administrators as owner and only inherited entries.
    - **`ConnectorFinder`** never reads HKCR, which merges in the administrator's own classes,
      unseen by the service. Per view, 64 bits only on a 64-bit Windows, `{progId}\CLSID` and
      `CLSID\{class}\InprocServer32` give the path, expanded, `Registered` only when its file
      exists. `Unregistered`: each other `{platformFolder}\*\bin\comcntr.dll` by ordinal
      path, its bitness by the PE header's `Machine` (0x8664, 0x14C), its version its
      `FileVersion`. `Find` throws nothing and may run on several slots at once: a key,
      folder or file it cannot read is not `Registered`, or left out of `Unregistered`, as the
      launcher reads today; a platform installed elsewhere is found only by its registration.
      `ComWorkerLauncher` takes an `IConnectorFinder` for its ProgID and starts the first
      `Registered` finding's program, 64 bits first, nothing else changing.
      `RegSvr32Registration` runs `%SystemRoot%\System32\regsvr32.exe /s "{path}"`, `SysWOW64`'s
      for 32 bits, with the token and no deadline; `CommandOf` omits `/s`.
    - **Tests on every platform**, `Moltaro.OnecOutpost.Console.Tests`, by `CommandAppTester`
      with `ConsoleApp.Configure` and fakes recording their calls in one list, the agent's
      tests' moved to `TestSupport`: each command's steps in order, each failure at its step
      with its code and line and nothing after; `install` from the program folder and from inside it, `--stop-timeout` at
      both bounds and `00:00:00`, `01:00:01` and `1:00`; `pair` against a stub of the pair
      endpoint signed with a test key, the policy's proxy and `requestTimeout` on a fake
      clock, each `AgentPairingFailureEnum`, the stub's unverified body never in the output;
      `secret` by an interactive `TestConsole`'s prompt and by `Input`, every other secret
      kept, the batch right first, `NotInteractive`, the password never in the output;
      `Usage`, `Failure`, and `Cancelled` by `RunAsync` with a cancelled token; in the agent's,
      `ServiceAccount.Sid` of TrustedInstaller. `NotWindows` has no test, as the agent's has
      none: no platform runs both branches.
    - **Tests with `Requires=Windows`**, in the agent's tests: `ConnectorFinder` over two test
      keys under HKCU and a temporary platform folder holding the x64 and x86 worker programs
      as `comcntr.dll`; no worker for a registration whose file is gone, `TestConnector` now
      registering a file that exists. In the console's: the built console as a child process
      with redirected streams, as `ProgramTests` run the agent, each command with `--help`
      and with one refused argument, which change nothing, each its code and no `\x1b[`.
    - **Tests with `Requires=Windows` and `Requires=Administrator`**, each with its own
      service `MoltaroOnecOutpostTest-{guid:N}`, whose virtual account stands for the
      agent's and gets every right, the batch right too, so that no local user is made, and
      its own folders, the program folder under `%ProgramFiles%`, whose parents admit
      nobody else, all removed in `finally`: `Install` read back by the test's own
      `QueryServiceConfigW` and `QueryServiceConfig2W`, and again; 1072 while the test
      holds a handle of a deleted service; `StopAsync` of a stopped and an unknown service;
      `ServiceAccount.Sid` as Windows translates it; `Grant` and `Revoke` by `Held`; both
      folders by their SDDL and `WindowsAccessLists`; `install` then `uninstall` through
      `CommandAppTester`. Stopping a running service is the fakes' and S10c's lab's.
    - **The steps.** Contract: the two projects, on Spectre.Console and Spectre.Console.Cli
      0.57.2, the tests also on their Testing packages 0.57.2. Settings, validation,
      `ConsoleExitCodes`, `ConsoleApp`, `Program` and `ServiceAccount` work; the commands and
      the Windows implementations throw `NotSupportedException`. Edits that state nothing
      new, made there: `Find` gives each
      view's `Registered` from the classes it is given as the launcher reads them today,
      without the file check, and no `Unregistered`, since it replaces a working member; the
      file check and `Unregistered` come with the implementation, their tests skipped until
      then; `WindowsAccessLists(string dataFolder, SecurityIdentifier agent)`, the agent's
      `Program` giving its own account, other callers the current user's and
      `ProgramWritersTests` ANONYMOUS LOGON's, so `AnonymousLogon` goes, and TrustedInstaller's
      SID from `ServiceAccount.Sid`; `AgentPolicyReader.ReadFile(byte[])` takes `AgentStart`'s
      UTF-8 and byte order mark, `TryReadTimeSpan(string, out TimeSpan)` its constant form;
      `AgentStart`'s file names become `public const`; the agent's `InternalsVisibleTo` names
      the console and its tests; the launcher's callers pass a finder. Tests step:
      `AGENTS.md`, "Tests And Samples", after the line on time, gains "A test that needs the
      rights of an administrator, such as one that registers a service, grants a right
      through LSA or makes Administrators the owner of a file, has the trait
      `Requires=Administrator` with `Requires=Windows`. CI's `windows` job runs elevated and
      runs it; a developer whose shell is not elevated adds `--filter-not-trait
      "Requires=Administrator"`."; the comment of the `windows` job's test step in `ci.yml`
      gains "Its runner is elevated, so the tests with Requires=Administrator run here too."
      Documents: `README.md` names the setup commands; "The files of the agent" and the
      contract of stage 3 take the drafts below.
  - *Internal of S10b* (the contract author, 24 November 2026, from S10b's study, with the
    coordinator's decisions under PQ2; reviewed in two rounds; approved by the coordinator under
    PQ2, 24 November 2026, with round 2's fixes, which no further round reviews). Every type is
    `internal`. The console gains a boundary folder, `Workers`, which uses the agent alone.

    ```csharp
    // Moltaro.OnecOutpost.Agent.Start: the layouts named once (review 2), the start's steps free of the log (review 1, 2)
    internal sealed record AgentFolders(IAgentFolder Root, IAgentFolder Audit, IAgentFolder Outcomes)
    {
        // consts, one per line in code: Name "Moltaro.OnecOutpost", AuditName, OutcomesName, and PolicyName, PairingName and
        // SecretsName, which leave AgentStart
        public static string OnMachine();                     // %ProgramData%\Moltaro.OnecOutpost
        [SupportedOSPlatform("windows")] public static AgentFolders OnDisk(string dataFolder); // DiskFolders of the root, audit\, outcomes\
    }
    internal static class ProgramFolderLayout // consts WorkerFile, which leaves ComWorkerLauncher, and ExecutorFile, OutpostExecutor.epf
    {
        public static string WorkerOf(string programFolder, ConnectorBitnessEnum bitness); // {folder}\x64\ or \x86\, then WorkerFile
        public static string ExecutorOf(string programFolder);
    }
    internal static class StartChecks
    {
        public static IReadOnlyList<string> DataPaths(IReadOnlyList<string> rootNames, IEnumerable<string> auditFiles, IReadOnlyList<string> outboxFiles);
        public static string? AccessReason(string path, OtherAccounts others); // null when others IsEmpty
        public static string WritersReason(string path, IReadOnlyList<string> accounts);
        public static AgentPairingSettings ReadPairing(IAgentFolder root, IReadOnlyList<string> rootNames);                       // AgentStopException
        public static AgentSecrets ReadSecrets(IAgentFolder root, IReadOnlyList<string> rootNames, IMachineProtection protection); // AgentStopException
        public static AgentPolicy ReadPolicy(IAgentFolder root, IReadOnlyList<string> rootNames);                                 // AgentPolicyException
        public static IReadOnlyList<string> MissingPasswords(AgentPolicy policy, AgentSecrets secrets);                           // all, in the policy's order
    }

    // Moltaro.OnecOutpost.Agent.Access: IAccessLists.Others returns this, Writers is unchanged; ServiceAccount gains Name(serviceName)
    // IsEmpty: no other owner and no entry; All: the owner, then the entries, each once, Others' list of today
    internal sealed record OtherAccounts(string FullPath, string? Owner, IReadOnlyList<string> Entries) { public bool IsEmpty { get; } public IReadOnlyList<string> All { get; } }

    // Moltaro.OnecOutpost.Agent.Workers: the launcher's steps, one home each (review 1, 1; review 2, 3)
    internal static class AccountRightNames { /* its three consts, from the console */ public static readonly IReadOnlyList<string> AgentPrivileges; }
    internal static class WorkerAccountReasons { /* consts NotOrdinary, AgentLacksPrivileges */ public static string CannotLogOn(string baseKey, int error); }
    [SupportedOSPlatform("windows")]
    internal static partial class WorkerLogon
    {
        public static SafeAccessTokenHandle? LogOn(string account, string password, out int error); // LogonUserW, BATCH, DEFAULT
        public static bool IsOrdinary(SafeAccessTokenHandle token); // WorkerAccountRule over Groups and Privileges; Win32Exception
    }
    internal enum WorkerCallEnum { AsUser, WithToken } // CreateProcessAsUserW; CreateProcessWithTokenW through Secondary Logon
    [SupportedOSPlatform("windows")]
    internal static partial class WorkerCreation
    {
        public static (SafeProcessHandle Process, nint Thread) CreateSuspended(WorkerCallEnum call, SafeAccessTokenHandle token,
            string program, string commandLine, string desktop, SafePipeHandle input, SafePipeHandle output, SafePipeHandle? error);
        public static void JoinAndResume(WorkerJob job, SafeProcessHandle process, nint thread); // a failure kills, then throws
    }
    internal sealed class WorkerConnectionLine { public static WorkerConnectionLine For(AgentBasePolicy served, AgentModePolicy mode, string password, string executor); }
    internal sealed record JudgedOutcome(byte[] Outcome, bool EndsWorker);
    internal static class WorkerOutcome { public static JudgedOutcome Judge(OutpostModeEnum mode, (long Size, byte[] Outcome)? given); }

    // Moltaro.OnecOutpost.Agent.Transport (review 1, 4; review 2, 2): AgentPairingFailureEnum goes, AgentPairingResult.Failure takes the cause
    internal enum ServerAnswerCauseEnum { Unreachable, ProtocolVersion, ServerKey, Signature, Refusal, Bare, Answer }
    internal sealed record OutcomeAnswer(OutcomeResultEnum Result, DateTimeOffset? Time, int? Status, ServerAnswerCauseEnum? Cause, string? Code);

    // Moltaro.OnecOutpost.Console.Workers: the COM workers that check starts
    internal interface IWorkerStarts
    {
        IWorkerAccount LogOn(string baseKey, string account, string password); // Win32Exception with LogonUserW's code
        Task<uint?> ProbeAsync(IWorkerAccount account, string program, string baseKey, TimeSpan deadline, CancellationToken cancellationToken);
        CheckWorker Start(IWorkerAccount? account, string program, string baseKey); // read mode; null: the console's own token
    }
    internal interface IWorkerAccount : IDisposable { bool IsOrdinary(); string Desktop(); } // each a Win32Exception
    internal sealed record CheckWorker(IWorkerProcess Process, Task<string> Diagnostics);
    [SupportedOSPlatform("windows")] internal sealed class WindowsWorkerStarts(TimeProvider clock) : IWorkerStarts
    { public IWorkerAccount Account(string baseKey, SafeAccessTokenHandle token); } // the test author's seam, as Use was S9c's

    // Moltaro.OnecOutpost.Console.Commands. ConsoleParts: string AgentSid after ServiceName, Data an AgentFolders, IWorkerStarts
    // Workers after Registration. IAccountRights gains Held(string sid), today WindowsAccountRights' own.
    internal sealed partial class CheckCommand : AsyncCommand<CheckCommand.Settings> { } // Settings: TimeSpan ClockSkew
    internal enum CheckLineEnum { Connector, Connection, Subscriptions, ReadRights, Server, ClockSkew, WorkerAccount, AgentRights, WorkerStart, AccessLists, Files }
    internal sealed record CheckLine(CheckLineEnum Kind, string Subject, bool Passed, string Reason);
    internal static class ServerAnswerSentences { public static string Of(OutpostRequestKindEnum request, ServerAnswerCauseEnum cause, int? status, string? code); }
    internal abstract class TimeOptionConverter(string rule) : TypeConverter { } // StopTimeoutConverter and ClockSkewConverter derive
    ```

    - **The refactorings**, each keeping what the architect's list names:
      - *The launcher's steps.* `LogOn` is `WorkerLogon.LogOn`, `IsOrdinary`, the agent's token
        against `AgentPrivileges`, `Make` and `Use`; `Start` the finder, two pipes,
        `CreateSuspended(AsUser, …, error: null)` and `JoinAndResume`, a `Win32Exception` or
        `IOException` giving a `NoProcess`. `AsUser` is today's call, its handle list the ends
        given, two or three. `WithToken` is `CreateProcessWithTokenW`, logon flags 0, the flags
        but `EXTENDED_STARTUPINFO_PRESENT`, the token's environment, the program's folder,
        `STARTF_USESTDHANDLES`: the Secondary Logon service creates the process and gives it the
        standard handles alone; it acts as with `CREATE_NEW_CONSOLE`, so `CREATE_NO_WINDOW` is
        ignored and a console window opens on the worker's desktop [Win32 docs; the lab proves
        both]. Unchanged: the password reaches only `LogonUserW`, from native memory zeroed after
        it, and no message holds it; a worker of the agent inherits only its two pipe ends; it
        joins the job before its thread resumes, a process the job refuses killed; `LogOn`'s four
        checks keep their order.
      - *The start's checks*, in today's order: `Others` of each of `DataPaths(rootNames, log.Files,
        outbox names)`, an `IOException` today's `cannot be read`, the first `AccessReason` stopping
        it; the first of `Writers(programFolder)`; `ReadPairing`, `ReadSecrets`, the outbox, records
        and numbers, `ReadPolicy`, the first `MissingPasswords`, the logons; `IsAuditFile` becomes
        internal. Unchanged: the order; the log read and cut before any entry; `started` and
        `policy` with their reasons, but for the wording below; a failed start writes nothing else.
      - *The answer to an outcome*, computed once by §9's order: no status `Unreachable`, a
        proxy's `Answer`; a verified `200` `Confirmed` with its `time`; a `200` whose signature
        does not verify `Signature`, any other unverified `200` `Answer`; a verified refusal
        `Refusal` with its `time` and code, `TooLarge` for `result-too-large`; the unsigned
        `protocol-version` body `ProtocolVersion`; a 401, 403 or 413 without a signature that
        verifies `Bare`, the 413 `TooLarge`; else `Answer`. `AgentPairing` gives `Bare` for its
        403, today's `Refused`. Unchanged: §9's table for an outcome; answers to outcomes never
        change the connection; the loop reads `Result` alone.
      - *The pool's statics.* `For` is `LocationOf`, the quoting and the mode's `Account` and
        `SafeMode`; `Judge` is `Accept` without the `Drop`, which the pool makes for `EndsWorker`.
        Unchanged: the password only in the first line; an outcome accepted only when its size is
        its length, `ReadOutcome` reads it for the mode and `IsComWorkerOutcome` allows it, else
        `result-type`, Failed for a Read and Unknown for a Write; the drops of today.
      - *Names.* `SharedSteps.AgentPrivileges`, the launcher's constants and `LabLogon` go; `Name`
        replaces the three spellings, `OnDisk` and `OnMachine` the programs' paths, `LabAgent.Folders`
        and `DiskDataFolder`. Unchanged: the rights granted and revoked, the lines of `install`,
        `uninstall` and `secret`.
      - *One wording, with a remedy that exists* (review 2 and its note to the planner), for the
        reasons that `started` and `check` share; its change of the audit log's text is a draft:
        - `AccessReason`, both when both apply, the owner's first: `{entry} is owned by {owner},
          who can change its access list; make Administrators its owner with icacls "{FullPath}"
          /setowner *S-1-5-32-544, run as administrator` (Q14); for entries, today's sentence
          ending `: {remedy}`, for the folder, `audit` and `outcomes` `inspect and correct its access
          list as install sets it; never remove it, which holds {the audit log and the outcomes not
          yet sent | the audit log | the outcomes not yet sent}`, for a file `inspect it,
          then reset it to its folder's with icacls "{FullPath}" /reset, run as administrator`,
          and for `secrets.dat` also `; whoever it admitted could decrypt it: pair the agent anew
          and change the passwords it held`;
        - `WritersReason`: today's sentence ending `…the agent's account: remove their entries,
          and make Administrators the owner of what they own`;
        - `agent.json is absent: the agent is not paired; pair it with the console's pair`;
          `secrets.dat is absent | cannot be decrypted on this machine; to pair the agent anew,
          remove agent.json, if any, and run the console's pair`, since `pair` refuses while
          `agent.json` exists; `MissingPasswords`, `…; write it with secret {key} read | write |
          worker`; `pair` and `secret` read through `StartChecks`, their copies going, their codes kept;
        - `NotOrdinary`, `is an administrator or holds a privilege beyond an ordinary user's; take
          it out of Administrators and of every group that grants a privilege, or name another
          local user in the policy`; `AgentLacksPrivileges`, `lacks "Replace a process level
          token" or "Adjust memory quotas for a process"; install grants them, and the service has
          them from its next start`;
        - `CannotLogOn`, the start's step 1 too: 1326 `unknown account or wrong password; check
          workerAccount in the policy, or write the password again with secret {key} worker`;
          1385 `it lacks "Log on as a batch job", or a "Deny log on as a batch job" names it;
          secret {key} worker grants the right`; 1909, 1331, 1793 `the account is locked out |
          disabled | expired`; 1330, 1907 `its password has expired | must be changed; set a new
          one and write it with secret {key} worker`; 1327 `an account restriction refuses it, such
          as a blank password outside the console`; 1328 and 1329 its logon hours and its
          computer; any other `{the Win32 message} (Windows error {code})`.
    - **`check [--clock-skew <TIME>]`**, `TIME` as `--stop-timeout`'s, `MinClockSkew` to
      `MaxClockSkew`, `00:02:00`, the server's default, when absent, since the console cannot read
      the server's (principle 10), else `--clock-skew: expected a time of the form [d.]hh:mm:ss
      from 00:00:01 to 01:00:00, such as 00:02:00`. It writes nothing to a base or the data folder, runs Reads
      alone, never polls, and works while the service runs. Steps, each behind a spinner, the
      token checked before each: elevation; `Kind(DataFolder)`, `Absent` giving `NotInstalled`,
      `the data folder {folder} does not exist; run install first`; `Files`, `Connector`,
      `AccessLists`, `AgentRights`; per base `WorkerAccount`, `WorkerStart`, `Connection` read then
      write, `Subscriptions`, `ReadRights`; `Server`, `ClockSkew`. A line whose input failed fails
      as `not checked: {subject} failed`. `WorkerStart` and each `Connection` take as inputs the
      `AccessLists` rows of the program folder and of the connector's folder of the bitness they
      start, a `Connection` its base's `WorkerAccount` and `WorkerStart` too: `check` never starts
      code that somebody else may have changed. Then one table, `Check`, `Result` (`passed` green,
      `failed` red), `Reason`, by `CheckLineEnum`, the stage contract's order, then the policy's;
      `Every line passed.` and `Success`, else `CheckFailed (19): {n} of {m} lines failed`; the
      row `| 19 | CheckFailed | check | A line failed, or was not checked |`, `NotInstalled` gaining `check`.

      | Line, rows | Passed | Failed |
      | --- | --- | --- |
      | `Connector` | `{bits}-bit: {path}, {version}, is registered; the workers start with it`, the first `Registered`, 64 bits first, every other copy named | `none is registered`, each unregistered `{bits}-bit: {path}, {version}` named, `; register it with connector register` |
      | `Connection`, per base, read then write account | `opened as {user} ({fullName})` | `{program} did not start: {message} (Windows error {code})`; `1C opened no session: {diagnostics}`, or `the worker gave no reason`; `… within {runLimit}, the mode's runLimit`; `the script of the check ended {kind} {code}: {errorText}`, or `did not end within {runLimit}`; `the session's user is {user}, not {account} of the policy`; `the base has no users, so every connection has every right` |
      | `Subscriptions`, per base with `write` | `every event subscription that an external connection can raise has its common module available to it` | `a Write that reaches these fails: {subscription} ({event}) in {module}, …; set "External connection" of each module`, or `{module}, which is not a common module` |
      | `ReadRights`, per base | `{account} holds no right to change data` | `{account} holds rights to change data: {global rights}; {right} on {count} {category}, …: {first ten full names} and {n} more` |
      | `Server` | `{server} confirmed an outcome of a made-up command from {agent}: it is reachable and accepts the agent's key` | `ServerAnswerSentences.Of(Outcome, …)` |
      | `ClockSkew` | `the server's clock is ahead of this machine's by {skew} ms ± {uncertainty} ms, within {allowed}`, or behind | `… beyond {allowed}; with the server ahead, the agent refuses every command with clock-skew, and with it behind, with expired once the gap also passes the take timeout: set the clocks`; `cannot be judged within {allowed}: the round trip took {rtt} ms` |
      | `WorkerAccount`, per base with a `workerAccount` | `{account} logs on for a batch job, and its token is an ordinary user's` | `CannotLogOn`, for a token that cannot be read too; `{account} {NotOrdinary}` |
      | `AgentRights` | `{the account of AgentSid, as Windows names it} holds "Replace a process level token" and "Adjust memory quotas for a process"` | `… {AgentLacksPrivileges}`; `… cannot be read: {message} (Windows error {code})` |
      | `WorkerStart`, per `workerAccount` | `the worker started under {account} on {desktop} and ended at the end of its input` | the probe's sentences below |
      | `AccessLists`: the data folder, the program folder, each registered connector's folder | `admits nobody else, nor do its {n} files`; `nobody else may change it` | every `AccessReason` or `WritersReason`, one per line; `{folder} is a reparse point; inspect it and correct it, never remove what it holds` |
      | `Files`: `policy.json`, `agent.json`, `secrets.dat` | `{n} bases`; `paired with {server} as {agent}`; `decrypts and holds every password the policy needs` | the reason of `ReadPolicy`, `ReadPairing` or `ReadSecrets`, and every `MissingPasswords` |

      The audit files are `Audit.Names()` by `IsAuditFile`, the outbox files `Outcomes.Names()`; a
      listed file gone before its list, a `FileNotFoundException`, counts as gone, the one
      difference from the start, which owns the folder. Reads share writing and `READ_CONTROL`
      shares all, so the service's lock changes nothing [Win32 docs]. *Limitations:* LSA shows
      `AgentRights` only a grant to the account itself, not through a group such as `NT
      SERVICE\ALL SERVICES`, and the service has a right granted after its start from its next
      start; a connector's folder and its parents are judged, not its siblings such as `conf`.
    - **The probe**, `ProbeAsync`, after its account's line passed: `Desktop()`, which makes
      `WorkerDesktop.Make(baseKey, token)` as it is, on the console's own `Service-0x…$` station,
      once per account; two pipes; `WithToken`, `"{WorkerOf}" {base key} write`, `lpDesktop` that
      name; the console's end of the input disposed before the resume, so the worker reads null
      and ends with `WorkerExitCodes.Ended` before any COM call; no standard error;
      `JoinAndResume` into a `WorkerJob` of its own, since a process already in a job, the
      Secondary Logon service's or a terminal's, joins only an empty job or one of its hierarchy
      [Win32 docs]; its exit raced with `Task.Delay(deadline, clock)`, the write mode's
      `runLimit`, `TerminateProcess` at it; the output read to its end, awaited after the job is
      closed. Lines: 0 the passed one; 0xC0000142 `the worker failed to initialise on its desktop
      (STATUS_DLL_INIT_FAILED): the account or the desktop lacks access`; 1 `the worker refused its
      command line (exit code 1): the program folder holds another build`; another `the worker
      ended with exit code 0x{code:X8}`; null `the worker did not end within {runLimit} and was
      ended`; the call's 1314 `the console lacks "Impersonate a client after authentication"`,
      1058 `the Secondary Logon service is disabled; check does not start it`, 2 or 3 `{program}
      is missing`, 5 `{account} may not read or run {program}`; `Desktop()`'s `{account} cannot get
      a desktop: {message}`; any other `{message} (Windows error {code})`. One logon per account per
      run, never retried: each failure counts toward a lockout. *Limitations:* the probe runs in
      the console's session, so S9c's manual line stays the proof of session 0, and for its run a
      process of the worker account is in the administrator's session, on a station of the
      console's own, which keeps an ACE for the probe's logon until the administrator signs out.
    - **The connections**, `Start` with the first `Registered` bitness's `WorkerOf` and
      `ExecutorOf`: for a base without a `workerAccount` `null`, `AsUser` with the console's own
      token and `lpDesktop` `""`; else the account, `WithToken` on its desktop, as in the service.
      Three pipes, `"{program}" {base key} read`, a job of its own; `Diagnostics` the standard
      error read to its end as UTF-8, awaited after the job is closed, shown when the open gives
      false. The worker opens with `For(served, mode with { SafeMode = true }, password,
      executor)`, runs `WorkerCommandLine(script, "{}", null, OutpostProtocol.MaxOutcomeSizeLimit)`,
      the open and each run raced with the mode's `runLimit`, killed at it, each output by
      `Judge(Read, …)` and `ReadOutcome`. The read account runs three scripts, the second only for
      a base with `write`, the write account the first, in the English variant within
      compatibility mode 8.2.13 [1C docs; the lab proves each]: `Result = New Structure("user,
      fullName", UserName(), UserFullName())`, the user compared with the policy's account
      ignoring case; each of `Metadata.EventSubscriptions` but those to `FormGetProcessing`, which
      no external connection raises, whose module, the second part of `Handler`,
      `Metadata.CommonModules.Find` misses or finds without `ExternalConnection`, as
      `subscription`, `event`, `module`, `found`; `AccessRight(right, object)`, no third argument,
      each in a `Try`, a raise not held: `Administration`, `DataAdministration`,
      `UpdateDataBaseConfiguration` over `Metadata`; `Insert`, `Update`, `Delete` over catalogs, the
      three charts, exchange plans, business processes with `Start`, tasks with `Execute`, documents
      with `Posting` and `UndoPosting`; `Update` over constants, the four register kinds and
      sequences; as `global`, `categories` of `category`, `right`, `count`, and `objects` by full
      name. *Limitations:* the write account runs in Read and safe mode, so its executor without
      safe mode first opens at the service's first Write; each worker takes a client licence while
      open (O3); the console's token may reach a file base's folder that the agent's account may not.
    - **The server and the skew**: `ReadPairing`, `ReadSecrets`' key, an `AgentConnection` on
      `Handlers(server, policy.Proxy)`, `SendOutcomeAsync(Guid.NewGuid(), OwnOutcome.Write(Refused,
      not-addressed), policy.RequestTimeout)`, `t0` and `t1` by `Clock.GetUtcNow()` around it.
      `Server` passes for `Confirmed` alone. With a `Time`, of a confirmation or of a verified
      refusal, `skew = Time − (t0 + (t1 − t0) / 2)` and `uncertainty = (t1 − t0) / 2 + 1 ms`, the
      millisecond of §2: passed when `|skew| + uncertainty ≤ allowed`, beyond when `|skew| −
      uncertainty > allowed`, else not judged; without one, `not checked: no verified answer came`.
      `ServerAnswerSentences.Of` is pair's sentences as they are, `HTTP {status}: ` first when one
      came, and for an outcome: `Refusal` `signature`, `the server does not verify the agent's
      signature: secrets.dat and agent.json come from different pairings`, `form`, `the server
      refused the form of the request`, `result-too-large`, `the server refused even this small
      outcome for its size`; `Bare` 401 `the server knows no such agent, or it is still pending`,
      403 `the agent is revoked, or something on the path refused it`, 413 `something on the path
      refused even this small body`; `Answer` `an answer that is not a signed answer of §6.3 or §9
      of the protocol`. Nothing quotes a body.
    - **Tests on every platform**, `Moltaro.OnecOutpost.Console.Tests`, folder `Check`, by
      `CommandAppTester` on `TestMachine` with a scripted `FakeWorkerStarts`, `FakeAccountRights.Held`
      and a stub of the outcome endpoint signed with test keys: every line passed, in order,
      `Success`; each failed sentence above at its line, with `CheckFailed` and the lines that need
      it not checked; a program or connector's folder that somebody else may change: no worker
      starts; one logon per account; `AsUser` in safe mode, the account without; every command line
      `read` with `SafeMode` true, the data folder only read; a file gone between listing and list;
      every missing password; no poll; each `ServerAnswerSentences` row, never the body; the skew
      ahead, behind, beyond and not judged, the stub moving the fake clock; `--clock-skew` at both
      bounds, `00:00:00`, `01:00:01`, `2:00`, its default `new OutpostServerOptions().ClockSkew`;
      no password in any output. `check` joins the theories of `ElevationTests`, `RunEndTests`
      (help, a cancelled run), `DataFolderStepTests` (an absent data folder) and
      `RedirectedOutputTests` (`check --help`, `check --clock-skew 00:00:00`); `Pair/*` unchanged. Agent's:
      `OutcomeAnswerTests` per row; `WorkerAccountReasons` as a table; `Name`; the reworded
      reasons; `OnDisk` with `Requires=Windows`. `ComWorker.Tests`: the diagnostics come before
      `{"ready":false}` and the worker's `base-unreachable` line.
    - **Tests with `Requires=Windows`**: `AsUser` with three ends, the error end carrying the test
      program's diagnostics; with `Requires=Administrator`, the probe of the own token by `Account`, 0, a missing program 2.
    - **Lab tests**, `Moltaro.OnecOutpost.OneC.Tests`, folder `Console`, built in S10b and run with
      S10c, by `CommandAppTester` with `LabConsole.Parts(agent)`: `Program.MachineParts` for the
      agent's folders, its program folder now in the `x64\` layout, `with` the test's account as
      `AgentSid`, the agent's clock and a `LabServer` of its keys; each asserts it saw no poll. The
      test base in safe mode: every line passed but `Subscriptions`, which names `NoteBeforeWrite`
      in `NoteChecks` and not `NoteFormGet`, the users those of the session; under `outpost-worker`: its account, rights, probe 0 and
      connections; each typical copy, as its `writeSafeMode` says: its connections pass, the other
      lines run, nothing of it a literal; the write account as the read account: `ReadRights`
      names `Catalog.Products`. Broken without changing the machine: a changed worker password and
      a made-up account, 1326; `Guest`, disabled, with a blank password, 1331, or 1327 if Windows
      judges the blank password first, which the run records and the test then states; a program
      folder without Users' entry, the probe's 5. `LabWorkerProbe` goes for the probe's line in
      `LabRun.WhyNotOpened`. The test configuration gains the catalog `Notes`, written by nothing,
      the common module `NoteChecks`, `Server` without `ExternalConnection`, and the subscriptions
      on it `NoteBeforeWrite`, `BeforeWrite` of `CatalogObject.Notes`, and `NoteFormGet`,
      `FormGetProcessing` of `CatalogManager.Notes`; both roles read `Notes` alone; its `README.md`
      names them.
    - **The steps.** Contract: the members above; what replaces a working member works at once
      with today's texts (the layouts, `StartChecks`, `OtherAccounts`, `Held`, `Name`,
      `AccountRightNames`, `WorkerLogon`, `AsUser`, `For`, `Judge`, the cause in pairing),
      `OutcomeAnswer` with `Result` alone; the two tables, the settings, the
      converters, `CheckFailed` and `check` in `ConsoleApp` work; `ExecuteAsync`,
      `WindowsWorkerStarts` and `WithToken` throw `NotSupportedException`. Edits that state nothing
      new, made there: the callers of every moved name; `ConsoleParts`' new members in
      `MachineParts`, `TestMachine` and `InstallOnWindowsTests`, the steps using `AgentSid`;
      `FakeAccessLists`; `InternalsVisibleTo` for `OneC.Tests`, which references the console and
      Spectre's two Testing packages; `LabLogon` going. Tests step: the tests above, the reworded
      assertions skipped, the test configuration. Implementation: the rest, the wording, the
      worker's order. Documents: `README.md` names `check`; "The files of the agent" and the
      stage-3 contract take the drafts below.
- **What S10a changes in the public contract, as a draft for the owner** (the contract
  author, 23 November 2026). Each holds until the owner overturns it:
  - the commands `install [--stop-timeout <TIME>]`, `uninstall [--stop-timeout <TIME>]`,
    `pair <server address> <token>`, `secret <base key> <read|write|worker>` and `connector
    register`, their messages and the exit codes of the table above;
  - `install` copies the unpacked archive into `%ProgramFiles%\Moltaro.OnecOutpost`, never
    deletes an existing service, writes the failure actions above only when it creates
    it, does not start it, and refuses a data folder that exists and admits anyone else;
  - "Log on as a batch job" of a worker account, a local user, is granted by `secret <base
    key> worker`, not by `install` as S9c's draft said: the policy names it after `install`;
  - `uninstall` takes the agent's two rights, then removes the service, leaving both folders;
  - `pair` refuses an agent that is paired and writes `secrets.dat` with the key alone;
    `pair` and `secret` refuse a data folder that admits anyone else; `secret` refuses to
    read a password at a console whose output is redirected (`NotInteractive`);
  - `connector register` registers only where exactly one version of a bitness is
    installed; the agent judges the connector from HKLM as the console does, and a
    registration whose file is gone no longer picks that bitness;
  - the trait `Requires=Administrator`, with CI's `windows` job elevated.
- **What S10b changes in the public contract, as a draft for the owner** (the contract
  author, 24 November 2026). Each holds until the owner overturns it:
  - `check [--clock-skew <TIME>]`, its lines in the stage contract's order, their messages, exit
    code 19 `CheckFailed`, and the allowed skew two minutes, the server's default, when absent;
  - `check` reads only: no Write, no script that changes data, no file of the data folder
    written, no poll; while the service runs it signs one `outcome` of a made-up command with the
    agent's key, and each worker it starts takes a client licence while open;
  - `check` judges the event subscriptions of a base with `write` alone, all but those to
    `FormGetProcessing`, which no external connection raises;
  - `check` judges each registered connector's folder by the program folder's rule, starts no
    worker from a folder that somebody else may change, and names a data file owned by somebody
    else with the `icacls "{path}" /setowner *S-1-5-32-544` that restores it (Q14);
  - the agent's `started` reasons take one wording with a remedy that exists (data folder entries
    and owners, program-folder writers, `agent.json` and `secrets.dat`, missing passwords, the
    worker account's logon and rule, the agent's privileges), shared with `pair` and `secret`;
  - the COM worker writes its diagnostics before `{"ready":false}` and its `base-unreachable` line;
  - `check`'s limitations above: the write account in Read and safe mode; the probe in the
    console's session; direct grants alone seen; a connector's siblings; a file base's folder.
- **To read.** The [concept](../concept.md), §3 "Pairing" and §5 (20 November 2026);
  `docs/protocol.md`; `AGENTS.md`, principles 9 and 12; this plan, "The files of the agent",
  the contract of stage 2 and S9c's brief.
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
    - every command's exit codes and errors. `pair` and `check` report an answer they do
      not accept with its status and the console's own reason, as §6.1 and §12 of
      `docs/protocol.md` say, a server key that does not match the token included, and
      never with text taken from an unverified body;
    - with the output redirected, every command writes plain text, tested.
  - In `Moltaro.OnecOutpost.OneC.Tests`, on the lab machine: `check` against the test base
    and each copy of a typical base, the rights of the read account included, with the
    server's part against a stub; `metadata` writes a report of each; the lines of the
    worker account, on the lab's `outpost-worker`, each failure case named by a test that
    breaks it on purpose where that needs no change of the machine (the owner, 23 November
    2026). How `check` makes the probe start from the console is settled in S10's internal
    contract.
  - Manual, on the lab machine: the owner runs every command in a terminal and accepts
    how it looks.
  - Manual, on the lab machine, S9c's line (the coordinator, 23 November 2026): `install`
    registers the service and then grants its account the two privileges; the console
    writes the data folder with its list, `agent.json` and `secrets.dat` with `workers`;
    the policy names the test base with `safeMode: false` and its `workerAccount`. The
    service records `started` without an error, and each worker of that base runs under
    the worker account and keeps its process past the policy's `runLimit`, set to a
    minute, so its open gave true and 1C opened. This alone proves session 0 and that
    the virtual account has `WRITE_DAC` on its station; without it, `LogOn` stops the
    start with its reason. Without the privileges, and with an administrator as the worker
    account, it records `started` with its reason and stops.
  - **Guide G1**, "Installing and running an agent", as
    [The user guides](#the-user-guides) lists it. Manual, on the lab machine: the owner
    installs, pairs and checks an agent from the built archive by the guide alone, and
    every place where the guide was not enough is corrected in it.
  - The documents step: `README.md` says in a few lines how an agent is installed and
    paired, and leads to G1.
- **Out of scope.** The shell.

### S10aR · Refactoring after S10a

**Track:** refactoring · **Branch:** `epic-1/sprint-10ar-refactoring` · **Needs:** S10a,
merged.

- **Goal.** The entries of the architect's reviews 1 and 2 that the planner took (23
  November 2026) are carried out, as the [development process](process.md#the-refactoring-sprint)
  says, and nothing else.
- **Entries**, each its own commit, each keeping what is named:
  - *8.* `OutpostStore` repeats its transaction frame in 18 operations and rebuilds its
    table names 19 times: one private `InTransactionAsync` that opens the context, begins
    ReadCommitted, runs the operation and commits, and the table names and the filter of
    `HoldingTheirKey` computed once. Unchanged: one transaction per operation under
    ReadCommitted, with its statements in their order; the order of locks, token, agent
    row, claims, commands; one transaction per purge batch. The cases of S7b are measured
    again on a quiet machine, and the p95 of each stays within the figure S7b recorded; a
    run on a loaded machine counts neither way;
  - *9.* the growing delay of a retry is written twice, in `AgentLoop` and `WorkerPool`: one
    `AgentPolicy.NextRetryDelay` beside `FirstRetryDelay`. Unchanged: the default sequence
    3, 6, 12, 24, 30, 30 seconds; the first worker of a slot waits for nothing; the delay
    goes back to a tenth after an answer, or after a worker that opened;
  - *10.* the audit log repeats readers of `OutpostJsonReader`, and the millisecond of §2
    is cut in three places: the log uses the protocol's readers, and one
    `OutpostForms.ToMilliseconds` serves the three places. Unchanged: the log reads
    everything its writer writes and sets `Fault` on any other line; the store keeps
    exactly the deadline and the skew that were signed;
  - *11.* small cleanups: the overload of `OutpostCodec.ReadOutcomeRequest` that only tests
    call goes; the protocol's `InternalsVisibleTo` for the shell goes, since the shell
    depends on the server (principle 12); the server's `CommandChecks` becomes
    `ParameterChecks`; the comment of `WorkerExitCodes` names who reads the exit code;
  - *review 2's 1* (after S10a; the planner, 23 November 2026): `InstallOnWindowsTests`
    copies the console's private `Program.MachineParts` by hand: `MachineParts(serviceName,
    source, programFolder, dataFolder)` becomes internal, the test calls it and its copy
    goes. Unchanged: the parts the console's program runs with.
- **Refused** (the planner, 23 November 2026): splitting `Moltaro.OnecOutpost.TestSupport`
  by side, so that the tests that need 1C do not reference the server's store. A
  referenced assembly is not a test that needs PostgreSQL: the rule of
  `Moltaro.OnecOutpost.OneC.Tests` is about what a test needs to run (KISS).
- **Out of scope.** Every other entry of the list, which S10a, S10b, S11 or S12 carries.

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
  - Manual: the shell runs on a machine with PostgreSQL, the agent on the lab machine
    reaches it over HTTPS, and a Read and a Write of S9's scenarios are typed in the shell
    against the test base. This is also S9b's manual line, moved here by the planner, 22
    November 2026: S10's console pairs the agent, and the shell runs the real server. The
    owner runs every command in a terminal and accepts how it looks. The shell has no test that needs 1C: it would need PostgreSQL and 1C on one
    machine, and the automated session with the fake worker and S9's tests cover the
    rest.
  - **Guide G3**, its first part: a first session with the shell, as
    [The user guides](#the-user-guides) lists it.
  - The documents step: `README.md` shows a session, and leads to G3.
- **From the architect's review 1** (the planner, 23 November 2026): the harness of the
  real agent with the fake worker, `LoopbackAgent` and its fakes, moves from
  `Moltaro.OnecOutpost.Agent.Tests` to `Moltaro.OnecOutpost.TestSupport`, where the shell's
  tests reach it.
- **From the architect's review 2** (the planner, 23 November 2026): the shell writes a
  redirected output as the console does, its `Program` and `RedirectedOutputTests` the
  model: a UTF-8 writer, Spectre's default enrichers off, so that CI's variables force no
  colour, no wrapping of a line, and the end line. The twenty lines are written twice; the
  two programs share no project (principle 12).
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
  - **Guide G3**, "Embedding the server", completed as
    [The user guides](#the-user-guides) lists it, with the C# of the sample.
  - The three guides read once more against the final build: every command, script and
    message in them is what the build prints. `README.md` leads to each in its first
    screen, and the text of the GitHub release leads to G1.
  - The final figures of [Measured results](#measured-results) in `README.md`.
  - The documents step over the whole product documentation, as the process says for the
    last sprint of an epic.
- **From the architect's review 1** (the planner, 23 November 2026), for the documents
  step:
  - the comments of the product code that cite working documents removed after the
    release, a brief, a question of this plan or a sprint, about 12 files, state the rule
    itself or name a section of the concept or of `docs/protocol.md`;
  - the formats of `agent.json`, `secrets.dat`, the entries of the audit log and the
    worker's lines, which today only this plan holds, move into product documentation,
    `docs/agent-files.md`, and the status line of `docs/protocol.md` points there.
- **Out of scope.** The tag itself, which is the owner's.

## The contracts of stages 2 and 3

The planner writes these contracts out exactly before stage 2 starts, with L1's answers.
An independent reviewer reviews them, and the owner approves them. Until then this is what they have to be able to do.

- **The executor**, an external data processor:
  - one exported function that runs a script in a mode with its parameters, caller key and
    outcome limit, and returns the outcome as JSON;
  - safe mode;
  - the transaction of a Write and the checks of rule 5 before its commit;
  - the user messages, trimmed with a marker;
  - after L1: it uses only the language that the compatibility mode 8.2.13 allows, and it
    replaces `Null` with `Undefined` before it writes a result, since `WriteJSON` refuses
    `Null`.
- **The COM worker**, a program:
  - its command line names the base and the mode;
  - it receives the connection, with its password, from the agent over its standard input,
    never on the command line;
  - it exchanges one JSON message per line with the agent over standard input and output:
    a command in, an outcome out. The outcome's JSON from the executor is forwarded as it
    is, so that its size is what the executor measured;
  - it ends when the agent closes its input;
  - after L1: it opens the executor with an `UnsafeOperationProtectionDescription` whose
    warnings are off, since nobody can answer the platform's warning in an external
    connection;
  - after D12 changed: the agent tells it whether its mode runs in safe mode, and it opens
    the executor so. A worker of a base whose Writes run without safe mode runs under a
    Windows account of its own, which can read neither the agent's secrets nor its audit
    log. S9 builds the worker's side, S9c the account and how the agent starts a worker
    under it, and the console's `secret <base key> worker` grants the account its right.
- **The console**, a program that references the agent, the protocol, Spectre.Console and
  Spectre.Console.Cli (principle 12), built as `AGENTS.md`, "Console programs", says, with
  these commands and their exit codes. *(S10a)* The commands of S10a are built; their
  steps, messages and exit codes are those of "Internal of S10a" in
  [S10's brief](#s10--the-console), with the list "What S10a changes in the public
  contract", and each needs a console run as administrator:
  - `install [--stop-timeout <TIME>]` and `uninstall [--stop-timeout <TIME>]`;
  - `pair <server address> <token>`;
  - `secret <base key> <read|write|worker>`, which asks for the password without echoing
    it, or reads it as one line of a redirected standard input;
  - `check`, which reports, each as passed or failed with its reason:
    - the COM connector and its version: whether one is registered for each bitness, and
      where a `comcntr.dll` that is not registered lies (the owner, 21 November 2026, after
      L1: the console finds, diagnoses and registers the connector);
    - the connection to each base under each account;
    - for each base, the event subscriptions whose common module is not available to an
      external connection, named with their modules, since a write that reaches one of
      them fails (concept §1 and §12, O1);
    - the rights to change data that each read account has;
    - the server;
    - the clock skew;
    - for each base whose Writes run without safe mode, its worker account: that it logs on
      with the password of `secrets.dat`, naming a wrong password, a missing "Log on as a
      batch job" and a locked, disabled or expired account, and that its token passes the
      rule of the worker account;
    - that the agent's account holds "Replace a process level token" and "Adjust memory
      quotas for a process";
    - a start of the COM worker under each worker account on the desktop the agent makes
      for it, as the service starts it, which opens no base and reports the exit code or
      the error of Windows, 0xC0000142 named as a failure to initialise on its desktop;
    - the access lists of the data folder, its files and the program folder, naming who
      else may write to them;
    - that `policy.json` reads and that `secrets.dat` decrypts and holds every password
      the policy needs.

    `check` only reads: it writes nothing to a base and runs no script that changes data.
    Every cause of a failure that a lab run finds becomes a line of `check` with its test
    (the owner, 23 November 2026).
  - `connector register`, after L1, which registers the COM connector of the installed
    platform for each bitness that has none registered and exactly one version installed;
  - `metadata <base key> <file>`.
- **The shell**, a program that references the server, Spectre.Console and
  Spectre.Console.Cli (principle 12), built as `AGENTS.md`, "Console programs", says:
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
  - *(S9b, a draft for the owner)* the COM worker of each bitness lies in `x64\` and
    `x86\` beside the agent's program, each with the files of its build, since the agent
    starts it from there; S12 builds the archive so;
  - *(S10a)* the console lies beside the agent's program, since `install`, run from the
    unpacked archive, copies its whole content into `%ProgramFiles%\Moltaro.OnecOutpost`
    and registers the service as `Moltaro.OnecOutpost.Agent.exe` there;
  - every program in it is self-contained, so that no .NET runtime of either bitness has to
    be installed;
  - its SHA-256 checksum is on the release page;
  - it runs on the Windows versions that .NET 10 supports.
- **Outcome codes of stage 2:** `base-unreachable` and `connection-lost`, as the last
  bullet under [`OutpostCodes`](#outpostcodes) gives them. S9 adds them to `OutpostCodes`,
  to `OutpostOutcomeCodes` and to the table of §8 of `docs/protocol.md`.

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
