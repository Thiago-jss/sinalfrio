#!/bin/sh
# Runs only on a new Postgres volume, as part of the official image entrypoint.
set -eu
psql -X --set ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<'SQL'
\getenv migrator_password SINALFRIO_DB_MIGRATOR_PASSWORD
\getenv runtime_password SINALFRIO_DB_RUNTIME_PASSWORD
CREATE ROLE sinalfrio_migrator LOGIN PASSWORD :'migrator_password';
CREATE ROLE sinalfrio_runtime LOGIN PASSWORD :'runtime_password';
ALTER DATABASE sinalfrio OWNER TO sinalfrio_migrator;
ALTER SCHEMA public OWNER TO sinalfrio_migrator;
REVOKE ALL ON DATABASE sinalfrio FROM PUBLIC;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT CONNECT ON DATABASE sinalfrio TO sinalfrio_runtime;
GRANT USAGE ON SCHEMA public TO sinalfrio_runtime;

SQL
