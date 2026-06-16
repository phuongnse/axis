namespace Axis.FormBuilder.Domain.ReadModels;

/// <summary>
/// Local read model: form Relation Picker fields that target a DataModeling model.
/// Updated when forms change; <see cref="IsBroken"/> is set when the target model is deleted (Kafka).
/// </summary>
public sealed class FormModelReference
{
    public Guid FormId { get; private set; }
    public Guid FormFieldId { get; private set; }
    public Guid ModelId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public bool IsBroken { get; private set; }

    private FormModelReference() { } // EF Core materialisation

    public static FormModelReference Create(
        Guid formId,
        Guid formFieldId,
        Guid modelId,
        Guid organizationId,
        bool isBroken = false)
        => new()
        {
            FormId = formId,
            FormFieldId = formFieldId,
            ModelId = modelId,
            OrganizationId = organizationId,
            IsBroken = isBroken,
        };

    public void MarkBroken() => IsBroken = true;

    public void MarkHealthy() => IsBroken = false;

    public void Retarget(Guid modelId)
    {
        ModelId = modelId;
        IsBroken = false;
    }
}
