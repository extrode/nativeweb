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

is_tracked() {
  local version="$1" count
  count="$(app_psql -Atqc "SELECT count(*) FROM schema_migrations WHERE version = '$version'")"
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
# Two kinds of file cannot be wrapped, and both are detected rather than listed,
# so a new migration of either kind is handled without editing this runner:
#
#   concurrent  CREATE INDEX CONCURRENTLY cannot run inside a transaction block
#               at all (Postgres rejects it). These stay in autocommit and are
#               recorded afterwards, which leaves exactly the old gap. That is
#               inherent to CONCURRENTLY rather than a shortcoming here: an index
#               build that is interrupted leaves an INVALID index which the
#               re-apply drops and rebuilds, so a second apply is the recovery
#               path, not a hazard.
#
#   self        The file opens its own BEGIN/COMMIT. Wrapping it would nest, and
#               its COMMIT would end the runner's transaction early, silently
#               un-protecting everything after it. Only 0054 does this today,
#               and it does it precisely because of the bug being fixed here --
#               it writes its own schema_migrations row inside its transaction.
#               Left alone deliberately: it is already applied to real databases,
#               and the record() below is a no-op against its own row thanks to
#               ON CONFLICT DO NOTHING.
mode_of() {
  if grep -qiE '\bconcurrently\b' "$1"; then
    echo concurrent
  elif grep -qiE '^[[:space:]]*begin[[:space:]]*;' "$1"; then
    echo self
  else
    echo wrapped
  fi
}

# Applies one migration and records it, atomically where that is possible.
apply_and_record() {
  local f="$1" version="$2" mode
  mode="$(mode_of "$f")"

  case "$mode" in
    wrapped)
      { cat "$f"; record_sql "$version"; } | app_psql --single-transaction -f - >/dev/null
      ;;
    concurrent|self)
      echo "      ($mode: applied outside a wrapping transaction)"
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
