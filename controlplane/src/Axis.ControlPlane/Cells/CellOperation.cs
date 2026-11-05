using System.Text.RegularExpressions;

namespace Axis.ControlPlane.Cells;

/// <summary>
/// One operation on one record of the control plane, such as <c>tenant-create</c> for a sign-up. Its
/// <see cref="IdempotencyKey"/> is the same on every retry, so the cell answers the first job again.
/// </summary>
internal sealed partial record CellOperation
{
    public CellOperation(string name, Guid recordId)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!OperationName().IsMatch(name))
        {
            throw new ArgumentException("An operation name is lowercase letters joined by single hyphens.", nameof(name));
        }

        if (recordId == Guid.Empty)
        {
            throw new ArgumentException("A record id must not be empty.", nameof(recordId));
        }

        Name = name;
        RecordId = recordId;
    }

    public string Name { get; }

    public Guid RecordId { get; }

    /// <summary><c>&lt;operation&gt;:&lt;record id&gt;</c>, far under the 255 characters the cell allows.</summary>
    public string IdempotencyKey => $"{Name}:{RecordId:D}";

    [GeneratedRegex("^[a-z]+(-[a-z]+)*$")]
    private static partial Regex OperationName();
}
