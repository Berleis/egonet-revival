# F1 2018 Event Activator

This folder contains the helper script for the F1 2018 Event Activator.

The activator patches only the two old weekly Event payloads after F1 2018 has loaded them into process memory. It does not run the RaceNet server and does not change game files, saves, Steam stats, certificates, or the Windows hosts file.

For player releases, use `games/f1-2018`: the release package builds a standalone `EgoNet Revival - F1 2018 Event Activator.exe`. The `.cmd` in this folder is a development helper.

Usage:

```bat
activate-events.cmd
```

Recommended flow:

1. Open F1 2018.
2. Enter the in-game Events screen.
3. Run `activate-events.cmd`.
4. Return to Events or switch between current/previous Event.
5. Start and finish an Event.

Direct executable after build:

```bat
..\..\src\F12018EventActivator\bin\Debug\net10.0\F12018EventActivator.exe
```

Useful options:

```bat
F12018EventActivator.exe --dry-run
F12018EventActivator.exe --days 60
F12018EventActivator.exe --wait 120
```
