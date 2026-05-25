namespace Axis.Identity.Infrastructure.Persistence.Entities;

internal enum RegistrationIdempotencyStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
}
