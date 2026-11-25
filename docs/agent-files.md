# Agent files and COM worker messages

The agent's data folder is `%ProgramData%\Moltaro.OnecOutpost`. Administrators and
SYSTEM have full control. The service account can read the folder and write its audit
log and pending outcomes. The [agent guide](guides/agent.md) shows how to install the
agent, write its local policy and read its audit log.

| Path | Written by | Contents |
| --- | --- | --- |
| `policy.json` | Administrator | [Local policy](guides/agent.md#the-local-policy) for bases, accounts, modes and limits. The agent reads it at startup. |
| `agent.json` | Console `pair` | Server URL and pinned public key, agent ID and initial poll timings. |
| `secrets.dat` | Console `pair` and `secret` | Agent private key and passwords, encrypted with Windows DPAPI. |
| `audit\YYYY-MM-DD.jsonl` | Agent | Audit and recovery journal, one file per UTC day. |
| `audit\lock` | Agent | Exclusive lock held while the agent runs. |
| `outcomes\{command}.json` | Agent | One outcome awaiting confirmation from the server. |

## `agent.json`

The console writes one UTF-8 JSON object with these five fields. Their encodings follow
[protocol §2](protocol.md#2-encodings).

| Field | Value |
| --- | --- |
| `server` | Absolute URL with the agent endpoint prefix. HTTPS is required except for loopback HTTP. See [protocol §11](protocol.md#11-addresses-tls-and-the-proxy). |
| `agent` | Agent ID as a lowercase GUID with hyphens. |
| `serverKey` | Pinned server public key as the 91-byte P-256 `SubjectPublicKeyInfo` in unpadded base64url. |
| `pollTimeoutMs` | Poll timeout from the pairing answer, a whole number from 1,000 to 300,000 milliseconds. |
| `leaseGraceMs` | Poll lease grace from the pairing answer, a whole number from 1,000 to 300,000 milliseconds. |

## `secrets.dat`

The file is a Windows DPAPI blob protected for the machine. It depends on the data
folder access list because an account on that machine that can read the blob can
decrypt it. The console writes it for the service account to read.

The encrypted payload is UTF-8 JSON with three required fields in the encodings of
[protocol §2](protocol.md#2-encodings).

| Field | Value |
| --- | --- |
| `key` | Agent's ECDSA P-256 private key in PKCS#8 DER, encoded as unpadded base64url. |
| `passwords` | Array of objects with `base`, `mode` (`read` or `write`) and `password`. Each base and mode can appear once. |
| `workers` | Array of objects with `base` and `password` for the local Windows worker accounts. Each base can appear once. The array is empty when none are configured. |

`pair` writes a new key with empty password arrays, then writes `agent.json`. `secret`
writes the selected password and preserves the others. A required password missing
from the file stops the next service start.

## Audit log

Each entry is one compact UTF-8 JSON object ending in LF. It contains `time`
(`yyyy-MM-ddTHH:mm:ss.fffZ` in UTC) and `entry`. Command and agent IDs and durations
use the encodings of [protocol §2](protocol.md#2-encodings).

| `entry` | Other fields | When recorded |
| --- | --- | --- |
| `started` | `version`, `withoutSafeMode`, optional `error`. `withoutSafeMode` lists base keys whose Writes run without safe mode. It is empty if no base opts out or startup fails. | The agent starts or records why it could not start. |
| `policy` | `valid`, with `error` if false. | The agent records whether it could read a valid policy. |
| `connection` | `state` (`connected` or `lost`), with `reason` when lost. | The state of the agent's polls changes. See [protocol §9](protocol.md#9-answers-refusals-and-lost-answers) for the loss reasons. |
| `bases` | `held` and `claimed`, arrays of granted base keys and keys held by another agent. | The poll answer changes the agent's claims on its declared keys. |
| `received` | `command`, `base`, `mode` (`read` or `write`), `callerKey` (string or `null`), `script`, `parameters` (object), `issued`, `deadline`. | A command passed the agent's checks. This entry is durable before the command runs. |
| `refused` | `command`, `base`, `mode`, each possibly `null`, and `code`. For `signature` and `form` all three are `null`. | A command failed a check. A refusal with a command ID is the final local record of how it ended. |
| `ended` | `command`, `outcome` (`succeeded`, `failed` or `unknown`), `code` (string or `null`), `durationMs` (nonnegative integer or `null`). Recovery uses `null` when it has no recorded duration. | A command ended or its outcome was replaced. When the agent records a replacement, the later `ended` describes it. |
| `confirmed` | `command`. | The agent verified the server's confirmation of the outcome. |
| `reservation` | `agent`, `below` (an integer exclusive upper bound). | Before the first poll for which the agent has no reservation or whose number reaches the last reservation's bound. See [protocol §6.2](protocol.md#62-poll). |
| `stopped` | `error`, a plain reason that excludes scripts, parameters, results and secrets. | A fault stopped the agent after `started` and the audit log was still writable. |

The agent holds `audit\lock` for its whole run and flushes each entry to disk before
proceeding. The log is also its journal of command IDs and reserved poll numbers. It
cuts a torn final line at startup. Other unreadable entries stop startup. For recovery
and replay rules, see [protocol §13](protocol.md#13-what-stops-a-replay-and-what-does-not).

The agent considers retention after a verified poll answer and when its clock is
within one day of the answer's time. A nonempty file is eligible when its last entry
is older than `auditRetentionDays` plus one day. Empty files are also eligible. The
agent keeps the highest reservation for its current identity and entries for
unconfirmed commands. A confirmation stays while any other entry for its command
remains. A file that cannot be removed stays until a later retention pass.

## Pending outcomes

Each `outcomes\{command}.json` file holds the UTF-8 outcome object of
[protocol §8](protocol.md#8-the-outcome-object). The agent writes it durably under a
temporary name and renames it into place. It writes `ended` after that file and
`confirmed` before removing the file. A refusal goes to the log first. The agent
then writes a Refused outcome file when it knows the command ID.

On restart the agent removes temporary files and reconciles the outcome files with the
journal. It sends a saved outcome again, writes any missing or differing `ended` entry,
rebuilds a known refusal if its file is missing, and removes files already confirmed.
A `received` with no outcome file or `ended` becomes Unknown `agent-stopped` for a Write
or Failed `lost` for a Read. An invalid outcome file, one that the journal cannot match
to a command, or an unconfirmed `ended` with a missing outcome file stops startup.

If the server or ingress refuses a Succeeded or Failed outcome for its size, the agent
can replace it once with Unknown `result-too-large` for a Write or Failed
`result-too-large` for a Read. It records a second `ended` and sends the replacement.
See [protocol §9](protocol.md#9-answers-refusals-and-lost-answers). The result itself is
stored in the outcome file and stays out of the audit log.

## COM worker messages

The agent starts `Moltaro.OnecOutpost.ComWorker.exe <base key> <read|write>`. The key
identifies the base in diagnostics. The Windows console uses the same worker when it
checks a base. The worker exchanges one UTF-8 JSON object per LF-ended line on its
standard input and output, without a byte order mark. It reads commands sequentially
and opens a fresh `OutpostExecutor.epf` object for each command in its one COM session.

| Stream | Line | When |
| --- | --- | --- |
| Input | `{"location":"Srvr=\"srv:1541\";Ref=\"base\";","user":"outpost-read","password":"…","safeMode":true,"executor":"C:\\Outpost\\OutpostExecutor.epf"}` | First line only. `location` is the connection string up to the account details, with `Srvr` and `Ref` for a cluster or `File` for a file base. The worker adds the quoted user and password. |
| Output | `{"ready":true}` | The session opened. |
| Output | `{"ready":false}` | The session failed to open. The worker writes and flushes the failure diagnostic to standard error first, then writes this line and exits with 2. |
| Input | `{"script":"Result = 1;","parameters":{},"callerKey":null,"outcomeLimit":16777216}` | Each command. `parameters` is passed to the executor as received. `callerKey` is a string or `null`. `outcomeLimit` is the byte limit passed to the executor. |
| Output | `{"size":…,"outcome":{…}}` | Each outcome. `size` is the UTF-8 byte count of the outcome object of [protocol §8](protocol.md#8-the-outcome-object). The worker forwards the executor's line unchanged, with its LF. |

Standard error carries diagnostics separately. The worker never repeats the
connection line. It withholds a 1C or .NET failure message if it contains the
nonempty password literally or with quotes doubled. The service does not keep
worker diagnostics. The console's `check` captures them for a session that fails to
open.

| Exit code | Meaning |
| --- | --- |
| 0 | Standard input ended, including before the connection line. A line at EOF without LF still counts as a line. |
| 1 | Invalid command line or invalid input. A Read connection line with `safeMode` set to `false` is invalid. The worker writes a reason to standard error and emits no output line for that input. |
| 2 | The session failed to open or the executor could not open. For the latter, the worker writes a diagnostic first, then reports Failed `base-unreachable` for either mode. |
| 3 | The executor's `Run` call threw. The worker writes a diagnostic first, then reports Failed `connection-lost` for a Read or Unknown `connection-lost` for a Write. |

A line break returned by the executor would split the output frame. The worker reports
Failed `result-type` for a Read or Unknown `result-type` for a Write and continues.
Worker-generated outcomes have JSON `null` for `result` and `errorText`, an empty
`messages` array and `messagesTrimmed` set to `false`.

The console's startup probe closes the worker's input before it opens a session. It
treats exit code 0 as success and code 1 as a refused command line. That probe checks
whether the worker can start under its Windows account. The separate connection check
opens a 1C session.
