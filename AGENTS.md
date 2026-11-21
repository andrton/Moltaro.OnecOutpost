# Repository Guidelines

`AGENTS.md` is the one instruction file for coding agents and a short statement of how
this project is built. Shared rules live here; `CLAUDE.md` only points to this file.

## Project

Moltaro.OnecOutpost lets an application read and write a 1C:Enterprise 8.3 database that
sits deep in a back office, where nothing outside can connect to it. It has three parts:

- **The agent**, a Windows service next to 1C. It always dials out to the server over
  HTTPS and receives commands. It runs each command as a script inside 1C through the COM
  connector installed with 1C, and returns the result as JSON.
- **The server**, a .NET library that an ASP.NET Core application embeds. It pairs agents,
  signs commands, hands them to the agents and keeps their outcomes. Its nodes keep no
  state of their own, so the application can run it on several nodes.
- **The tools.** A console installs, pairs and checks an agent on its machine. A shell runs
  a server of its own and sends commands by hand, like a terminal.

The tunnel has exactly two commands, **Read** and **Write**. A command carries a script in
the 1C language and its parameters. The library knows nothing of any 1C configuration, so
BAS and every other configuration on the 1C:Enterprise 8.3 platform are the same case. The
oldest platform supported is 8.3.17.1851.

What exists today is the [concept](docs/concept.md), these rules, the development process,
the build settings, and the public contract of the first stage in code: the server
library, the protocol assembly and the agent program. Only the checks that need no
implementation work, and the codec of the protocol with its signatures and pairing proof,
which no endpoint serves yet: every operation of the server throws `NotSupportedException`,
and the agent program starts and stops and does nothing else. The server is the one
package of the repository, `Moltaro.OnecOutpost.Server`, and it carries the protocol
assembly inside it, so that the two can never be mixed at different versions. The concept
is the authority for what the library guarantees: its semantics, its rules, its decisions
D1–D17, its non-goals and its open items. `README.md` says what works today; a feature is
delivered when the README says so.

This repository is public. Nothing specific to a private product or a customer goes into
it: no names of customers, of their databases or configurations, and none of their data,
in code, tests, samples or documents.

## Layout

| Path | Contents |
| --- | --- |
| `src` | The solution, every project and the build settings. Run `dotnet` from here |
| `src/OneC.TestConfiguration` | Planned: the XML sources of the repository's own 1C test configuration, with a script that builds a file base from them through the Designer's command line on the lab machine, and the git-ignored `lab.local.json` of the lab. No built configuration or base is committed |
| `docs/concept.md` | Where the library comes from, its semantics and rules, protocol principles, decisions, quality bar and open items |
| `docs/protocol.md` | Planned: the wire protocol, written with the sprint that introduces it |
| `docs/explanations` | Planned: explanations of the architecture and of how the library works, each as Markdown and as HTML |
| `docs/work` | Working documents: the epics in progress, the development process, branches and releases, exceptions to the rules, open questions |

Each project is created by the sprint that first needs it, under the name the plan of the
first epic fixes. The protocol, the server, the agent, the samples, the test support and
the test projects of these three exist; the others are planned. Only the server is packed;
every other project is not.

| Project | Contents |
| --- | --- |
| `Moltaro.OnecOutpost.Protocol` | The messages of the tunnel, their version and their signatures, shared by the server and the agent. No package dependencies, and no package of its own: it travels inside the server's package |
| `Moltaro.OnecOutpost.Server` | The server library: the endpoints for agents, pairing, the store of agents and commands on PostgreSQL through its own EF Core context, and the Read and Write calls of the application that embeds it. The one package of the repository |
| `Moltaro.OnecOutpost.Agent` | The Windows service: the connection to the server, the checks of every command, the local policy and the audit log, and the COM worker processes it runs |
| `Moltaro.OnecOutpost.ComWorker` | The only process that touches COM: it opens the external connection to one database and runs scripts in it. Built for x64 and x86 |
| `Moltaro.OnecOutpost.Console` | The console of the agent machine: install, pair, check, collect metadata |
| `Moltaro.OnecOutpost.Shell` | A console that runs a server of its own and sends Read and Write by hand, to try the tunnel without an application |
| `src/Executor` | The 1C external data processor that runs a script inside 1C: its XML sources and the built `.epf`. Only the 1C Designer builds it |
| `Moltaro.OnecOutpost.Samples` | The samples of use: an ASP.NET Core application that embeds the server. Each runs to the end with `dotnet run` |
| `Moltaro.OnecOutpost.Measurements` | Planned: the program that measures the latency target of the concept, run by hand, never in CI or in the tests |
| `src/*.Tests` | Tests, one project per library or program |
| `Moltaro.OnecOutpost.OneC.Tests` | Planned: the tests that need 1C, run only on the lab machine. It is not in the solution, so `dotnet test` from `src` never runs it; CI builds it and checks its format |
| `Moltaro.OnecOutpost.TestSupport` | Code that two or more test projects need, such as the fake clock, the PostgreSQL fixture and the fake of the COM worker. Never shipped; no tests |

