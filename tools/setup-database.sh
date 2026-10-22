#!/usr/bin/env bash
# Creates (or recreates) the Workbench database and loads the synthetic data.
#
# Needs sqlcmd on the PATH (Mac: brew install sqlcmd).
#
# Local Docker SQL Server:
#   SQL_SERVER=localhost SQL_ADMIN_USER=sa SQL_ADMIN_PASSWORD='...' ./tools/setup-database.sh
# Azure SQL (database must already exist, script won't drop it):
#   SQL_SERVER=yourserver.database.windows.net SQL_ADMIN_USER=... SQL_ADMIN_PASSWORD='...' AZURE=1 ./tools/setup-database.sh
#
# APP_PASSWORD and READER_PASSWORD set the passwords for the two app logins.
set -euo pipefail

: "${SQL_SERVER:?set SQL_SERVER}"
: "${SQL_ADMIN_USER:?set SQL_ADMIN_USER}"
: "${SQL_ADMIN_PASSWORD:?set SQL_ADMIN_PASSWORD}"
: "${APP_PASSWORD:?set APP_PASSWORD}"
: "${READER_PASSWORD:?set READER_PASSWORD}"
DB_NAME="${DB_NAME:-StudentFinanceWorkbench}"
SQLCMD="${SQLCMD:-sqlcmd}"
DIR="$(cd "$(dirname "$0")/../db" && pwd)"

run() {
    "$SQLCMD" -S "$SQL_SERVER" -U "$SQL_ADMIN_USER" -P "$SQL_ADMIN_PASSWORD" -C -b -I "$@"
}

if [[ "${AZURE:-0}" != "1" ]]; then
    echo "Recreating database $DB_NAME"
    run -d master -Q "IF DB_ID('$DB_NAME') IS NOT NULL BEGIN ALTER DATABASE [$DB_NAME] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DB_NAME]; END; CREATE DATABASE [$DB_NAME];"
fi

for f in 01_schema.sql 02_views.sql 03_procedures.sql 05_seed_data.sql; do
    echo "Running $f"
    run -d "$DB_NAME" -i "$DIR/$f"
done

echo "Running 04_security.sql"
run -d "$DB_NAME" -i "$DIR/04_security.sql" -v AppPassword="$APP_PASSWORD" ReaderPassword="$READER_PASSWORD"

echo "Done."
