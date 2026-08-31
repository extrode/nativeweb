#!/usr/bin/env bash
#
# Proves db/migrate.sh serialises itself. Run it against a database that is
# already fully migrated; it applies nothing and only exercises the lock.
#
#   bash db/test-run-lock.sh
#
# Exits 0 when every check passes, 1 on the first that does not. Takes the same
# PGHOST/PGPORT/PGDATABASE/APP_ROLE environment the runner does -- and, since
# 2026-08-31, the same MIGRATIONS_DIR: this script drives migrate.sh rather than
# naming a schema itself, so it followed the runner's retarget onto
# db/sample/migrations/ without a change of its own.
#
# WHY A SHELL SCRIPT AND NOT A TEST IN tests/. This was first written as an
# xUnit fact in the integration tier and abandoned there, measured 2026-08-18:
# launching db/migrate.sh from .NET on Windows either hangs WaitForExitAsync for
# its full timeout (stdout redirected to a pipe: the runner writes "done" and
# exits, but the pipe never reaches EOF, because the coprocess psql inherits the
# handle) or produces an empty log and exit 1 (redirect written by bash itself).
# The same script from a shell exits 0 in ~37s. The harness was the problem, not
# the lock -- but a test whose failure mode is "three minutes, then a timeout
# that looks exactly like the bug" is worse than no test, so the verification
# lives here where it is deterministic.
set -euo pipefail

PGHOST="${PGHOST:-127.0.0.1}"
PGPORT="${PGPORT:-5432}"
PGDATABASE="${PGDATABASE:-nativeweb_dev}"
APP_ROLE="${APP_ROLE:-nativeweb_app}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUNNER="$SCRIPT_DIR/migrate.sh"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# The key the runner itself uses, read out of the runner. Hardcoding it here
# would let the two drift apart and leave this passing against a lock nothing
# else ever takes.
KEY_HEX="$(sed -nE 's/^RUN_LOCK_KEY=([0-9a-fA-F]+)[[:space:]]*$/\1/p' "$RUNNER" | head -1)"
if [ -z "$KEY_HEX" ]; then
  echo "FAIL: migrate.sh no longer declares RUN_LOCK_KEY as a hex literal" >&2
  exit 1
fi
KEY=$((0x$KEY_HEX))

q() { psql -h "$PGHOST" -p "$PGPORT" -U "$APP_ROLE" -d "$PGDATABASE" -w -Atqc "$1"; }

pass=0
fail=0
check() {
  if [ "$2" = "$3" ]; then
    echo "  ok   $1"
    pass=$((pass + 1))
  else
    echo "  FAIL $1: expected [$3], got [$2]" >&2
    fail=$((fail + 1))
  fi
}

echo "run lock: key 0x$KEY_HEX ($KEY) on $PGDATABASE"

# --- 1. A held lock makes the runner wait -----------------------------------
#
# Standing in for a first runner by holding its key, rather than launching two
# copies: with two real runners, which one wins is a coin toss, and the check
# would degrade to "one of them waited" instead of "this one did".
coproc HOLDER { psql -h "$PGHOST" -p "$PGPORT" -U "$APP_ROLE" -d "$PGDATABASE" -w -Atq -f - 2>&1; }
printf "DO \$h\$ BEGIN PERFORM pg_advisory_lock(%s); RAISE WARNING 'HELD'; END \$h\$;\n" "$KEY" >&"${HOLDER[1]}"
cr=$'\r'
while IFS= read -r -t 30 line <&"${HOLDER[0]}"; do
  line="${line%"$cr"}"
  case "$line" in *HELD*) break ;; esac
done

bash "$RUNNER" </dev/null >"$WORK/waiting.log" 2>&1 &
runner_pid=$!

waited=no
for _ in $(seq 1 100); do
  if grep -q '^wait ' "$WORK/waiting.log" 2>/dev/null; then waited=yes; break; fi
  sleep 0.2
done

# It must SAY it is queuing. A runner that blocks silently is indistinguishable
# from one that has hung, and an operator mid-cutover will kill it.
check "the second runner announces that it is queuing" "$waited" yes

# And it must still be waiting: reaching "done" here would mean the lock was not
# honoured at all, which is the whole defect.
still_running=no
kill -0 "$runner_pid" 2>/dev/null && still_running=yes
check "the second runner is still blocked while the lock is held" "$still_running" yes
check "the second runner has not finished" "$(grep -c '^done$' "$WORK/waiting.log" || true)" 0

# --- 2. Releasing lets it through -------------------------------------------
printf '\\q\n' >&"${HOLDER[1]}" || true
wait "$HOLDER_PID" 2>/dev/null || true

runner_status=0
wait "$runner_pid" || runner_status=$?
check "the second runner completes once the lock is released" "$runner_status" 0
check "the second runner reports done" "$(grep -c '^done$' "$WORK/waiting.log" || true)" 1

# Anti-vacuity: it really did walk the migration list rather than exiting early
# on something unrelated that happened to return 0.
skips="$(grep -c '^skip ' "$WORK/waiting.log" || true)"
check "the second runner walked the migration list" "$([ "$skips" -gt 0 ] && echo yes || echo no)" yes

# --- 3. The lock does not outlive the run -----------------------------------
#
# A session-level advisory lock lives and dies with its connection, so a runner
# that left its holder open would block every later run against this database
# until someone went looking in pg_locks.
# No matching unlock: q() opens its own psql per call, so the lock this takes is
# released when that session ends a moment later. Calling pg_advisory_unlock from
# the NEXT connection would only warn that it does not own one.
check "the lock is released when the runner exits" "$(q "SELECT pg_try_advisory_lock($KEY)")" t

echo ""
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
