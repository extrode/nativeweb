#!/usr/bin/env bash
#
# Applies a directory of NNNN_*.sql migrations to a database in order, tracking
# applied versions in schema_migrations so re-runs skip what's already there.
#
# RETARGETED 2026-08-31. It used to resolve db/migrations/, which left with the
# identity provider (docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md), and
# it named 0002_extensions.sql unconditionally -- so every run aborted on a
# missing file, taking db/test-run-lock.sh with it. It now defaults to
# db/sample/migrations/, takes MIGRATIONS_DIR for anything else, and treats the
# superuser extensions step as optional: a schema that needs no extension has no
# 0002 and needs no superuser at all.
#
# It was kept rather than retired because the locking design below is the part
# worth having, and skeleton/db/migrate.sh -- the copy a stamped product gets --
# was derived from it.
#
# Env (all optional; the defaults are the local dev setup):
#   PGHOST            default 127.0.0.1
#   PGPORT            default 5432
#   PGDATABASE        default nativeweb_dev -- this repo provisions its own
#                     database (never epass.io's live one); the test tiers'
#                     fallback connection strings assume the same name
#   APP_ROLE          default nativeweb_app -- PGPASSWORD (or ~/.pgpass) must
#                     authenticate this role for every migration except the
#                     NNNN_extensions.sql one
#   SUPERUSER_ROLE    default postgres -- must authenticate for NNNN_extensions.sql
#                     only, and only if the directory has one (it creates
#                     extensions, which the app role cannot)
#   MIGRATIONS_DIR    default db/sample/migrations -- the directory to apply
#
# Each migration is applied together with its schema_migrations row in a single
# transaction, so a crash cannot commit one without the other. See mode_of()
# below for the two kinds of file that cannot be wrapped and why.
#
# ONE THING THIS MADE WORSE, and it matters at the epass.io cutover: locks are now
# held for the whole file rather than released statement by statement. Under the
# old autocommit behaviour a multi-ALTER TABLE migration took and released each
# lock in turn; now every lock the file takes is held until its last statement
# commits. Against an empty or idle database that is free. Against a live one it
# blocks readers and writers for the duration of the file and widens the window
# for a deadlock with application traffic. Atomicity is worth that trade -- a
# half-applied migration is unrecoverable without hand-editing schema_migrations,
# whereas a lock wait is visible and self-resolving -- but if this is ever pointed
# at a database serving traffic, split long migrations rather than assuming the
# transaction is free. Raised by Fable in review, 2026-08-17.
#
# CREATE INDEX CONCURRENTLY migrations (e.g. 0025) require autocommit with no
# wrapping transaction, so `--single-transaction` must never be applied to them
# and mode_of() detects them by content. Do not pass `-1` / `--single-transaction`
# globally or to app_psql/super_psql themselves; it belongs only on the one
# invocation in apply_and_record's `wrapped` arm.

set -euo pipefail

PGHOST="${PGHOST:-127.0.0.1}"
PGPORT="${PGPORT:-5432}"
PGDATABASE="${PGDATABASE:-nativeweb_dev}"
APP_ROLE="${APP_ROLE:-nativeweb_app}"
SUPERUSER_ROLE="${SUPERUSER_ROLE:-postgres}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MIGRATIONS_DIR="${MIGRATIONS_DIR:-$SCRIPT_DIR/sample/migrations}"

if [ ! -d "$MIGRATIONS_DIR" ]; then
  echo "ERROR: no migrations directory at $MIGRATIONS_DIR." >&2
  echo "       Pass MIGRATIONS_DIR=<dir> for the schema you mean to apply." >&2
  exit 1
fi

# An empty directory is not "nothing to do": every glob below would expand to the
# literal pattern and psql would be handed a filename with brackets in it.
if ! compgen -G "$MIGRATIONS_DIR/[0-9][0-9][0-9][0-9]_*.sql" >/dev/null; then
  echo "ERROR: $MIGRATIONS_DIR contains no NNNN_*.sql migrations." >&2
  exit 1
