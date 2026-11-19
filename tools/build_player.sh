#!/usr/bin/env bash
# Builds the Windows player headlessly. Unity Editor must be CLOSED.
#
# Success = the literal "PlayerBuilder: Succeeded" line in the log. The old
# version piped Unity through `tail -20` and then read $? (tail's exit, not
# Unity's) and checked that AlienSkies.exe exists (it always does — stale
# builds leave one behind). Three ways to call a broken build "build ok";
# every phase of the pivot had to re-verify builds by hand because of it.
set -u
UNITY="/mnt/c/Program Files/Unity/Hub/Editor/6000.5.1f1/Editor/Unity.exe"
cd "$(dirname "$0")/.."
LOG_WSL="TestResults/build_player.log"
LOG_WIN='C:\Users\computer\My project\TestResults\build_player.log'
mkdir -p TestResults

timeout -k 30 1150 "$UNITY" -batchmode -quit \
  -projectPath "C:\\Users\\computer\\My project" \
  -executeMethod AlienSkies.EditorTools.PlayerBuilder.Build \
  -logFile "$LOG_WIN"
code=$?
[ $code -eq 124 ] && echo "build TIMED OUT" && exit 124

if grep -q "PlayerBuilder: Succeeded" "$LOG_WSL"; then
  echo "build ok ($(grep -m1 'PlayerBuilder: Succeeded' "$LOG_WSL"))"
else
  echo "build FAILED: no 'PlayerBuilder: Succeeded' in $LOG_WSL (unity exit $code). Errors:"
  grep -m5 "error CS" "$LOG_WSL"
  tail -10 "$LOG_WSL"
  exit 1
fi

# Restore the soundtrack: rebuilds recreate StreamingAssets, wiping any
# tracks dropped into the previous build. Masters live outside the repo.
MUSIC_MASTERS="/mnt/c/Users/computer/Music/AlienSkies"
if [ -d "$MUSIC_MASTERS" ]; then
  mkdir -p "Builds/AlienSkies_Data/StreamingAssets/Music"
  cp -n "$MUSIC_MASTERS"/*.ogg "$MUSIC_MASTERS"/*.wav \
    "Builds/AlienSkies_Data/StreamingAssets/Music/" 2>/dev/null
  echo "music: $(ls "Builds/AlienSkies_Data/StreamingAssets/Music" | grep -c -E '\.(ogg|wav)$') tracks installed"
fi
