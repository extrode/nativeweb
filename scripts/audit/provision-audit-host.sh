#!/usr/bin/env bash
# Provision the audit host. Run BY THE USER, ON audit-host — not by an agent, and not over ssh from a
# session. It installs software and loads a launchd agent, which is the user's call.
#
#   scp scripts/audit/provision-audit-host.sh audit-host.local:~/
#   ssh audit-host.local
#   ./provision-audit-host.sh              # dry run: prints every step, changes nothing
#   ./provision-audit-host.sh --execute    # does them
#
# What it establishes, and why an agent cannot:
#
#   R2  the audit runs unattended, daily, on a host that is not the workstation, and survives a
#       reboot. That is a property of launchd on this machine after this script has run.
#   R15 the audit host holds no production credential. The script asserts it; only someone with
#       the machine in front of them can keep it true.
#
# macOS 14.5, arm64. Verified present on audit-host: git 2.39.5, python3 3.9.6, launchd. Verified
# absent: deno, dotnet, claude, rg, node, pwsh. Only Deno is installed, to ~/.deno, by its own
# installer, which needs no administrator rights. Per the project's Deno-never-Node rule this
# introduces no package.json, no node_modules and no npm invocation.

set -euo pipefail

EXECUTE=0
ACCEPT_RISK=0
AUDIT_HOME="${AUDIT_HOME:-$HOME/audit}"
DENO_DIR="${DENO_INSTALL:-$HOME/.deno}"
LABEL="com.extrode.nativeweb.audit"
PLIST="$HOME/Library/LaunchAgents/${LABEL}.plist"
RUN_HOUR="${AUDIT_HOUR:-3}"

usage() {
  cat <<'EOF'
Usage: ./provision-audit-host.sh [--execute] [--hour <0-23>] [--accept-credential-risk]

  (no flags)                dry run: print every step, change nothing
  --execute, -e             install Deno, create the store, load the launchd agent
  --hour <0-23>             local hour to run the audit (default 3)
  --accept-credential-risk  proceed even though a production credential is present here

Idempotent: safe to re-run. Installs nothing but Deno, and creates nothing outside
~/audit, ~/.deno and one launchd plist. Refuses (exit 3) if this host holds a
production credential, because R15 says it must not.
EOF
}

while [ $# -gt 0 ]; do
  case "$1" in
    --execute|-e) EXECUTE=1 ;;
    --hour) RUN_HOUR="$2"; shift ;;
    --accept-credential-risk) ACCEPT_RISK=1 ;;
    --help|-h) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
  shift
done

say() { printf '%s\n' "$*"; }
step() { if [ "$EXECUTE" -eq 1 ]; then say "  doing : $*"; else say "  would : $*"; fi; }

if [ "$(uname -s)" != "Darwin" ]; then
  say "This script provisions a macOS host; uname says $(uname -s). Refusing."
  exit 2
fi

say "audit host  : $(hostname)"
say "store       : $AUDIT_HOME"
say "deno        : $DENO_DIR"
say "launchd     : $PLIST (daily at ${RUN_HOUR}:07 local)"
say ""

# --- 1 and 2. R15 and R1, before anything is installed ---------------------------------------

say "1. Credentials (R15)"
if [ -r "$HOME/.zai" ]; then
  say "  present : ~/.zai (the GLM key — the only secret this host needs)"
else
  say "  MISSING : ~/.zai. The reading pass will be skipped and the mechanical checks will still"
  say "            run. Put the GLM key there when you want the reading pass."
fi

# Anything here would be a production credential on a host whose whole job is to read code it
# does not trust. Reported, never removed: deleting a credential is the user's call, and this
# script does not clean up.
FOUND=0
for suspect in \
  "$HOME/.aws/credentials" \
  "$HOME/.pgpass" \
  "$HOME/.ssh/id_rsa_deploy" \
  "$HOME/.config/gh/hosts.yml" \
  "$HOME/.docker/config.json" \
  "$HOME/.npmrc" \
  "$HOME/.nuget/NuGet/NuGet.Config"; do
  if [ -e "$suspect" ]; then
    say "  FOUND   : $suspect — R15 says this host holds no production credential."
    FOUND=$((FOUND + 1))
  fi
done
if [ "$FOUND" -eq 0 ]; then
  say "  clear   : no production credential found in the usual places"
