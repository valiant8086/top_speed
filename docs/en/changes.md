# Changes

This file tracks new changes to the game for both client and server to make it easier to find previous changes.

The game versioning follows a specific pattern by using year.month.day.revision, where revision is an incremental number if there is more than one release in a single day.


## 2026.8.9.11
### Server Changes
- Installing the service on Linux and macOS now works out which account to register from the owner of the server folder, rather than from how root was reached. This fixes installing on a machine with no sudo set up. Debian offers a root password during installation and leaves your own account out of the sudo group when you take it, so `sudo` fails there and `su` is the ordinary way to become root — and installing after `su` used to be refused, with advice to use a `sudo` that machine did not have. It now registers the service to run as the account that owns the folder, which is you rather than root.
- Running the server itself as root is now refused on the same basis. It used to be worked out from whether `sudo` recorded who asked, which meant reaching root with `su` avoided the check entirely and made exactly the mess the check exists to prevent. The folder's owner is the thing that actually matters, and it is now what gets asked. Where root owns the folder, which is the case on a machine whose only account is root, nothing is refused.

## 2026.8.9.10
### Server Changes
- On Linux the server now says where the service actually stands. systemd reports whether a unit is installed, running and set to start with the machine without needing any rights, so the server asks on your behalf instead of telling you to go and look. `service status` and `--service-status` answer properly, and any other service command typed without `sudo` reports the status first and then names the command that would change it. macOS is unchanged: reading the system domain there needs root, so there is nothing to ask.
- A Linux or macOS server that is itself running as root now gets the full service menu, the same one Windows gets, rather than being told to use `sudo`. It already has the rights, which happens where root is the only account on the machine.
- Asking a Linux or macOS server to start, stop, restart or uninstall the service told you to run the install command instead. All five verbs were answered before the one you typed had been read, and the answer names a command, so it read as correct unless you compared the verb against it.
- The message shown when a service command needs root no longer repeats a warning about running the server itself as root. That is a different mistake made at a different moment, and it is now said only when somebody actually makes it. What is left is the command to run.
- A server running as root is no longer refused where root is the only account, which is common on a rented server handed over with root as its only login, and inside containers. The harm being prevented needs an ordinary account that owns the folder and is then locked out of it; where none exists there is nobody to lock out. Starting the server with sudo from your own account is still refused.

### Game Changes
- When a new version is announced but its download has not finished publishing, the game now says so plainly and suggests trying again shortly, instead of reporting that an update package was not found. That message appeared for a short time after every release and read like a fault in the game when nothing was wrong.
- Downloads are now checked against their expected size, so a connection that drops partway through is reported as an incomplete download instead of being treated as a finished one. The partial file is removed rather than left behind.

## 2026.8.9.9
### Server Changes
- A second server started from a folder that already had one running could take the folder over instead of attaching to the server already there. The socket file a running server is reached through was removed and replaced without asking whether anything was still listening on it, which succeeds silently: the first server kept running and kept serving players, but could never be reached or shut down again, and both were then bound to the same port. Starting a second copy now asks first, and is refused when a server answers.
- The server no longer writes a `Start TopSpeed Server.desktop` entry on Linux. No desktop environment will run one until the person who downloaded it marks it trusted by hand, and that mark is stored per user rather than in the file, so it can never be shipped working. What it did instead was fail silently, which is worse than not being there. `start-server.sh`, and `Start Server.command` on macOS, are unchanged. An entry already written into a folder is left alone.
- Installing or removing the service on Linux and macOS no longer writes a script to run afterwards. Running a script the server had just written is exactly as much work as running the server again with `sudo`, and it cost two files appearing in the folder, a naming rule for them, their own quoting, and several sentences explaining which to read and which to run. There is now one way to reach the service on those systems — `sudo` and the matching command line option — and everything else names that command, with the full path filled in so it can be pasted from anywhere. The `service` menu says the same rather than offering choices it cannot carry out.
- On Linux and macOS the server now refuses to run as root. A server started that way wrote its settings, its log, its control socket and its own updates into the folder owned by root, which the account owning the folder could then no longer replace. Nothing said so at the time; it surfaced later as an update that would not install. Installing the service still uses `sudo`, and is unaffected: that runs, registers the service to run as your own account, and exits.
- Asking a Linux or macOS server whether it is installed as a service now says that the system holds that answer and which command reports it, rather than describing a route that no longer exists.

