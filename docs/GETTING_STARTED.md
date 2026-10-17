# Getting started

[Documentation index](README.md) · [Examples](EXAMPLES.md) · [Troubleshooting](TROUBLESHOOTING.md)

## Install the mod

1. Install **BepInExPack 5.4.2305** (BepInEx 5.4.23.5, Mono x64) using a compatible mod manager or the [package's manual instructions](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/).
2. Extract [RepoRestApi-1.0.0.zip](../artifacts/RepoRestApi-1.0.0.zip) into the game directory or the active mod profile. BepInEx 6/IL2CPP is not the target framework.
3. Check these files exist relative to that directory:

```text
BepInEx/plugins/RepoRestApi/RepoRestApi.dll
BepInEx/plugins/RepoRestApi/RepoRestApi.Core.dll
BepInEx/plugins/RepoRestApi/Newtonsoft.Json.dll
BepInEx/plugins/REPOLib/REPOLib.dll
```

The ZIP includes REPOLib 4.2.0. If a mod manager already installs REPOLib, keep one copy of the required version instead of adding a duplicate plugin.

### Windows

Launch through the mod manager, or run the game after installing BepInEx manually. In Steam, **Manage → Browse local files** opens the game directory. Your mod manager may keep `BepInEx` inside its profile rather than the original game directory; use the profile's configuration and logs in that case.

### Linux, CachyOS and Steam Deck

Run the Windows game through Steam/Proton. For a manual BepInEx installation, open **R.E.P.O. → Properties → General → Launch Options** and use:

```text
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

If you already have launch options, add the environment assignment before the existing `%command%` expression. A mod manager may configure the loader for you.

A `REPO.exe`-specific `winhttp=native,builtin` override in the game's Proton prefix also enables the loader. If your installation is already configured that way, launch normally from Steam without adding another override.

The usual native Steam game path is:

```text
~/.local/share/Steam/steamapps/common/REPO/
```

Use Steam's **Browse local files** if you installed to another library. Flatpak Steam and custom libraries can use different paths.

## Confirm startup

Launch the game and check `BepInEx/LogOutput.log` for:

```text
REST server listening on 127.0.0.1:17453
```

The API starts after the first scene activates. First launch generates `BepInEx/config/org.repo.rest-api.cfg`, including a random `AuthenticationToken`. The token is not printed in normal plugin logs. If the file is missing, fix plugin loading before trying HTTP requests.

## Load your token

### Bash on Linux/CachyOS

Adjust the configuration path for a different Steam library or mod profile:

```bash
export REPO_API_CONFIG="$HOME/.local/share/Steam/steamapps/common/REPO/BepInEx/config/org.repo.rest-api.cfg"
export REPO_API_TOKEN="$(sed -n 's/^AuthenticationToken = //p' "$REPO_API_CONFIG" | tr -d '\r')"
export REPO_API_URL='http://127.0.0.1:17453'
```

This loads the token into the current terminal without printing it. Node.js and Python examples launched from that terminal inherit the variables. Bash examples use `curl`; catalog selection examples also use `jq`. On CachyOS/Arch, install missing tools with `sudo pacman -S --needed curl jq`.

### PowerShell on Windows

Adjust the game directory for your Steam library or mod profile:

```powershell
$RepoGameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\REPO'
$RepoConfig = Join-Path $RepoGameDirectory 'BepInEx\config\org.repo.rest-api.cfg'
$RepoTokenLine = Get-Content $RepoConfig |
    Where-Object { $_ -match '^AuthenticationToken\s*=' } |
    Select-Object -First 1
$env:REPO_API_TOKEN = ($RepoTokenLine -replace '^AuthenticationToken\s*=\s*', '').Trim()
$env:REPO_API_URL = 'http://127.0.0.1:17453'
```

Keep the token and configuration private. The clients read environment variables; they do not automatically load `.env` files.

## Make a read-only request

From the repository root, choose one client:

| Client | Command |
|---|---|
| curl | `bash examples/curl/repo-api.sh GET /health` |
| JavaScript, Node.js 22+ | `node examples/javascript/repo-api.mjs GET /health` |
| Python 3.10+ | `python3 examples/python/repo_api.py GET /health` |
| PowerShell 7+ | `pwsh -NoProfile -File examples/powershell/repo-api.ps1 -Method GET -Path /health` |

A healthy server before gameplay can return:

```json
{
  "status": "ready",
  "gameRunning": true,
  "inLevel": false,
  "isHost": true,
  "gameplayReady": false,
  "apiVersion": "1.0.0"
}
```

HTTP readiness and gameplay readiness are separate. Menus, lobbies, shops and transitions do not permit gameplay mutations. A stalled Unity loop can cause a 504 even if the process is open.

## Spawn your first item or upgrade

1. Host or start singleplayer and enter a generated gameplay level.
2. Call `/health` again and confirm `gameplayReady=true`.
3. Choose **one** example to execute:

```bash
# Discover a health item, then spawn one near a current player.
node examples/javascript/spawn.mjs item health

# The same workflow using Python's standard library.
python3 examples/python/spawn.py item health

# Discover and spawn an available upgrade pickup.
node examples/javascript/spawn.mjs upgrade
# Or:
python3 examples/python/spawn.py upgrade
```

These commands select the first spawnable match in the current catalog and report its name and ID. An empty catalog/search produces a clear error and does not send a spawn request. A 422 means no safe position was available for that command.

An upgrade is a physical pickup to collect. It does not immediately change player stats. See [Examples](EXAMPLES.md) for curl, exact-ID spawning, enemy/valuable spawning, positioning, player actions and retry handling.
