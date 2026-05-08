using Axis.Shared.Infrastructure.Persistence;
using Axis.WorkflowBuilder.Application.Services;
using Wolverine;

namespace Axis.WorkflowBuilder.Infrastructure.Persistence;

internal sealed class WorkflowBuilderUnitOfWork(WorkflowBuilderDbContext context, IMessageBus bus)
    : UnitOfWork(context, bus), IUnitOfWork;
