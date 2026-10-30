#!/bin/sh
set -e

# A fresh host key per container; run.sh tells ssh not to remember it
ssh-keygen -A >/dev/null

# Optional network delay, so key sequences and pastes arrive split across packets as on a slow link
if [ "${LATENCY_MS:-0}" -gt 0 ]; then
    tc qdisc add dev eth0 root netem delay "${LATENCY_MS}ms" "${JITTER_MS:-0}ms"
    echo "Added ${LATENCY_MS} ms delay (jitter ${JITTER_MS:-0} ms) on eth0"
fi

exec /usr/sbin/sshd -D -e
