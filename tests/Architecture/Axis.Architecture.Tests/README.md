# Axis Architecture Fitness Tests

> [AGENTS.md](../../../AGENTS.md) · [TECH_STACK.md](../../../docs/TECH_STACK.md) · [WORKAROUNDS.md](../../../docs/WORKAROUNDS.md)

These tests run under `dotnet test` and enforce structural rules that are cheap to verify mechanically.

## What This Catches

| Test file | Rule |
|---|---|
| [DomainPurityTests](./DomainPurityTests.cs) | Domain assemblies stay pure C# and do not reference infrastructure/runtime dependencies. |
| [ModuleBoundaryTests](./ModuleBoundaryTests.cs) | Module projects do not reference another module's implementation projects. |
| [GatewayBoundaryTests](./GatewayBoundaryTests.cs) | `Axis.Api` endpoints and infrastructure do not take direct dependencies on another module's repository/service internals. |
| [SharedKernelTests](./SharedKernelTests.cs) | `Axis.Shared.Domain` and `Axis.Shared.Application` stay free of EF/Npgsql/runtime implementation dependencies. |
| [MediatorScopeTests](./MediatorScopeTests.cs) | MediatR remains an in-module dispatch mechanism. |
| [HandlerConventionTests](./HandlerConventionTests.cs) | Handlers are sealed and accept `CancellationToken` as the last `Handle` parameter. |
| [RepositoryConventionTests](./RepositoryConventionTests.cs) | Repositories materialize results and do not expose commit methods. |
| [AggregateConventionTests](./AggregateConventionTests.cs) | Aggregate roots use factories/behavior methods rather than public mutable state. |
| [EndpointConventionTests](./EndpointConventionTests.cs) | Endpoint extension hosts follow `*Endpoints` naming. |

## What This Does Not Catch

These tests inspect assemblies and namespaces. They do not prove product correctness, API semantics, raw SQL intent, or test coverage quality. Use use-case acceptance criteria and review for those.

## Adding A New Rule

1. Add a focused `*Tests.cs` file with a clear failure message.
2. Quote or link the owner rule.
3. Update this README.
4. Keep allow-lists paired with an active [WORKAROUNDS.md](../../../docs/WORKAROUNDS.md) entry.