The repository root holds only repository-level files. Code never lives there.

## Keep It Simple

KISS is the base rule for design, code, tests and documentation.

- Start from the simplest thing that fully solves today's case. Prefer one plain rule or
  an existing mechanism over a new one.
- Build what is needed now. What is not needed yet is not built; at most it is noted as
  planned in the document that owns it.
- No speculative abstractions, options, switches, retries, fallbacks or defensive checks
  "just in case".
- When something is awkward to use, fix its cause. Do not wrap it: no adapter or facade
  class around a core type to work around what the core lacks.
- Simple to use comes first: a small public surface, few options, one obvious way to do
  each thing. Then a simple implementation.
- A case that is rare in practice and costs no guarantee (see "Quality Priorities") is not
  handled. Its limitation is stated in one sentence, and nothing is built for it.
- A review finding is fixed by the smallest change that removes it. A new mechanism,
  option, state or public type is added only when a guarantee needs it; otherwise the fix
  states the limitation or removes the cause. An area whose fixes keep producing new
  findings is over-built: simplify it instead of fixing it again.

Simple is not the goal by itself. Do not make something simpler by dropping a guarantee,
blurring a boundary or pushing the difficulty onto the caller. The target is the simplest
design that keeps every accepted guarantee. When simple and correct disagree, correct
wins. When simple and fast disagree, measure before choosing.

The value of this library is that the person responsible for a back office can read it
and trust it. A request outside the tunnel is declined, or noted as planned in the concept.

## Architectural Principles

1. **The agent dials out; nothing dials in.** The agent opens every connection, over HTTPS
   on port 443, through the proxy of its network when there is one. It never listens on a
   port, and the server never needs a route into the agent's network.
2. **A tunnel, not an API.** There are two commands, Read and Write. A command is a script
   in the 1C language with parameters in JSON, and its result is JSON. The script runs
   whole, inside 1C, in one call. The library does not model the objects of a 1C
   configuration and offers no operation on them; a request for one is declined.
3. **An agent runs only what its server signed.** A command runs only when all of these
   hold:
   - it carries a valid signature by the server key that the agent pinned at pairing;
   - it is addressed to this agent and to a base and a mode it serves;
   - it has not expired.

   A command that ran before does not run again. Anything else is refused and logged. The
   check does not depend on the transport, so nothing between the agent and the server
   can inject a command: not a proxy that opens TLS, and not anything between an ingress
   and the server.
4. **The administrator of the agent machine has the last word.** A local policy on that
   machine says which bases the agent serves, which 1C account reads and which writes, and
   whether a base may be written at all. The server cannot change it. The policy decides
   bases and modes, not operations: a list of allowed scripts is not part of the library.
   Every command is written to the local audit log, script included, before it runs, and
   its outcome after.
5. **A Write runs at most once per caller key.** Every Write carries a key chosen by its
   caller. A repeat with the same key waits for the outcome of the first and returns it;
   it never runs the script again. When the agent cannot know the outcome, for example
   because the connection to 1C ended after the script may have committed, the outcome is
   reported as unknown. It is never reported as a success or a failure.
6. **Server nodes share nothing in memory.** Every fact another node needs, such as
   agents, commands and outcomes, is in the store. A node is woken through Moltaro.Signals
   and then reads the store. Any node can serve any request of any agent or caller, so an
   application runs as many nodes as it needs and can restart any of them at any time.
7. **An agent that is not connected is an error at once.** The server keeps no command for
   an agent that is not polling: the call returns that the agent is unavailable. A command
   that no agent took before its deadline ends as expired. Nothing is delivered later.
8. **COM lives in its own process.** Only the COM worker touches COM. It may crash, hang or
   leak; the agent then ends it and starts another, and its failure ends at most the
   commands of its base. A COM worker runs with the bitness of the COM connector it uses.
9. **Secrets stay on the machine they belong to.** The 1C passwords and the agent's key
   stay on the agent machine, protected with DPAPI, and the signing key of the server stays
   with the server. No secret travels in a command, a result, a log line or an error
   message.
