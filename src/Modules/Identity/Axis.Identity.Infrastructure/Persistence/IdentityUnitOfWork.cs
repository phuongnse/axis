using Axis.Identity.Application.Services;
using Axis.Shared.Infrastructure.Persistence;
using Wolverine;

namespace Axis.Identity.Infrastructure.Persistence;

internal sealed class IdentityUnitOfWork(IdentityDbContext context, IMessageBus bus)
    : UnitOfWork(context, bus), IUnitOfWork;