## 2026.8.9.8
### Server Changes
- On Linux and macOS the server now writes a way to start itself that does not need a terminal, because pressing enter on a program with no extension does something different in every file manager and nothing at all in some. On macOS that is `Start Server.command`, which Finder runs in Terminal. On Linux it is `start-server.sh` and a `Start TopSpeed Server.desktop` entry, which asks for a terminal so the server has somewhere to be typed at. They are written only when they are not already there, so one you edited or deleted stays that way.
- Installing the server as a service on Linux and macOS no longer means reading commands off the screen and retyping them. Run the program with `sudo` and the `--install-service` option and it does the whole thing itself, working out from `SUDO_USER` which account the service should run as so that it never runs as root. Run it without `sudo`, or type `service install` inside a running server, and it writes the unit or job beside the server along with a short script that installs it: read them if you like, then run the script, which asks for your password once and does the rest.
- The commands written into those scripts have every path quoted. A folder whose name contains a space produced a command that failed with an error about directories, which said nothing about the real cause.
- On macOS the script is named so Finder will run it, and both scripts say what they are doing as they go. The removal script clears away the service, the file describing it and itself.
- Typing `service start` or `service restart` inside a server running on Linux or macOS no longer stops that server. It used to treat the request as a handover, stop the server, print a command nobody had run, wait a minute for a service that was never started, and then report that nothing was running.

## 2026.8.9.7
### Server Changes
- Nothing. This version carries no changes at all: it exists so that a server running 2026.8.9.6 can be offered an update whose download has been removed on purpose, which is the one path that cannot be tested without publishing something to break.

## 2026.8.9.6
### Server Changes
- A new "Log level" setting decides how much goes into the log, using the same words as the --log-level option: error, warning, info, debug, or all. A server installed as a service could not be told to record debug detail before, and a log turned on in settings.json recorded everything whether you wanted it or not. It now records errors, warnings and activity unless you say otherwise.
- The update check leaves a line in the log at debug level saying what it found, and says when it skipped a check because one was installed moments ago. A check that found nothing used to say nothing, so a check that happened and a check that never ran looked identical.
- The instructions for installing the service on Linux and macOS are now one message for both, and macOS is also told how to check on the service afterwards. The commands inside them are no longer part of what gets translated, so a translation cannot change what you are told to type.
- The advice about where to keep the log claimed a Windows rule on every system. On Linux and macOS the service runs as the user who installed it and can write wherever that user can; only on Windows must the log live inside the server's folder.
- Wording throughout the service and update messages is shorter, and says "instance" rather than "window" where it means another copy of the program. The message shown when a server will not accept control now names the account that owns it instead of suggesting administrator rights, which were never the answer on Linux or macOS and are not needed by anyone sitting at a Windows machine.
- The service menu no longer repeats the port, which is already announced on attaching, and a word the service command does not understand opens the menu rather than reporting the mistake and then listing the same choices.

## 2026.8.9.5
### Server Changes
- An update that installs but comes back reporting the old version is no longer installed again and again. The server records which version it handed over, notices it is still the older one, says so once, and leaves that version alone until a newer one appears. A later version installs as usual and "update --force" installs the refused one anyway.
- The server no longer checks for updates again in the first few minutes after installing one, having asked that same question on its way into the install.
- An update approved while players are connected is no longer forgotten if a daily check happens before the last of them leaves. The approval now survives a check that finds the same version, and is dropped with a message only when the version it was for is no longer the one being offered.
- An update offered but never approved now expires, so typing update the next day shows the changes again rather than installing on one keystroke.
- The Windows service now starts with the machine instead of two minutes after it. It was installed as a delayed start, which also ran it at background priority until it finished starting.
- Setting a log file now says to keep it inside the server's folder. A service can only write there, so a log anywhere else works when you run the server yourself and silently produces nothing when the same folder runs as a service.