10. **Waiting is finite and outside locks.** No lock is held across I/O or an `await`.
    Every wait honors cancellation; a wait that can be long has a deadline. A timeout or a
    limit an application or an administrator can observe is an option with a default,
    validated when the object that uses it is created. Examples are the length of a poll,
    the deadline of a command, the size of a result and the COM workers of a base. Such a
    value is never a constant in the code. A limit that a side needs before it can read
    what the other side sent, such as the length of a name or the nesting of JSON, is not
    an option: the protocol fixes it, so that both sides check the same limit. A limit that
    travels in a signed message, such as the size of an outcome, stays an option. A type
    that takes more than one lock documents the order.
11. **Never an approximate success.** A request is honored or refused with a code. A
    protocol violation ends the request with a reason; it is not guessed around. Not
    connected, refused by the policy, expired, failed in 1C and unknown are distinct
    outcomes.
12. **Dependencies point one way.**
    - The protocol depends on nothing outside .NET.
    - The server depends on the protocol, ASP.NET Core, EF Core with PostgreSQL and
      Moltaro.Signals.
    - The agent depends on the protocol, the console on the agent, the protocol,
      Spectre.Console and Spectre.Console.Cli, and the shell on the server,
      Spectre.Console and Spectre.Console.Cli.
    - The COM worker depends on nothing outside .NET.

    Nothing depends on a product that uses the library. Inside an assembly a folder is a
    namespace and one responsibility, and folders depend on each other in one direction.
    The server runs on every platform .NET runs on; the agent, the COM worker and the
    console run on Windows only.

## Quality Priorities

When two good things pull apart, the zone of the code decides which one wins. Read each
row left to right: an earlier quality is not traded for a later one.

| Zone | Priorities, in order |
| --- | --- |
| Command checks, pairing, local policy, audit log, secrets | Correctness and security → simplicity → speed |
| Protocol, transport and the store | Correctness → robustness under disconnects, restarts and bad input → simplicity → latency |
| COM worker | Correctness → containment of failures → readability |
| Public contract: the server API, the protocol, the console commands, the local policy and audit log formats | Correctness → clarity of use → stability → speed |
| Shell, samples, tests | Readability → simplicity; speed does not matter |

- **Correctness is first everywhere.** For this library it means:
  - a command never runs without a valid signature, twice under one key, after its
    deadline, or against a base or mode that the local policy closes;
  - an outcome never reaches the caller of another command;
  - an unknown outcome is never reported as known;
  - a secret never leaves its machine;
  - an agent that is gone is reported as connected no longer than its lease, plus the
    difference between the clocks of the server nodes.
- **A performance claim needs a measurement.** "Faster" or "fewer queries" is accepted
  with a number from a benchmark or a query count, not by argument. Without one, the
  simpler code stays.

## Design Patterns

Patterns are vocabulary for explaining a decision, not a requirement to meet. No pattern
is applied because it exists.

- Put logic where the data it needs lives.
- Introduce an interface only at a real boundary: the public contract, the transport, the
  store, the COM worker. One implementation behind a private interface is not a boundary.
- A known pattern is welcome when it removes real duplication or names a real variation.
  Say which problem it solves; if that cannot be said in a sentence, leave it out.

## The Contract Comes First

A feature starts as contract: public types with documentation, a sample of its use, and
tests that state its behavior. Implementation follows.

- Respect the stage that was agreed. "In the contract, not implemented yet" means exactly
  that: the entry points throw `NotSupportedException`, the tests that need the feature
  are skipped with a shared reason, the sample is written and reports where it stops.
- Checks that need no implementation belong to the contract and work from the start, for
  example validating a key, the name of a base or an option.
- When the implementation arrives it has to pass the tests that were written for the
  contract. Do not weaken or delete a test to get a green run.
- Versions stay 0.x until a first production adopter runs on the library, so there are no
  compatibility shims. When something is replaced, the old code, tests and documentation
  are removed in the same change.
- The protocol is part of the contract. Its grammar lives in `docs/protocol.md`, written
  with the sprint that introduces it. Every request of an agent carries the protocol
  version, and the server refuses a version it does not support with a code that says so.

## Code Style

Language and build:

- C# on .NET 10, nullable enabled, warnings are errors. `dotnet format --verify-no-changes`
  stays clean.
- Package versions are managed centrally in `src/Directory.Packages.props`; a
  `PackageReference` has no `Version`.
- COM is reached only in the COM worker, late-bound through `IDispatch`, so that no build
  depends on one installed version of 1C. Every COM object it obtains is released by its
  owner, once.
- Code in the 1C language uses the English variant of the language, as the rest of the
  repository is in English. The names of a configuration's objects stay as the
  configuration has them.

