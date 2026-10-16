-- Creates the two database roles of the development environments, with the development password
-- axis. axis_schema owns the tenant database and runs startup activation. axis_runtime serves
-- requests and runs the worker. Default privileges give axis_runtime row access to everything
-- axis_schema creates, as the integration test fixtures do.
-- The postgres image runs this file once, as the bootstrap superuser axis, connected to POSTGRES_DB.
-- Docker Compose and scripts/e2e.sh mount it into /docker-entrypoint-initdb.d.
CREATE ROLE axis_schema LOGIN PASSWORD 'axis';
CREATE ROLE axis_runtime LOGIN PASSWORD 'axis';
DO $$ BEGIN EXECUTE format('ALTER DATABASE %I OWNER TO axis_schema', current_database()); END $$;
ALTER DEFAULT PRIVILEGES FOR ROLE axis_schema GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO axis_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE axis_schema GRANT USAGE, SELECT ON SEQUENCES TO axis_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE axis_schema GRANT USAGE ON SCHEMAS TO axis_runtime;