## 2026.8.9.3
### Server Changes
- Approving an update is no longer a yes or no question. Typing "update" shows what has changed and says what to type next; typing "update" again approves it, and the install then waits for the last player to leave as before. Nothing installs because of a single mistyped word, and the server no longer stops answering commands while it waits for an answer. "notify" works the same way: it says a version is available, and approving it is still the second thing you type.
- "update --force" now runs an update through to the end from wherever it has got to. At a server with nothing pending it checks, downloads and installs in one go, rather than stopping to be approved first.
- Running the server program while an update is being installed now says so and stops, instead of finding no server, starting a second one, and taking the folder the updater is still writing into. What it suggests depends on what is being updated: after a service update, run it again in a moment to attach; after an update to a server you started yourself, leave it alone, because the updater opens that one again itself.
- The server now reports when a previous update did not finish, so a folder holding parts of two versions says so at startup rather than behaving oddly later. Installing the update again puts it right.

## 2026.8.9.2
### Server Changes
- A service update is no longer reported to Windows as a crash. The server used to say its stop had gone wrong so that the service manager would restart it, which also armed a restart that could fire later and switch a service back on minutes after somebody had deliberately stopped it. The updater asks the manager directly instead, so a stop that was meant stays stopped.
- On Linux and macOS an update no longer costs two minutes of downtime. The unit used to pause for long enough to cover any update before starting the server again; it now waits for the update itself and starts as soon as it is done, which is a couple of seconds.
- An update to a server that has a console attached to it closes that window. The update itself is unaffected and the server comes back on its own; run the program again once it is done to attach to the updated server.

## 2026.8.9.1
### Server Changes
- Running the server program again from a folder that already has a server now opens a console onto the one that is running, instead of starting a second server that quietly claims the same ports. Everything the running server prints appears in the new window, commands typed there are answered by it, and typing exit leaves the server running. Only one window at a time may hold the console, and a second one is told which window already has it.
- The server can now be installed as a system service, so it starts with the machine and keeps running when nobody is logged in. Type "service" for a menu, or add the verb to skip it: service install, uninstall, start, stop, restart or status. Each folder gets its own service worked out from where the server lives, so two servers on one machine can both be installed without naming either.
- On Linux and macOS, installing writes the systemd unit or launchd job next to the server and prints the two commands that install it, rather than trying to obtain root for itself. The file can be read in full before anything is agreed to.
- Starting or restarting the service from a window attached to a running server now hands the folder over rather than refusing. The running server stops, the service starts, and the same window attaches to the service, so the console is never lost.
- A server running as a service now installs updates properly and is back within a second, where before it relied on the service manager noticing a stop it had been told to read as a crash, and stayed down for two minutes.
- The log file can now be read while the server is running, instead of only after it stops. The server holds the file open in order to write to it, so Notepad and most log viewers can open it, while an editor that insists on having the file to itself still has to wait until the server stops.
- The server's window is now named after what it is doing, such as "TopSpeed Server, port 28630" or the same with "attached" on the end, so two servers on one machine can be told apart. A window opened by a shell is left with the name its owner gave it.
- The server no longer waits for an answer about an update before it starts. Previously, when a new version was available at startup, the server printed the changes and waited for you to type yes or no, which meant a server launched with its window hidden looked like it was running while nobody could actually connect. The server now starts first and the update check happens afterwards.
- Replaced the "Check for updates on startup" switch with an "Update checking" setting offering three choices. "off" never checks, "notify" says when a new version is available and leaves the decision to you, and "auto" installs new versions by itself. Servers that had the old switch turned on become "notify", and those that had it off become "off".
- Updates now wait for the server to empty before installing rather than interrupting a race. When you approve an update while players are connected, the server tells you how many are on and installs as soon as the last one leaves. "auto" waits the same way. Typing "update" again while an install is waiting reports what is scheduled instead of starting another one, and "update --force" installs immediately, disconnecting anyone still connected.
- A new version whose download has not been published yet is now treated as something to retry rather than an error. The server checks again after twenty minutes, then forty, then hourly, and reports this when it first notices, once more after the second attempt, and when it gives up after twenty-three hours. It stays quiet in between so an unattended server does not fill its console with the same message.
- Added a "Log file" setting. Logging is still off by default; setting a file name or path turns it on. A name or relative path is written next to the server program and an absolute path is used as written. A log configured this way records everything and appends, so it is still readable after a restart. The --log-file and log level command line options continue to override it.
- Update messages on the console are now timestamped, so it is possible to tell how long ago one appeared on a server that has been left running.
- Downloads are now checked against their expected size, and an incomplete one is reported and removed instead of being handed to the updater.