fi

app_psql() {
  psql -h "$PGHOST" -p "$PGPORT" -U "$APP_ROLE" -d "$PGDATABASE" -v ON_ERROR_STOP=1 "$@"
}

super_psql() {
  psql -h "$PGHOST" -p "$PGPORT" -U "$SUPERUSER_ROLE" -d "$PGDATABASE" -v ON_ERROR_STOP=1 "$@"
}

version_of() {
  basename "$1" | sed -E 's/^([0-9]{4})_.*/\1/'
}

# --- Run lock ---------------------------------------------------------------
#
# ONE runner at a time, for the whole run. Without this, two concurrent runs
# both pass is_tracked before either records, both apply the file, and the
# damage depends on what kind of migration it was: loud for DDL, SILENT for a
# data migration. That is the class this runner exists to protect, and 0054 is
# the worst case -- re-running it would hash already-hashed session ids and sign
# every user out permanently. It guards itself, which made this a gap rather
# than a live bug. Found by Fable, 2026-08-17.
#
# Not a problem with one operator on one machine, which is why it waited. It
# becomes one the moment a deploy pipeline can run two jobs at once.
#
# WHY A COPROCESS. A session-level advisory lock lives and dies with its
# connection, and every other psql call here opens its own. So the lock needs a
# connection that outlives them all: a coprocess psql reading SQL from a pipe,
# held open for the whole run and closed by the EXIT trap. A transaction-scoped
# lock inside apply_and_record would not do -- by the time a runner reaches the
# apply it has ALREADY decided the migration is untracked, so serialising there
# only queues the second apply rather than preventing it.
#
# Advisory locks are per-database, so the key is a constant: two runs against
# different databases do not block each other, and two against the same one do,
# which is exactly the boundary that matters.
RUN_LOCK_KEY=6f1a2b3c
RUN_LOCK_KEY=$((0x$RUN_LOCK_KEY))
RUN_LOCK_IN=''
RUN_LOCK_OUT=''
# `coproc RUN_LOCK` sets RUN_LOCK_PID itself; declared here only so the EXIT trap
# can test it before the coprocess exists.
RUN_LOCK_PID=''

# Markers come back on STDERR, via RAISE WARNING, and that is not a stylistic
# choice. psql's stdout is block-buffered when it is a pipe, so a `\echo` or a
# SELECT result sits in the buffer and the read below blocks forever -- measured,
# 2026-08-18. stderr is unbuffered, so a warning arrives as soon as it is
# raised.
#
# The CR strip is the other half of that measurement: psql on Windows terminates
# lines with CRLF, so `read` yields "RUN_LOCK_HELD\r" and an equality test
# against the bare marker silently never matches. It cost an afternoon once.
run_lock_marker() {
  local want="$1" timeout="$2" line cr
  cr=$'\r'
  while IFS= read -r -t "$timeout" line <&"$RUN_LOCK_OUT"; do
    line="${line%"$cr"}"
    case "$line" in
      *"$want"*) return 0 ;;
    esac
  done
  return 1
}

