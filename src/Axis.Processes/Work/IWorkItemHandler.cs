namespace Axis.Processes.Work;

/// <summary>Runs the work items of one kind. The tenant context is the item's tenant while it runs.</summary>
public interface IWorkItemHandler
{
    /// <summary>The work item kind this handler runs. Each kind has at most one handler.</summary>
    string Kind { get; }

    /// <summary>Runs the item in the context's transaction. Throwing rolls the handler's writes back.</summary>
    Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Called after <see cref="HandleAsync"/> threw and its writes were rolled back, in a new
    /// transaction in which the item has already been deleted. It is not retried.
    /// </summary>
    Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken);
}
