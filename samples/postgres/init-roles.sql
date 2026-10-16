-- Creates the two database roles of the development environments, with the development password
-- axis. axis_schema owns the tenant database and runs startup activation. axis_runtime serves
-- requests and runs the worker. This file grants axis_runtime nothing: the server grants it its rights
-- in the axis and entities schemas at every start, before and after the migrations.
-- The postgres image runs this file once, as the bootstrap superuser axis, connected to POSTGRES_DB.
-- Docker Compose and scripts/e2e.sh mount it into /docker-entrypoint-initdb.d.
CREATE ROLE axis_schema LOGIN PASSWORD 'axis';
CREATE ROLE axis_runtime LOGIN PASSWORD 'axis';
DO $$ BEGIN EXECUTE format('ALTER DATABASE %I OWNER TO axis_schema', current_database()); END $$;
