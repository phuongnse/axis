namespace Axis.ControlPlane.Cells;

/// <summary>
/// One entry of <c>ControlPlane:Cells</c>: a cell of the region and how to sign in to its operator realm. The client
/// secret is not here: it is <c>ControlPlane:CellSecrets:&lt;Id&gt;</c>.
/// </summary>
public sealed class CellOptions
{
    /// <summary>The cell id, such as <c>vn-1</c>.</summary>
    public string? Id { get; set; }

    /// <summary>The address of the cell's internal listener. It must be <c>https</c>.</summary>
    public string? InternalUrl { get; set; }

    /// <summary>The token endpoint of the cell's operator realm. It must be <c>https</c>.</summary>
    public string? TokenUrl { get; set; }

    /// <summary>The id of the realm's client for the control plane.</summary>
    public string? ClientId { get; set; }
}

/// <summary>The files of the client certificate that the control plane presents to every cell.</summary>
public sealed class CellClientCertificateOptions
{
    /// <summary>The PEM certificate.</summary>
    public string? Path { get; set; }

    /// <summary>The PEM private key of the certificate.</summary>
    public string? KeyPath { get; set; }
}
