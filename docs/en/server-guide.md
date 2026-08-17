# Running the server

This page covers starting the dedicated server, using its console, and installing it as a
system service so it runs without anyone logged in. For the configuration file and what each
setting does, see the server settings page.

## Starting it

Run `TopSpeed.Server`. It creates `settings.json` beside itself on the first run and starts
listening straight away.

On Linux and macOS it also writes a way to start itself without a terminal, since pressing
enter on a program with no extension does something different in every file manager. On macOS
that is `Start Server.command`, which Finder runs in Terminal. On Linux it is `start-server.sh`,
which most file managers offer to run in a terminal from its context menu. Each is written only
if it is not already there, so one you have edited or deleted stays that way.

The server needs a terminal. Started without one it reaches the end of its input straight away
and stops, which looks like nothing having happened at all, so launching the program file
itself from a file manager is not a way round this.

The console prints what the server is doing and takes commands. Type `help` for the list.

| Command | What it does |
| --- | --- |
| `help` | List the commands. |
| `options` | Open the settings menu. |
| `players` | List connected players and their protocol versions. |
| `version` | Show the server and protocol versions. |
| `update` | Check for a new version. See "Updating" below. |
| `service` | Install or control this server as a system service. |
| `shutdown` | Stop the server. |

## Attaching a second window

Run the program again from a folder that already has a server running and it does **not**
start a second one. It becomes a console onto the server already there: everything that
server prints appears in the new window, and commands typed there are answered by it.

Only one window at a time may hold the console. Run a third and it tells you which window
already has it.

In an attached window, `exit` closes that window and leaves the server running. `shutdown`
stops the server itself. That difference matters: `exit` is about the window, `shutdown` is
about the server.

## Command line options

Running the program with **any** option means you want to start a server, so it will not
attach to one already running. The single exception is `--attach`. Passing any option also
mirrors the log to the console.

| Option | Effect |
| --- | --- |
| `--port <number>` | Port to listen on, 1 to 65535. |
| `--max-players <number>` | Maximum connected players, 1 to 255. |
| `--motd <text>` | Message of the day shown to players when they join. |
| `--log-level <levels>` | Comma separated: `error`, `warning`, `info`, `debug` or `all`. |
| `--log <levels>` | Alias for `--log-level`. |
| `--log-file <path>` | Write the log here. Starts a fresh file each run rather than appending. |
| `--attach` | Attach to the server running in this folder. If there is none, it says so and stops. |
| `-h`, `--help` | Show the built-in help. |

These act on the service for this folder rather than starting a server:

| Option | Effect |
| --- | --- |
| `--service-status` | Say whether this folder is installed as a service. |
| `--install-service` | Install this folder's server as a service. |
| `--uninstall-service` | Remove it again. The folder itself is left alone. |
| `--start-service` | Start the installed service. |
| `--stop-service` | Stop it. |
| `--restart-service` | Stop it and start it again. |

There is one more, `--service`, which you never need to type. It is how a service manager
starts the program, and the installer writes it into the registration. Typed by hand it does
nothing but explain itself.

## Running as a service

A service starts with the machine and keeps running when nobody is logged in, which is what
you want for a server people rely on.

Each folder gets its own service, worked out from where the server lives, so two servers on
one machine can both be installed without naming either.

Type `service` for a menu, or add the verb to skip it: `service install`, `service uninstall`,
`service start`, `service stop`, `service restart` or `service status`. The command line
options above do the same thing without opening a console.

### Windows

`service install` asks for administrator rights and installs it. The service runs as
`NT AUTHORITY\LocalService`, not as you and not as an administrator, and is set to start with
the machine.

That account is chosen for two reasons. Windows will only run a service as a named person if it
is handed that person's password to store and reuse, and nothing here will ever ask you for one.
And a program listening for players from the internet is the last thing that should be carrying
your account: as `LocalService` it cannot read your documents or act as you on the network, so a
server that is broken into does not take you with it.

Installing grants that account permission to write **inside the server folder**, which is what
lets the server update itself in place. It is given nothing outside that folder. So anything the
server needs to write has to live inside it, and the log is where this usually shows up: a path
somewhere else, such as your Documents folder, works when you run the server yourself and
produces nothing at all once the same folder is running as a service.

### Linux and macOS

Here the service is reached with `sudo` and in no other way:

```
sudo ./TopSpeed.Server --install-service
```

That writes the systemd unit or launchd job where the system keeps them, loads it, and reports
what it registered. It works out which account the service should run as from `SUDO_USER`, so
the server runs as **you** and not as root. That matters: a server running as root leaves files
in its own folder that your account cannot replace when it updates. For the same reason it
refuses to install when it cannot tell who you are, such as when you are logged in as root
rather than using `sudo`.

`--uninstall-service`, `--start-service`, `--stop-service` and `--restart-service` work the same
way. Run any of them without `sudo` and nothing happens except that you are told the command to
run, with the full path already filled in so you can paste it from wherever you are.

The `service` command inside a running server does not open a menu here, because everything in
one needs root and the server cannot obtain it while it is running. Instead it tells you where
the service stands and what to run to change it. Ask for `service stop` and you are told whether
it is even running before being given the command.

The exception is a server that is itself running as root, which is allowed where root is the
only account. That one can carry all of it out, so it gets the same menu Windows gets.

### Which account runs the server

**Run the server as one account and keep it that way.** `sudo` belongs on the service options
above and nowhere else.

