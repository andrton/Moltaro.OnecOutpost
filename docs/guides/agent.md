# Installing and running an agent

For the administrator of a back office: how to install the Moltaro.OnecOutpost agent on a Windows machine next to a
1C:Enterprise base, pair it with the server of an application, check it and keep it running.

**Status:** every command of the console that this guide shows is built and tested: against fakes of Windows, of 1C
and of the server on every platform, and in CI's elevated Windows job against the service controller, the local
security policy and the access lists. The lab confirmed `check` and `metadata` on the test base and a copy of a
typical base. The manual run of the full installation with this guide is planned with the shell. The archive is
built with the first release.

## How it fits together

The agent is a Windows service that runs next to 1C. It opens every connection itself, over HTTPS to the server of an
application, and never listens on a port. The server sends it commands, Read and Write, each a script in the 1C
language. The agent checks that the server signed a command, runs it in 1C through the COM connector, and sends the
result back. It records every command in a local audit log before it runs it.

You, the administrator of the agent machine, keep the last word. A file on this machine, the local policy, says which
bases the agent serves, under which 1C accounts it reads and writes, and whether a base may be written at all. The
server cannot change it. The agent does not decide which scripts may run: the local policy decides bases, accounts and
modes, and an application that wants to approve scripts does so before it sends them.

The console, `Moltaro.OnecOutpost.Console.exe`, sets the agent up. Its commands, in the order of this guide:

| Command | What it does |
| --- | --- |
| `install` | Installs the service and its two folders, or updates them |
| `connector register` | Registers the COM connector of 1C, when installing 1C did not |
| `pair <SERVER> <TOKEN>` | Pairs the agent with the server |
| `secret <BASE> <KIND>` | Writes a password into the agent's secrets |
| `check` | Checks the whole machine and shows one table |
| `metadata <BASE> <FILE>` | Writes a local report of the metadata of a base |
| `uninstall` | Removes the service, leaving its folders |

Every command needs PowerShell run as administrator: right-click PowerShell and choose "Run as administrator". Without
it, a command changes nothing and ends with, for example:

```text
NotElevated (3): install needs a console run as administrator; nothing was changed
```

