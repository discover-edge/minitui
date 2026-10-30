# SSH test server

A throwaway SSH server in Docker with the MiniTui Example in it, for checking MiniTui over SSH: input passes
through sshd's pseudo-terminal, and with `--latency` key sequences and pastes arrive split across packets, as
on a slow link. Local testing only: it listens on `127.0.0.1` and the login is `test` / `test`.

Needs Docker and the .NET 10 SDK. Run from the repository root, in WSL or on Linux:

```bash
tools/ssh-test/run.sh                 # publish the Example, start the server on localhost:2222
tools/ssh-test/run.sh --latency 100   # the same, with 100 ms of network delay and 20 ms of jitter
tools/ssh-test/run.sh --no-build      # restart without republishing
tools/ssh-test/run.sh stop
```

Then connect (password `test`) and run `MiniTui.Example`:

```bash
ssh -p 2222 -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null test@localhost      # WSL or Linux
ssh -p 2222 -o StrictHostKeyChecking=no -o UserKnownHostsFile=NUL test@localhost            # Windows PowerShell
```

Each container gets a new host key, which is why the commands don't save it. `MINITUI_SSH_PORT` changes the
port. The Example is mounted from `tools/ssh-test/out`, so `run.sh` picks up a rebuilt Example without
rebuilding the image. `tmux` is installed in the container too.

## Checks

The input checks from `docs/SPEC-1.x.md` §2.9 and §3.9, best run once without and once with `--latency 100`:

1. **Arrow burst:** on List Selection, hold ↓ (or `tmux send-keys -N 2000 Down`). It never leaves the screen.
2. **Keys:** the Input demo shows Alt+letter, Ctrl/Shift/Alt arrows, Shift+Tab, F1–F12, Home/End/PgUp/PgDn,
   Insert and Delete as named, and Esc responds without a noticeable delay.
3. **Paste:** multi-line text pasted into a Form field goes in as one line without submitting; the Input demo
   shows one `Paste` entry.
4. **Restore:** run `stty -a > before.txt` first. After quitting with q, Ctrl+C, and
   `docker exec minitui-ssh-test pkill -TERM -f MiniTui.Example` (and `-HUP`), `stty -a | diff before.txt -`
   prints nothing, and a paste into `cat -v` shows no `^[[200~`.
