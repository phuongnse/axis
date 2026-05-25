using Axis.FormBuilder.Domain.Aggregates;

namespace Axis.FormBuilder.Application.Services;

/// <summary>Keeps <c>form_model_references</c> in sync with Relation Picker fields on a form.</summary>
public interface IFormModelReferenceSync
{
    Task SyncRelationPickerReferencesAsync(FormDefinition form, CancellationToken ct = default);
}