acquire_run_lock() {
  coproc RUN_LOCK { app_psql -Atq -f - 2>&1; }
  RUN_LOCK_IN=${RUN_LOCK[1]}
  RUN_LOCK_OUT=${RUN_LOCK[0]}
  trap release_run_lock EXIT

  # Try first, so the common case says nothing and the contended case explains
  # itself rather than looking like a hang.
  printf "DO \$lock\$ BEGIN IF pg_try_advisory_lock(%s) THEN RAISE WARNING 'NW_RUN_LOCK_HELD'; ELSE RAISE WARNING 'NW_RUN_LOCK_BUSY'; END IF; END \$lock\$;\n" \
    "$RUN_LOCK_KEY" >&"$RUN_LOCK_IN"

  local line cr status=busy
  cr=$'\r'
  while IFS= read -r -t 30 line <&"$RUN_LOCK_OUT"; do
    line="${line%"$cr"}"
    case "$line" in
      *NW_RUN_LOCK_HELD*) status=held; break ;;
      *NW_RUN_LOCK_BUSY*) status=busy; break ;;
    esac
  done

  if [ "$status" = busy ]; then
    echo "wait  another migrate.sh is running against $PGDATABASE; queuing behind it"
    printf "DO \$lock\$ BEGIN PERFORM pg_advisory_lock(%s); RAISE WARNING 'NW_RUN_LOCK_HELD'; END \$lock\$;\n" \
      "$RUN_LOCK_KEY" >&"$RUN_LOCK_IN"
    # No timeout here: waiting is the correct behaviour, and a runner that gave
    # up after N seconds would go on to do the unserialised thing this exists to
    # prevent. A wait that is too long is an operator question; a wait that
    # expires into a double-apply is a corrupted database.
    if ! run_lock_marker NW_RUN_LOCK_HELD 86400; then
      echo "ERROR: lost the connection holding the migration run lock while waiting." >&2
      exit 1
    fi
  fi
}

# Releasing by asking psql to quit, rather than by closing a file descriptor:
# bash will not take an array element as the {var} of an exec redirection, and
# killing it would leave the exit status ambiguous. `\q` ends the session, which
# is what actually drops the lock.
release_run_lock() {
  local held_status=0
  [ -n "$RUN_LOCK_PID" ] || return 0

  # If the holder died during the run, every migration after that point ran
  # unserialised. Say so: the run may have been fine, but "probably fine" is not
  # what this lock is for.
  if ! kill -0 "$RUN_LOCK_PID" 2>/dev/null; then
    echo "ERROR: the connection holding the migration run lock died before the run" >&2
    echo "       finished, so some migrations were applied without it. Check for a" >&2
    echo "       concurrent runner before trusting this database." >&2
    held_status=1
  else
    printf '\\q\n' >&"$RUN_LOCK_IN" 2>/dev/null || true
    wait "$RUN_LOCK_PID" 2>/dev/null || true
  fi

  RUN_LOCK_PID=''
  return "$held_status"
}

# Runs in an `if` context, which suppresses set -e, so the psql exit status has to
# be checked by hand. Without that a connection blip returns an empty count, reads
# as "not tracked", and re-applies a migration that was already applied -- the very
# double-apply the transaction work below exists to prevent, reachable from a
# network hiccup. Fable flagged it during review of that work, 2026-08-17.
is_tracked() {
  local version="$1" count status
  count="$(app_psql -Atqc "SELECT count(*) FROM schema_migrations WHERE version = '$version'")" || status=$?
  if [ -n "${status:-}" ] || [ -z "$count" ]; then
    echo "ERROR: could not read schema_migrations while checking $version." >&2
    echo "       Refusing to continue: an unreadable tracking table is indistinguishable" >&2
    echo "       from an empty one, and guessing means re-applying migrations." >&2
    exit 1
  fi
  [ "$count" = "1" ]
}

record() {
  app_psql -c "INSERT INTO schema_migrations (version) VALUES ('$1') ON CONFLICT (version) DO NOTHING;" >/dev/null
}

# Deliberately a PLAIN insert, where record() below uses ON CONFLICT DO NOTHING.
#
# This one runs inside the wrap, and that changes what the primary key is worth.
# Two runners racing both pass is_tracked before either commits, so both apply
# the file. With ON CONFLICT the loser's INSERT quietly does nothing, its
# transaction commits, and both exit 0 having applied the same migration twice --
# loud for DDL, SILENT for a data migration, which is the exact class this runner
# exists to protect. With a plain INSERT the loser hits the unique constraint,
# and because it is inside the wrap the whole transaction rolls back: the second
# apply is undone atomically and the run exits non-zero.
#
# So the conflict clause was not protection here, it was the thing suppressing
# the only symptom. is_tracked has already filtered out versions that are
# genuinely applied, so the only conflict reachable from this path IS the race.
# Fable, fifth pass.
record_sql() {
  printf "\nINSERT INTO schema_migrations (version) VALUES ('%s');\n" "$1"
}

