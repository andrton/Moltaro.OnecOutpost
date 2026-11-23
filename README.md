# Moltaro.OnecOutpost

Run read and write scripts in 1C:Enterprise 8.3 and BAS databases hidden in a back office.
A Windows agent next to 1C dials out over HTTPS, runs signed scripts through the COM
connector and returns JSON; an embeddable .NET server queues the commands and scales
across nodes.

## Status

Contract stage. The [concept](docs/concept.md) is agreed, and the first epic, up to the
first release, is under way in [current work](docs/work/README.md). How to write the
scripts that Read and Write run is in the guide [Writing scripts](docs/guides/scripts.md).
The public contract of
its first stage exists in code: the types and members of the server library, with their
documentation. The checks work: the options of the server, the arguments of every call,
the outcome factories, and the rules of the agent's local policy file. The protocol
assembly holds the codec of the
[wire protocol](docs/protocol.md): its messages, the signatures of commands, requests and
answers, and the pairing proof, with their tests. The COM worker runs scripts in 1C: it
holds an external connection to one base and runs each command through the executor, an
external data processor in `src/Executor`, in safe mode or without it. Its tests run against a fake of the COM connection, and its 45 tests against a real
1C, in `src/Moltaro.OnecOutpost.OneC.Tests`, passed on the lab machine with
1C:Enterprise 8.3.17.1851, against the repository's test base and a copy of a typical
base. The migrations of the store work: `OutpostMigrations` creates the
store's tables on PostgreSQL in the schema the application chooses, or gives them as one
SQL script per migration.

The server pairs agents, holds their polls and runs commands through them.
`AddOutpostServer` registers it and `MapOutpostAgents` maps the endpoints of the agents: an
agent pairs over HTTP with a pairing token, its signed polls are authenticated, and each
newer poll records the agent's lease, its bases and its claims of base keys, and is
answered with the commands it takes, or without commands when its hold ends. The
application creates pairing tokens, lists and revokes agents and releases base keys, and
every node purges the tokens that expired unused, with their pending agents, and the
ended commands after `OutcomeRetention`.

Commands work on the server, against an agent that speaks the protocol. `ReadAsync` and
`WriteAsync` check the caller key, the base and the agent, create a signed command and
wake the agent's poll through Moltaro.Signals; the poll hands the command out, the
`outcome` request takes the agent's outcome, and the call is woken and returns it. The
server keeps one command per base and caller key, and a repeat returns its outcome; that a
Write runs once also needs the agent's audit log. A command that no poll took in time ends
as Expired, and revoking an agent ends its commands. All of this is tested over loopback
HTTP with a test agent, and with the agent below.