Starting the server itself with `sudo` is refused. The reason is worth understanding, because it
is not about security. A server started as root writes its settings, its log, its control socket
and its own updates into the folder **as root**, while the folder itself still belongs to you.
Your account can no longer replace those files. Nothing complains at the time; it surfaces later
as settings that will not save, or an update that stops partway and reports that the folder may
hold parts of two versions.

The same applies to `su`, which switches your whole shell to another account using *that*
account's password rather than running one command with your own. It leaves no record of who you
were before, so nothing in the environment can tell it apart from a genuine root login. The
server does not rely on that record: it asks the system who owns the folder, which is the same
question asked properly. A server started after `su` in your own folder is refused just as one
started with `sudo` is.

### Installing on a machine without sudo

Debian offers a root password during installation, and choosing one leaves your own account
**out of the `sudo` group entirely**. On such a machine `sudo ./TopSpeed.Server --install-service`
fails with "is not in the sudoers file", and `su` is the ordinary way to become root.

That works. Become root however that machine expects, then install:

```
./TopSpeed.Server --install-service
```

The service is registered to run as the account that owns the server folder — which is you, not
root — because that is read from the folder itself rather than from how root was reached.

If you would rather have `sudo` anyway, become root and add yourself to it, then log out and
back in:

```
usermod -aG sudo yourname
```

### When root is your only account

Then none of this applies and the server runs normally. That is common on a rented server handed
over with root as its only login, and inside containers. The problem needs an ordinary account
that owns the folder and is then locked out of it, and where no such account exists there is
nobody to lock out.

The server settles this by asking who owns the folder it is running from. Root running in a
folder that belongs to root is the account there is, and nothing is refused. Root running in a
folder that belongs to you is the mistake, however root was reached, and is.

Installing the service is unaffected either way. That runs as root deliberately, registers the
service to run as whoever owns the folder, and exits.

`service status` needs no `sudo` at all. **On Linux it answers properly**: systemd will say
whether a unit is installed, whether it is running and whether it starts with the machine
without asking for any rights, so the server asks on your behalf. For more detail than it
reports, `systemctl status <name>` with the name the install gave you.

**On macOS it cannot.** Reading the system domain with `launchctl print` requires root, so there
is nothing to ask and the server says so instead, naming the command. Run
`sudo launchctl print system/<name>` yourself.

### Where it cannot be installed

The server updates itself in place, so it must run from a folder it can write to. Installing
from a protected location such as Program Files is refused, because giving a service write
access inside one would let anything that can write there run as that service later. Move the
folder somewhere you created yourself and install from there.

### Starting or restarting from an attached window

On Windows, if you are attached to a running server and ask for `service start` or
`service restart`, the folder is handed over rather than the request being refused. The running
server stops, the service starts, and the same window attaches to the service, so you do not
lose the console. This does not arise on Linux or macOS, where the service is controlled from
outside the server rather than from inside it.

## Updating

`update` checks for a new version. What follows is the same in every mode, including `off`:

1. `update` shows what has changed and tells you what to type next.
2. `update` again approves it. If players are connected, the install waits for the last of
   them to leave. If nobody is connected, it installs immediately.
3. `update` after that reports what is scheduled rather than starting anything else.

`update --force` runs the whole thing to the end from wherever it has got to. Typed at a
server with nothing pending, it checks, downloads and installs in one go, disconnecting
anyone connected.

In `notify` mode the server tells you when a version is available; the steps above are then
exactly the same. In `auto` mode it does all of it by itself once no players are connected.

### When you started the server yourself

On **Linux and macOS** the update happens in the window you were already using. The server hands
that window over to the update rather than exiting, so the updater's own output appears there,
and when it finishes the new server starts in the same place. You keep the console throughout,
and it works the same way however many times the server updates.

On **Windows** the updated server opens in a **new console window** and your original one
returns to its prompt. Windows has no way for a program to hand its console to a successor, and
a second program sharing one would compete with the shell for your keystrokes with nothing to
decide between them. If you would rather not have windows come and go, install the server as a
service, which is the better answer on every platform.

### When the server is a service

The server stops, the files are replaced, and the service is started again. It is back in
about a second on Windows and a couple of seconds on Linux and macOS.

**If a console was attached when the update began, that window closes.** The update itself is
unaffected and the server comes back on its own; run the program again once it is done and
you will attach to the updated server. This is expected: the point of a service is that the
server does not depend on anyone watching it, and the console is only a viewer.

### If you run the program during an update

It tells you an update is being installed and stops, without starting anything. This is
deliberate. There is nothing to attach to at that moment, and starting a second server would
take the folder the updater is still writing into.

What it suggests depends on what is being updated. After a service update you are told to run
it again in a moment to attach. After an update to a server you started yourself, you are told
to leave it alone, because that server comes back by itself — in the window it was already in on
Linux and macOS, and in a new one on Windows.

### Files the update keeps

While an update is being written, a file named `.updating` sits in the folder. It disappears
when the update finishes. If the server ever reports that an update did not finish, that file
outlived the program writing it, which means the folder may hold parts of two versions.
Running the update again puts it right.

A file named `.last-update` records which version was most recently handed to the updater, and
when. It does two things. It stops the server checking for updates again in the first few
minutes after an install, since it asked that question on its way in. And it lets the server
notice a version that was installed here but never arrived: if it comes back still reporting the
older version, the build is not doing what its name says, so the server says so once and stops
installing that version by itself rather than fetching the same build every day.

That is not an off switch. A newer version installs as usual, `update --force` installs the
refused one anyway, and deleting the file forgets the whole thing.
