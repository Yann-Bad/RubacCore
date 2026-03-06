using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using RubacCore.Authorization;
using RubacCore.Dtos;
using RubacCore.Interfaces;
using static OpenIddict.Abstractions.OpenIddictConstants;

// ── Why are there TWO levels of [Authorize] here? ──────────────────────────────
//
//  1. [Authorize] on the class  → guarantees every endpoint requires a VALID TOKEN.
//     Anonymous requests (no Bearer header at all) are rejected with 401 here.
//
//  2. [Authorize(Policy = ...)] on each method  → further checks ROLE.
//     A valid token from a Consultant in DashboardCore is authentic but not
//     authorised to list users here — rejected with 403.
//
// Without #1, a user with an expired token could still slip through before
// the policy check fires. Defence-in-depth.
// ───────────────────────────────────────────────────────────────────────────────

namespace RubacCore.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize] // ← level 1: must be authenticated
public class UsersController : ControllerBase
{
    private readonly IUserRepository _userRepository;

    public UsersController(IUserRepository userRepository)
        => _userRepository = userRepository;

    // ── Queries (SuperAdmin only) ──────────────────────────────────────────────

    /// <summary>
    /// List users with server-side pagination and optional search.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Policies.ManageUsers)] // ← level 2: SuperAdmin only
    public async Task<IActionResult> GetAll(
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 10,
        [FromQuery] string? search   = null,
        [FromQuery] string? sortBy   = "userName",
        [FromQuery] string? sortDir  = "asc")
        => Ok(await _userRepository.GetPagedAsync(page, pageSize, search, sortBy, sortDir));

    /// <summary>
    /// Get a single user by id. SuperAdmin can fetch anyone;
    /// regular users call GET /api/me on DashboardCore to see themselves.
    /// </summary>
    [HttpGet("{id:long}")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> GetById(long id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        return user is null ? NotFound() : Ok(user);
    }

    // ── Commands (SuperAdmin only) ──────────────────────────────────────────────

    /// <summary>
    /// Create a new user account.
    /// Open registration (no [Authorize]) would let anyone create an account.
    /// Here it is protected so only SuperAdmin provisions accounts — no self-sign-up.
    /// Change to [AllowAnonymous] if public registration is desired.
    /// </summary>
    [HttpPost("register")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        try
        {
            var user = await _userRepository.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Update a user's name and/or email address.
    /// This does NOT change the password — use the dedicated endpoint for that.
    /// Separating the two operations avoids accidental password resets when
    /// an admin edits profile info.
    /// </summary>
    [HttpPut("{id:long}")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> Update(long id, UpdateUserDto dto)
    {
        try
        {
            var user = await _userRepository.UpdateAsync(id, dto);
            return user is null ? NotFound() : Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Activate or deactivate a user account (soft-disable).
    ///
    /// Deactivation stops the user from logging in (AuthService rejects inactive users)
    /// without deleting their data or breaking foreign keys in other tables.
    /// Existing access tokens remain valid until they expire naturally (≈1 hour).
    /// </summary>
    [HttpPatch("{id:long}/active")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> SetActive(long id, [FromBody] SetActiveDto dto)
    {
        var success = await _userRepository.SetActiveAsync(id, dto.IsActive);
        if (!success) return NotFound();

        return Ok(new { message = $"User {(dto.IsActive ? "activated" : "deactivated")} successfully." });
    }

    /// <summary>
    /// Force-reset a user's password (SuperAdmin action, no current-password required).
    /// Identity's password validators still run — weak passwords are rejected with 400.
    /// </summary>
    [HttpPatch("{id:long}/password")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> ResetPassword(long id, [FromBody] ResetPasswordDto dto)
    {
        try
        {
            var success = await _userRepository.ResetPasswordAsync(id, dto.NewPassword);
            if (!success) return NotFound();
            return Ok(new { message = "Password reset successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── Role assignment (SuperAdmin only) ──────────────────────────────────────

    /// <summary>
    /// Assign a role to a user. The role must already exist in the Roles table.
    /// Roles control what the user can do in each application (DashboardCore, etc.).
    /// The new role takes effect at the user's NEXT login (it is encoded in the token).
    /// </summary>
    [HttpPost("assign-role")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> AssignRole(AssignRoleDto dto)
    {
        var success = await _userRepository.AssignRoleAsync(dto.UserId, dto.RoleName);
        return success ? Ok($"Role '{dto.RoleName}' assigned.") : NotFound("User not found.");
    }

    /// <summary>
    /// Remove a role from a user.
    /// Role removal takes effect at the user's NEXT login.
    /// To revoke access immediately, also deactivate the user (PATCH /active)
    /// so their current token cannot be refreshed.
    /// </summary>
    [HttpDelete("{id:long}/roles/{roleName}")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> RemoveRole(long id, string roleName)
    {
        var success = await _userRepository.RemoveRoleAsync(id, roleName);
        return success ? Ok($"Role '{roleName}' removed.") : NotFound("User not found.");
    }

    /// <summary>
    /// Permanently delete a user account and all their Identity data.
    /// This is irreversible — use PATCH /active to deactivate instead for recoverable disabling.
    /// Only SuperAdmin can hard-delete.
    /// </summary>
    [HttpDelete("{id:long}")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> Delete(long id)
    {
        var success = await _userRepository.DeleteAsync(id);
        return success ? NoContent() : NotFound();
    }

    // ── Self-service (any authenticated user) ───────────────────────────────────

    /// <summary>
    /// Returns the profile of whoever is calling (from the token's subject claim).
    /// Allows a logged-in user to see their own data without knowing their numeric id,
    /// and without exposing a broader GET /api/users endpoint to non-admins.
    /// </summary>
    [HttpGet("me")]
    [Authorize(Policy = Policies.SelfService)]
    public async Task<IActionResult> GetMe()
    {
        // The subject claim is the user's id, put there by AuthController during sign-in.
        var userId = User.GetClaim(Claims.Subject);
        if (userId is null) return Unauthorized();

        var user = await _userRepository.GetByIdAsync(long.Parse(userId));
        return user is null ? NotFound() : Ok(user);
    }

    /// <summary>
    /// Update the calling user's own profile fields (name, email).
    /// Uses the same UpdateUserDto as the admin endpoint — just scoped to self.
    /// </summary>
    [HttpPut("me")]
    [Authorize(Policy = Policies.SelfService)]
    public async Task<IActionResult> UpdateMe(UpdateUserDto dto)
    {
        var userId = User.GetClaim(Claims.Subject);
        if (userId is null) return Unauthorized();

        try
        {
            var user = await _userRepository.UpdateAsync(long.Parse(userId), dto);
            return user is null ? NotFound() : Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Self-service password change. CurrentPassword is always required here
    /// (unlike the admin-only PATCH /{id}/password which skips it).
    /// </summary>
    [HttpPatch("me/password")]
    [Authorize(Policy = Policies.SelfService)]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangePasswordDto dto)
    {
        var userId = User.GetClaim(Claims.Subject);
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
            return BadRequest(new { error = "Le mot de passe actuel est requis." });

        try
        {
            var success = await _userRepository.ChangePasswordAsync(
                long.Parse(userId), dto.CurrentPassword, dto.NewPassword);
            if (!success) return NotFound();
            return Ok(new { message = "Mot de passe modifié avec succès." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
