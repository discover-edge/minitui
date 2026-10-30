#!/usr/bin/env bash
# Start an SSH server in Docker with the MiniTui Example in it, for manual QA over SSH (see README.md).
#
#   tools/ssh-test/run.sh                 publish the Example and start the server on localhost:2222
#   tools/ssh-test/run.sh --latency 100   the same, with 100 ms of network delay (and 20 ms of jitter)
#   tools/ssh-test/run.sh --no-build      start with the Example as last published
#   tools/ssh-test/run.sh stop            stop the server
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
name=minitui-ssh-test
port="${MINITUI_SSH_PORT:-2222}"
latency=0
build=1

while [ $# -gt 0 ]; do
    case "$1" in
        stop) docker rm -f "$name" >/dev/null 2>&1 && echo "Stopped $name." || echo "$name isn't running."; exit 0 ;;
        --latency) latency="$2"; shift 2 ;;
        --no-build) build=0; shift ;;
        -h|--help) sed -n '2,8p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

if [ "$build" = 1 ]; then
    echo "Publishing the Example for linux-x64..."
    dotnet publish "$root/src/MiniTui.Example" -c Release -r linux-x64 --self-contained -o "$here/out" -v quiet -nologo
fi
[ -x "$here/out/MiniTui.Example" ] || { echo "No published Example in $here/out; run without --no-build." >&2; exit 1; }

docker build -q -t "$name" "$here" >/dev/null
docker rm -f "$name" >/dev/null 2>&1 || true
docker run -d --rm --name "$name" \
    --cap-add NET_ADMIN \
    -e LATENCY_MS="$latency" -e JITTER_MS="$([ "$latency" -gt 0 ] && echo 20 || echo 0)" \
    -p "127.0.0.1:$port:22" \
    -v "$here/out:/opt/minitui:ro" \
    "$name" >/dev/null

# Wait for sshd to accept connections
for _ in $(seq 50); do
    docker logs "$name" 2>&1 | grep -q "Server listening" && break
    sleep 0.1
done

cat <<MSG
SSH test server is running on localhost:$port$([ "$latency" -gt 0 ] && echo " with $latency ms delay").
Password: test

  From WSL or Linux:        ssh -p $port -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null test@localhost
  From Windows PowerShell:  ssh -p $port -o StrictHostKeyChecking=no -o UserKnownHostsFile=NUL test@localhost

Then run:  MiniTui.Example
Stop with: tools/ssh-test/run.sh stop
MSG
