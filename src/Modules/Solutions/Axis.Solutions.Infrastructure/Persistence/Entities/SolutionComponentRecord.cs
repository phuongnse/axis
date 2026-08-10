namespace Axis.Solutions.Infrastructure.Persistence.Entities;

internal sealed class SolutionComponentRecord
{
    public Guid SolutionVersionId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public string DependsOnJson { get; set; } = "[]";
}
