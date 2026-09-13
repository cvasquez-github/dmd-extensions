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

# backglass window showing <table name>.png (or .jpg), e.g. WMS_Indiana_Jones.png
BACKGLASS=false

# screen area of the backglass, in desktop pixels. each screen's position is shown as
# "Geometry" by "kscreen-doctor -o". the virtual DMD is placed relative to it.
BACKGLASS_X=0
BACKGLASS_Y=0
BACKGLASS_WIDTH=1920
BACKGLASS_HEIGHT=1080

# folder with the backglass images. leave empty to use the game's data/steam folder,
# where the tables are.
BACKGLASS_PATH=""

# image shown in the backglass window while no table is loaded, or if a table has no
# image. leave empty to use the game's default image from the backglass folder
# (PinballFX3.png for Pinball FX Classic), or black if there's none.
BACKGLASS_IDLE=""

# virtual DMD window: its width in desktop pixels, the black border around the dots
# (in dots), and the space below it, to the bottom of the backglass area. the height
# follows from the DMD's aspect ratio, and it's centered on the backglass area.
DMD_WIDTH=736
DMD_PADDING=3
DMD_BOTTOM_MARGIN=12
DMD_HEIGHT=$(( (DMD_WIDTH * (32 + 2 * DMD_PADDING) + 64 + DMD_PADDING) / (128 + 2 * DMD_PADDING) ))
DMD_X=$((BACKGLASS_X + (BACKGLASS_WIDTH - DMD_WIDTH) / 2))
DMD_Y=$((BACKGLASS_Y + BACKGLASS_HEIGHT - DMD_BOTTOM_MARGIN - DMD_HEIGHT))

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
	OUTPUT_ARGS+=(-d virtual --virtual-position "$DMD_X" "$DMD_Y" "$DMD_WIDTH" "$DMD_HEIGHT" --virtual-stay-on-top
		--virtual-frame-padding "$DMD_PADDING" "$DMD_PADDING" "$DMD_PADDING" "$DMD_PADDING")
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

	BACKGLASS_ARGS=()
	if [ "$BACKGLASS" = true ]; then
		BACKGLASS_ARGS=(--backglass --backglass-position "$BACKGLASS_X" "$BACKGLASS_Y" "$BACKGLASS_WIDTH" "$BACKGLASS_HEIGHT")
		if [ -n "$BACKGLASS_PATH" ]; then
			BACKGLASS_ARGS+=(--backglass-path "$(win_path "$BACKGLASS_PATH")")
		fi
		if [ -n "$BACKGLASS_IDLE" ]; then
			if [ -f "$BACKGLASS_IDLE" ]; then
				BACKGLASS_ARGS+=(--backglass-idle "$(win_path "$BACKGLASS_IDLE")")
			else
				echo "Backglass idle image $BACKGLASS_IDLE not found, the backglass will be black." >> "$LOG"
			fi
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
			echo "Starting dmdext: mirror -s $SOURCE -q ${OUTPUT_ARGS[*]} ${IDLE_ARGS[*]} ${BACKGLASS_ARGS[*]} ${EXTRA_ARGS[*]}" >> "$LOG"
			client --bus-name="$BUS_NAME" -- \
				env WINEDEBUG=-all wine "$DMDEXT" mirror -s "$SOURCE" -q "${OUTPUT_ARGS[@]}" "${IDLE_ARGS[@]}" "${BACKGLASS_ARGS[@]}" "${EXTRA_ARGS[@]}" \
				< /dev/null >> "$LOG" 2>&1
			exit
		fi
		sleep 1
	done
	echo "Timed out waiting for $BUS_NAME." >> "$LOG"
) &

wait "$GAME_PID"
