# Building and contributing

Flare is licensed under the MIT license. Forks, bug reports and pull requests are welcome.

## Requirements

- Windows with the .NET 10 SDK and .NET Framework 4.8.1 Developer Pack.
- A local Valheim installation.
- BepInExPack Valheim installed in the game or a mod manager profile.
- Jotunn 2.30.0 or newer installed to play. The build restores its pinned JotunnLib 2.29.2 reference from NuGet.

## Build

Clone your fork, then copy `Environment.props.example` to `Environment.props` in the repository root. Set `VALHEIM_INSTALL` to your game directory and `BEPINEX_PATH` to the BepInEx directory containing `core/BepInEx.dll` and `core/0Harmony.dll`.

```powershell
dotnet build src/Flare/Flare.csproj -c Release
dotnet test tests/Flare.Tests/Flare.Tests.csproj
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Build-FlarePackage.ps1
```

The plugin is written to `src/Flare/bin/Release/net481/Flare.dll`. The validated installation ZIP is written to `artifacts/flare/`. Builds do not install or launch the game. Close Valheim before replacing an installed DLL.

Game and dependency DLLs are referenced from your installation and are not distributed here. `Environment.props` is ignored by Git; keep machine-specific paths and credentials out of commits.

## Layout

- `src/Flare`: plugin, networking, lighting, smoke and original icon assets.
- `src/Shared`: signal item identity.
- `tests/Flare.Tests`: flight, smoke, configuration and shot authorization tests; no running game required.
- `scripts`: package build, icon export and package validation.
- `images`: README screenshots and animated showcase.

## Changes and testing

Keep player-facing text in English. Shared flight, lighting and smoke settings are controlled by the server; shadows are a local preference. Preserve the harmless, single-use item behavior.

Run the build and tests, then test relevant gameplay changes with matching client and server versions. Check drawing and cancelling, shooting, smoke at distance, joining an active session and live configuration changes. Automated tests do not validate visual appearance or multiplayer rendering.

When changing the release version, update both `Flare.csproj` and `Plugin.Version`, and add a changelog entry. For forks, update README image links and package metadata to your own repository as appropriate. Describe the behavior change and how you tested it in your pull request.
