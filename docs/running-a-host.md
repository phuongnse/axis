# Running a host on its own

This page says how to run the worker or the server alone, outside `scripts/dev.sh`, and how to make the development
server activate another application folder. The scripts, the local stack and the development data are in
[development](development.md).

## Running a host on its own

A host can also run alone, outside `scripts/dev.sh`. To run the worker, run
`dotnet run --project src/Axis.Worker`. It reads its tenants from its `Tenants` section, as the server
does, and runs their due work items. It waits until the server has migrated each tenant database, so
either can start first. If you moved PostgreSQL with `AXIS_POSTGRES_PORT`, override
`Tenants__default__ConnectionString` for the worker.

`dotnet run` of the server or the worker stops at start when a folder it reads does not exist. Create
them first:

- `Extensions:Path`: build the sample extension package, with `scripts/build.sh` or
  `dotnet build samples/packages/PaymentReferences/PaymentReferences.csproj`;
- `Modules:Path`: install the built-in and sample modules, with `scripts/build.sh` or
  `bash scripts/lib/install-modules.sh`;
- `Templates:Path`, the server only: `bash scripts/lib/install-sample-templates.sh` creates it.

## Activating other application folders

The development server activates the four samples at start, and can activate any other folder.
`src/Axis.Server/appsettings.Development.json` lists the purchase request sample,
`../../samples/apps/purchase-requests`, the leave request sample, `../../samples/apps/leave-requests`,
the service desk sample, `../../samples/apps/service-desk`, and the asset management sample,
`../../samples/apps/asset-management`, in `ActivateOnStartup`. So `scripts/dev.sh` migrates every
tenant database, then compiles and activates all four samples before it listens, and stops on any
diagnostic.

To activate another application folder, add it to that list, or set `ActivateOnStartup__0=<folder>`
to activate it in place of the purchase request sample, while the other samples stay active. Relative
paths are resolved from `src/Axis.Server`.
