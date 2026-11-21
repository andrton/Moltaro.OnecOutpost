# Moltaro.OnecOutpost

Run read and write scripts in 1C:Enterprise 8.3 and BAS databases hidden in a back office.
A Windows agent next to 1C dials out over HTTPS, runs signed scripts through the COM
connector and returns JSON; an embeddable .NET server queues the commands and scales
across nodes.

## Status

Contract stage. The [concept](docs/concept.md) is agreed, and the first epic, up to the
first release, is under way in [current work](docs/work/README.md). The public contract of
its first stage exists in code: the types and members of the server library, with their
documentation. Only the checks work: the options of the server, the arguments of every
call, the outcome factories, and the rules of the agent's local policy file, which the
agent program does not read yet. The protocol assembly holds the codec of the
[wire protocol](docs/protocol.md): its messages, the signatures of commands, requests and
answers, and the pairing proof, with their tests; no endpoint serves it yet. The COM worker
runs scripts in 1C: it holds an external connection to one base and runs each command
through the executor, an external data processor in `src/Executor`, in safe mode or
without it. No agent starts it yet. Its tests run against a fake of the COM connection;
its tests against a real 1C, in `src/Moltaro.OnecOutpost.OneC.Tests`, have not yet run on
the lab machine, and the executor's `.epf` is not built yet. Everything else throws
`NotSupportedException`: pairing, Read and Write, the endpoints of the agents and the
migrations of the store. Nothing below works yet.

The sample in `src/Moltaro.OnecOutpost.Samples` shows how an ASP.NET Core application
embeds the server. It runs with `dotnet run`, takes a PostgreSQL connection string from
the environment variable `OUTPOST_SAMPLE_POSTGRES`, and says where it stops.

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