Organization:

- One type per file, named after the type. The generic form of a type that also has a
  non-generic one goes to `Name.Generic.cs`; a nested type with a file of its own goes to
  `Outer.Nested.cs`.
- A folder is a namespace and one responsibility. No `Internal`, `Helpers`, `Utils` or
  other folders that collect unrelated types.
- `internal` by default. A type is public only when it is part of the contract.

Naming:

- Public types are named in the contract stage and reviewed as public API. A name that
  would be generic on its own, such as `Command` or `Result`, takes the `Outpost` prefix.
- Every enum type ends with `Enum`.
- Asynchronous methods end with `Async` and take a `CancellationToken` last, when a
  caller awaits them; a private loop that ends when its connection or timer does, or a
  method that completes a dispose, takes none.
- Private fields are `_camelCase`.
- Name things for what they do for the user, not for the framework mechanism inside.
- Use these words as the concept defines them:
  - *agent*;
  - *server*;
  - *base*: one 1C database an agent serves, named by a key;
  - *command*: one Read or Write;
  - *script*: the 1C language text a command runs;
  - *caller key*: the key of a Write;
  - *pairing*;
  - *local policy*;
  - *audit log*.

Shape of the public API:

- Something that answers at once from local memory is synchronous. Something that can
  wait is asynchronous.
- Expected outcomes are results, not exceptions: unavailable, refused, expired, failed in
  1C, unknown. Exceptions are for failures. `ArgumentException` and its relatives report
  a caller's mistake; `ObjectDisposedException` follows a dispose;
  `NotSupportedException` marks what is not implemented.
- Every public member has XML documentation: what it does, what it guarantees, what it
  does not, and which errors it reports. Plain sentences, no restating of the signature.
  Internal members are commented with `//`, not `///`, so that the XML documentation of
  a package holds only its public contract.

Writing code:

- Write for a reader. Descriptive names, one statement per line, blank lines between
  steps; no nested ternaries or dense chains.
- Keep a method focused, without splitting a simple operation across needless helpers.
- Comments are in English and explain why: an invariant, an ordering requirement, who
  owns mutable data, what a lock protects. They do not repeat the code.
- Never return an invented success in place of a failure.
- No reflection on a call path outside the COM worker. No duplicated checks. No redundant
  models.

Console programs:

- The console and the shell are products for people, not scripts. They are built on
  Spectre.Console, with Spectre.Console.Cli for their commands, options, validation and
  `--help`.
- A wait that a person sees shows a spinner that says what is happening; work of a known
  size shows a progress bar; lists are tables; an outcome or an error is shown with its
  kind, its code and a plain reason, in colour; a secret is asked with a prompt that does
  not echo.
- When the output is not an interactive terminal, the programs write plain text without
  colour, animation or prompts, so that a script and a test read the same facts. Every
  command has documented exit codes.
- Development tools such as the measurement program are exempt.

## Tests And Samples

- A public behavior has a scenario test that reads like its use. A test name is a
  sentence stating the behavior.
- Tests use the public API. A mechanism with no public surface, such as the command
  checks or the store, is tested directly through `InternalsVisibleTo` for its test
  project. Each such test states a rule of the concept or of `docs/protocol.md`, never
  how the code happens to be built.
- Every numbered rule of the concept's section 3 has a deterministic test.
- Time is injected through `TimeProvider`. Deadlines, expiry, polls and the timeouts of
  the COM worker are tested with a fake clock.
- A test waits for an observed event; it never sleeps and never polls with a delay.
- The store is tested against a real PostgreSQL, never an in-memory provider.
- The transport is tested over loopback HTTP, with an agent and a server in one test. The
  tests cover polls, a lost connection, a restarted node and a second node.
- Tests that need 1C live in one project, `Moltaro.OnecOutpost.OneC.Tests`, and run only
  on a lab machine with 1C:Enterprise 8.3.17.1851, the oldest platform supported (concept
  D14), never in CI: GitHub's runners have no 1C, and no self-hosted runner is attached to
  a public repository. A test there needs Windows and 1C, never PostgreSQL or a server's
  store. Every other test replaces 1C with a fake: the COM worker behind its boundary, or,
  in the tests of the worker program itself, the COM connection.
- The 1C tests run against two kinds of base: a file base built on the lab machine from
  the repository's own test configuration, and at least one copy of a real typical
  database on the lab's 1C cluster, which is a client-server base. A typical
  configuration, a customer's database and its data never enter the repository:
  - a test takes the bases, the accounts and the objects of a typical configuration it
    uses, such as a document type, from `src/OneC.TestConfiguration/lab.local.json`, which
    Git ignores;
  - it takes data, such as which document to post again, from the base while it runs,
    never as a literal.
