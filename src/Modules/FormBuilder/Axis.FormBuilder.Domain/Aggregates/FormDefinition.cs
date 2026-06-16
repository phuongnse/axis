using System.Text.RegularExpressions;
using Axis.FormBuilder.Domain.Entities;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.Events;
using Axis.FormBuilder.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Domain.Aggregates;

public sealed class FormDefinition : AggregateRoot<Guid>
{
    // Field key: 1–64 chars, alphanumeric+underscore, must start with letter
    private static readonly Regex FieldKeyRegex = new(@"^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);

    private readonly List<FormField> _fields = [];

    public string Name { get; private set; }
    public string? Description { get; private set; }
    public Guid OrganizationId { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string CreatedBy { get; private set; }

    public IReadOnlyList<FormField> Fields => _fields.AsReadOnly();

    private FormDefinition() : base(default) { Name = null!; CreatedBy = string.Empty; } // EF Core materialisation

    private FormDefinition(Guid id, string name, string? description, Guid organizationId,
        string createdBy, DateTimeOffset createdAt)
        : base(id)
    {
        Name = name;
        Description = description;
        OrganizationId = organizationId;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static FormDefinition Create(string name, string? description, Guid organizationId, string createdBy)
    {
        ValidateName(name);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        FormDefinition form = new(Guid.NewGuid(), name.Trim(), description?.Trim(), organizationId, createdBy, now);
        form.RaiseDomainEvent(new FormCreated(form.Id, organizationId, form.Name));
        return form;
    }

    public void Update(string name, string? description)
    {
        ValidateName(name);
        Name = name.Trim();
        Description = description?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public FormField AddField(string key, string label, FormFieldType type, bool required, FormFieldConfig? config)
    {
        if (DeletedAt.HasValue)
            throw new InvalidOperationException("Cannot modify a deleted form.");

        ValidateFieldKey(key);
        ValidateFieldConfig(type, config);

        int order = _fields.Count;
        FormField field = FormField.Create(key.ToLowerInvariant(), label, type, required, order, config);
        _fields.Add(field);
        UpdatedAt = DateTimeOffset.UtcNow;
        return field;
    }

    public void RemoveField(Guid fieldId)
    {
        FormField field = _fields.SingleOrDefault(f => f.Id == fieldId)
            ?? throw new InvalidOperationException("Field not found.");

        _fields.Remove(field);
        RecalculateOrder();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ReorderFields(IReadOnlyList<Guid> orderedFieldIds)
    {
        if (orderedFieldIds.Count != _fields.Count ||
            !orderedFieldIds.All(id => _fields.Any(f => f.Id == id)))
            throw new ArgumentException("The provided field IDs must match all fields exactly.");

        for (int i = 0; i < orderedFieldIds.Count; i++)
            _fields.Single(f => f.Id == orderedFieldIds[i]).DisplayOrder = i;

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Delete()
    {
        if (DeletedAt.HasValue)
            throw new InvalidOperationException("Form is already deleted.");

        DeletedAt = DateTimeOffset.UtcNow;
        RaiseDomainEvent(new FormDeleted(Id, OrganizationId));
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static void ValidateName(string name)
    {
        string trimmed = name?.Trim() ?? "";
        if (trimmed.Length < 2 || trimmed.Length > 200)
            throw new ArgumentException("Form name must be 2–200 characters.");
    }

    private void ValidateFieldKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !FieldKeyRegex.IsMatch(key))
            throw new ArgumentException(
                $"Field key '{key}' is invalid. Must be 1–64 characters, start with a letter, and contain only alphanumeric characters and underscores.");

        if (_fields.Any(f => f.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A field with key '{key}' already exists in this form.");
    }

    private static void ValidateFieldConfig(FormFieldType type, FormFieldConfig? config)
    {
        if (type == FormFieldType.Dropdown && config is DropdownFieldConfig dropdownConfig)
        {
            if (dropdownConfig.Options.Count < 2)
                throw new ArgumentException("Dropdown field requires at least 2 options.");
        }
        else if (type == FormFieldType.MultiSelect && config is MultiSelectFieldConfig multiConfig)
        {
            if (multiConfig.Options.Count < 2)
                throw new ArgumentException("Multi-select field requires at least 2 options.");
        }
    }

    private void RecalculateOrder()
    {
        List<FormField> sorted = _fields.OrderBy(f => f.DisplayOrder).ToList();
        for (int i = 0; i < sorted.Count; i++)
            sorted[i].DisplayOrder = i;
    }
}
