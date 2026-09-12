#!/usr/bin/env bash
#
# Steam launch wrapper that starts a Pinball FX game under Proton together with
# "dmdext mirror", both inside the same Proton session, so dmdext can read the
# game's memory. dmdext quits when the game exits, so Steam doesn't keep the
# game marked as running.
#
# Setup:
#   1. Keep this script next to dmdext.exe and make it executable:
#        chmod +x dmdext-proton.sh
#   2. Set the game's launch options in Steam to:
#        /full/path/to/dmdext-proton.sh %command%
#
# dmdext's output goes to ~/dmdext-mirror.log.

# --- settings ----------------------------------------------------------------

# dmdext mirror source: pinballfxclassic (also Pinball FX3) or pinballfx2
SOURCE="pinballfxclassic"

# position of the virtual DMD's top-left corner, in desktop pixels. each screen's
# position is shown as "Geometry" by "kscreen-doctor -o".
DMD_X=1920
DMD_Y=0

# width of the virtual DMD in pixels, the height follows the DMD's aspect ratio
DMD_WIDTH=1280

# additional dmdext arguments, e.g. EXTRA_ARGS=(--virtual-dot-glow 0.5)
EXTRA_ARGS=()

LOG="$HOME/dmdext-mirror.log"

# --- end of settings ---------------------------------------------------------

DMDEXT="$(cd "$(dirname "$0")" && pwd)/dmdext.exe"
APP_ID="${SteamAppId:-${STEAM_COMPAT_APP_ID:-442120}}"
BUS_NAME="com.steampowered.App${APP_ID}"

# the virtual DMD is placed at the given position and kept on top of other windows
DMD_ARGS=(--virtual-position "$DMD_X" "$DMD_Y" "$DMD_WIDTH" --virtual-stay-on-top)

# makes Proton expose a service that runs commands inside the game's session
export STEAM_COMPAT_LAUNCHER_SERVICE=proton

"$@" &
GAME_PID=$!

(
	: > "$LOG"

	CLIENT=""
	for candidate in "$HOME"/.local/share/Steam/steamapps/common/SteamLinuxRuntime_*/pressure-vessel/bin/steam-runtime-launch-client; do
		if [ -x "$candidate" ]; then
			CLIENT="$candidate"
			break
		fi
	done
	if [ -z "$CLIENT" ]; then
		echo "steam-runtime-launch-client not found." >> "$LOG"
		exit 1
	fi

	# the client runs on the host, so drop the libraries Steam injects for the game
	client() {
		env -u LD_PRELOAD -u LD_LIBRARY_PATH "$CLIENT" "$@"
	}

	# wait up to two minutes for the game's session to come up
	for _ in $(seq 120); do
		if client --list 2>/dev/null | grep -qx -- "--bus-name=$BUS_NAME"; then
			echo "Starting dmdext: mirror -s $SOURCE -q ${DMD_ARGS[*]} ${EXTRA_ARGS[*]}" >> "$LOG"
			client --bus-name="$BUS_NAME" -- \
				env WINEDEBUG=-all wine "$DMDEXT" mirror -s "$SOURCE" -q "${DMD_ARGS[@]}" "${EXTRA_ARGS[@]}" \
				< /dev/null >> "$LOG" 2>&1
			exit
		fi
		sleep 1
	done
	echo "Timed out waiting for $BUS_NAME." >> "$LOG"
) &

wait "$GAME_PID"