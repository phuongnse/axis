using Axis.FormBuilder.Domain.Aggregates;

namespace Axis.FormBuilder.Application.Repositories;

public interface IFormRepository
{
    Task AddAsync(FormDefinition form, CancellationToken ct = default);
    Task<FormDefinition?> GetByIdAsync(Guid id, Guid teamAccountId, CancellationToken ct = default);
    Task<IReadOnlyList<FormDefinition>> GetAllAsync(Guid teamAccountId, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid teamAccountId, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>Returns true if the form is referenced by any non-Archived workflow step.</summary>
    Task<bool> IsReferencedByWorkflowAsync(Guid formId, CancellationToken ct = default);
}
