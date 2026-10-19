-- Creates the database roles of the development environments, with the development password
-- axis. axis_schema owns the tenant database and runs startup activation. axis_runtime serves
-- requests and runs the worker. This file grants axis_runtime nothing: the server grants it its rights
-- in the axis and entities schemas at every start, before and after the migrations.
-- It also creates the preview tenant's database axis_preview and its two roles, which play the same
-- parts for the tenant default-preview: axis_preview_schema owns the database, and
-- axis_preview_runtime serves requests and runs the worker.
-- The postgres image runs this file once, as the bootstrap superuser axis, connected to POSTGRES_DB.
-- Docker Compose and scripts/e2e.sh mount it into /docker-entrypoint-initdb.d.
CREATE ROLE axis_schema LOGIN PASSWORD 'axis';
CREATE ROLE axis_runtime LOGIN PASSWORD 'axis';
DO $$ BEGIN EXECUTE format('ALTER DATABASE %I OWNER TO axis_schema', current_database()); END $$;
CREATE ROLE axis_preview_schema LOGIN PASSWORD 'axis';
CREATE ROLE axis_preview_runtime LOGIN PASSWORD 'axis';
CREATE DATABASE axis_preview OWNER axis_preview_schema;
