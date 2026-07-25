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
#   APP_ROLE          default epass_app -- PGPASSWORD (or ~/.pgpass) must
#                     authenticate this role for every migration except 0002
#   SUPERUSER_ROLE    default postgres -- must authenticate for 0002 only
#                     (creates the citext extension; epass_app can't)
#
# CREATE INDEX CONCURRENTLY migrations (e.g. 0025) require autocommit with no
# wrapping transaction. Plain `psql -f` already runs in autocommit mode --
# each statement commits as it executes -- as long as we never pass `-1` /
# `--single-transaction`. Do not add that flag here.

set -euo pipefail

PGHOST="${PGHOST:-127.0.0.1}"
PGPORT="${PGPORT:-5432}"
PGDATABASE="${PGDATABASE:-nativeweb_dev}"
APP_ROLE="${APP_ROLE:-epass_app}"
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
  app_psql -f "$f"
  record "$version"
done

echo "done"
