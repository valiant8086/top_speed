# Server settings

This page covers the dedicated server's configuration file and the settings you are most
likely to change. Everything here can also be reached from the server console by typing
`options`.

The server keeps its configuration in `settings.json`, saved beside the server program.
It is created with defaults the first time the server runs.

## Update checking

`StartupUpdateMode` decides whether, and how, the server looks for new versions. It takes
one of three values:

| Value | Behaviour |
| --- | --- |
| `off` | Never checks. The server makes no update requests at all. This is the default. |
| `notify` | Checks shortly after startup and once a day after that. When a new version exists it says so and does nothing else, leaving the decision to you. |
| `auto` | Checks on the same schedule, and installs a new version by itself once no players are connected. |

In the console this is the "Update checking" entry under `options`. A change takes effect
the next time the server starts.

### Installing an update

Type `update` to check on demand. This works in every mode, including `off`.

If a new version is found you are shown its changes and asked whether to download it. When
players are connected, the install waits until the last of them disconnects rather than
interrupting a race, and the server tells you so. Typing `update` again while an install is
waiting reports what is scheduled; it never starts a second one or forces the first.

Type `update --force` when you do not want to wait. If an install is scheduled it happens
immediately and connected players are disconnected. If the server is instead waiting to
re-check a download, that check happens straight away.

In `auto` mode the same waiting applies: the server holds the update until it is empty.
Because a restart leaves the server empty, restarting is usually the quickest way to get a
waiting update applied.

### When a download is not available yet

A new version appears in the update list as soon as it is tagged, but the files to download
are only published once that version finishes building. In between, the server reports that
the download is not available yet and checks again, first after twenty minutes, then after
forty, then hourly. It says so when it first notices, once more after the second try, and
then stays quiet until it either succeeds or gives up after twenty-three hours.

This is normal for a short while after a release. If it never resolves, that version's build
did not complete, and the server returns to its usual daily schedule.

## Logging

`LogFile` turns on a log file. Leave it blank, which is the default, and nothing is written
to disk.

- A bare file name or a relative path is written next to the server program.
- An absolute path is used exactly as written.

A log configured here records everything and appends, so it survives restarts and is still
readable later. This matters if you run the server with its window hidden, since it is the
only place messages remain after the fact.

In the console this is the "Log file" entry under `options`, and a change takes effect the
next time the server starts.

### Command line overrides

Options given on the command line take precedence over `settings.json`:

| Option | Effect |
| --- | --- |
| `--log-file <path>` | Log to this path instead of the configured one. Starts a fresh file each run rather than appending. |
| `--log-level <levels>` | Comma separated: `error`, `warning`, `info`, `debug`, or `all`. |
| `--log <levels>` | Alias for `--log-level`. |

Passing any command line option also mirrors the log to the console.

## Other settings

| Setting | Meaning |
| --- | --- |
| `Language` | Language used for server messages. |
| `Port` | Port players connect to. Default 28630. |
| `DiscoveryPort` | Port used to advertise the server on a local network. Default 28631. |
| `MaxPlayers` | Maximum connected players. |
| `Motd` | Message of the day shown to players when they join. |
| `UpdateRuntimeAssetTag` | Which build to download when updating. `auto` picks the one matching this machine. |
| `features` | Turns custom tracks, custom vehicles, text chat and voice chat on or off for the whole server. |
| `moderation` | Call sign rules: maximum length, whether repeated letters are blocked, and whether duplicates are allowed. |

`Port`, `DiscoveryPort` and `MaxPlayers` can also be set for a single run with `--port`,
`--max-players` and `--motd`.
