#!/usr/bin/env bash
#
# Applies db/migrations/*.sql to a database in order, tracking applied
# versions in schema_migrations so re-runs skip what's already there. This is
# the primary way to bring a database up to date.
#
# Env (all optional; the defaults are the local dev setup):
#   PGHOST            default 127.0.0.1
#   PGPORT            default 5432
#   PGDATABASE        default nativeweb_dev -- this repo provisions its own
#                     database (never epass.io's live one); the test tiers'
#                     fallback connection strings assume the same name
#   APP_ROLE          default nativeweb_app -- PGPASSWORD (or ~/.pgpass) must
#                     authenticate this role for every migration except 0002
#   SUPERUSER_ROLE    default postgres -- must authenticate for 0002 only
#                     (creates the citext extension; nativeweb_app can't)
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
MIGRATIONS_DIR="$SCRIPT_DIR/migrations"

app_psql() {
  psql -h "$PGHOST" -p "$PGPORT" -U "$APP_ROLE" -d "$PGDATABASE" -v ON_ERROR_STOP=1 "$@"
}

super_psql() {
  psql -h "$PGHOST" -p "$PGPORT" -U "$SUPERUSER_ROLE" -d "$PGDATABASE" -v ON_ERROR_STOP=1 "$@"
}

version_of() {
  basename "$1" | sed -E 's/^([0-9]{4})_.*/\1/'
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

record_sql() {
  printf "\nINSERT INTO schema_migrations (version) VALUES ('%s') ON CONFLICT (version) DO NOTHING;\n" "$1"
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
    {
      line = $0
      # Dollar-quoted regions ($$ or $tag$), which may span lines. Inside one,
      # nothing is SQL as far as classification is concerned.
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
      $0 = out
    }
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
      print
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
  if printf '%s' "$stripped" | grep -qiE '\bconcurrently\b|\bvacuum\b|\balter[[:space:]]+system\b|\bcreate[[:space:]]+(database|tablespace)\b|\b(begin|end|commit|rollback)[[:space:]]*;|\b(begin|start)[[:space:]]+(transaction|work|isolation)\b'; then
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
      { cat "$f"; record_sql "$version"; } \
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
      if [[ "$errtext" == *"already a transaction in progress"* || "$errtext" == '(unreadable)' ]]; then
        rm -f "$err"
        echo "ERROR: $(basename "$f") opened its own transaction while being wrapped." >&2
        echo "       Its COMMIT ended the wrapping transaction early, so any statement" >&2
        echo "       after it ran in autocommit and this migration was NOT atomic." >&2
        echo "       The database is in an unknown state -- inspect it before re-running." >&2
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

# schema_migrations is defined by migration 0044, but the runner needs it to
# exist before it can even check whether 0002 is tracked -- create it
# defensively up front (IF NOT EXISTS makes 0044 itself a no-op later).
app_psql -c "CREATE TABLE IF NOT EXISTS schema_migrations (version TEXT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT now());" >/dev/null

EXT_FILE="$MIGRATIONS_DIR/0002_extensions.sql"
EXT_VERSION="$(version_of "$EXT_FILE")"
if is_tracked "$EXT_VERSION"; then
  echo "skip  $EXT_VERSION (already applied)"
else
  echo "apply $EXT_VERSION (as $SUPERUSER_ROLE)"
  # 0002 runs as the superuser, so it cannot share apply_and_record's app_psql.
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