## 2026.8.3.1
### Game Changes
- Custom vehicles now work in multiplayer races. A room host turns them on with the new "Custom vehicles" game rule, and when another driver picks a vehicle you do not have, your game fetches it from the server by itself. Vehicles are matched by what is inside them rather than by their name, so one you already have is never downloaded, renaming or moving a vehicle does not cause it to download again, and a vehicle you do download arrives only once however many races you use it in. If a vehicle cannot be downloaded or cannot be loaded, the race still starts and you are told which vehicle was unavailable and who was using it.
- After a multiplayer race that used a downloaded vehicle, you can save it into your own Vehicles folder and then use it offline in time trial and single race. If you already have a vehicle saved under that name, you can keep both, replace yours with the downloaded one, or keep yours and discard the download. A "Prompt to keep downloaded custom vehicles" checkbox under Options, Server settings turns the offer off if you would rather nothing were saved.
- Custom vehicles and tracks you save on Android and iOS are now kept apart from the files that ship with the game, so updating the app doesn't delete them.
- Number keys no longer activate menu items in every menu. Pressing a number used to activate the item at that position anywhere in the game, which could fire by accident while you were doing something else; it now happens only where a dialog asks for it. A new "Enable digit navigation globally" checkbox under Options, General turns the old behavior back on everywhere.
- Fixed the game hanging at startup on macOS when VoiceOver is running. Speech and VoiceOver each ended up waiting on the other, so the game never finished launching.
- Fixed two input problems on macOS. Keys no longer stay held down after the media file dialog closes, which had left every later Control press reopening the file picker, and Control plus Tab moves between panels again.
- Fixed a Brazilian Portuguese copilot recording that announced a hard left when the corner ahead was actually a hairpin right. Players using Portuguese were occasionally told the wrong direction; the recording is now used for hard left corners, where it belongs.
- Added more Spanish, Armenian, Brazilian Portuguese, Vietnamese and Chinese translations.

### Server Changes
- Added a custom_vehicles feature that allows or blocks custom vehicles for the whole server, alongside the existing custom_tracks feature. Vehicles placed in a Vehicles folder next to the server executable are offered to rooms that turn the "Custom vehicles" game rule on, and a vehicle is sent to a player only when that player asks for one they do not already have.
- Added more Brazilian Portuguese and Chinese translations.


## 2026.7.28.1
### Game Changes
- You can now check your car's status while the pit stop menu is open. Fuel, tire wear, distance, speed, gear and race progress all answer while you decide whether to refuel, change tires, or both. You can also sound the horn during the whole pit stop, including in the pit box and along the pit roads.
- Status keys now keep working after you finish a race while you wait for the rest of the field, instead of going silent. You can also rev your engine while waiting; the car stays in neutral and cannot move.
- Added a "Brief status reports" option in race settings. When turned on, spoken status reports are shortened for players who already know their keys: labels such as "lap percentage" and "gear" are dropped, and the number row gives a quick rundown.
- Added a "Report lap and turn instead of race percentage" option in race settings. The number row and player information then report a car's lap and the turn it is in or approaching, spoken like a spotter, for your own car and any other driver you check.
- Added a "Curve announcement method" option in race settings. The copilot can signal upcoming curves with tones instead of speech. The tighter the curve the higher the tone, and each tone plays from the side the curve turns toward.
- Keys that a menu uses to navigate no longer double as driving keys while that menu is open, so a status key mapped to an arrow key moves the menu without also speaking.
- Improved the localization workflow so translation updates are easier to keep current, and added more Spanish, Brazilian Portuguese and Chinese translations.