- A check that no test can make is a manual acceptance, described in the document of the
  sprint. Raw output stays out of Git.
- Code that two or more test projects need lives once, in a
  `Moltaro.OnecOutpost.TestSupport` project, created when the second test project needs
  it. It is never shipped and contains no tests.
- A test project is organized like a library: a folder is a namespace and one area of
  behavior.
- A feature that is not implemented has its tests written and marked
  `[Fact(Skip = ...)]` with one shared reason. Skipping is never used for a flaky test.
- Tests come first and from someone else. On the full track of the
  [development process](docs/work/process.md), a different agent than the implementer
  writes and commits the tests of a sprint before its implementation. The implementer
  removes the skip and makes them pass; it does not change what they assert. A test that
  looks wrong is reported, not edited.
- The shell and every sample run to the end with `dotnet run`.
- Code shown in `README.md` must compile against the current contract.

From `src`:

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
dotnet build Moltaro.OnecOutpost.OneC.Tests
dotnet format Moltaro.OnecOutpost.OneC.Tests/Moltaro.OnecOutpost.OneC.Tests.csproj --verify-no-changes
```

The last two lines apply once that project exists. On the lab machine, also from `src`, the
tests that need 1C:

```bash
dotnet test --project Moltaro.OnecOutpost.OneC.Tests
```

## Documentation

- Everything in the repository is written in English: documentation, code comments, XML
  documentation, test names and commit messages. This holds whatever language the task
  was given in.
- A document lives next to the code it changes. Product documentation is in `docs`;
  working documents, the epics, are in `docs/work`. The plan for using this library inside
  another product belongs to that product's repository.
- A decision is not an implementation. Say plainly what exists and what is planned.
- An explanation of the architecture or of how the library works is written twice, in
  `docs/explanations`: as Markdown and as HTML, saying the same thing. The two change
  together, in the same change. The HTML is one self-contained file that opens from disk,
  without a build, a server or anything loaded from elsewhere. In the main cases, named
  when the explanation is planned, the HTML is animated: the animation plays on its own,
  and the reader can step through it, with a caption for each step.
- When behavior changes, update the document that owns it in the same change, and the
  explanations that describe it, both forms:
  - the concept for semantics and decisions;
  - `docs/protocol.md` for the wire format;
  - `README.md` for status and use.
- When a larger block of work is finished — a sprint, an epic, a change of the public
  contract — compare `README.md`, the concept, `docs/protocol.md` and the explanations
  with what now exists. Correct every statement of status, every name, limit and
  described behavior that no longer holds, in the same change.
- That check never changes a guarantee. When the code and an accepted decision disagree,
  it is a finding to report, not a sentence to edit.
- Requirements that come from a particular consumer are written in general terms.

## Working Rules

- The user's instructions define the task. Among repository documents this file owns the
  global rules. Code and tests show what the code does, not that it is intended.
- A departure from a rule of this file is recorded in
  [exceptions to the rules](docs/work/exceptions.md) in the same change: which rule,
  where, why and until when.
- Before designing a change to the contract, read the concept section that covers it.
- Make routine, reversible choices within the task. Ask only when a missing decision
  changes behavior, the contract or the scope. Say what was left out.
- Review the whole affected flow before finishing: contract, implementation, tests,
  samples, `README.md`, `docs`.
- Keep plans, progress and findings in the conversation. Do not create report, audit or
  plan files unless the user asks for that document.
- Never revert the user's changes or unrelated work.
- A check against a real 1C runs on a lab machine, on its test base or on a copy of a
  database, never on a working database of a customer.
- Do not commit or push unless asked. Stage only the files of the task. No secrets,
  tokens, machine paths, build output or run output in Git.
- Never commit to `main`. Work on a branch named as
  [branches and releases](docs/work/branches.md) says. `main` changes only through a pull
  request, merged by the owner or, for a closed sprint and with the owner's standing
  permission, by the coordinator of an epic. Never move a release tag; create one only
  when the owner tells you to, for that version.
- Commit messages carry no agent attribution: no `Co-Authored-By` line and no "generated
  with" line. This overrides the default of any tool.
- Planning an epic, coordinating its sprints, reviewing a sprint or refactoring follows
  the [development process](docs/work/process.md). Read it before taking one of those
  roles.
- Technical debt is removed by the refactorer in the refactoring sprint of every epic, as
  the development process describes. Its measure is this file.
- Finish by reporting what changed, which checks ran and what is not done. When the task
  leaves changes in Git, suggest a commit message.
