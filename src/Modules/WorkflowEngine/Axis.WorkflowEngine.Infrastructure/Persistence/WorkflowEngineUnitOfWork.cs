using Axis.Shared.Infrastructure.Persistence;
using Axis.WorkflowEngine.Application.Services;
using Wolverine;

namespace Axis.WorkflowEngine.Infrastructure.Persistence;

internal sealed class WorkflowEngineUnitOfWork(WorkflowEngineDbContext context, IMessageBus bus)
    : UnitOfWork(context, bus), IUnitOfWork;
