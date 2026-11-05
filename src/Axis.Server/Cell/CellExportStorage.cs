using Axis.Administration;

namespace Axis.Server.CellApi;

/// <summary>
/// The storage of tenant exports, read from the five <c>Cell:ExportStorage</c> settings, for the download route of the provisioning API.
/// <see cref="Storage"/> is null when a setting is blank. The server still starts, the JSON read of an export still works and only a
/// download answers <c>503</c>.
/// </summary>
internal sealed record CellExportStorage(CellBackupStorage? Storage);
