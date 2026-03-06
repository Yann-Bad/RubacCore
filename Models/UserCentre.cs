namespace RubacCore.Models;

public class UserCentre
{
    public long UserId    { get; set; }
    public int  CentreId  { get; set; }

    /// <summary>Marks one centre as the user's primary work location (JWT centre_primary claim).</summary>
    public bool IsPrimary { get; set; }

    public ApplicationUser User   { get; set; } = null!;
    public Centre          Centre { get; set; } = null!;
}
