# Why a command never runs twice

A message between the agent and the server can be lost, held back, repeated or replayed,
by the network or by something on the path that opens TLS, and a caller can call again.
None of this makes a command run twice. This page shows why, one short scenario per case.
It is written for the person responsible for a back office and for a developer of an
application that embeds the server. The rules are rules 3, 4, 7, 10 and 11 of the
[concept](../concept.md) and §6 and §13 of the [protocol](../protocol.md). The same
explanation, animated, is [`never-twice.html`](never-twice.html); the path of a command
without losses is [The path of a command](command-path.md).

**Status:** built. Each case below is a test that passes today over loopback HTTP, without
1C: what the cases show happens before a script starts and after it ends. Cases 2 and 3,
case 4's lost confirmation, and case 5's command that stays taken past the take deadline
plus the skew and runs once, run with two server nodes on one PostgreSQL, the real agent
with a fake in place of the COM worker, and a proxy between them that drops and holds
chosen requests and answers. Case 1, and case 5's expiry by a later poll, run against one
server node with a test agent. Case 4's sending again after a restart runs in the tests of
the agent's loop.

Three things carry the rule:

- **the caller key**: the server keeps one command per base and caller key;
- **the audit log**: the agent records every command before it runs it, and a command
  whose identity is in its log never runs again;
- **the list of every poll**: each poll lists the commands the agent received whose
  outcome the server has not confirmed, and only the newest poll counts.

Once is not the same as known. When the agent cannot tell whether a Write committed, for
example because the connection to 1C ended during the commit, its outcome is Unknown,
never a success or a failure.

In the scenarios, poll numbers are shortened. The agent takes real ones from its clock,
always growing.

## 1. A repeated call with its key

1. The application calls `WriteAsync` with the caller key `invoice-17`. The server creates
   the command, and the agent runs it.
2. The application's wait ends, by a timeout of its own or a restart, and it calls again
   with the same key, script and parameters.
3. The server finds the command of that key before anything else, creates nothing, waits
   for its outcome and returns it. A repeat with another script or other parameters is
   Refused with `caller-key-conflict`.

The key is free for a new command only when the first ended as Expired or Refused, so that
nothing ran, or when its outcome was removed after its retention, 7 days by default.

## 2. A lost poll answer

1. Poll 41 takes the command, and its answer is lost on the path. The agent never saw the
   command.
2. The agent's next poll, 42, does not list the command as received.
3. The server hands the command out again in the answer to poll 42, since the take
   deadline has not passed. The agent records it and runs it once.

A command that the list of a newer poll does not hold had not reached the agent by the
time that poll was sent; it may still arrive, which case 5 handles. Should the same
command ever come twice, the audit log holds its identity, and it does not run
again.

## 3. An older poll

1. Poll 41 is held back on the path. The agent's wait ends, and it sends poll 42, which
   becomes the newest.
2. Later the held poll 41 arrives, and someone replays poll 42 byte for byte.
3. Both are older: their numbers are not greater than the newest the server recorded. They
   end without commands and change nothing: not the lease, not the bases, not the list of
   received commands.

Only the newest list is believed, so an old list cannot make the server hand out again a
command that the agent has received. The numbers grow across restarts, even when the clock
is set back, because the agent reserves them in its audit log.

## 4. A lost outcome

1. The command ran. The agent keeps its outcome in a file on its disk, records it in its
   audit log, and sends it.
2. The server keeps the outcome and the caller gets it, but the confirmation is lost on the
   path.
3. The agent sends the same outcome again, even after a restart. The server confirms it
   and changes nothing: only the first outcome of a command counts, and only from the agent it
   was handed to.

The outcome travels again; the script does not run again.

## 5. The expiry decided by the agent's time

1. Poll 41 takes a Write, and its answer is lost. The server cannot tell whether it is gone
   or only held back on the path, to reach the agent later.
2. The take deadline passes by the server's clock. Were the command Expired now, its caller
   key would be free, a repeat would create a new command, and the old one, delivered late,
   could run as well.
3. So the command stays taken. The agent accepts a command only until its own clock passes
   the take deadline plus the allowed skew, 2 minutes by default, and every poll carries
   the agent's time inside its signature.
4. The command ends as Expired only when a poll whose time is past the take deadline plus
   the skew does not list it. By then the agent would refuse it as `expired`, so nothing
   can run it, and the key is free.

The clock of the agent decides, because the agent is the one that would run the command.

## What this rests on

- **The audit log of the agent.** It is the agent's journal of received commands and its
  reserved poll numbers. A log that was cleared is found, with the one gap that §13 of the
  protocol names, and the agent stops until it is paired anew. A log restored from an older
  copy is not found, and can make a Write run twice; such an agent is paired anew.
- **One running copy of each agent.** A clone of the agent machine carries its identity and
  voids these rules until it is paired anew with a token of its own.
- **A clock that does not jump back.** An agent whose clock jumps back voids the expiry of
  case 5.
