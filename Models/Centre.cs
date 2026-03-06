namespace RubacCore.Models;

public enum CodeSubdivisionCentre
{
    CAPITAL,
    PROVINCE,
    VILLE,
    TERRITOIRE,
    DISTRICT,
    SECTEUR_CHEFFERIES,
    GROUPEMENT,
    COMMUNE,
    VILLAGE,
}

public class Centre
{
    public int Id { get; set; }

    /// <summary>Business code matching Referentielcentredegestion.Code in DashboardCore.</summary>
    public string? Code { get; set; }

    public string? Name { get; set; }

    public bool IsActive { get; set; } = true;

    public CodeSubdivisionCentre SubdivisionAdministrative { get; set; }

    /// <summary>Self-referencing parent FK for the CAPITAL → … → VILLAGE hierarchy.</summary>
    public int? ParentId { get; set; }

    public Centre? Parent { get; set; }

    public ICollection<Centre> Children { get; set; } = [];

    public ICollection<UserCentre> UserCentres { get; set; } = [];
}
