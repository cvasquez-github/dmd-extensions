# DMD Extensions for Proton (.NET 8 build)

This branch (`net8`) is a reduced fork of [DMD Extensions](https://github.com/freezy/dmd-extensions)
that shows the DMD of **Pinball FX Classic** (formerly Pinball FX3) on a cabinet running
**Bazzite Linux**, where the game runs under **Steam Proton**.

The core idea: `dmdext.exe` is published as **a single self-contained win-x64 executable
(.NET 8)**, which runs inside the game's Proton prefix without installing .NET Framework
4.7.2 or any other runtime. On top of that come fixes for Wine, fixes for bugs of the
original dmdext, and new features (a backglass window, a "no table" state over the network).

For regular use on Windows, with hardware or with `DmdDevice.dll`, use the original dmdext:
this build does **not** replace it.

---

## 1. What's supported and what isn't

### Tested on the cabinet

| Feature | Status |
|---|---|
| `dmdext mirror -s pinballfxclassic` under Proton (reading the game's memory) | ✅ |
| Virtual DMD (window) | ✅ |
| Network stream (WebSocket) to another machine, together with the virtual DMD | ✅ |
| Table name over the network (`gameName`) and "no table" state | ✅ |
| DMD image while no table is loaded (`--idle-play`, PNG or animated GIF) | ✅ |
| Idle images found by the script for all games: `DEFAULT_IDLE.gif` next to `dmdext.exe` (DMD), `DEFAULT_IDLE.png` in the backglass folder (backglass) | ✅ |
| Idle images for one game: `DEFAULT_IDLE_DMD_442120.gif`, `DEFAULT_IDLE_BACKGLASS_442120.png` | ✅ |
| Script variables set before the script, as in a game's launch options (e.g. `DMD_WIDTH=600`) | ✅ |
| Backglass window with each table's image | ✅ |
| Default backglass while no table is loaded (`PinballFX3.png`) | ✅ |
| Closing the game without Steam still marking it as running | ✅ |

Test environment: Bazzite `bazzite-deck` 44 (Stable F44.20260907), KDE Plasma 6 (KWin 6.7,
Wayland), Proton 11.0, Steam Linux Runtime 4, two screens (portrait for the playfield and
landscape 1920×1080 for the backglass).

### Included in the build, but not tested under Proton

- Sources `pinballfx2`, `pinballarcade`, `futurepinball` and `screen`.
- The `test` command.
- Outputs `alphanumeric` (virtual alphanumeric display), `-o` (bitmaps to a folder),
  `--dump-frames` and `--pinup`.
- Script variables written in Steam's launch options themselves. They were tested with a
  wrapper that sets them before the script, which is what the launch options do.

### Not included

| What | Why |
|---|---|
| Hardware outputs over USB, serial or WiFi: PinDMD v1/v2/v3, PIN2DMD (XL, HD), ZeDMD (incl. HD and WiFi), Pixelcade | Out of scope: the goal is the virtual DMD and the network |
| `DmdDevice.dll` / `DmdDevice64.dll` (new Pinball FX, Visual PinMAME) | Only the executable is ported |
| `propinball` source | Depends on a C++/CLI bridge that doesn't exist in .NET 8. It still shows up in the help of `-s`, but doesn't work |
| `play` and `server` commands | Out of scope |
| Browser stream, VPDB stream, video output | Out of scope |
| Analytics and error reporting (Raygun) | Disabled (`AnalyticsStub.cs`) |

All of this is left out with the `DMDEXT_MIRROR_ONLY` symbol and the `Compile Remove` items of
`LibDmd/LibDmd.Net8.csproj` and `Console/Console.Net8.csproj`.

---

## 2. Building

Requires the .NET 8 SDK on Windows.

```
dotnet publish Console\Console.Net8.csproj -c Release -o publish\dmdext-net8-win-x64
```

Output in `publish\dmdext-net8-win-x64\`:

| File | What it is |
|---|---|
| `dmdext.exe` | Single executable, no dependencies |
| `dmdext.log.config` | Logging configuration (NLog) |
| `dmdext-proton.sh` | Steam launch script (section 4) |
| `idle.png` | DMD image while no table is loaded: dmdext's test image (`Console/Test/TestImage.png`). Used if there's no other idle image for the DMD (section 4, "Idle images") |

The .NET 8 projects live next to the original (.NET Framework) ones and use their own
`bin.net8/` and `obj.net8/` folders.

---

## 3. Changes to the original dmdext

### To make it work under Wine / Proton

- **Wine detection** (`InteropUtil.IsRunningOnWine`).
- **Software rendering** for WPF windows: with Direct3D only a colored triangle showed up.
- **Opaque windows**: Wine doesn't composite windows with per-pixel transparency properly.
- **The DMD's OpenGL** draws into SharpGL's frame buffer; the hidden window's back buffer stayed black.
- **The last frame is drawn again** after 100 ms, since the window sometimes kept showing the previous one.
- **Frozen images** (`Freeze`) so they can be used from any thread; otherwise dmdext hung.
- **Non-resizable virtual DMD**: a resizable window without transparency showed a thin white frame. Position and size come from the options.
- **Backglass without the monitor's last pixel row**: covering the whole monitor makes Wine mark it as full screen, and KWin then puts it above the "stay on top" DMD while the game is active on another screen.
- **Diagnostic logs** when the game process isn't found or its memory can't be read.
- **`dmdext-proton.sh` script**: starts dmdext inside the game's Proton session. A separate Wine (e.g. protontricks) doesn't see the game process.

### Fixed bugs of the original

- **Inverted dedupe** in the Pinball FX3 grabber: it sent repeated frames and dropped new ones.
- **`gameName` was never sent over the network** in mirror mode.
- **`gameName` without a terminating null**, although the deserializer itself expects one.
- **`--virtual-*` options ignored in mirror mode** (dot size and brightness, padding, textures): the virtual DMD always used the default style.
- **Stopping idle mode** disposed the outputs it shares with regular rendering.
- **Frames still queued** overwrote the idle image.
- **Blocking network connection**: with the receiver down, dmdext took ~37 s to start and ~36 s to exit. Under Proton that left Steam with the game "running" and a black screen.

### New features

- **"No table" state**: detected because the pointer to the DMD in the game's memory is null, not because frames stop coming (they also stop while the DMD doesn't change). It's reported only once, and dmdext switches to idle (`--idle-play` or a blank display).
- **`--url` adds the network** to the chosen destination: virtual DMD and network at the same time.
- **Backglass window** (`--backglass`, section 5).
- **`DEFAULT_IDLE` images**: own idle images for the backglass and the DMD, for all games or for each Steam game, just by dropping files in place (sections 4 and 5).
- **Per-game settings**: the script's variables can also be set in a game's Steam launch options (section 4).

---

## 4. Installing on Bazzite

1. Copy the published folder to the cabinet, e.g. to `~/Pinball/dmdext-net8-win-x64/`.
2. Make the script executable:
   ```bash
   chmod +x ~/Pinball/dmdext-net8-win-x64/dmdext-proton.sh
   ```
3. In Steam: Pinball FX Classic → Properties → Launch options:
   ```
   /var/home/<user>/Pinball/dmdext-net8-win-x64/dmdext-proton.sh %command%
   ```
4. Adjust the variables at the top of the script (table below), or per game in its launch
   options (see "Per-game settings"), and launch the game from Steam. dmdext's log goes to
   `~/dmdext-mirror.log`, which is cleared on every launch. Its first line is the game's Steam
   app id and the second one the full dmdext command. `ld.so` errors saying that an object
   from `LD_PRELOAD` cannot be preloaded (`wrong ELF class`) can be ignored.

The script launches the game, waits for its Proton session (`com.steampowered.App<app id>`,
e.g. `App442120` for Pinball FX Classic) to come up and starts dmdext inside it with
`steam-runtime-launch-client`. dmdext uses `-q`, so it quits by itself when the game exits.

### Script variables

Set them at the top of the script for all games, or in a game's launch options for that game
only (see "Per-game settings" below).

| Variable | Default | What it does |
|---|---|---|
| `SOURCE` | `pinballfxclassic` | Mirror source: `pinballfxclassic` (also Pinball FX3) or `pinballfx2` |
| `VIRTUAL_DMD` | `true` | `true` to show the virtual DMD on this machine |
| `BACKGLASS` | `false` | `true` to show the backglass window |
| `BACKGLASS_X`, `BACKGLASS_Y`, `BACKGLASS_WIDTH`, `BACKGLASS_HEIGHT` | `0`, `0`, `1920`, `1080` | Screen area of the backglass, in desktop pixels. The DMD is placed relative to it, even with `BACKGLASS=false` |
| `BACKGLASS_PATH` | empty | Folder of the backglass images. Empty = the game's `data/steam` folder |
| `BACKGLASS_IDLE` | empty | Backglass image while no table is loaded, or if the table has no image. Empty = look for one (see "Idle images" below) |
| `DMD_WIDTH` | `736` | Width of the DMD window in pixels; the height follows from the aspect ratio |
| `DMD_PADDING` | `3` | Black border around the dots, **in DMD dots** |
| `DMD_BOTTOM_MARGIN` | `12` | Space in pixels between the DMD and the bottom edge of the backglass area |
| `NETWORK_HOST`, `NETWORK_PORT`, `NETWORK_PATH` | empty, `8080`, `/dmd` | WebSocket receiver, `ws://<host>:<port><path>`. Empty `NETWORK_HOST` = no network |
| `IDLE_PLAY` | empty | DMD image while no table is loaded (PNG, JPG or animated GIF). Empty = look for one (see "Idle images" below). `none` = blank DMD |
| `EXTRA_ARGS` | `()` | Other dmdext arguments, e.g. `(--virtual-dot-glow 0.5)`. Only at the top of the script |

By default the DMD is centered at the bottom of the backglass area. To see each screen's
position, use `kscreen-doctor -o` (the `Geometry` field).

### Idle images

While no table is loaded, e.g. in the game's menus, the DMD and the backglass show an idle
image. It can be the same for all games or different for each Steam game. The script tells
games apart by their Steam app id, which Steam passes to it (`SteamAppId`, else
`STEAM_COMPAT_APP_ID`, else `442120`) and which is written to the first line of the log. It's also in the game's store URL,
`store.steampowered.com/app/<app id>/`.

The first image that exists is used:

| Order | DMD | Backglass |
|---|---|---|
| 1. Variable (launch options, else top of the script) | `IDLE_PLAY` | `BACKGLASS_IDLE` |
| 2. Image for this game, next to `dmdext.exe` | `DEFAULT_IDLE_DMD_<app id>.gif`, `.png` or `.jpg` | `DEFAULT_IDLE_BACKGLASS_<app id>.png` or `.jpg` |
| 3. Image for all games | `DEFAULT_IDLE.gif`, `.png` or `.jpg` next to `dmdext.exe` | `DEFAULT_IDLE.png` or `.jpg` in the backglass image folder (section 5) |
| 4. Default | `idle.png`, dmdext's test image | The game's default image (`PinballFX3.png`), else black |

For example, a pair of images only for Pinball FX Classic (app id `442120`), next to
`dmdext.exe`:

```
DEFAULT_IDLE_DMD_442120.gif
DEFAULT_IDLE_BACKGLASS_442120.png
```

Another game launched with this script gets its own pair, named after its app id. The backglass
idle image is also shown for tables without an image of their own.

Keep in mind:

- **A variable wins over the files.** Setting `IDLE_PLAY` or `BACKGLASS_IDLE` at the top of the
  script uses that image for all games, and the `DEFAULT_IDLE_*_<app id>` files are never used.
- **The backglass image for all games isn't next to `dmdext.exe`.** A `DEFAULT_IDLE.png` there is
  the DMD's idle image, not the backglass one. The backglass looks for `DEFAULT_IDLE.png` in its
  image folder, by default the game's `data/steam` (section 5).
- **A variable pointing to a missing image:** `IDLE_PLAY` leaves the DMD blank, and
  `BACKGLASS_IDLE` goes on with step 3 of the backglass column.

### Per-game settings

A variable set before the script in a game's launch options applies only to that game, and
wins over the value at the top of the script:

```
IDLE_PLAY=/var/home/<user>/Pinball/menu.gif BACKGLASS=false /var/home/<user>/Pinball/dmdext-net8-win-x64/dmdext-proton.sh %command%
```

This works for the variables whose line at the top of the script has the form
`NAME="${NAME:-value}"`. When changing a value there, keep that form (e.g.
`BACKGLASS="${BACKGLASS:-true}"`); a plain `BACKGLASS=true` can't be changed from the launch
options anymore.

An **empty value** in the launch options changes nothing, since the script treats it as not set.
E.g. `NETWORK_HOST=` doesn't turn off the network if the top of the script sets a host. To blank
the DMD's idle image for one game, use `IDLE_PLAY=none`; the backglass has no equivalent.

### Paths under Wine

dmdext sees the Linux file system as Windows drives:

| Drive | Linux path |
|---|---|
| `Z:` | `/` (the script converts paths by itself) |
| `S:` | `~/.local/share/Steam` |

---

## 5. Backglass

With `--backglass`, dmdext opens a borderless window showing the image of the running table:

1. **With a table:** shows `<table name>.png` (or `.jpg`), e.g. `UNIVERSAL_Jaws.png`,
   `WMS_Indiana_Jones.png`. It's the same name that goes over the network as `gameName`.
2. **Without a table, or if the table has no image:** shows `--backglass-idle` if set. The
   script sets it from `BACKGLASS_IDLE` or `DEFAULT_IDLE_BACKGLASS_<app id>` (section 4).
3. **Without `--backglass-idle`:** shows `DEFAULT_IDLE.png` (or `.jpg`) from the image folder,
   if there is one. This sets an own image without touching the options.
4. **Without `DEFAULT_IDLE`:** shows the game's default image, `PinballFX3.png` for Pinball FX3
   and Classic.
5. **If that's missing too:** black.

**Image folder:** without `--backglass-path`, dmdext takes the folder of the game's executable
and uses `data\steam`, where the tables (`*.pxp`) are. On the cabinet:
`~/.local/share/Steam/steamapps/common/Pinball FX Classic/data/steam/`.

**Window behavior:**
- It never takes the focus from the game.
- It stays below the virtual DMD, which is always on top.
- It loads images in the background, without slowing down capturing.
- It scales the image to fit the window, keeping its aspect ratio, with black bars around it.
  Use images with the aspect ratio of the backglass area, e.g. 16:9 for 1920×1080.

| Option | What it does |
|---|---|
| `--backglass` | Enables the window |
| `--backglass-position <Left> <Top> <Width> <Height>` | Position and size. Default `0 0 1920 1080` |
| `--backglass-path <folder>` | Image folder |
| `--backglass-idle <image>` | Image while no table is loaded |

The images are **not sent over the network**. A remote receiver that wants to show the
backglass needs its own copy of the images, and looks them up by `gameName`.

---

## 6. Network stream

Protocol: dmdext's binary WebSocket, one message per `<name>\0<payload>`. What changes with
this fork:

| Message / situation | What arrives |
|---|---|
| A table is loaded | `gameName` with the name, e.g. `WMS_Medieval_Madness` |
| Back to the menu | Empty `gameName`, **only once** |
| Idle with `--idle-play` | One `rgb24` frame with the image, or several for an animated GIF |
| Idle without `--idle-play` (with the script: `IDLE_PLAY=none` or no idle image found) | One blank `gray2Planes` frame |
| Repeated frames | Not sent |
| dmdext exits | One blank `gray2Planes` frame, then the connection is closed |
| Reconnection (`--retry`) | The last `gameName`, `dimensions` and `color` are sent again |

An empty `gameName` means **"no table loaded"**. That's not the same as "the DMD didn't
change": while playing with a still DMD no frames arrive, but `gameName` keeps the table name.

---

## 7. Operating system: Game Mode vs. Desktop Mode

### Why Desktop Mode is needed

Bazzite (`bazzite-deck` image) can start two sessions:

| Session | What it is | Virtual DMD and backglass |
|---|---|---|
| **Game Mode** (`gamescope-session`) | Steam Deck-like interface, gamescope compositor | ❌ gamescope shows a single screen, so there's no place for those windows |
| **Desktop Mode** (`plasma`) | KDE Plasma desktop (KWin), multiple screens | ✅ Where everything was tested |

In Desktop Mode, Steam can open in the classic interface or in **Big Picture**; both work.

**In Game Mode (not tested):** a **network-only** setup could work (`VIRTUAL_DMD=false`,
`BACKGLASS=false`, `NETWORK_HOST` set). dmdext would send the DMD and the table name to another
device, e.g. a Raspberry Pi, which would show the DMD and the backglass with its own images.

### Choosing the login session

Run in a terminal on the cabinet (no password needed):

| Action | Command |
|---|---|
| Start in Desktop Mode | `ujust set-default-desktop` |
| Start in Game Mode | `ujust set-default-game-mode` |
| Show the configured session | `steamosctl get-default-login-mode` |

Internally, `ujust set-default-desktop` runs `steamosctl set-default-login-mode desktop`, which
sets `Session=plasma.desktop` in `/etc/sddm.conf.d/zz-holo-autologin.conf`.

⚠️ The desktop's **"Return to Gaming Mode"** shortcut makes Game Mode the login session again.
After using it, run `ujust set-default-desktop` again.

### Steam in Big Picture when the desktop starts

Bazzite opens Steam when the desktop starts through `/etc/xdg/autostart/steam.desktop`
(`bazzite-steam -silent`: classic view, minimized). A file with the same name in
`~/.config/autostart/` overrides the system one, and it's also the file that Steam's own
"run at startup" option manages.

On the `bazzite-deck` image the launcher adds `-steamdeck`, and Steam's option to start in Big
Picture wasn't applied. That's why `~/.config/autostart/steam.desktop` is replaced with:

```ini
[Desktop Entry]
Type=Application
Name=Steam
Comment=Opens Steam in Big Picture mode when the desktop starts. Overrides Bazzite's /etc/xdg/autostart/steam.desktop (bazzite-steam -silent).
Exec=/usr/bin/bazzite-steam steam://open/bigpicture
Icon=steam
Terminal=false
```

⚠️ What **not** to do:
- **Don't change Steam's "run at startup" option.**
  - Turning it off makes Steam delete this file, and the system's `-silent` autostart comes back.
  - Turning it on makes Steam rewrite it with its own version, which opens the classic view.
- **Don't add another autostart entry that opens Steam.** With two of them, two Steam clients
  start at once: one ends up without an interface, "steamwebhelper is not responding" shows up,
  and in one test the joystick took ~2 minutes to respond.

**To undo it:** delete `~/.config/autostart/steam.desktop`; the system's autostart comes back.

### Known system issues

- **Big Picture stuck on a black screen with "ABORT GAME":** happened when dmdext took long to
  exit (fixed). If it happens again and the controller doesn't respond, Big Picture can be
  closed with `steam steam://close/bigpicture`.
- **Steam stuck on "Launching…" with the screens turned off by power saving:** turn the screens
  on (`kscreen-doctor --dpms on`) or disable the desktop's power saving on the cabinet.
- **Processes started over SSH:** logind has `KillUserProcesses=true`, so they're closed when the
  SSH session ends. To test dmdext remotely, launch the game from Steam
  (`steam steam://rungameid/442120`) and let the script start dmdext.

---

## 8. Limitations and open issues

- **Only tested with Pinball FX Classic** under Proton.
- **Backglass under Wine:** leaves its monitor's last pixel row uncovered (section 3).
- **Virtual DMD under Wine:** can't be resized with the mouse; it's configured with options.
- **Remaining bugs of the original:**
  - The `dedupe` parameter of the Pinball FX3 grabber is ignored. No effect: it always dedupes now.
  - `NetworkStream` doesn't declare `IColoredGray6Destination`.
  - `coloredGray6` writes 24 padding bytes that its own deserializer doesn't skip.
  - `NetworkStream` is a singleton, and initializing it again doesn't dispose the previous client.
- **Help of `-s`:** still lists `propinball`, which isn't included.
- **License:** GPLv2, like the original. If you distribute the binary, you also have to provide
  the source code.
