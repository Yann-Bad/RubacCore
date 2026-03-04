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
    /// List all users. SuperAdmin only — exposes every account in the system.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Policies.ManageUsers)] // ← level 2: SuperAdmin only
    public async Task<IActionResult> GetAll()
        => Ok(await _userRepository.GetAllAsync());

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
    public async Task<IActionResult> SetActive(long id, [FromBody] bool isActive)
    {
        var success = await _userRepository.SetActiveAsync(id, isActive);
        if (!success) return NotFound();

        return Ok(new { message = $"User {(isActive ? "activated" : "deactivated")} successfully." });
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
}
