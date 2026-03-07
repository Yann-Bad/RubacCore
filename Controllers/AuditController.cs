using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RubacCore.Authorization;
using RubacCore.Interfaces;

namespace RubacCore.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(Policy = Policies.ManageUsers)] // SuperAdmin only
public class AuditController : ControllerBase
{
    private readonly IAuditService _audit;

    public AuditController(IAuditService audit) => _audit = audit;

    /// <summary>
    /// Paginated audit log with optional filtering by actor, entity, action, or keyword search.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 10,
        [FromQuery] string? actor    = null,
        [FromQuery] string? entity   = null,
        [FromQuery] string? action   = null,
        [FromQuery] string? search   = null)
        => Ok(await _audit.GetPagedAsync(page, pageSize, actor, entity, action, search));
}