Several nodes keep these rules when messages are lost or held. Over loopback, with two
server nodes on one PostgreSQL, the signals host in one node and a client in the other, the
real agent with a fake in place of the COM worker, and a proxy that drops and holds chosen
requests and answers, the tests show: a call on one node wakes a poll held on the other; a
poll that waits on a node that stops ends, and the next poll on the other node takes the
command made meanwhile; the command of a lost poll answer is handed out again and runs once; an older
poll and an exact replay take nothing; a lost confirmation sends the outcome again and it is
counted once; a command delivered after a lost answer stays taken; with the signals host
stopped, waits end at their deadlines and nothing is lost; and a poll replayed after the
agent's clock was set back is older. On loopback without 1C, the time from a call to its
command reaching a waiting poll, and from an outcome to the waiting caller, was at most
31 ms at p95 with one node and with two (concept §11 asks 100 ms); the figures are in the
[epic document](docs/work/epic-1-first-release.md#measured-results). Two explanations show the
[path of a command](docs/explanations/command-path.md) and
[why a command never runs twice](docs/explanations/never-twice.md).

The agent keeps its files. At a start it checks that the access lists of its data folder
admit nobody else, reads `policy.json`, `agent.json` with its identity and the pinned
server key, and the secrets, encrypted with DPAPI for the machine. It writes its audit log
and its outbox of outcomes in order, each entry and file on the disk before the next, and
reserves its poll numbers in the log, so that they grow across restarts. After a restart
it cuts a torn entry and ends a command that was received without an outcome as Unknown
for a Write and Failed `lost` for a Read. These parts are tested with a data folder in memory, and on Windows
against the disk, DPAPI and the access lists. The agent also checks each command of a poll
answer as §7 of `docs/protocol.md` says, and the answer as a whole against the workers it
declared, and runs a command on a pool of workers bounded by the run limit of its mode;
this is tested with a fake in place of the COM worker.

The agent works end to end against the server, with a fake in place of the COM worker. It
sends numbered polls through the proxy that `policy.json` names, checks each command of an
answer and records it in the audit log before the next poll, keeps each outcome in its
outbox and sends it until the server confirms it, and replaces an outcome refused for its
size. It pairs with a pairing token, for the console to call. The agent program runs as a
Windows service, started with its one argument `service`, and exits with a code that says
why it stopped. This is tested over loopback HTTP with the server and a test proxy: a Read
and a Write from `ReadAsync` and `WriteAsync` reach the fake and come back. A proxy that
asks for authentication is not supported yet: the agent offers it no credentials, so it is
not connected through such a proxy.

The agent runs its commands in 1C through the COM workers it starts. For each mode of each
base it starts the workers that `policy.json` asks for, with the bitness of the registered
COM connector: the 64-bit worker from `x64\` beside its program when a 64-bit connector is
registered, otherwise the 32-bit one from `x86\`. Every worker runs inside a job object,
so it ends with the agent, however the agent ends. A worker whose script runs past the run
limit of its mode is ended: the Read fails and the Write is Unknown, both with
`run-limit`. A worker that ends or does not open its session is started again after a
delay, which grows while its session does not open, up to `retryDelayMax`. This is tested
on Windows with a test program in place of the worker. The tests that run a Read and a
Write through the agent and the worker against the repository's test base, and every
script of [Writing scripts](docs/guides/scripts.md), passed on the lab machine with
1C:Enterprise 8.3.17.1851 on 23 November 2026, the server being the test's own; a Read and
a Write from a real server on another machine come with the console and the shell.

A base whose policy turns safe mode off for its Writes is served for Write too. All its
COM workers, those of its Reads included, run under a local Windows account of its own,
which `workerAccount` of its `write` in `policy.json` names and whose password is in
`secrets.dat`, so that a script without safe mode can read neither the agent's secrets nor
its audit log. Each base has an account of its own. At a start the agent logs each such
account on before any worker starts, and stops when the account cannot log on, when it is
an administrator or holds a privilege beyond an ordinary user's, or when the agent's account
lacks the rights it needs to start a process under another account. At a start the agent
also checks the access lists of its program folder, everything in it and the folders
above it, and stops when anyone but Administrators, SYSTEM, TrustedInstaller and its own
account could change the code that it and its workers run. This is tested on every
platform with a fake in place of the workers, and on Windows against the access lists and
an account that does not exist. Its tests on the lab machine, under a real worker account
against the test base and a copy of a typical base, passed on 23 November 2026: a document
of the typical copy was posted again under the worker account with its movements
unchanged, and a Write without safe mode could not read the agent's secrets. The console's
`install` will grant the agent's account and each worker account their rights, and
prepare the program folder. Limitations:

- the folder of the COM connector is not checked by the agent; the console's `check` will
  judge it by the rule of the program folder;
- a script without safe mode can start a process outside the job object through a
  service that its account may use, such as Task Scheduler or WMI, so the rights of the
  worker account are the boundary;
- the workers of one base run under one account, so a script without safe mode can read
  the commands of the other workers of its base, their 1C passwords included, and forge
  what they report.

The sample in `src/Moltaro.OnecOutpost.Samples` shows how an ASP.NET Core application
embeds the server. It runs with `dotnet run`, takes a PostgreSQL connection string from
the environment variable `OUTPOST_SAMPLE_POSTGRES`, applies the migrations, maps the
endpoints, creates a pairing token, lists the agents, releases a base key, revokes the new
agent, and runs a Read in a base that no agent holds, which ends as Refused with
`unknown-base`.

## What it is, and what it is not

A 1C:Enterprise database usually lives deep inside a company's network. Nothing outside
can connect to it, and the only way in is the 1C client. This library reaches it from the
other side:

- an agent installed next to 1C opens every connection itself, over HTTPS, so no port is
  opened and no route into the network is needed;
- the agent runs a command only when the server it was paired with signed it, and only
  once;
- the administrator of the agent machine decides which databases it serves, which 1C
  account reads and which writes, and whether a database may be written at all; every
  command is recorded in a local audit log;
- a command is a script in the 1C language, run whole inside 1C through the COM connector,
  with its result returned as JSON;
- the server is a library for an ASP.NET Core application. Its nodes share nothing in
  memory, so the application can run several of them.

It is a tunnel, not an API for 1C. It has two commands, Read and Write, and it knows
nothing of the objects of any 1C configuration. It does not replace the 1C client and it
does not touch the database of 1C directly.

Supported: 1C:Enterprise 8.3.17.1851 and later, client-server and file databases, with the
COM connector of the same version installed on the agent machine. The agent runs on
Windows; the server runs wherever .NET runs.

## Documentation

- [Concept](docs/concept.md): where it comes from, semantics and rules, protocol
  principles, decisions and quality bar.
- [Writing scripts](docs/guides/scripts.md): the guide for the developer who writes the
  scripts of Read and Write.
- [Documentation index](docs/README.md) and [current work](docs/work/README.md).
- [How the project is built](AGENTS.md): architectural principles, code style and working
  rules, for people and coding agents.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Run the commands from
the `src` directory.

```bash
dotnet build
```

```bash
dotnet test
```

The tests that need PostgreSQL start it in Docker through Testcontainers.

## Packages and downloads

Planned: the server library on [nuget.org](https://www.nuget.org) as one package,
`Moltaro.OnecOutpost.Server`, which carries the protocol assembly inside it, published by
the [Publish](.github/workflows/publish.yml) workflow when a version tag is pushed, and
the agent with its console as an archive on GitHub Releases, with its SHA-256 checksum. The
programs of the archive are not signed, so an antivirus program may ask for an exception
for the agent. Nothing is published yet.
[Branches and releases](docs/work/branches.md) describes how work reaches `main` and how a
version is released.

## License

[Apache License 2.0](LICENSE). The repository contains no part of 1C:Enterprise: the agent
uses the COM connector installed on its machine.
