namespace RubacCore.Authorization;

/// <summary>
/// Centralises all RubacCore authorization policy names as constants.
///
/// WHY does RubacCore need its own policies?
///  RubacCore IS the identity server — it manages users, roles and OAuth2 clients.
///  Its own API endpoints must also be protected so only trusted callers can:
///    - list/create/delete users       → SuperAdmin only
///    - list/create/delete roles       → SuperAdmin only
///    - introspect tokens              → registered resource servers (DashboardCore…)
///
///  Any application can eventually use RubacCore as its auth server.
///  The policy layer means adding a new app never requires touching controller code.
/// </summary>
public static class Policies
{
    /// <summary>
    /// Full control over users and roles.
    /// Assigned to: SuperAdmin
    /// Typical callers: the system administrator's UI or CLI.
    /// </summary>
    public const string ManageUsers = "ManageUsers";

    /// <summary>
    /// Full control over role definitions.
    /// Assigned to: SuperAdmin
    /// </summary>
    public const string ManageRoles = "ManageRoles";

    /// <summary>
    /// Read own profile and change own password.
    /// Assigned to: any authenticated user.
    /// </summary>
    public const string SelfService = "SelfService";
}
