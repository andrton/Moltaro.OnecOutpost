# Exceptions to the rules

[AGENTS.md](../../AGENTS.md) owns the rules of the project. This document records every
place where the project departs from one of them: which rule, where, why, and until when.
It exists so that a departure is a decision somebody can find and question later, not
something a reader has to guess from the code.

## How an exception is recorded

- An exception is approved by the owner before it is used.
- It is entered under **Approved, not in force yet** when the owner approves it, moved to
  **In force** by the change that introduces the departure, and moved to **Ended** by the
  change that ends it.
- An entry is numbered `X1`, `X2`, … and keeps its number for good. It names the rule in
  the words of `AGENTS.md`, the exact place of the departure, the reason the rule cannot be
  followed there, and what keeps the departure small.
- A temporary exception names the sprint or the event that ends it.

Some exceptions are also named in `AGENTS.md`, next to their rule, so that nobody takes
them for violations; this register gives each its reason and its bounds. A rule that is
replaced by another rule is reworded in `AGENTS.md` and is not recorded here.

An entry is a table:

| | |
| --- | --- |
| **Rule** | The rule, in the words of `AGENTS.md` |
| **Departure** | Where exactly the project departs from it |
| **Why** | Why the rule cannot be followed there |
| **Kept small by** | What bounds the departure |
| **Approved** | By whom and when |
| **Since** | The change or sprint that introduced it |
| **Until** | The sprint or event that ends it, or "Permanent" |

## In force

None.

## Approved, not in force yet

### X1. The built executor in Git

| | |
| --- | --- |
| **Rule** | "No secrets, tokens, machine paths, build output or run output in Git." (`AGENTS.md`, "Working Rules") |
| **Departure** | The built external data processor `src/Executor/*.epf` is committed next to its XML sources |
| **Why** | Only the 1C Designer builds an `.epf`, and CI has no 1C. The archive of a release needs the built file, and a reader of the repository can open it without the Designer |
| **Kept small by** | Only the `.epf` of the executor, built by the owner with the Designer of 1C:Enterprise 8.3.17.1851 from the sources in the same commit, and rebuilt whenever they change. No other build output |
| **Approved** | The owner, 20 November 2026 |
| **Since** | Epic 1, S9, with the first commit of the `.epf` |
| **Until** | Permanent |

## Ended

None.
