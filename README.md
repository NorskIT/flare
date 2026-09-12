![A glowing flare with a drifting smoke trail in the night sky](https://raw.githubusercontent.com/NorskIT/flare/main/images/flare-banner.png)

A warm signal in the sky to help your friends find you, with a lingering smoke trail and light that illuminates its surroundings.

Perfect for **nomap adventures**: send up a flare so your friends can find their way back to you without map markers.

## Features

- Craft **Signalbow and Arrow** for **1 Wood**, with no workbench or ammunition required.
- Aim upward, draw, and release. Each item fires one harmless signal and is consumed after the shot.
- The arrow burns and lights up like a torch while you draw the bow.
- Flares remain visible beyond loaded areas, with visibility affected by distance, weather, and obstacles.
- Smoke follows the flare and lingers after it burns out.
- Server-controlled flight, lighting, and smoke settings, with a local shadow toggle.

![Drawing the Signalbow and Arrow and launching a flare into the night sky](https://raw.githubusercontent.com/NorskIT/flare/main/images/flare-showcase.gif)

Light the arrow, draw the bow, and send a signal into the night.

![A player watching a flare light up the night sky](https://raw.githubusercontent.com/NorskIT/flare/main/images/flare-in-game.png)

![Signalbow and Arrow inventory icon and item description](https://raw.githubusercontent.com/NorskIT/flare/main/images/signalbow-and-arrow.png)

## Installation (manual)

1. Install BepInExPack Valheim and Jotunn (see Dependencies below).
2. Extract the ZIP's `BepInEx` folder into your Valheim installation folder, merging it with the existing folder.
3. Install the same Flare version on the server and every player's client, then restart.

Using a mod manager? Click **Install**, or import the ZIP as a local mod. Dependencies are handled by the mod manager.

## Known issues

Area lighting and shadows depend on loaded terrain and the game's graphics settings. Seeing a distant flare does not load or illuminate the terrain around it on your client.

## Configuration

Launch the game or server once to create `BepInEx/config/norskit_flare_plugin.cfg`.

The server controls flight height, burn duration, descent speed, light radius, light intensity, glow strength, smoke lifetime, and smoke strength. Flight changes apply to new shots; lighting and smoke changes also apply to existing signals.

Server admins can change values in the console, for example:

```text
flare set LightRadius 250
flare set LightIntensity 3
flare set SmokeLifetime 20
flare set SmokeStrength 1
```

After editing the server's configuration file, use `flare reload` to apply it. Each player can toggle their own shadows with `flare shadows on` or `flare shadows off`.

## Dependencies

- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- [Jotunn 2.30.0 or newer](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/)

Install these separately; their DLLs are not included in the Flare package.

## Source and contributions

Flare is open source under the [MIT license](https://github.com/NorskIT/flare/blob/main/LICENSE). Forks and contributions are welcome. See [building and contributing](https://github.com/NorskIT/flare/blob/main/CONTRIBUTING.md) to get started.