else
  say "  $FOUND credential(s) present."
  if [ "$ACCEPT_RISK" -eq 0 ]; then
    say ""
    say "Refusing to provision. R15 says the audit host holds no production credential, and this"
    say "host would be running code it has no reason to trust on a nightly timer. Move them off,"
    say "or re-run with --accept-credential-risk to proceed knowing what is here."
    exit 3
  fi
  say "  proceeding anyway: --accept-credential-risk was given."
fi

# The audit host must not be able to reach origin. If it can, the throwaway-clone argument for R1
# stops being structural and becomes a promise.
say "2. Reachability (R1)"
if ssh -o BatchMode=yes -o ConnectTimeout=5 -T git@github.com 2>&1 | grep -q "successfully authenticated"; then
  say "  REACHES : this host authenticates to github.com. R1's structural guarantee is weakened —"
  say "            the audit cannot write to the repository, but this host could. Remove the key."
else
  say "  clear   : this host does not authenticate to github.com"
fi

# --- 3. Deno ------------------------------------------------------------------------------------

say "3. Deno"
if [ -x "$DENO_DIR/bin/deno" ]; then
  say "  present : $("$DENO_DIR/bin/deno" --version | head -1)"
else
  step "install Deno to $DENO_DIR via https://deno.land/install.sh"
  if [ "$EXECUTE" -eq 1 ]; then
    curl -fsSL https://deno.land/install.sh | DENO_INSTALL="$DENO_DIR" sh -s -- --yes
  fi
fi

# --- 4. the store -------------------------------------------------------------------------------

say "4. Store"
for dir in mirror ledger runs reports; do
  if [ -d "$AUDIT_HOME/$dir" ]; then
    say "  present : $AUDIT_HOME/$dir"
  else
    step "mkdir -p $AUDIT_HOME/$dir"
    [ "$EXECUTE" -eq 1 ] && mkdir -p "$AUDIT_HOME/$dir"
  fi
done

# --- 5. the launchd agent -----------------------------------------------------------------------

say "5. Scheduler (R2)"
step "write $PLIST"
if [ "$EXECUTE" -eq 1 ]; then
  mkdir -p "$HOME/Library/LaunchAgents"
  cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>${LABEL}</string>
  <key>ProgramArguments</key>
  <array>
    <string>${DENO_DIR}/bin/deno</string>
    <string>run</string>
    <string>-A</string>
    <string>${AUDIT_HOME}/nativeweb/tools/audit/run.ts</string>
    <string>--store</string>
    <string>${AUDIT_HOME}</string>
  </array>
  <key>StartCalendarInterval</key>
  <dict>
    <key>Hour</key><integer>${RUN_HOUR}</integer>
    <key>Minute</key><integer>7</integer>
  </dict>
  <!-- Missed while the machine was asleep: run at load rather than skip the day. A skipped run
       is reported as a gap by the digest, which is correct but not as good as running. -->
  <key>RunAtLoad</key><true/>
  <key>StandardOutPath</key><string>${AUDIT_HOME}/runs/launchd.out.log</string>
  <key>StandardErrorPath</key><string>${AUDIT_HOME}/runs/launchd.err.log</string>
  <key>EnvironmentVariables</key>
  <dict>
    <key>HOME</key><string>${HOME}</string>
    <key>PATH</key><string>${DENO_DIR}/bin:/usr/bin:/bin:/usr/sbin:/sbin</string>
  </dict>
</dict>
</plist>
EOF
fi

step "launchctl bootout then bootstrap gui/$(id -u) $PLIST"
if [ "$EXECUTE" -eq 1 ]; then
  launchctl bootout "gui/$(id -u)/${LABEL}" 2>/dev/null || true
  launchctl bootstrap "gui/$(id -u)" "$PLIST"
  launchctl enable "gui/$(id -u)/${LABEL}"
fi

say ""
if [ "$EXECUTE" -eq 0 ]; then
  say "Dry run. Nothing was installed, created or loaded. Re-run with --execute."
  exit 0
fi

say "Provisioned. Check it took:"
say "  launchctl print gui/$(id -u)/${LABEL} | head -20"
say ""
say "The audit needs the repository mirror and a checkout of the tooling. From the workstation:"
say "  scripts/audit/sync-to-audit-host.sh --execute"
say "  ssh $(hostname -s) 'git clone -q ~/audit/mirror/nativeweb.git ~/audit/nativeweb'"
say ""
say "Then read it from the workstation, which writes nothing here:"
say "  deno run -A tools/audit/digest.ts --ssh $(hostname -s)"