# Asks the SERVER whether the wrap survived, rather than reading its log.
#
# The transaction id is captured before the migration runs and compared after it.
# Any transaction control inside the file -- BEGIN, COMMIT, END, ROLLBACK, ABORT,
# COMMIT AND CHAIN, whatever spelling, in whatever place a text scan failed to
# look -- ends or replaces the transaction, so the id changes and this raises. A
# ROLLBACK instead discards the temp table, and the missing relation raises too.
#
# This replaced matching psql's stderr for "already a transaction in progress".
# Two reasons, both found the hard way:
#   * Those strings are localised by the server's lc_messages, so on a non-English
#     server the check would silently never fire. The obvious fix -- forcing
#     lc_messages=C through PGOPTIONS -- is not available: it is a superuser-only
#     parameter, and nativeweb_app setting it makes every connection fail outright
#     with "permission denied to set parameter". Measured, not assumed.
#   * COMMIT AND CHAIN produces NO warning at all, so no amount of log reading
#     could ever have caught it.
# A transaction id is a number the server computes. It is not translated, not
# suppressible, and not dependent on this script parsing SQL correctly.
xact_guard_open_sql() {
  printf "\nCREATE TEMP TABLE _migrate_xact_guard AS SELECT txid_current() AS xid;\n"
}

xact_guard_close_sql() {
  printf '%s\n' "" \
    "DO \$guard\$ BEGIN" \
    "  -- to_regclass('pg_temp....') resolves through THIS session's temp schema." \
    "  -- A bare pg_class lookup on relname matches another session's temp table of" \
    "  -- the same name, so two concurrent runs would hide each other's rollback." \
    "  IF to_regclass('pg_temp._migrate_xact_guard') IS NULL THEN" \
    "    RAISE EXCEPTION 'migration rolled back the transaction this runner opened';" \
    "  END IF;" \
    "  IF (SELECT xid FROM _migrate_xact_guard) <> txid_current() THEN" \
    "    RAISE EXCEPTION 'migration ran transaction control; the wrap was broken and this file was NOT applied atomically';" \
    "  END IF;" \
    "END \$guard\$;"
}

# How a migration must be applied. The default is "wrapped": the file and its
# schema_migrations row go to psql as ONE --single-transaction unit, so either
# both land or neither does.
#
# That matters because the two used to be separate psql invocations, which meant
# separate connections and separate transactions. A crash in the gap committed
# the migration and lost the tracking, so the next run applied it a second time.
# Harmless for DDL, which is every migration below 0054; not harmless for a data
# migration, and 0055 is one. 0055 survives a double-apply only because its
# WHERE NOT EXISTS guard was written for re-runnability on upgrade -- that is
# luck, not design, and the next data migration need not be so lucky.
#
# A migration that cannot be wrapped must SAY SO, with `-- @no-transaction` on a
# line of its own. Two kinds of file need it:
#
#   * CREATE INDEX CONCURRENTLY, VACUUM, ALTER SYSTEM, CREATE DATABASE and
#     friends, which Postgres refuses to run inside a transaction block at all.
#   * A file that opens its own BEGIN/COMMIT. Wrapping it would nest, and its
#     COMMIT would end the runner's transaction early -- silently un-protecting
#     every statement after it, including the schema_migrations INSERT appended
#     below. 0054 is the only one today, and it manages its own transaction
#     precisely because of the bug this runner is now fixing.
#
# These files keep the old two-step and are recorded afterwards, so they keep
# the old crash gap. That is inherent, not a shortcoming: for CONCURRENTLY an
# interrupted index build leaves an INVALID index which the re-apply drops and
# rebuilds, so a second apply is the recovery path.
#
# THE MARKER IS DECLARED, NOT GUESSED, and that is the whole point. The first
# version of this detected CONCURRENTLY by grepping the file, and Fable caught
# what that costs: 0030 contains the word only in a COMMENT ("no CONCURRENTLY
# needed") and has no such statement, so it was classified unwrappable and lost
# its atomicity while printing a line claiming that was required. Silent, and no
# test would have seen it. This repo's house style is to discuss CONCURRENTLY in
# migration comments, so the next author would have hit it too.
#
# The guess is now only a SAFETY NET, and it fails loudly rather than quietly:
# comments are stripped, and a file that looks unwrappable without carrying the
# marker aborts the run with an instruction instead of being wrapped and failing
# in Postgres, or worse, being unwrapped without anyone noticing.
NO_TRANSACTION_MARKER='@no-transaction'

