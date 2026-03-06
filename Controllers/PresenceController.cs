using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RubacCore.Authorization;
using RubacCore.Interfaces;

namespace RubacCore.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(Policy = Policies.ManageUsers)]
public class PresenceController : ControllerBase
{
    private readonly IPresenceService _presence;

    public PresenceController(IPresenceService presence)
        => _presence = presence;

    /// <summary>
    /// Returns a snapshot of all currently connected sessions.
    /// Used as a fallback / initial load before the SignalR WebSocket is ready.
    /// </summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(_presence.GetAll());
}
