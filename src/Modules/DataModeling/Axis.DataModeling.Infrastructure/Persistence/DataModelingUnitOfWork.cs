using Axis.DataModeling.Application.Services;
using Axis.Shared.Infrastructure.Persistence;
using Wolverine;

namespace Axis.DataModeling.Infrastructure.Persistence;

internal sealed class DataModelingUnitOfWork(DataModelingDbContext context, IMessageBus bus)
    : UnitOfWork(context, bus), IUnitOfWork;