# Comments AND dollar-quoted bodies removed, so the detection below reads
# statements rather than prose or plpgsql. Used ONLY for classification -- psql
# always receives the original file, so anything mangled here changes nothing
# about what is executed.
#
# Both exclusions were earned. Comments: 0030 says "no CONCURRENTLY needed" and
# has no such statement. Dollar quoting: 0049 defines a plpgsql function whose
# body opens with BEGIN, which is a block, not a transaction -- it tripped the
# safety net on the first real run of this code.
strip_sql_comments() {
  awk '
    # Line comments FIRST, and the order is load-bearing. Doing dollar quotes
    # first lets a $$ inside a comment ("payloads may contain $$") open a region
    # that closes on a real $$ later in the file, swallowing every statement in
    # between -- including a BEGIN;/COMMIT; that must not be wrapped. Comments
    # first can instead truncate a dollar-quoted body containing --, which at
    # worst hides a statement inside a plpgsql function; that direction is
    # harmless here because such statements are not transaction control.
    # Neither order is a real lexer. That is what the runtime warning check in
    # apply_and_record is for.
    { gsub(/--.*$/, "") }
    # Inside a block comment that opened on an earlier line: discard until the
    # closer, then carry on with whatever follows it on that line.
    inblock {
      if (match($0, /\*\//)) { $0 = substr($0, RSTART + RLENGTH); inblock = 0 }
      else { next }
    }
    {
      # Complete block comments. The naive /\*.*\*/ is greedy, so
      # `/* a */ BEGIN; /* b */` would delete the BEGIN; between them -- this
      # matches the shortest span instead.
      while (match($0, /\/\*[^*]*\*+([^\/*][^*]*\*+)*\//)) {
        $0 = substr($0, 1, RSTART - 1) " " substr($0, RSTART + RLENGTH)
      }
      # An opener with no closer on this line: keep the code BEFORE it. The
      # earlier version printed nothing for such a line, losing any statement
      # that preceded the comment.
      if (match($0, /\/\*/)) { $0 = substr($0, 1, RSTART - 1); inblock = 1 }
    }
    {
      # Dollar-quoted regions ($$ or $tag$), which may span lines. Inside one,
      # nothing is SQL as far as classification is concerned.
      #
      # LAST, after both comment forms, and that ordering is the fix for a bug
      # this file previously claimed to have fixed while not actually having done
      # so: a $$ appearing inside a comment would open a phantom region closing
      # on a real $$ further down, swallowing every statement between -- a
      # BEGIN;/COMMIT; pair included, silently. Fable caught the discrepancy
      # between the comment above and the rule order below it.
      line = $0
      out = ""
      while (length(line) > 0) {
        if (indollar) {
          idx = index(line, dollartag)
          if (idx == 0) { line = ""; break }
          line = substr(line, idx + length(dollartag))
          indollar = 0
          continue
        }
        if (match(line, /\$[A-Za-z_][A-Za-z_0-9]*\$|\$\$/)) {
          out = out substr(line, 1, RSTART - 1)
          dollartag = substr(line, RSTART, RLENGTH)
          line = substr(line, RSTART + RLENGTH)
          indollar = 1
          continue
        }
        out = out line
        line = ""
      }
      print out
    }
  ' "$1"
}

mode_of() {
  local f="$1" stripped
  # Anchored to a comment at the start of a line, not matched anywhere in the
  # file. A loose match relocates the 0030 bug onto the marker itself: a comment
  # saying "this does not need @no-transaction" would opt the file out of
  # atomicity, silently, while reading as the opposite.
  if grep -qE "^[[:space:]]*--[[:space:]]*$NO_TRANSACTION_MARKER\b" "$f"; then
    echo nowrap
    return
  fi

  stripped="$(strip_sql_comments "$f")"
  # Transaction control is detected by its TERMINATORS as well as its openers.
  # COMMIT and END are what actually break a wrap -- they end the runner's
  # transaction early -- and a file can carry one without a BEGIN this scan can
  # see (copy-paste, or a BEGIN hidden by whatever the stripper above missed).
  # Guarding only BEGIN guards the wrong keyword. Fable, second review pass.
  #
  # No line anchor: `TRUNCATE staging; BEGIN; ...` puts it mid-line, and the old
  # ^ made that invisible.
  #
  # The semicolon stays required for BEGIN/END: the transaction forms all take
  # one (`BEGIN;`, `BEGIN WORK;`, `BEGIN ISOLATION LEVEL ...;`), and plpgsql's
  # block-opening BEGIN and closing END never do. That is what keeps 0049's
  # function body from tripping this.
  # The terminator forms are spelled more ways than they look. Postgres accepts
  # COMMIT/END/ROLLBACK/ABORT bare, or with WORK or TRANSACTION, and AND CHAIN on
  # top of that. Matching only `commit;` misses `COMMIT WORK;` and friends, which
  # end the wrap exactly as effectively.
  #
  # AND CHAIN gets no warning from psql at all -- it commits the wrapping
  # transaction and immediately opens a fresh one, so the server reports neither
  # "already in progress" nor "no transaction in progress". Catching it here is
  # therefore worth doing, but it is NOT the only thing that can: the transaction
  # id guard sees it, because the id changes. That is what covers the spellings
  # this regex still misses, `COMMIT WORK AND CHAIN;` among them.
  if printf '%s' "$stripped" | grep -qiE '\bconcurrently\b|\bvacuum\b|\balter[[:space:]]+system\b|\bcreate[[:space:]]+(database|tablespace)\b|\bprepare[[:space:]]+transaction\b|\b(commit|end|rollback|abort|begin)([[:space:]]+(work|transaction))?[[:space:]]*;|\b(commit|end|rollback)([[:space:]]+(work|transaction))?[[:space:]]+and[[:space:]]+chain\b|\b(begin|start)[[:space:]]+(transaction|work|isolation)\b'; then
    echo "ERROR: $(basename "$f") contains a statement that cannot run inside a" >&2
    echo "       transaction block, or opens its own transaction, but does not carry" >&2
    echo "       the '-- $NO_TRANSACTION_MARKER' marker." >&2
    echo "       Add that marker on its own line if the statement is real. If the word" >&2
    echo "       only appears in prose, reword the comment -- do not add the marker," >&2
    echo "       because it costs the file its atomicity." >&2
    exit 1
  fi

  echo wrapped
}

# Applies one migration and records it, atomically where that is possible.
apply_and_record() {
  local f="$1" version="$2" mode
  mode="$(mode_of "$f")"

  case "$mode" in
    wrapped)
      # stderr is captured rather than passed through so it can be CHECKED, not
      # merely shown. See the warning check below -- that is the point of the
      # temp file, and it is the one guard here that does not depend on this
      # script parsing SQL correctly.
      #
      # psql also reports errors as `psql:<stdin>:N` rather than by filename, and
      # for a failure in the appended INSERT, N is past the end of the file. The
      # `apply NNNN` line above is the only correlation, so say the path outright:
      # during a cutover, "which file is <stdin>" is not a question worth minutes.
      local err status=0
      err="$(mktemp)"
      # ORDER: guard closes BEFORE the tracking row is written, and that is not
      # cosmetic. With the INSERT first, a migration that ROLLBACKs breaks the
      # wrap, the INSERT then commits on its own in autocommit, and the guard
      # raises afterwards -- leaving the database recorded-but-not-applied, which
      # every later run reports as `skip`. That is the single worst state this
      # runner can produce, and the previous ordering manufactured it. Closing
      # first means ON_ERROR_STOP halts psql before the INSERT is ever reached:
      # applied-but-unrecorded at worst, which a re-run surfaces loudly instead
      # of hiding. Fable, fourth pass.
      { xact_guard_open_sql; cat "$f"; xact_guard_close_sql; record_sql "$version"; } \
        | app_psql --single-transaction -f - >/dev/null 2>"$err" || status=$?
      cat "$err" >&2

      # THE BACKSTOP. Everything above tries to predict which files open their own
      # transaction; this detects it happening. psql emits exactly this warning
      # when a wrapped file runs BEGIN, and it is the only runtime trace -- without
      # the check, a file whose COMMIT ends the outer transaction early leaves the
      # remaining statements in autocommit and the run looks completely clean.
      #
      # Worth having precisely because it does not depend on the classifier being
      # right. Comments, dollar quoting, a COMMIT with no BEGIN, a BEGIN mid-line,
      # a keyword split across lines: every one of those defeats a text scan, and
      # none of them defeats this. Fable's second review pass, 2026-08-17.
      #
      # It fires AFTER the damage rather than preventing it, and that is still the
      # whole value: the operator learns the database is in an unknown state
      # instead of believing the migration was atomic.
      # Bash string matching rather than grep, and that is not a style choice.
      # The first version used `grep -qiF`, which SIGABRTs on this machine's GNU
      # grep 3.0 under git-bash (exit 134, reproducible: -i together with -F).
      # Sitting in an `if` condition, the crash read as "no match" -- so the
      # backstop silently failed open, and the first run that was supposed to
      # prove it worked instead exited 0 with the warning printed and ignored.
      # A safety net whose failure mode is "reports safe" is worse than none.
      local errtext
      errtext="$(cat "$err")" || errtext='(unreadable)'
      # Two warnings, not one. `already a transaction in progress` means the file
      # ran BEGIN inside the wrap; `there is no transaction in progress` means it
      # ran a COMMIT that ended the wrap, which psql reports at its own closing
      # commit. A file with a stray COMMIT and no BEGIN produces only the second,
      # so checking the first alone left that case completely silent.
      if [[ "$errtext" == *"the wrap was broken"* \
         || "$errtext" == *"rolled back the transaction this runner opened"* \
         || "$errtext" == *"already a transaction in progress"* \
         || "$errtext" == *"no transaction in progress"* \
         || "$errtext" == '(unreadable)' ]]; then
        rm -f "$err"
        echo "ERROR: $(basename "$f") ran transaction control while being wrapped." >&2
        echo "       Either it opened its own transaction, or it committed the one this" >&2
        echo "       runner opened. Every statement after that point ran in autocommit," >&2
        echo "       so the migration was NOT atomic." >&2
        echo "" >&2
        echo "       The version may ALREADY BE RECORDED: the tracking INSERT is appended" >&2
        echo "       after the file, so if it ran in autocommit it committed on its own." >&2
        echo "       A naive re-run would then print 'skip $version' and look healthy while" >&2
        echo "       the database is half-migrated. Inspect before re-running." >&2
        echo "" >&2
        echo "       Then add '-- $NO_TRANSACTION_MARKER' to that file so it is never wrapped." >&2
        exit 1
      fi
      rm -f "$err"

      if [ "$status" -ne 0 ]; then
        echo "      (failed applying $f -- psql reports it as <stdin>)" >&2
        return 1
      fi
      ;;
    nowrap)
      echo "      (@no-transaction: applied outside a wrapping transaction)"
      app_psql -f "$f"
      record "$version"
      ;;
  esac
}

# BEFORE anything reads or writes the database. is_tracked is the first thing a
# racing runner can get wrong, so the lock has to be held before the first one
# runs -- not merely before the first apply.
acquire_run_lock

# schema_migrations is defined by migration 0044, but the runner needs it to
# exist before it can even check whether 0002 is tracked -- create it
# defensively up front (IF NOT EXISTS makes 0044 itself a no-op later).
app_psql -c "CREATE TABLE IF NOT EXISTS schema_migrations (version TEXT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT now());" >/dev/null

# Two files sharing a version prefix (0007_a.sql and 0007_b.sql) is silent data
# loss dressed as success: the first is applied and recorded, and every run after
# that prints `skip 0007` for BOTH, so the second is never applied and never
# will be. Nothing downstream notices, because the tracking table only ever knew
# about versions. Cheap to detect, so detect it. Noted by Fable, 2026-08-17.
duplicate_versions="$(
  for f in "$MIGRATIONS_DIR"/[0-9][0-9][0-9][0-9]_*.sql; do version_of "$f"; done | sort | uniq -d
)"
if [ -n "$duplicate_versions" ]; then
  echo "ERROR: two or more migrations share a version prefix:" >&2
  for v in $duplicate_versions; do
    echo "       $v -> $(basename -a "$MIGRATIONS_DIR"/"$v"_*.sql | tr '\n' ' ')" >&2
  done
  echo "       Only the first would ever be applied; the rest are skipped forever." >&2
  exit 1
fi

# The extensions migration is optional. It is the one file applied as the
# superuser, and a schema that needs no extension needs no superuser: demanding
# one anyway would make every deploy of a plain schema depend on a credential it
# never uses.
#
# EXT_VERSION is the version the loop below skips, so when there is no
# extensions migration it must match NOTHING -- not a number, which would
# silently skip whatever other migration happens to carry it.
#
# Found by name, not by number. This repository's schema numbered it 0002 and a
# stamped product numbers it 0001; a hardcoded number means the other one is not
# recognised as the superuser step, falls into the loop below, and is applied as
# the app role -- which cannot CREATE EXTENSION, so every fresh product failed
# its first migration run. Found by Fable, 2026-08-31.
#
# EXT_VERSION holds ONE version, so more than one extensions migration cannot be
# handled here -- the second would fall into the loop below and be applied as the
# app role, which is the same failure with a later number on it. Refused rather
# than half-supported: the message says what to do, and nothing runs.
EXT_MATCHES="$(compgen -G "$MIGRATIONS_DIR/[0-9][0-9][0-9][0-9]_extensions.sql" || true)"
EXT_COUNT="$(printf '%s' "$EXT_MATCHES" | grep -c . || true)"
if [ "$EXT_COUNT" -gt 1 ]; then
  echo "ERROR: $MIGRATIONS_DIR has $EXT_COUNT extensions migrations:" >&2
  printf '  %s
' $EXT_MATCHES >&2
  echo "       Only one file may be applied as $SUPERUSER_ROLE. Fold them into one," >&2
  echo "       or move the later statements into a migration the app role can run." >&2
  exit 1
fi

EXT_FILE="$EXT_MATCHES"
if [ -z "$EXT_FILE" ]; then
  EXT_VERSION="none"
  echo "none  no extensions migration in this schema; no superuser step"
elif EXT_VERSION="$(version_of "$EXT_FILE")" && is_tracked "$EXT_VERSION"; then
  echo "skip  $EXT_VERSION (already applied)"
else
  echo "apply $EXT_VERSION (as $SUPERUSER_ROLE)"
  # The extensions migration runs as the superuser, so it cannot share
  # apply_and_record's app_psql.
  # It is CREATE EXTENSION only -- DDL, idempotent, and the one migration whose
  # double-apply is provably harmless -- so the old two-step is kept here.
  super_psql -f "$EXT_FILE"
  record "$EXT_VERSION"
fi

for f in "$MIGRATIONS_DIR"/[0-9][0-9][0-9][0-9]_*.sql; do
  version="$(version_of "$f")"
  [ "$version" = "$EXT_VERSION" ] && continue
  if is_tracked "$version"; then
    echo "skip  $version (already applied)"
    continue
  fi
  echo "apply $version"
  apply_and_record "$f" "$version"
done

echo "done"