### Server Changes
- Improved the localization workflow so translation updates are easier to keep current, and added more Brazilian Portuguese translations, including the pit area and lap limit messages.


## 2026.7.23.1
### Game Changes
- Fixed a bug that could wreck your car for no reason as you crossed the start/finish line or pulled out of the pit lane, most often during longer races. For a single frame the road shifted a full track-width sideways and the game counted you as off the track.

### Server Changes
- Fixed the same start/finish line glitch for the computer-controlled cars, which could spin them out for no reason as they completed a lap.


## 2026.7.17.1
### Game Changes
- Fixed a crash that could happen when starting a race with a custom vehicle that was missing one of its sound files. The vehicle now stays playable: any missing sound is replaced with a built-in default (or, for optional sounds, simply left silent), and the custom vehicles menu shows a warning describing what was missing.


## 2026.7.15.1
### Game Changes
- Fixed a single race bug where the computer cars could still bump into you after you had already finished the race, such as while waiting on the finish line with your engine running. Being hit could leave your car sliding out of your control and could delay the end of the race.
- Removed some unused sound files that were bundled with the non-English language packs.

### Server Changes
- Raised the minimum supported network protocol to the 500-lap update, so game clients older than that update are now rejected at connection instead of mis-reading race data. This corrects an oversight where the 500-lap protocol change did not raise the minimum.


## 2026.7.13.1
### Game Changes
- Added support for up to 500 laps.

### Server Changes
- Updated the network protocol to support the higher lap count.


## 2026.7.12.1
### Game Changes
- Cars now consume fuel over the course of a race. Press X to hear how much fuel you have left, and a low-fuel warning alerts you when you are running low. Be careful not to run out, or you won't be able to finish the race.
- Tires now heat up, wear down, and lose grip as a race goes on. Press B to hear their current condition: cold, warming up, optimal temperature, hot, or overheated.
- You can now make a pit stop to refuel and/or change tires. Press I to request a stop, then choose Refuel, Tires, or Both from the menu the next time you reach the pit entry area, or press 1, 2, or 3 to quickly pick the service you want.
- Fuel consumption and tire wear are optional. For single race and time trial, toggle them under Options, Race settings ("Enable fuel consumption" and "Enable tire wear"). For multiplayer, the host controls them through the race rules. Either way, races play the same as before when they are left off.
- Filled in missing race-announcement audio: voice for players 9 and 10, finishing positions 8 and 9, live "you are in 8th/9th" position callouts, and "finished last" / "you are last" callouts so the final racer is always announced correctly no matter how many are in the field.
- The F1 through F8 keys for player information have been replaced with the number row keys, which now speak all the information about each player.
- Added Brazilian Portuguese (pt-BR) voice audio.
- Added a Persian translation.
- Improved how track callouts are announced by separating them from other race information.
- Fixed a multiplayer bug where a race would never finish if an earlier race in the session had been aborted.
- Fixed a hard crash that could happen when a vehicle's data file was missing its engine RPM values.
- Fixed multiplayer track names not being translated.
- Fixed incorrect Chinese wording on the race results screen and corrected several other dynamic-text translation issues.
- On Android, the game now prefers the Android text-to-speech voice first (including in automatic mode) and clears leftover update files on startup.

### Server Changes
- Added support for the fuel consumption and tire wear race rules, broadcasting the effective rules for each race so clients set up their cars correctly.
- Updated the network protocol for the fuel, tire wear, and pit stop features. Clients and servers must both be on this release to play together.


