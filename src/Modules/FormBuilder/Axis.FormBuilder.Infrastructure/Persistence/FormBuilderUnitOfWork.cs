using Axis.FormBuilder.Application.Services;
using Axis.Shared.Infrastructure.Persistence;
using Wolverine;

namespace Axis.FormBuilder.Infrastructure.Persistence;

internal sealed class FormBuilderUnitOfWork(FormBuilderDbContext context, IMessageBus bus)
    : UnitOfWork(context, bus), IUnitOfWork;
