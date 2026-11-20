# Current work

The work is organized in epics. An epic is a result that is useful on its own; it is
delivered over several sprints.

| Epic | Result | Status |
| --- | --- | --- |
| [1. First release](epic-1-first-release.md) | The agent, the server and the tools as the concept describes them; the server published on nuget.org and the agent released as its first version | In progress: the plan is approved, and the [status of its sprints](epic-1-first-release.md#status) says where each stands |

## The repository as it is set up

- `src` holds the build settings and the solution. Its projects are the protocol, the
  server, the agent, the samples, the test support and the test projects of the first
  three. The other projects that `AGENTS.md` plans are created by the sprints that first
  need them.
- The tests run on xUnit.net v3 and Microsoft.Testing.Platform, which `src/global.json`
  names as the runner. A test that needs PostgreSQL has the trait `Requires=PostgreSQL`
  and starts it in Docker through Testcontainers; a test that needs Windows has the trait
  `Requires=Windows`.
- CI builds and tests every pull request on Linux (`build`) and on Windows (`windows`).
  Both are required checks of `main`, because the agent and the COM worker run on Windows
  only. The `build` job runs every test without `Requires=Windows`, and the `windows` job
  every test without `Requires=PostgreSQL`, because GitHub's Windows runners have no
  Linux containers.
- The rules of `main` and of release tags are files in `.github/rulesets`. GitHub applies
  them only after the owner does, as [branches and releases](branches.md#setting-it-up-on-github)
  describes.
- Publishing uses nuget.org Trusted Publishing. The owner creates its policy once, as
  [branches and releases](branches.md#setting-it-up-on-github) describes.

How an epic is planned, carried out sprint by sprint and reviewed is described in the
[development process](process.md). Where the commits go, what `main` refuses and how a
version is released is in [branches and releases](branches.md). Every place where the
project departs from a rule of `AGENTS.md` is recorded, with its reason, in
[exceptions to the rules](exceptions.md). A question that a sprint finds and that nothing
answers yet is written down in the [open questions](open-questions.md), and they are gone
through with the owner when an epic closes.

## How an epic document is written

One file per epic, `epic-<number>-<short-name>.md`, with these parts:

1. **Goal** — the result and who it is for.
2. **Scope** — what is included and what is deliberately left out.
3. **Requirements** — numbered `R1`, `R2`, …; each says what is needed, what needs it and
   whether it exists today. Other documents refer to them as "Epic 2 R3".
4. **Acceptance** — how we know the epic is done.
5. **Open questions** — decisions that are not made yet, each with the options.
6. **Sprints** — the order of work, without dates. An epic that is ready to be carried out
   has one brief per sprint, as the [development process](process.md#the-plan) describes,
   and the status of each sprint. Before that, an outline of stages is enough.

A requirement level is one of:

- **Must** — the epic cannot be finished without it.
- **Should** — needed for a good result; a documented workaround is acceptable at first.
- **Release** — not needed to develop and test, but needed before anybody relies on it.

When an epic is finished, what it delivered is described in the product documentation, and
its working document is removed from this folder.