## 2026.5.14.1
### Game Changes
- Added a full voice chat system to the game. Any player who is connected to a server can enable their communicator by pressing ctrl+shift+c to listen to other players, and either holding v or ctrl+shift+v to talk.
- The communicator has a frequency, between 0.0 and 1000.0. The default public frequency is 1.0 which is by default all players are tuned to. You can read the current frequency by pressing f, and change it by pressing ctrl+f.
- There are new settings in the audio to choose the default voice input device and Microphone gain.
- Added a new category in the volume settings for communicator. This controls the loudness of communicator sounds as well as other players. This does not affect the radio.
- You now have the ability to stream files anywhere using your communicator by pressing ctrl+f to load a folder, or ctrl+o to load a file, then playing it with ctrl+p. Shortcut keys are similar to the radio, except adding ctrl with the key. For example, toggle loop is ctrl+l.
- Added a new quicker way of controling different volume categories, by pressing f6 and shift+f6 to switch between different categories, and f7, f8 + adding shift with those keys control the actual volume.
- Added proxy support to the game when downloading updates or external requests.


### Server Changes
- Added voice chat support.
- Added a new flag to control voice chat on the server level.


## 2026.5.9.2
### Game Changes
- Fixed multiplayer voice chat: remote players could not hear each other at all. The communicator now works in the multiplayer lobby in addition to inside rooms. Anyone tuned to the same communicator frequency hears the transmission regardless of which room (or no room) they are in.
- Removed the leftover `TOPSPEED_VOICE_DEBUG` opt-in voice-chat tracing introduced while diagnosing the regression above.

### Server Changes
- Voice chat is now relayed to every connected player on the server (filtered client-side by communicator frequency) instead of being scoped to a single room, so voice works in the lobby and across rooms.


## 2026.5.9.1
### Game Changes
- Fixed the in-vehicle radio in multiplayer crashing when a track finishes and loops back to the start (notably with FLAC files). The fix is in the SoundFlow native FFmpeg wrapper: tail-of-stream codec/demuxer hiccups are now reported as graceful end-of-stream instead of as fatal decoder errors, so the radio source's `Seek(0)`+retry path recovers cleanly.


## 2026.5.5.1
### Game Changes
- Fixed many bugs with the multiplayer server.
- Added a new way of navigating through message history by using the comma to move to the previous item, period to move to the next item, and left/right brackets are used to navigate between buffers. The separate history screen is still available.
- Added an ability to copy the current buffer item to the clipboard by pressing ctrl+space, or by going to the history and pressing enter on any message there.
- Added the ability to reset menu shortcuts to their defaults.


### Server Changes
- Fixed many bugs related to server connection and room deadlocks where players were being stuck in a room after joining multiple times.


## 2026.5.4.3
### Game Changes
- Added the ability to choose which modifier keys are being used when you remap a key in the game. This allows you to either use both modifiers, or the left/right.
- Fixed a critical crash with ZDSR by disabling CET compatibility. The game should no longer crash again when ZDSR is installed.
- Fixed some critical crashes when discovering local servers on the network.
- Android version now runs in landscape mode.


### Server Changes
- Fixed a regression where protocol version mismatches did not trigger a hard fail.

## 2026.5.4.2
This is a hot fix for Android arm 32 and Mac.

## 2026.5.4.1

### Game Changes

* Fixed many crashes that could happen randomly due to audio processing for invalid audio buffers.
* Added Spanish translation for copilot and race announcements.
* Added support for Mac ARM-64 and Android arm-32 (ARM-v7) builds.
* Added support for uploading your custom tracks to the server.
* Android builds now use a permenant signature and no longer conflicts with existing versions.


### Server Changes

* Refactored server and made race finish events much more reliable.
* Added reconnect support, when a player loses connection suddenly, there is now a 20 seconds reconnect period before fully disposing the player.
* Fixed player randomization.
* Added moderation tools to prevent duplicate names on the server, prevent long names, prevent repeated letters in a name, and control text chat on the server level.
* Added initial support for custom tracks.
* You can now host your own custom tracks on the server, and other people can see them when they enable custom tracks.