Each command ends with an exit code, which PowerShell shows as `$LASTEXITCODE`; the codes are listed in
[The exit codes of the console](#the-exit-codes-of-the-console). A command that ends otherwise than with success
writes one line in red, `{Kind} ({code}): {reason}`. A step that failed for a reason of Windows or of the disk ends
with `Failure (1): the step "{step}" failed: {message}`, followed by `(Windows error {code})` when Windows gave one.
Ctrl+C ends a command between two of its steps with `Cancelled (5): the command was cancelled`. When the output is
not a terminal, as in a script, the console writes the same lines as plain text, without colour, spinners or prompts.

This guide writes the two folders of the agent as `C:\Program Files\Moltaro.OnecOutpost` and
`C:\ProgramData\Moltaro.OnecOutpost`, where Windows keeps `%ProgramFiles%` and `%ProgramData%` on most machines. Its
examples use the repository's own test configuration: the base key `test`, the 1C accounts `outpost-read` and
`outpost-write`, and the Windows account `outpost-worker`. A value that depends on your machine is written in angle
brackets, such as `<version>`.

## Before you start

You need:

- **A Windows machine** that runs the versions of Windows that .NET 10 supports. Nothing else needs installing: the
  programs carry their own .NET runtime.
- **The 1C:Enterprise platform of exactly the version of the 1C cluster**, installed on that machine with its COM
  connector, `comcntr.dll` in the platform's `bin` folder. A connector of another version does not connect. The oldest
  version supported is 8.3.17.1851. For a file base, the version that the base is opened with.
- **A route to the 1C cluster**, as any 1C client of the network has, or to the folder of a file base.
- **A route to the server over HTTPS**, usually on port 443, directly or through an HTTP proxy. A proxy that asks for
  authentication is not supported: the agent offers a proxy no credentials, so that nothing on the path can relay
  them, and it is not connected through such a proxy. A proxy that opens TLS works when Windows trusts its
  certificate: the agent validates the server's certificate as Windows does, and checks the server's signature on
  everything it acts on.
- **Two 1C accounts** in each base, as [The 1C accounts](#the-1c-accounts) says.
- **From the application's side**, which the developer or operator of the application gives you:
  - the address of the server, an `https://` address that this machine can reach, such as
    `https://<server host>/<prefix>`;
  - a pairing token for this agent, a string that starts with `outpost1.`. It works once, and expires after
    15 minutes unless the application chose another time, so ask for it when you are ready to pair;
  - the base key under which the application will call each base: 1 to 64 characters of `a`–`z`, `0`–`9`, `-`, `_`
    and `.`, the first a letter or a digit. The key in your local policy has to be the one the application uses.

One agent machine serves the bases of one platform version: the agent starts every COM worker with the first
registered connector, 64 bits first. Bases of another version need an agent machine of their own.

## The 1C accounts

The agent opens each base under two 1C accounts of your choice, which the administrator of the base creates with 1C
authentication and a password:

- **The read account** runs every Read. A Read is read-only only through the rights of this account, so it gets the
  rights to read what the scripts read, and **no right to change data**: no right to insert, update or delete, to
  post or unpost a document, to start a business process or execute a task, and none of `Administration`,
  `DataAdministration` or `UpdateDataBaseConfiguration`. `check` lists every such right it holds.
- **The write account** runs every Write, inside one transaction. It gets the rights to read and change what the
  scripts of the application change, such as posting its documents.
- **Both need the right "External connection"** of the configuration. Without it 1C opens no session for the account.

A base without users opens to anybody with every right, so `check` fails such a base: create the users first.
In the repository's test configuration, the role `OutpostRead` reads every object and changes none, and the role
`OutpostWrite` also writes and posts; both have the right "External connection" and nothing else on the
configuration. In another configuration, the roles that give these rights are those of that configuration; ask its
developer which ones read only.

The passwords of the two accounts stay on the agent machine, encrypted, as [The passwords](#the-passwords) says. They
never travel to the server.

## Unpacking the archive

The agent comes as one archive, `Moltaro.OnecOutpost-<version>-win.zip`, on the release page of the repository, with
its SHA-256 checksum. It holds the agent `Moltaro.OnecOutpost.Agent.exe`, the console
`Moltaro.OnecOutpost.Console.exe`, the COM worker for 64 bits in `x64\` and for 32 bits in `x86\`, the executor
`OutpostExecutor.epf` that runs a script inside 1C, the licence and the notices. Its programs are not signed, so an
antivirus program may ask you to allow the agent.

Check the checksum, then unpack the archive into a folder of its own:

```powershell
Get-FileHash .\Moltaro.OnecOutpost-<version>-win.zip -Algorithm SHA256
Expand-Archive .\Moltaro.OnecOutpost-<version>-win.zip -DestinationPath <folder>
```

The `Hash` that `Get-FileHash` shows has to be the checksum of the release page, letter for letter, ignoring case. If
it is not, the file is not the one released: do not use it. The folder may lie anywhere except inside
`C:\Program Files\Moltaro.OnecOutpost`, and it may not hold that folder, such as `C:\Program Files` itself, since
`install` copies its whole content there.

## Installing

From the unpacked folder:

```powershell
Set-Location <folder>
.\Moltaro.OnecOutpost.Console.exe install
```

On a machine where the agent was never installed:

```text
There is no service MoltaroOnecOutpost yet.
Copied the program to C:\Program Files\Moltaro.OnecOutpost and set its access lists.
Registered the service MoltaroOnecOutpost under NT SERVICE\MoltaroOnecOutpost.
Granted NT SERVICE\MoltaroOnecOutpost "Replace a process level token" and "Adjust memory quotas for a process".
Created C:\ProgramData\Moltaro.OnecOutpost with its access lists.
Installed; the service is not started. Start it once policy.json, the pairing and the passwords are written; an update keeps those written before.
```

What it did:

- **The program folder**, `C:\Program Files\Moltaro.OnecOutpost`, holds a copy of the unpacked archive. Administrators
  and SYSTEM have full control, Users may read and run it, and nobody else has a right; Administrators own everything
  in it. The agent refuses to start when anyone else could change its code, since an account that could replace a
  program the agent runs would get its secrets. `install` judges the folder by the same rule, and the folders above
  it up to the root of the volume, before it registers the service.
- **The service** `MoltaroOnecOutpost`, shown as "Moltaro OnecOutpost agent", runs
  `"C:\Program Files\Moltaro.OnecOutpost\Moltaro.OnecOutpost.Agent.exe" service` under its own virtual account
  `NT SERVICE\MoltaroOnecOutpost`, which has no password. It starts automatically with Windows. When the agent
  program crashes, Windows starts it again after a minute, at most three times a day; when it stops on its own with an
  error, it stays stopped until you start it. `install` does not start it.
- **The two rights** of that account, "Replace a process level token" and "Adjust memory quotas for a process", let it
  start a COM worker under another Windows account, as [Writes without safe mode](#writes-without-safe-mode) needs.
  The service has them from its next start.
- **The data folder**, `C:\ProgramData\Moltaro.OnecOutpost`, holds the local policy, the pairing, the secrets, the
  audit log and the outcomes not yet sent. Administrators and SYSTEM have full control; the agent's account may read
  it, and change only `audit\` and `outcomes\`; nobody else has any right, and inheritance from `C:\ProgramData` is
  off, since every local user may create files there. The agent refuses to start when the folder, or a file in it,
  admits anybody else. So you read and edit its files from PowerShell run as administrator.

From now on, run the console from the program folder, which holds the copy that `install` made:

```powershell
Set-Location "$env:ProgramFiles\Moltaro.OnecOutpost"
```

### When it fails

| Line | Cause | Remedy |
| --- | --- | --- |
| `Usage (2): unpack the archive outside C:\Program Files\Moltaro.OnecOutpost and run install from there` | The console ran from a folder inside the program folder | Unpack the archive elsewhere |
| `Usage (2): install runs from {folder}, which holds C:\Program Files\Moltaro.OnecOutpost; unpack the archive into a folder of its own outside %ProgramFiles% and run install from there` | The archive was unpacked straight into a folder that holds the program folder | Unpack it into a folder of its own |
| `StopTimedOut (7): the service MoltaroOnecOutpost did not stop within 00:01:00; run install again once it has, or with a longer --stop-timeout` | On an update, the service was still finishing its commands | Run it again, or with `--stop-timeout 00:05:00`; the time is `[d.]hh:mm:ss`, from `00:00:01` to `01:00:00` |
| `ProgramFolderWriters (8): someone else may change C:\Program Files\Moltaro.OnecOutpost through the entries above, so the agent would refuse to start; take those rights away and run install again` | A table above the line names each entry and who else may change it, such as a parent folder or the root of the volume that gives Authenticated Users the right to delete | Remove those rights, then run `install` again |
| `DataFolderAdmitsOthers (9): {entry} admits {accounts}; install does not repair it: inspect it, remove it and run install again; if the agent ever ran there, copy its audit\ folder elsewhere first` | The data folder, its `audit` or its `outcomes` existed and gives somebody else a right, or `is a reparse point` | As it says |
| `Failure (1): the step "service" failed: … (Windows error 1072); close the Services console and Event Viewer, and run install again` | A service of that name was deleted, and a program still holds it open | Close them and run it again |

## The COM connector

Installing 1C does not register its COM connector, so after a new installation of the platform no program can connect
to a base until it is registered. The agent and `check` look for the connector only where the service sees it: in the
classes of the machine, in the 64-bit view and in the 32-bit one, and a registration counts only when its file exists.
A registration of the current user alone does not count. The agent starts its COM workers with the bitness of the
first registered connector, 64 bits first.

`connector register` registers it for you:

```powershell
.\Moltaro.OnecOutpost.Console.exe connector register
```

It looks in `C:\Program Files\1cv8` and `C:\Program Files (x86)\1cv8` and writes one line for each bitness. A
connector that was registered already:

```text
64-bit: C:\Program Files\1cv8\<version>\bin\comcntr.dll, <version>, is registered.
```

One that it registered now:

```text
64-bit: registered C:\Program Files\1cv8\<version>\bin\comcntr.dll, <version>.
```

It registers a connector only when exactly one version of that bitness is installed, since the connector has to match
the version of the 1C cluster and the console cannot know which that is. With several versions it registers none and
names each with the command that registers it:

```text
64-bit: none is registered, and the connector has to match the version of the 1C cluster, so register that one:
<version>: C:\Windows\System32\regsvr32.exe "C:\Program Files\1cv8\<version>\bin\comcntr.dll"
```

Run the line of the cluster's version yourself, in the same PowerShell, then run `connector register` again to see it
registered. A platform installed outside the two `1cv8` folders is found only once it is registered; register its
`comcntr.dll` with `regsvr32.exe` in the same way.

### When it fails

| Line | Cause | Remedy |
| --- | --- | --- |
| `64-bit: none is registered or installed.` | No platform of that bitness is in the `1cv8` folders | Fine when the other bitness has a connector. Otherwise install the platform of the cluster's version |
| `64-bit: regsvr32 ended with {code}, {kind}, for {path}.` | `regsvr32` refused the file: `bad arguments`, `OleInitialize failed`, `loading the DLL failed`, `no DllRegisterServer in the DLL` or `DllRegisterServer failed` | Usually a damaged or incomplete installation of the platform: repair it, then run the command again |
| `64-bit: regsvr32 ended with 0, yet HKLM shows no registration of {path}.` | The registration did not reach the classes of the machine | Run the command again from PowerShell run as administrator |

Then the command ends with one of:

```text
RegistrationFailed (18): regsvr32 registered no 64-bit connector, as the line of each says; run connector register again once the cause is removed
ConnectorNotRegistered (17): no bitness has a COM connector registered, so the agent can start no COM worker; register the copy that matches the version of the 1C cluster
```

## The local policy

The local policy is the file `C:\ProgramData\Moltaro.OnecOutpost\policy.json`, which you write. The agent reads it
when it starts, so a change takes effect when the service restarts. `pair` and `secret` read it too, so write it
before them.

### A client-server base

A whole policy for the test base on a 1C cluster, read and written, with Writes outside safe mode:

```json
{
  "auditRetentionDays": 90,
  "proxy": "http://<proxy host>:<proxy port>",
  "retryDelayMax": "00:00:30",
  "requestTimeout": "00:05:00",
  "bases": [
    {
      "key": "test",
      "server": "<cluster host>:1541",
      "database": "outpost_test",
      "read": { "account": "outpost-read", "workers": 1, "runLimit": "00:10:00" },
      "write": { "account": "outpost-write", "workers": 1, "runLimit": "00:10:00", "safeMode": false, "workerAccount": "outpost-worker" }
    }
  ]
}
```

### A file base

A whole policy for a file base, read and written in safe mode, without a proxy, every other member at its default:

```json
{
  "bases": [
    {
      "key": "test-file",
      "file": "<folder of the file base>",
      "read": { "account": "outpost-read" },
      "write": { "account": "outpost-write" }
    }
  ]
}
```

JSON writes a backslash twice, so the folder `D:\Bases\Test` is `"D:\\Bases\\Test"`. The COM workers open a file base
under the agent's account, or under the worker account of the base when it has one, so that account needs the rights
to change the files of that folder. No test of the lab reaches a file base yet: the lab's 1C server gives no licence
to a file base.

### Member by member

| Member | Where | Rule |
| --- | --- | --- |
| `auditRetentionDays` | top | How long the audit log is kept, 1 to 3650 days; 90 when absent |
| `proxy` | top | The HTTP proxy to the server, as an absolute `http://` address without a user or a password. When absent, the agent connects directly |
| `retryDelayMax` | top | The longest delay between attempts after an error, to reach the server and to start a COM worker again; the first delay is a tenth of it. From `00:00:01` to `01:00:00`; `00:00:30` when absent |
| `requestTimeout` | top | How long the agent waits for an answer of the server to `pair` or to an outcome, and for the body of a poll answer. From `00:00:10` to `01:00:00`; `00:05:00` when absent |
| `bases` | top | At least one base and at most 256, each key once |
| `key` | base | The base key that the application uses |
| `server`, `database` | base | The 1C cluster, with its port when it is not the default, and the name of the base on it. Either both, or `file` |
| `file` | base | The folder of a file base. Either this, or `server` and `database` |
| `read` | base | Required: the read mode |
| `write` | base | Only when the base may be written. Without it, every Write of the base is refused |
| `account` | mode | The 1C account of the mode. Its password is written with `secret` |
| `workers` | mode | How many COM workers the mode runs, 1 to 8; 1 when absent. Each runs one command at a time and holds a licence, as [Licences](#licences) says |
| `runLimit` | mode | The longest a script of the mode may run, and the longest a worker may take to open its session, from `00:00:01` to `1.00:00:00`; `00:10:00` when absent. A script that runs longer is ended with its worker: a Read fails, a Write is reported as unknown |
| `safeMode` | `write` only | Whether the Writes of the base run in 1C's safe mode; `true` when absent. A Read always runs in safe mode, so `read` does not take it. See [Writes without safe mode](#writes-without-safe-mode) |
| `workerAccount` | `write` only | The local Windows account that runs every COM worker of the base, Reads included. Required when `safeMode` is false, refused otherwise. 1 to 20 characters, none of `" / \ [ ] : ; \| = , + * ? < > @`, so never a domain account; a different account for each base |

A time is written as `[d.]hh:mm:ss`, such as `"00:10:00"` for ten minutes or `"1.00:00:00"` for a day. An unknown
member, a member given twice, a wrong type, `null`, a value outside its rule, an empty string, a comment or a trailing
comma makes the whole file invalid, and then the agent serves nothing. The reason names the member and what is
expected there, and never repeats a value of the file, which may hold a secret, for example:

```text
policy.json is not valid: bases[0].server: the value is empty; expected the 1C cluster address such as "srv-1c:1541", or remove "server" and "database" and give "file"
```

### Writing and editing the file

The agent counts the owner of a file as somebody who could change its access list, so a file of the data folder has to
be owned by Administrators, SYSTEM or the agent's account, and a file moved into the folder keeps the access list it
had elsewhere. So write the file in place, from PowerShell run as administrator, and never move one in. For example,
put your policy into a variable and write it over the file:

```powershell
$policy = "$env:ProgramData\Moltaro.OnecOutpost\policy.json"
@'
{
  "bases": [
    { "key": "test", "server": "<cluster host>:1541", "database": "outpost_test", "read": { "account": "outpost-read" } }
  ]
}
'@ | Set-Content -Path $policy -Encoding utf8
icacls $policy /setowner *S-1-5-32-544
```

`Set-Content` writes into the existing file, which keeps its owner and its access list. Whatever editor you use, run
as administrator, end with the `icacls` line: it makes Administrators the owner of the file, `S-1-5-32-544` being
their identity in every language of Windows, and changes nothing when they own it already. `check` says whether every
file of the data folder is right.

### When it fails

`pair`, `secret`, `check`, `metadata` and the start of the agent read the policy alike, and give the same reason:

| Line | Cause | Remedy |
| --- | --- | --- |
| `PolicyInvalid (10): policy.json is not valid: the file is absent; expected the local policy of this machine` | No `policy.json` yet | Write it |
| `PolicyInvalid (10): policy.json is not valid: the file is not valid JSON at line {n}, column {m}; expected plain JSON, without comments or trailing commas` | A typing mistake at that place | Correct it |
| `PolicyInvalid (10): policy.json is not valid: {member}: …` | A member breaks its rule | Correct it as the line says |

## Pairing

Pairing makes the agent known to the server, and the server known to the agent: the agent creates its own key, sends
its public half with a proof made from the token, and pins the server's key, so that it runs only what that server
signed. Run it with the address and the token that the application gave you:

```powershell
.\Moltaro.OnecOutpost.Console.exe pair <server address> <pairing token>
```

```text
Paired with <server address> as <agent id>; write the passwords with secret.
```

`pair` wrote `agent.json`, with the server's address, the agent's identity and the server's key, and `secrets.dat`
with the agent's private key. It uses the `proxy` and the `requestTimeout` of the policy. The token works once: after
it was used, a copy of it is worth nothing.

### When it fails

| Line | Cause | Remedy |
| --- | --- | --- |
| `Usage (2): <SERVER>: expected an https address, or an http one on loopback (§11 of the protocol)` | The address is not `https://` | Use the address as the application gave it |
| `Usage (2): <TOKEN>: expected a pairing token as the application gave it` | The token is cut or mistyped | Copy it again whole; the line never repeats it |
| `NotInstalled (6): the data folder C:\ProgramData\Moltaro.OnecOutpost does not exist; run install first` | `install` has not run | Run `install` |
| `DataFolderAdmitsOthers (9): the access list of the data folder admits {accounts}; expected nobody but Administrators, SYSTEM and the agent's account: inspect and correct its access list as install sets it; never remove it, which holds the audit log and the outcomes not yet sent` | Somebody else has a right to the data folder, so anybody it admits could decrypt the secrets | As it says |
| `DataFolderAdmitsOthers (9): the data folder is owned by {owner}, who can change its access list; make Administrators its owner with icacls "C:\ProgramData\Moltaro.OnecOutpost" /setowner *S-1-5-32-544, run as administrator` | Somebody else owns the data folder | Run that command |
| `DataFolderAdmitsOthers (9): C:\ProgramData\Moltaro.OnecOutpost is a reparse point; inspect it and correct it, never remove what it holds` | The data folder was replaced by a link | As it says |
| `AlreadyPaired (11): the agent is paired; to pair it anew, remove agent.json; …` | `agent.json` exists | Pair anew only on purpose, as [Pairing anew](#pairing-anew) says |
| `PairingFailed (12): no answer came: the server, or the proxy of the policy, could not be reached, or did not answer within the requestTimeout of the policy` | No route to the server or the proxy, a certificate that Windows does not trust, or no answer in time | Check the address, the proxy, the firewall and the certificate |
| `PairingFailed (12): HTTP 403: a bare 403: the server answers so to any failure of pairing, and so can anything on the path` | The token was used, expired, or does not belong to this server; or a proxy refused the request | Ask the application for a new token |
| `PairingFailed (12): HTTP 200: the server's key does not match the fingerprint in the token` | Another server answered, or something on the path opens TLS and answered in its place | Check the address with the application; nothing was written |
| `PairingFailed (12): HTTP {status}: the answer's signature does not verify` | The answer was changed on the path | As above |
| `PairingFailed (12): HTTP 400: an unsigned answer in the form the server uses for a protocol version it does not speak` | The server and the agent are of versions that do not speak to each other | Update the one that is older |
| `PairingFailed (12): HTTP {status}: an answer that is not the signed answer of §6.1 of the protocol` | Another answer, such as an error page of a proxy or of a web server | Check the address and the path to the server |
| `Failure (1): the server paired the agent, but {file} could not be written: …; the application revokes it and gives a new token` | The server paired it, but the disk refused the file | Remove the cause, ask the application to revoke the new agent and for a new token, and pair again |

The console never shows text from an answer whose signature it did not verify. A pairing that fails writes nothing.

## The passwords

`secret` writes the password of an account into `secrets.dat`, encrypted for this machine with DPAPI, keeping every
other password. It needs the policy, which names the account, and the pairing, which wrote the file:

```powershell
.\Moltaro.OnecOutpost.Console.exe secret test read
```

It asks for the password without showing it:

```text
Password of outpost-read for the read mode of the base test:
Wrote the password of outpost-read for the read mode of the base test; the service uses it from its next start.
```

Write each password the policy needs: `read` for every base, `write` for a base with `write`, and `worker` for a base
with a `workerAccount`, as [Writes without safe mode](#writes-without-safe-mode) shows. An empty password is accepted
for a 1C account, which 1C allows; not for a worker account.

From a script, give the password as the first line of the standard input, for example from your password manager's
command line:

```powershell
<command that writes the password> | .\Moltaro.OnecOutpost.Console.exe secret test read
```

Windows PowerShell passes text to a program as ASCII unless `$OutputEncoding` says otherwise, so type a password with
letters outside ASCII at the prompt. When the output is redirected but the input is the console, `secret` cannot ask
and reads nothing:

```text
NotInteractive (16): run secret in a terminal, or give the password through standard input
```

DPAPI for the machine lets any program running on this machine decrypt `secrets.dat`; only the access list of the data
folder keeps it from others, which is why the agent and the console refuse a data folder that admits anybody else. A
copy of the file is useless on another machine.

### When it fails

| Line | Cause | Remedy |
| --- | --- | --- |
| `Usage (2): <BASE>: expected a base key: 1 to 64 characters of a-z, 0-9, -, _ and ., the first a letter or a digit` | The key breaks the rule of base keys | Give the key as the policy has it |
| `Usage (2): <KIND>: expected read, write or worker` | Another word | Give `read`, `write` or `worker` |
| `NotPaired (13): secrets.dat is absent; to pair the agent anew, remove agent.json, if any, and run the console's pair` | The agent is not paired | Run `pair` first |
| `Failure (1): secrets.dat cannot be decrypted on this machine; …` | The file comes from another machine, such as a clone | Pair anew, as [Pairing anew](#pairing-anew) says |
| `NotInPolicy (14): the policy names no base {key}` | No base of that key | Check the key, or add the base to the policy |
| `NotInPolicy (14): the base {key} is in the policy, which does not let it be written` | `secret {key} write` for a base without `write` | Add `write` to the base first, if it may be written |
| `NotInPolicy (14): the base {key} is in the policy, which names no worker account for it` | `secret {key} worker` for a base without `workerAccount` | Only a base whose Writes run without safe mode has one |
| `UnknownAccount (15): {account} of the base {key} is not a local user of this machine` | The worker account does not exist, or is a group | Create the local user first |
| `Usage (2): the password of a worker account cannot be empty, since Windows refuses a blank password outside its console` | An empty line | Give the account a password |
| `Usage (2): the standard input ended before a line with the password` | Nothing came through the pipe | Check the command before the pipe |

`secret` also refuses a data folder that is missing or admits anybody else, and an invalid policy, with the lines of
[Pairing](#pairing) and [The local policy](#the-local-policy).

## Writes without safe mode

**Safe mode** is a mode of 1C in which a script cannot reach the operating system: no files, no network, no COM, no
external components and no programs. It can still do with the data everything the account's rights allow. Every Read
runs in safe mode, and so does every Write unless you turn it off for the Writes of a base.

**When the Writes of a base need it off.** Some configurations' own code does what safe mode refuses whenever an object
is written. A typical configuration, for example, opens a file to keep the versions of an object when a document is
written, so in safe mode no Write can post one of its documents: the developer of the application sees each such
Write fail with `script-error` and 1C's text. Then, and only for the bases that need it, set `"safeMode": false` in
`write`.

**What protects the machine then.** A Write without safe mode can do on this machine what a Windows account can do,
so every COM worker of that base, those of its Reads included, runs under a local Windows account of its own, which
`workerAccount` names. That account can read neither the agent's secrets nor its audit log, since the data folder
admits nobody else. Give it no right that a script of that base should not have. Three limitations remain:

- such a script can start a process outside the agent's control, which then does not end with the agent, through a
  service that the account may use, such as Task Scheduler or WMI, so the account's rights are the boundary;
- the workers of one base run under one account, so such a script can read the commands of the other workers of its
  base, their 1C passwords included, and forge what they report;
- Windows does not load the account's profile for a worker.

**Preparing the account.** Create a local user that is not in Administrators and holds no privilege beyond an ordinary
user's, one for each such base, with a password that does not expire, since an expired password stops the agent:

```powershell
$password = Read-Host -AsSecureString -Prompt "Password of outpost-worker"
New-LocalUser -Name "outpost-worker" -Password $password -PasswordNeverExpires -UserMayNotChangePassword
```

Name it in the policy, as in [A client-server base](#a-client-server-base), and write its password with `secret`,
which first grants it "Log on as a batch job", the one right it needs:

```powershell
.\Moltaro.OnecOutpost.Console.exe secret test worker
```

```text
Granted outpost-worker "Log on as a batch job".
Password of outpost-worker for the workers of the base test:
Wrote the password of outpost-worker for the workers of the base test; the service uses it from its next start.
```

When the service starts, it logs each worker account on before any worker starts, and stops with the reason when one
cannot, so a wrong password never runs a Write. Each start logs each account on once, so a wrong password counts once
toward a lockout. Its `started` entry in the audit log lists the bases whose Writes run without safe mode, in
`withoutSafeMode`. `check` checks the account, and starts a worker under it, as its lines
[WorkerAccount](#workeraccount) and [WorkerStart](#workerstart) say.

## Checking the machine

`check` checks everything the agent needs, as its start would, and more. It reads only: it writes nothing to a base or
to the data folder, runs only Reads of its own in safe mode, never polls the server, and works while the service runs,
so run it after every change as well as before the first start:

```powershell
.\Moltaro.OnecOutpost.Console.exe check
```

It shows what it is doing, then one table with the columns `Check`, `Result` and `Reason`, one line for each thing it
checked, and ends with `Every line passed.` or, for example, `CheckFailed (19): 1 of 14 lines failed`. A line that
needs another that failed is not checked, and says so: `not checked: Files secrets.dat failed`. For the test base,
read and written in safe mode, on a machine where everything is right:

| Check | Result | Reason |
| --- | --- | --- |
| `Connector` | passed | `64-bit: C:\Program Files\1cv8\<version>\bin\comcntr.dll, <version>, is registered; the workers start with it` |
| `Connection test read outpost-read` | passed | `opened as outpost-read (<full name>)` |
| `Connection test write outpost-write` | passed | `opened as outpost-write (<full name>)` |
| `Subscriptions test` | failed | `a Write that reaches these fails: NoteBeforeWrite (BeforeWrite) in NoteChecks; set "External connection" of each module` |
| `ReadRights test` | passed | `outpost-read holds no right to change data` |
| `Server` | passed | `<server address> confirmed an outcome of a made-up command from <agent id>: it is reachable and accepts the agent's key` |
| `ClockSkew` | passed | `the server's clock is ahead of this machine's by <n> ms ± <m> ms, within 00:02:00` |
| `AgentRights` | passed | `NT SERVICE\MoltaroOnecOutpost holds "Replace a process level token" and "Adjust memory quotas for a process"` |
| `AccessLists C:\ProgramData\Moltaro.OnecOutpost` | passed | `admits nobody else, nor do its 3 files` |
| `AccessLists C:\Program Files\Moltaro.OnecOutpost` | passed | `nobody else may change it` |
| `AccessLists C:\Program Files\1cv8\<version>\bin` | passed | `nobody else may change it` |
| `Files policy.json` | passed | `1 base` |
| `Files agent.json` | passed | `paired with <server address> as <agent id>` |
| `Files secrets.dat` | passed | `decrypts and holds every password the policy needs` |

The test configuration fails `Subscriptions` on purpose, so that the line can be seen: see
[A configuration with modules of its own](#a-configuration-with-modules-of-its-own). A base with a `workerAccount`
also has the lines `WorkerAccount` and `WorkerStart`.

What `check` costs and does not cover:

- each COM worker it starts holds a client licence of 1C while it is open, one at a time. `check` waits for each
  worker to exit before it returns;
- the write account is checked in a Read, in safe mode, so its first Write without safe mode opens at the service's
  first Write;
- the start under a worker account runs in the console's session, not in the service's;
- a base without a worker account runs its own code under your account, without the rights of an administrator but
  with your other files, and while `check` runs a program you started without elevation could read the 1C password
  that the worker holds. So run `check` only on a machine whose bases you trust as the local policy does.

Line by line, in the order of the table:

### Connector

Passed: the registered connector with which the workers start, and every other copy, registered or not.

| Reason | Remedy |
| --- | --- |
| `none is registered; 64-bit: {path}, {version}; register it with connector register` | Run `connector register` |
| `none is registered or installed` | Install the platform of the cluster's version |

Without a connector no worker starts, so the lines `Connection`, `Subscriptions`, `ReadRights` and `WorkerStart` are
not checked.

### Connection

`check` gives one line for each configured mode of a base, read then write. Each line names its mode, including when
both modes use the same 1C account. A COM worker opens the base in safe mode and checks the session's user. A passing
line says `opened as {user} ({full name})`.

| Reason | Cause | Remedy |
| --- | --- | --- |
| `1C opened no session: {1C's reason}` | 1C refused the session: a wrong password, an account without the right "External connection", a cluster that does not answer, a connector of another version, no free licence | Act on 1C's reason; write a password again with `secret {key} read` or `write`. 1C writes its reason in the language of the Windows account |
| `1C opened no session: the worker gave no reason` | The worker ended before it reported | Run `check` again; reinstall when it persists |
| `1C opened no session within {runLimit}, the mode's runLimit` | 1C took longer than the `runLimit` of the mode to open. The first connection of a Windows account that never used 1C can take tens of seconds | Look at the cluster; raise `runLimit` if it is short |
| `{program} did not start: {message} (Windows error {code})` | The worker program could not start | Run `install` again from the archive |
| `the script of the check ended {kind} {code}: {error text}` | The base opened, but the script of the check failed, for example because the executor could not be opened in it | Act on the text |
| `the script of the check did not end within {runLimit}` | 1C did not answer in time | Look at the cluster's load |
| `the session's user is {user}, not {account} of the policy` | 1C opened the session as another user | Check `account` in the policy and the user in the base |
| `the base has no users, so every connection has every right` | Anybody opens such a base with every right | Create the users of the base |

### Subscriptions

One line for each base with `write`: the event subscriptions that a Write can raise and whose common module an
external connection cannot run. See [A configuration with modules of its own](#a-configuration-with-modules-of-its-own).

### ReadRights

One line for each base: the rights to change data that its read account holds. Passed: `outpost-read holds no right to
change data`. Failed, for example:

```text
outpost-read holds rights to change data: DataAdministration; Insert, Update on 5 catalogs: Catalog.Products, Catalog.Notes, Catalog.… and 2 more
```

Remove the roles that grant those rights from the read account. For each kind of object, `check` lists the rights,
counts the objects with at least one of those rights and names up to three of them.

### Server

`check` sends the server one outcome of a command it has just made up, signed with the agent's key, through the proxy
of the policy, as the agent sends its outcomes, and never a poll, which would take the place of the running service's.
Passed: the server is reachable and accepts the agent's key. Failed, with `HTTP {status}: ` first when an answer came:

| Reason | Cause | Remedy |
| --- | --- | --- |
| `no answer came: the server, or the proxy of the policy, could not be reached, or did not answer within the requestTimeout of the policy` | No route, a certificate Windows does not trust, no answer in time | Check the address in `agent.json`, the proxy and the firewall |
| `HTTP 401: the server knows no such agent, or it is still pending` | The server does not know this agent | Ask the application whether it is listed; pair anew if not |
| `HTTP 403: the agent is revoked, or something on the path refused it` | The application revoked the agent, or a proxy refused | Ask the application; a revoked agent works again only after a new pairing |
| `HTTP 401: the server does not verify the agent's signature: secrets.dat and agent.json come from different pairings` | The two files do not belong together | Pair anew |
| `HTTP 400: the server refused the form of the request` | The agent and the server disagree on the form of a message | Update the agent or the server |
| `HTTP 413: something on the path refused even this small body` | A proxy or ingress limits the size of a request | Ask for the limit to be raised: outcomes can be large |
| `HTTP {status}: an unsigned answer in the form the server uses for a protocol version it does not speak` | Versions that do not speak to each other | Update the older one |
| `HTTP {status}: the answer's signature does not verify` | The answer was changed on the path | Check the path to the server |
| `HTTP {status}: an answer that is not a signed answer of §6.3 or §9 of the protocol` | Another answer, such as an error page | Check the address and the path |

### ClockSkew

The difference between the server's clock and this machine's, measured on the same outcome, within the allowed skew:
two minutes, the server's default, unless you give the server's own with `--clock-skew`, such as
`check --clock-skew 00:05:00`.

| Reason | Remedy |
| --- | --- |
| `the server's clock is ahead of this machine's by {n} ms ± {m} ms, beyond {allowed}; with the server ahead, the agent refuses every command with clock-skew, and with it behind, with expired once the gap also passes the take timeout: set the clocks` | Set this machine's clock, or the server's, by a time server |
| `cannot be judged within {allowed}: the round trip took {n} ms` | Run `check` again when the network is quieter |
| `not checked: no verified answer came` | See the line `Server` |

### WorkerAccount

One line for each base with a `workerAccount`: one logon of the account for a batch job with the password of
`secrets.dat`, then whether its token is an ordinary user's. Passed: `outpost-worker logs on for a batch job, and its
token is an ordinary user's`. Failed: `outpost-worker cannot log on: {reason}`, the same reason the agent's start
gives:

| Reason | Remedy |
| --- | --- |
| `unknown account or wrong password; check workerAccount in the policy, or write the password again with secret test worker` | As it says |
| `it lacks "Log on as a batch job", or a "Deny log on as a batch job" names it; secret test worker grants the right` | Run `secret test worker`; remove the account from "Deny log on as a batch job" in the local security policy |
| `the account is locked out`, `the account is disabled`, `the account has expired` | Unlock, enable or extend the account |
| `its password has expired; set a new one and write it with secret test worker` | As it says; set the password never to expire |
| `its password must be changed; set a new one and write it with secret test worker` | As it says |
| `an account restriction refuses it, such as a blank password outside the console` | Give the account a password |
| `its logon hours do not allow it now`, `it may not log on at this computer` | Change the account's restrictions |

`outpost-worker is an administrator or holds a privilege beyond an ordinary user's; take it out of Administrators and
of every group that grants a privilege, or name another local user in the policy` means what it says: such an account
would give a script without safe mode the whole machine. `check` logs each account on once and never retries, since
each failure counts toward a lockout.

### AgentRights

Whether the agent's account holds "Replace a process level token" and "Adjust memory quotas for a process". Failed:
`NT SERVICE\MoltaroOnecOutpost lacks "Replace a process level token" or "Adjust memory quotas for a process"; install
grants them, and the service has them from its next start`: run `install` again, then restart the service. `check`
sees only a right granted to the account itself, not one granted through a group.

### WorkerStart

One line for each base with a `workerAccount`: the worker program started under the account on a desktop in the
console's Windows session and ended at once, without opening the base. Passed: `the worker started under
outpost-worker on {desktop} and ended at the end of its input`.

The installed service starts workers in Session 0. Windows chooses the non-interactive desktop for the worker
account's batch logon. Send a Read and a Write after starting the service to verify both workers can open 1C in
that session.

| Reason | Cause | Remedy |
| --- | --- | --- |
| `the worker failed to initialise on its desktop (STATUS_DLL_INIT_FAILED): the account or the desktop lacks access` | Windows refused the account its desktop | Check the account's restrictions |
| `the worker refused its command line (exit code 1): the program folder holds another build` | The program folder mixes versions | Run `install` again from one archive |
| `outpost-worker may not read or run {program}` | The program folder does not let Users read and run it | Run `install` again |
| `outpost-worker may not read the folder of {program}, or it does not exist` | As above | Run `install` again |
| `{program} is missing` | A file of the program folder is gone | Run `install` again |
| `the Secondary Logon service is disabled; check does not start it` | `check` starts the worker through the Secondary Logon service of Windows | Set the start type of "Secondary Logon" to Manual |
| `the console lacks "Impersonate a client after authentication"` | The administrator's token lacks it | Run PowerShell as an administrator who holds it |
| `the worker did not end within {runLimit} and was ended` | The worker hung before its first step | Run `check` again; look at what else runs on the machine |

### AccessLists

One line for the data folder with its files, one for the program folder, and one for the folder of each registered
connector, judged by the rule of the program folder, since the workers run the connector's code. Passed: `admits
nobody else, nor do its {n} files`, or `nobody else may change it`. A failure in the data folder or program folder
prevents every worker start. A worker also needs the folder of its selected connector to pass. Each registered
connector folder contributes to the overall result of `check`. The reason in each failed line gives the remedy.

| Reason | Remedy |
| --- | --- |
| `policy.json is owned by {owner}, who can change its access list; make Administrators its owner with icacls "C:\ProgramData\Moltaro.OnecOutpost\policy.json" /setowner *S-1-5-32-544, run as administrator` | Run that command |
| `the access list of policy.json admits {accounts}; expected nobody but Administrators, SYSTEM and the agent's account: inspect it, then reset it to its folder's with icacls "C:\ProgramData\Moltaro.OnecOutpost\policy.json" /reset, run as administrator` | Such a file was moved into the folder, or its list was changed. Run that command |
| `the access list of secrets.dat admits {accounts}; …; whoever it admitted could decrypt it: pair the agent anew and change the passwords it held` | Reset it, pair anew and change the passwords of its accounts in 1C and in Windows |
| `the access list of the data folder admits {accounts}; …: inspect and correct its access list as install sets it; never remove it, which holds the audit log and the outcomes not yet sent` | Correct the list; `install`, which refuses such a folder, shows what it sets |
| `the access list of {path} lets {accounts} change it; expected nobody but Administrators, SYSTEM, TrustedInstaller and the agent's account: remove their entries, and make Administrators the owner of what they own` | As it says, for the program folder, the connector's folder or a folder above them |
| `{folder} is a reparse point; inspect it and correct it, never remove what it holds` | The data folder was replaced by a link |

### Files

`policy.json`, `agent.json` and `secrets.dat`, read as the agent's start reads them. Passed: `{n} bases`; `paired with
{server} as {agent}`; `decrypts and holds every password the policy needs`.

| Reason | Remedy |
| --- | --- |
| `policy.json is not valid: …` | See [The local policy](#the-local-policy) |
| `agent.json is absent: the agent is not paired; pair it with the console's pair` | Run `pair` |
| `agent.json cannot be read: …` | Pair anew |
| `secrets.dat is absent; to pair the agent anew, remove agent.json, if any, and run the console's pair` | As it says |
| `secrets.dat cannot be decrypted on this machine; to pair the agent anew, remove agent.json, if any, and run the console's pair` | The file comes from another machine; see [When the machine is cloned](#when-the-machine-is-cloned) |
| `secrets.dat holds no password for the read mode of the base test; write it with secret test read` | One line for each password missing, `read`, `write` or `worker` |

When `check` itself ends before its table: `NotInstalled (6): the data folder C:\ProgramData\Moltaro.OnecOutpost does
not exist; run install first`, or `Usage (2): --clock-skew: expected a time of the form [d.]hh:mm:ss from 00:00:01 to
01:00:00, such as 00:02:00`.

## A configuration with modules of its own

The code of an external connection runs in the process of the COM connector, on the agent machine, so a common module
of the configuration takes part only when the configuration makes it available to an external connection. A typical
configuration does that for its own modules. A module that somebody added without it stops every Write that reaches
one of its event handlers: 1C refuses to pass the object to the handler, and the Write fails.

`check` finds them before a Write does. For each base with `write`, the line `Subscriptions` names each event
subscription that a Write can raise and whose module an external connection cannot run. The repository's test
configuration has one on purpose:

```text
a Write that reaches these fails: NoteBeforeWrite (BeforeWrite) in NoteChecks; set "External connection" of each module
```

The event name is the one 1C returns for the session. For example, `ПередЗаписью` is the Russian spelling of `BeforeWrite`.

The remedy is not yours to make on the agent machine: ask the developer of the configuration to set the one flag
"External connection" in the properties of each common module named, and to update the base with it. A subscription that
`check` names with `, which is not a common module` has a handler that names no common module of the configuration;
show the line to its developer too. Subscriptions to the event that forms use, `FormGetProcessing`, are not named,
since no external connection raises them.

## Licences

Each COM worker holds one client licence of 1C for as long as its session in the base is open, and the agent opens
every worker that the policy asks for when it starts and keeps them open, so the agent holds, all the time it runs, as
many licences as the `workers` of every mode of every base add up to: two for a base read and written with the
defaults. `check` and `metadata` hold one more while they run.

Start with one worker per mode. A worker runs one command at a time, so a long Read holds the Read worker of its base,
and the next Read waits; when the application sees Reads expire while the base is busy, raise the `workers` of the
read mode, one licence each. A worker that gets no licence opens no session: it takes no command, the commands of its
mode expire meanwhile, and the agent tries again with a growing delay up to `retryDelayMax`.

## Starting the service

Once the policy, the pairing and the passwords are written and `check` passed:

```powershell
Start-Service MoltaroOnecOutpost
Get-Service MoltaroOnecOutpost
```

The service reports that it runs as soon as its program has started, and then the agent checks its files. So look at
the end of the audit log, as [The audit log](#the-audit-log) shows. A start that went well writes `started` without an
`error`, `policy` with `valid` true, and soon `connection` with `state` `connected` and `bases` with the keys the
server granted. A start that failed writes `started` with its `error`, the same reason `check` gives with its remedy,
and the service stops with the exit code 1. Then correct the cause and start it again.

`bases` lists in `claimed` the keys that another agent holds already: a base key belongs to the first agent that
declares it, so that the scripts of one database never reach another. The application releases the key, or revokes the
other agent, for this one to take it.

The service starts automatically with Windows from now on. A change of the policy or of a password takes effect when
it restarts:

```powershell
Restart-Service MoltaroOnecOutpost
```

A Write that runs while the service stops is reported to the application as unknown, since nobody can tell whether 1C
committed it.

## The exit codes of the agent

The agent program runs only as the service. When it stops with a code other than 0, Windows records the service as
stopped with a service-specific error, 1066, and the code, which `sc.exe query MoltaroOnecOutpost` shows as
`SERVICE_EXIT_CODE`:

| Code | Meaning | What to do |
| --- | --- | --- |
| 0 | Stopped by Windows or by you | |
| 1 | It stopped on its own; the reason is in the audit log, as `started` with an `error` or as `stopped`. When the log ends without either, the log itself could not be written, as on a full disk, and nothing more was recorded: the exit code is the reason | Read the last entries, act on the reason, start it again; when there is none, free the disk, then start it again |
| 2 | It could not open its audit log, so it recorded nothing: another copy of the agent runs, or `audit\` is missing or refuses the agent's account | Look for another copy. If the audit folder is missing, run `install`. If the folder exists, inspect and correct its access list as [AccessLists](#accesslists) describes. Preserve the existing audit log and pending outcomes. |
| 3 | Its program was started by hand, outside the service controller; nothing in the data folder changed | Start the service instead |
| 4 | Its program was started with other arguments than `service` | Run `install` again, which sets the service's command line |
| 5 | Not on Windows | |

Windows restarts the agent on its own only after a crash, after a minute and at most three times a day; a stop with an
error stays a stop, so that a cause is corrected before the agent runs again.

## The audit log

The agent records every command in `C:\ProgramData\Moltaro.OnecOutpost\audit`, before it runs it and again when it
ended, with its script and parameters but never its result or a password. There is one file for each day by UTC,
`YYYY-MM-DD.jsonl`, and one JSON object on each line. Read it from PowerShell run as administrator, even while the
service runs; the last entries of the newest file:

```powershell
Get-ChildItem "$env:ProgramData\Moltaro.OnecOutpost\audit" -Filter *.jsonl | Sort-Object Name | Select-Object -Last 1 | Get-Content -Tail 20
```

Never edit, move or remove a file of the log: it is also the agent's journal, from which it learns after a restart what
ran, and a line that is not an entry stops its next start with `the audit log cannot be read: line {n} of
audit\{file} is not an entry of the log`.

Every entry has `time`, in UTC with milliseconds, and `entry`, its kind. A start, a Read and its end, for example:

```json
{"time":"2026-11-24T10:15:07.123Z","entry":"started","version":"<version>","withoutSafeMode":["test"]}
{"time":"2026-11-24T10:15:07.125Z","entry":"policy","valid":true}
{"time":"2026-11-24T10:15:07.410Z","entry":"connection","state":"connected"}
{"time":"2026-11-24T10:15:07.411Z","entry":"bases","held":["test"],"claimed":[]}
{"time":"2026-11-24T10:16:02.031Z","entry":"received","command":"<command id>","base":"test","mode":"read","callerKey":null,"script":"Result = 1;","parameters":{},"issued":"2026-11-24T10:16:01.998Z","deadline":"2026-11-24T10:16:31.998Z"}
{"time":"2026-11-24T10:16:02.452Z","entry":"ended","command":"<command id>","outcome":"succeeded","code":null,"durationMs":421}
{"time":"2026-11-24T10:16:02.470Z","entry":"confirmed","command":"<command id>"}
```

| `entry` | Members | When |
| --- | --- | --- |
| `started` | `version`; `withoutSafeMode`, the bases whose Writes run without safe mode; `error` when the start failed | The agent started |
| `policy` | `valid`; `error` when not | The agent read its policy |
| `connection` | `state`, `connected` or `lost`; `reason` when lost, such as `unreachable`, `status-407` from a proxy that asks for authentication, `status-` and another HTTP status, `protocol-version`, or the code of a refusal of the server | The agent's polls began or stopped reaching the server |
| `bases` | `held`, the keys the server granted; `claimed`, those that another agent holds | That changed |
| `received` | `command`, `base`, `mode`, `callerKey` (a Write's, or `null`), `script`, `parameters`, `issued`, `deadline` | A command passed every check, before it runs |
| `refused` | `command`, `base`, `mode`, each `null` when nothing in the command can be trusted; `code`, such as `signature`, `expired`, `clock-skew` or `not-served` | A command failed a check and did not run |
| `ended` | `command`; `outcome`, `succeeded`, `failed` or `unknown`; `code`, such as `script-error` or `run-limit`; `durationMs` | A command ended; a later `ended` of the same command replaces an earlier one |
| `confirmed` | `command` | The server keeps the outcome, so the agent no longer sends it |
| `reservation` | `agent`, `below` | The agent reserved poll numbers, so that they grow across restarts |
| `stopped` | `error` | The agent stopped for a fault after it had started, such as `outcomes\{id}.json cannot be written`; its exit code is 1. A normal stop writes none, and after a failed write of the log itself nothing more is written |

The reasons of `stopped` that need you:

- `outcomes\{id}.json cannot be written`, `… cannot be read`, `… cannot be removed`: the disk refused the outbox,
  where outcomes wait until the server confirms them. Free the disk, then start the service again.
- `the server has recorded polls of this agent that its audit log does not hold: the log was cleared; pair the agent
  anew`, and `the poll numbers are used up; pair the agent anew`: see [Pairing anew](#pairing-anew).

**How long it is kept.** A file is removed once its last entry is older than `auditRetentionDays`, 90 by default, and
one day more for the difference between clocks; never sooner than a day. A file stays as long as it holds an entry of
a command whose outcome the server has not confirmed. The agent removes files only right after an answer of the server
that it verified, and only when its clock is within a day of the server's, so an agent that never reached the server,
or whose clock is far off, removes nothing.

## Metadata reports

`metadata` writes a local report of the metadata of a base, for the developer of the application: its objects, their
fields with their types, and how they are stored in the database. It holds no data of the base, no password and no
account, and never goes to the server.

```powershell
.\Moltaro.OnecOutpost.Console.exe metadata test <folder>\test-metadata.json
```

It reads the base in Reads under its read account, in safe mode, writing nothing to the base or to the data folder,
and works while the service runs. It starts a worker only when the lines of `check` that a worker needs pass: the
files, the connector, the access lists and the worker account. It shows a progress bar of the objects it reads, then a
table with the columns `Category`, `Found`, `Read` and `Failed`, and ends with:

```text
Wrote the metadata report of the base test to <folder>\test-metadata.json: <read> of <found> objects read, <failed> failed.
```

The report is written atomically as one JSON file. The compatibility mode, enumerated object settings such as posting,
and field indexing use the text 1C displays for the session. If 1C cannot read the platform, configuration name, version or
compatibility mode, `metadata` puts an `error` object in place of that value and names the property in the coverage
table's caption. If reading an object's property fails, the report keeps the properties it could read, records the
error and counts the object as failed. `metadata` waits for each worker to exit before it returns. When the read
account may not read how the base is stored, it says
`The storage structure could not be read: {reason}; the report holds the objects and their fields without it.`, and
the report holds the rest. The file gets the access list of its folder, so put it where only those who should read the
structure of the base can. A large configuration takes many Reads, each in the `runLimit` of the read mode; a
configuration changed during the walk gives a report of both. A base without a worker account runs its code under your
account, as in `check`.

### When it fails

| Line | Remedy |
| --- | --- |
| `Usage (2): {file} exists; metadata never replaces a file: name a new one` | Name a new file |
| `Usage (2): the folder of {file} does not exist` | Create the folder |
| `Usage (2): {file} lies in the data folder C:\ProgramData\Moltaro.OnecOutpost, which metadata never writes` | Name a file elsewhere |
| `NotInPolicy (14): the policy names no base {key}` | Check the key |
| `CheckFailed (19): the line {Check} of check failed, so metadata starts no worker: {reason}` | Run `check`, and act on that line |
| `ReportFailed (20): the base test did not open as outpost-read: {reason}; run check` | Run `check`, whose line `Connection` gives the cause |
| `ReportFailed (20): the census …`, `… the probe …`, `… no page of the walk was read: …` | 1C could not read the metadata at all; the reason gives 1C's text |
| `ReportFailed (20): the configuration has no object to read` | Nothing to report |

No file is written when it fails.

## Updating

Unpack the archive of the new version into a new folder, check its checksum, and run its `install`, as for the first
installation:

```powershell
Set-Location <folder of the new version>
.\Moltaro.OnecOutpost.Console.exe install
```

It stops the service, waiting up to `--stop-timeout`, a minute by default, replaces the content of the program folder,
reconfigures the service, and keeps everything in the data folder: the policy, the pairing, the passwords, the audit log
and the outcomes not yet sent. Then:

```powershell
Set-Location "$env:ProgramFiles\Moltaro.OnecOutpost"
.\Moltaro.OnecOutpost.Console.exe check
Start-Service MoltaroOnecOutpost
```

A Write that ran while the service stopped is reported as unknown.

## Uninstalling

```powershell
.\Moltaro.OnecOutpost.Console.exe uninstall
```

```text
Removed the service MoltaroOnecOutpost and its account's two rights. Left C:\Program Files\Moltaro.OnecOutpost and C:\ProgramData\Moltaro.OnecOutpost, with the audit log; the worker accounts keep "Log on as a batch job".
```

It stops the service first, as `install` does, with the same `--stop-timeout`; without a service it ends with
`NotInstalled (6): the service MoltaroOnecOutpost is not installed; nothing was changed`. It leaves both folders, since
the audit log is the record of what the agent did. When you no longer need them, keep a copy of `audit\` as long as
your rules ask, then remove the two folders by hand, from PowerShell run as administrator, outside the program folder:

```powershell
Set-Location $env:SystemDrive\
Remove-Item -Recurse -Force "$env:ProgramFiles\Moltaro.OnecOutpost"
Remove-Item -Recurse -Force "$env:ProgramData\Moltaro.OnecOutpost"
```

Remove the worker accounts you created for the agent, and ask the application to revoke the agent.

## When the machine is cloned

A clone of the machine, or a copy of its disk, carries the agent's identity and its secrets. Two copies of one agent
running at once break the guarantees that a command runs once and that its outcome is known, so a clone must not run
the agent until it is paired anew with a token of its own.

The service starts with Windows, so before you clone, stop it and keep it from starting:

```powershell
Stop-Service MoltaroOnecOutpost
Set-Service MoltaroOnecOutpost -StartupType Disabled
```

On the original, set it back with `Set-Service MoltaroOnecOutpost -StartupType Automatic` and start it. On the clone,
pair anew, as below, before you set it back.

### Pairing anew

Pairing anew gives the agent a new identity. Do it on a clone, after `stopped` asks for it, or when the server's key
changed:

1. Stop the service.
2. Ask the application for a new token, and to revoke the old agent if it should no longer run.
3. Remove `C:\ProgramData\Moltaro.OnecOutpost\agent.json`. The outcomes that its outbox still holds then reach the
   server under the new identity, which drops them as another agent's.
4. Run `pair` with the new token. It writes `secrets.dat` with the new key alone, so write every password again with
   `secret`.
5. Run `check`, then start the service.

The base keys that the old agent held stay with it until the application revokes it or releases them.

## The exit codes of the console

| Code | Name | Commands | When |
| --- | --- | --- | --- |
| 0 | `Success` | all | Done; `--help` too |
| 1 | `Failure` | all | A step failed for a reason of Windows or of the disk |
| 2 | `Usage` | all | An unknown command or option, a missing or invalid argument or password, `install` run from inside the program folder or from a folder that holds it, and `metadata` given a file that exists, whose folder does not, or that lies in the data folder |
| 3 | `NotElevated` | all | Not run as administrator; nothing was changed |
| 4 | `NotWindows` | all | Not on Windows |
| 5 | `Cancelled` | all | Ctrl+C |
| 6 | `NotInstalled` | `uninstall`, `pair`, `secret`, `check`, `metadata` | The service, or the data folder, does not exist |
| 7 | `StopTimedOut` | `install`, `uninstall` | The service did not stop within `--stop-timeout`; nothing further was done |
| 8 | `ProgramFolderWriters` | `install` | Someone else may change the program folder |
| 9 | `DataFolderAdmitsOthers` | `install`, `pair`, `secret` | The data folder admits someone else, or is a reparse point |
| 10 | `PolicyInvalid` | `pair`, `secret` | `policy.json` is absent, unreadable or invalid |
| 11 | `AlreadyPaired` | `pair` | `agent.json` exists |
| 12 | `PairingFailed` | `pair` | Anything but a verified answer of the server |
| 13 | `NotPaired` | `secret` | `secrets.dat` is absent |
| 14 | `NotInPolicy` | `secret`, `metadata` | The policy names no such base, mode or worker account |
| 15 | `UnknownAccount` | `secret` | The worker account is not a local user |
| 16 | `NotInteractive` | `secret` | The input is the console and the output is redirected |
| 17 | `ConnectorNotRegistered` | `connector register` | No bitness ends with a registered connector |
| 18 | `RegistrationFailed` | `connector register` | `regsvr32` failed, or the machine shows no registration after it |
| 19 | `CheckFailed` | `check`, `metadata` | A line of `check` failed or was not checked; for `metadata`, a line that a worker needs failed, so no worker started |
| 20 | `ReportFailed` | `metadata` | The base did not open, its metadata could not be read, or no page was read; no file was written |

## What the agent does not do

- It does not decide which scripts may run: the local policy decides bases, accounts and modes, and the application
  approves scripts before it sends them, if it wants to.
- It does not let the server change the policy or the secrets: only an administrator of this machine can.
- It does not authenticate to a proxy, so that nothing on the path can relay its credentials.
- It does not read a change of the policy or of a password before it restarts.
- It does not accept a domain account as a worker account: a local account of this machine keeps a script without
  safe mode inside this machine.
- It does not keep a command for later: while it is not connected, the application's calls end as unavailable at once.
- `install` does not start the service, create a worker account or repair a folder that admits somebody else, and
  `uninstall` does not remove the folders, so that nothing you need is lost by mistake.
