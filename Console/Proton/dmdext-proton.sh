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

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# --- settings ----------------------------------------------------------------

# dmdext mirror source: pinballfxclassic (also Pinball FX3) or pinballfx2
SOURCE="pinballfxclassic"

# show the virtual DMD on this machine (true or false)
VIRTUAL_DMD=true

# position of the virtual DMD's top-left corner, in desktop pixels. each screen's
# position is shown as "Geometry" by "kscreen-doctor -o".
DMD_X=1920
DMD_Y=0

# width of the virtual DMD in pixels, the height follows the DMD's aspect ratio
DMD_WIDTH=1280

# stream the DMD over the network to another machine (dmdext's WebSocket network
# stream, e.g. to a receiver on a Raspberry Pi). leave NETWORK_HOST empty to disable.
NETWORK_HOST=""
NETWORK_PORT=8080
NETWORK_PATH="/dmd"

# image (png, jpg or gif) shown while no table is loaded, e.g. in the game's menus.
# it goes to all outputs, including the network, where receivers also get an empty
# game name. by default dmdext's test image. leave empty to just clear the DMD.
IDLE_PLAY="$SCRIPT_DIR/idle.png"

# additional dmdext arguments, e.g. EXTRA_ARGS=(--virtual-dot-glow 0.5)
EXTRA_ARGS=()

LOG="$HOME/dmdext-mirror.log"

# --- end of settings ---------------------------------------------------------

DMDEXT="$SCRIPT_DIR/dmdext.exe"
APP_ID="${SteamAppId:-${STEAM_COMPAT_APP_ID:-442120}}"
BUS_NAME="com.steampowered.App${APP_ID}"

# dmdext runs under Wine, which sees the Linux file system as drive Z:
win_path() {
	printf 'Z:%s' "${1//\//\\}"
}

# outputs: the virtual DMD placed at the given position and kept on top, and/or the
# network stream, which keeps retrying so the receiver can start at any time.
OUTPUT_ARGS=()
if [ "$VIRTUAL_DMD" = true ]; then
	OUTPUT_ARGS+=(-d virtual --virtual-position "$DMD_X" "$DMD_Y" "$DMD_WIDTH" --virtual-stay-on-top)
elif [ -n "$NETWORK_HOST" ]; then
	OUTPUT_ARGS+=(-d network)
fi
if [ -n "$NETWORK_HOST" ]; then
	OUTPUT_ARGS+=("--url=ws://$NETWORK_HOST:$NETWORK_PORT$NETWORK_PATH" --retry)
fi

# makes Proton expose a service that runs commands inside the game's session
export STEAM_COMPAT_LAUNCHER_SERVICE=proton

"$@" &
GAME_PID=$!

(
	: > "$LOG"

	if [ ${#OUTPUT_ARGS[@]} -eq 0 ]; then
		echo "Nothing to output: enable VIRTUAL_DMD or set NETWORK_HOST." >> "$LOG"
		exit 1
	fi

	IDLE_ARGS=()
	if [ -n "$IDLE_PLAY" ]; then
		if [ -f "$IDLE_PLAY" ]; then
			IDLE_ARGS=(--idle-play "$(win_path "$IDLE_PLAY")")
		else
			echo "Idle image $IDLE_PLAY not found, the DMD will just be cleared." >> "$LOG"
		fi
	fi

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
			echo "Starting dmdext: mirror -s $SOURCE -q ${OUTPUT_ARGS[*]} ${IDLE_ARGS[*]} ${EXTRA_ARGS[*]}" >> "$LOG"
			client --bus-name="$BUS_NAME" -- \
				env WINEDEBUG=-all wine "$DMDEXT" mirror -s "$SOURCE" -q "${OUTPUT_ARGS[@]}" "${IDLE_ARGS[@]}" "${EXTRA_ARGS[@]}" \
				< /dev/null >> "$LOG" 2>&1
			exit
		fi
		sleep 1
	done
	echo "Timed out waiting for $BUS_NAME." >> "$LOG"
) &

wait "$GAME_PID"
