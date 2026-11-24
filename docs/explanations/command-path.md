# The path of a command

How one Read or Write travels from the application to 1C and back, and what each part
checks on the way. It is written for the person responsible for a back office and for a
developer of an application that embeds the server. The rules themselves are in the
[concept](../concept.md) and the [protocol](../protocol.md); this page follows them along
the path. The same explanation, animated, is [`command-path.html`](command-path.html).

**Status:** built. The path works end to end. It is tested over loopback HTTP with one
server node and with two on one PostgreSQL, with the real agent and a fake in place of the
COM worker. The agent with its COM workers ran Reads and Writes in a real
1C:Enterprise 8.3.17.1851 on the lab machine, with the test's own server, a Write without
safe mode under the Windows account of its base included. The console installs, pairs and
checks an agent on its machine and writes a metadata report of a base. The lab confirmed
the check and the report on the test base and a copy of a typical base. The shell is planned.

## The parts

- **The application** embeds the server library and calls `ReadAsync` or `WriteAsync`.
- **A server node** is any process of the application. Nodes share nothing in memory.
- **The store** is the library's tables in PostgreSQL. Every node reads and writes the
  commands and their outcomes there.
- **The signals** are Moltaro.Signals, which wake a waiter on any node. A signal carries no
  data: it says "read the store again".
- **The agent** is the Windows service next to 1C. It opens every connection itself, over
  HTTPS, and keeps one poll open at a time.
- **The audit log** is the agent's record of every command, on the disk of its machine.
- **The COM worker** is a process of the agent that holds a session in one base under one
  1C account, and runs scripts there through the executor. It runs under the agent's
  Windows account, or, in a base whose Writes run without safe mode, under the Windows
  account of that base.

## The path

1. **The call.** The application calls `WriteAsync` or `ReadAsync` on any node, with the
   key of the base, the script, its parameters and, for a Write, a caller key. The node
   looks up the caller key first: a command that already has it is followed, not created
   again ([Why a command never runs twice](never-twice.md)). Then it checks that an agent
   holds the base and serves the mode, and that this agent is connected, which means that
   the lease of its newest poll has not run out. If it is not connected, the call returns
   Unavailable at once and nothing is created.
2. **The store.** The node signs the command with the server's key and writes it to the
   store, where it waits to be taken. The signed bytes name the agent, the base, the mode
   and the caller key, and carry the script, the parameters, the time it was issued, its
   take deadline (30 seconds by default), the allowed clock skew and the size limit of its
   outcome. The call now waits for the outcome of that command.
3. **The signal.** Once the command is in the store, the node sets a signal for the agent.
   It wakes whatever waits for that agent, on any node, to read the store. When a signal is
   lost, the command waits for the agent's next poll, at most one poll's hold later. If its
   take deadline passes meanwhile, it ends as Expired, untaken; a lost signal never makes a
   command run twice.
4. **The poll.** A poll of the agent waits on some node, often not the node of the call.
   Woken, the poll takes the command in one conditional change of the store, if it is
   still the newest poll of its agent: oldest commands first, and no more than the free
   workers it declared. Its answer carries the exact bytes of the command and the server's
   signature over them. A command that no poll takes before its take deadline ends as
   Expired.
5. **The agent's checks.** The agent verifies the signature with the server key it pinned
   at pairing, and looks for the command in its audit log: a command already there never
   runs again. Then it checks the protocol version, that the command names this agent,
   that its local policy serves the base and the mode, and, by the agent's own clock, that
   the command was not issued too far ahead and that its take deadline has not passed by
   more than the allowed skew. The first check that fails refuses the command with its
   code, such as `not-served` or `expired`. A command whose signature does not verify is
   only recorded, since nothing in it can be trusted.
6. **The audit log.** Before the command runs, and before the next poll opens, the agent
   writes it to its audit log on the disk: the time, the identity, the base, the mode, the
   caller key, the script and the parameters. A command that cannot be recorded does not
   run. From then on every poll lists the command as received, until the server confirms
   its outcome.
7. **The worker.** The agent gives the command to a free COM worker of its base and mode,
   a process of its own that holds a 1C session under the account of that mode. The
   worker runs the script whole, in 1C's safe mode unless the local policy turns it off
   for the Writes of the base: a Read outside a transaction, a Write inside one
   transaction, committed or rolled back as a whole. Every worker of a base whose Writes
   run without safe mode runs under a Windows account of its own, which can read neither
   the agent's secrets nor its audit log. The executor writes the
   result as JSON, and checks the result's type and the outcome's size before a Write
   commits. A script that passes the run limit of its mode ends with its worker.
8. **The outcome.** The agent keeps the outcome in a file on its disk, records how the
   command ended in its audit log, and sends the outcome to any node, signed with its own
   key. The node keeps the first outcome of the command, and only from the agent it handed
   the command to. It sets a signal for the command and confirms the outcome to the agent
   with a signed answer. The agent records the confirmation and removes the file; until then it
   sends the outcome again.
9. **The caller.** The node where the call waits wakes, reads the outcome from the store
   and returns it: Succeeded with the result and the user messages of 1C, Failed with a
   code, Refused with the code of the check, Expired, or Unknown when the effect of a Write
   cannot be known. The caller may stop waiting at any time. That does not stop the
   command, and a repeat with the caller key returns its outcome.

## What the path does not do

- Nothing dials into the back office: the agent opens every connection.
- The server keeps nothing for later delivery. A call for an agent that is not connected
  is Unavailable at once, and a command that no poll took in time is Expired.
- Nothing on the path can change or inject a command, since the agent runs only bytes the
  server signed. Something that opens TLS on the path can read, delay and drop messages;
  [Why a command never runs twice](never-twice.md) shows what then happens.
- The library does not decide which scripts are allowed. The local policy decides the
  bases, the accounts and the modes, and an application that approves scripts does so
  before it calls.
