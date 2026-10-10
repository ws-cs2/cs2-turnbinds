# CS2 Turnbinds

![image](https://github.com/ws-cs2/cs2-turnbinds/assets/149922947/02da5f15-f803-4e53-aa2b-06a5a2ffba2d)

## Overview

The CS2 Turnbinds program is a customizable tool designed to assist gamers in executing precise turning movements in games. It allows users to configure specific yaw speeds, mouse sensitivity, and key bindings to automate mouse movements for turning. This is functionality that used to be in the game in CounterStrike Source and is needed to complete many surf maps.

## Anti-cheat software

From t5mat turnbinds (the program this was based on):

> The program does not patch or inject anything into the game.
> 
> Apart from simulating mouse input, it doesn't really do anything suspicious.
> 
> It would be fair to say it's as VAC bannable as an AutoHotkey script.
> 
> Anti-cheat software (FACEIT AC, ...) can easily detect the simulation of mouse movement though, and either prevent it or prevent the program from running completely. This is expected, just don't actively try to use this program in unintended scenarios.

We had alot of evidence that simulating mouse input was not bannable in CS:GO. I used t5mat's turnbind program for years. This program does not do anything differently however cs2 is a different game so there may be a risk.

## Configuration

The program reads its configuration from a `config.json` file, which it will create with default settings if it does not already exist. You can customize this file to suit your preferences. Here are the configurable options:

- `Yaw`: A list of integers representing the yaw speeds you can toggle between.
- `Sensitivity`: Your mouse sensitivity. (Match to your in game value)
- `M_Yaw`: The yaw factor that determines how much the view angles change per mouse movement. (Match to your in game value)
- `Left`: The key used to move the mouse left.
- `Right`: The key used to move the mouse right.
- `Toggle`: The key used to toggle between different yaw speeds.
- `Pause`: The key used to pause and unpause the program.
- `Inc`: The key used to increase the current yaw speed.
- `Dec`: The key used to decrease the current yaw speed.
- `Exit`: The key used to exit the program.
- `PauseWhenCursorVisible`: Suppress movement and yaw hotkeys when the Windows cursor is visible (default: `true`). The JSON key is `pause_when_cursor_visible`; existing configs without it also default to enabled.

![image](https://github.com/ws-cs2/cs2-turnbinds/assets/149922947/8550fec1-3727-47d8-bcd6-860a4841e455)

## Usage

1. Run the program once, it will make `config.json` with default keybinds.
1. Ensure the `config.json` file is set up with your preferred settings.
2. Restart the program for your changes to `config.json` to take effect.
3. Use the configured keys to control the program during your game. You can toggle yaw speeds, pause the program, adjust yaw speeds on-the-fly, and more.

### CS2 Focus Only

Mouse movement and the pause, yaw toggle, increase, and decrease hotkeys only work while the foreground window belongs to `cs2.exe`. Alt-tabbing to another application automatically suppresses these inputs; returning to CS2 resumes normal operation without changing your manual pause setting. If the foreground process cannot be identified, no mouse movement is sent. The exit hotkey remains available globally.

Existing `config.json` files work unchanged. The program still starts manually paused, so press your pause key while CS2 is focused to enable turning.

To check the behavior:

1. Focus CS2, unpause, and hold a configured turn key to verify turning works.
2. Alt-tab to another application while holding the turn key and verify simulated mouse movement stops.
3. Use the pause and yaw hotkeys outside CS2 and verify the program's settings do not change.
4. Return to CS2 and verify turning resumes without a catch-up jump.
5. Manually pause in CS2, alt-tab away and back, and verify it stays paused. Check the exit hotkey also works outside CS2.

### Smooth Movement

The movement loop uses a high-resolution Windows timer targeting 1,000 updates per second, with fractional mouse movement carried between updates to preserve yaw speed. Actual timing depends on system load and Windows scheduling. Windows 10 version 1803 or newer (including Windows 11) is required.

Gameplay hotkeys have independent, non-blocking 200 ms cooldowns. Holding a hotkey repeats its action without stopping the movement loop. To test, hold a turn key while increasing, decreasing, or toggling yaw speed and check that turning stays continuous.

### Cursor Visibility Guard

By default, movement and yaw hotkeys are suppressed whenever Windows reports a visible cursor, or when querying cursor visibility fails. This can prevent turning in Steam overlay chat and CS2 menus. Turning resumes when the cursor becomes hidden, without changing the manual pause setting or accumulating catch-up movement. Manual pause remains usable while CS2 is focused, and exit still works globally.

This is a cursor visibility heuristic, not exact Steam overlay detection. If Steam or CS2 draws its own cursor without exposing it to Windows, the guard may not detect it. Test by unpausing in gameplay, opening overlay chat and clicking with a turn button, then closing the overlay and checking that turning resumes smoothly. Also test CS2 menus and manual pause.

If it interferes with gameplay, add `"pause_when_cursor_visible": false` to your existing `config.json` object and restart. Otherwise, existing configs need no changes. If overlay detection does not work on your setup, use manual pause before opening it.

### Build

Install the .NET 8 SDK or newer and run `./build.ps1` from the repository directory. The self-contained Windows x64 executable is published to `bin/Release/net8.0/win-x64/publish/cs2-turnbinds.exe`. Attach that executable to a GitHub release to distribute it; users do not need to install .NET.

## Key Bindings

When configuring keys in `config.json`, use the key names from the [Virtual-Key Codes](https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes) documentation without the `VK_` prefix. For example, use `LBUTTON` for the left mouse button, `RBUTTON` for the right mouse button, `A` for the "A" key, etc.


## Credits

 - [t5mat's CS:GO Turnbinds](https://github.com/t5mat/turnbinds) - This program is based on t5mat's original CS:GO Turnbinds program.
 - Spaz - Figuring out how to get mouse movement in CS2
 - [oce.surf Community](https://oce.surf) - Testing
