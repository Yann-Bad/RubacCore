namespace RubacCore.Dtos;

/// <summary>
/// Response from GET /api/centres/my — returns all management centres 
/// (centres de gestion) the authenticated user has access to.
///
/// The JWT token carries:
///   • <c>centre_primary</c>  — the user's primary centre Code
///   • <c>centres</c>         — array of all assigned centre Codes
///
/// This DTO enriches that with full details (Id, Name, SubdivisionAdministrative)
/// so the frontend centre-switcher can display human-readable labels
/// and send the numeric Id in the <c>X-Centre-ID</c> header.
/// </summary>
public class UserCentresResponseDto
{
    /// <summary>
    /// The primary centre from the JWT (centre_primary claim).
    /// Cannot change without re-login / token refresh.
    /// </summary>
    public CentreSwitchItemDto? DefaultCentre { get; set; }

    /// <summary>
    /// The currently active centre (from X-Centre-ID header or default).
    /// This is the centre scoped for all data queries in the current session.
    /// </summary>
    public int ActiveCentreId { get; set; }

    /// <summary>All centres the user has access to.</summary>
    public List<CentreSwitchItemDto> Centres { get; set; } = [];
}

/// <summary>
/// A single management centre item for the centre-switcher dropdown.
/// </summary>
public class CentreSwitchItemDto
{
    /// <summary>Primary key of the Centre.</summary>
    public int Id { get; set; }

    /// <summary>Short code, e.g. "DG", "DRH", "DP-KIN".</summary>
    public string Code { get; set; } = "";

    /// <summary>Full name, e.g. "Direction Générale".</summary>
    public string? Name { get; set; }

    /// <summary>Administrative subdivision (CAPITAL, PROVINCE, VILLE, etc.).</summary>
    public string Subdivision { get; set; } = "";

    /// <summary>True if this is the user's primary centre (IsPrimary in UserCentre).</summary>
    public bool IsPrimary { get; set; }

    /// <summary>True if this is the currently active centre for this session.</summary>
    public bool IsActive { get; set; }
}
