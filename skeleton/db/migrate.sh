#!/usr/bin/env bash
#
# Applies db/migrations/*.sql to the __PRODUCT__ database in order, tracking
# applied versions in schema_migrations so re-runs skip what's already there.
# 0001 (extensions) is the one superuser migration; everything else runs as
# the app role.
#
# Env (all optional; the defaults are the local dev setup):
#   PGHOST            default 127.0.0.1
#   PGPORT            default 5432
#   PGDATABASE        default __product___dev
#   APP_ROLE          default __product___app -- PGPASSWORD (or ~/.pgpass) must
#                     authenticate this role for every migration except 0001
#   SUPERUSER_ROLE    default postgres -- must authenticate for 0001 only
#                     (creates the citext extension; __product___app can't)
#
# Plain `psql -f` runs autocommit -- never add `-1` / `--single-transaction`
# here, so a future CREATE INDEX CONCURRENTLY migration keeps working.

set -euo pipefail

PGHOST="${PGHOST:-127.0.0.1}"
PGPORT="${PGPORT:-5432}"
PGDATABASE="${PGDATABASE:-__product___dev}"
APP_ROLE="${APP_ROLE:-__product___app}"
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

app_psql -c "CREATE TABLE IF NOT EXISTS schema_migrations (version TEXT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT now());" >/dev/null

EXT_FILE="$MIGRATIONS_DIR/0001_extensions.sql"
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
