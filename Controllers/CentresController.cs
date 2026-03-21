using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using RubacCore.Authorization;
using RubacCore.Dtos;
using RubacCore.Interfaces;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace RubacCore.Controllers;

[ApiController]
[Route("api/centres")]
[Authorize]
public class CentresController : ControllerBase
{
    private readonly ICentreService _centreService;

    public CentresController(ICentreService centreService) => _centreService = centreService;

    // ── GET /api/centres ──────────────────────────────────────────
    [HttpGet]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> GetAll() =>
        Ok(await _centreService.GetAllAsync());

    // ── GET /api/centres/user/{userId} ────────────────────────────
    [HttpGet("user/{userId:long}")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> GetForUser(long userId) =>
        Ok(await _centreService.GetCentresForUserAsync(userId));

    // ── GET /api/centres/tree ─────────────────────────────────────
    [HttpGet("tree")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> GetTree()
    {
        var tree = await _centreService.GetTreeAsync();
        return tree is null ? NoContent() : Ok(tree);
    }

    // ── GET /api/centres/my ──────────────────────────────────────
    /// <summary>
    /// Returns all centres the current user has access to, with their
    /// primary and active flags — used by the frontend centre-switcher.
    ///
    /// Data sources:
    ///   • UserCentre join table — all assigned centres (IsPrimary flag)
    ///   • X-Centre-ID header    — overrides the active centre for this session
    ///
    /// Requires only authentication (no admin role needed — every user
    /// can see their own assigned centres).
    /// </summary>
    [HttpGet("my")]
    public async Task<IActionResult> GetMyCentres()
    {
        var userIdStr = User.GetClaim(Claims.Subject);
        if (!long.TryParse(userIdStr, out var userId))
            return Unauthorized();

        // Read the active centre override from the middleware-injected claim
        int.TryParse(User.FindFirstValue("ActiveCentreId"), out var activeCentreId);

        var result = await _centreService.GetUserCentreSwitchAsync(userId, activeCentreId);
        return Ok(result);
    }

    // ── GET /api/centres/{id} ─────────────────────────────────────
    [HttpGet("{id:int}")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> GetById(int id)
    {
        var dto = await _centreService.GetByIdAsync(id);
        return dto is null ? NotFound() : Ok(dto);
    }

    // ── POST /api/centres ─────────────────────────────────────────
    [HttpPost]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> Create([FromBody] CreateCentreRequest request)
    {
        var dto = await _centreService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    // ── PUT /api/centres/{id} ─────────────────────────────────────
    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCentreRequest request)
    {
        var dto = await _centreService.UpdateAsync(id, request);
        return dto is null ? NotFound() : Ok(dto);
    }

    // ── DELETE /api/centres/{id} ──────────────────────────────────
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _centreService.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }

    // ── GET /api/centres/{id}/users ───────────────────────────────
    [HttpGet("{id:int}/users")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> GetUsers(
        int id,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 15,
        [FromQuery] string? search   = null) =>
        Ok(await _centreService.GetCentreUsersAsync(id, page, pageSize, search));

    // ── POST /api/centres/assign ──────────────────────────────────
    /// <summary>Assign a user to a centre (or update IsPrimary).</summary>
    [HttpPost("assign")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> Assign([FromBody] AssignUserCentreRequest request)
    {
        await _centreService.AssignUserCentreAsync(request);
        return NoContent();
    }

    // ── DELETE /api/centres/assign/{userId}/{centreId} ────────────
    [HttpDelete("assign/{userId:long}/{centreId:int}")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> Unassign(long userId, int centreId)
    {
        await _centreService.RemoveUserCentreAsync(userId, centreId);
        return NoContent();
    }
}
