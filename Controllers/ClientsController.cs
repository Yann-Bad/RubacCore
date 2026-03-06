using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RubacCore.Authorization;
using RubacCore.Dtos;
using RubacCore.Interfaces;

namespace RubacCore.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(Policy = Policies.ManageRoles)] // SuperAdmin only
public class ClientsController : ControllerBase
{
    private readonly IClientService _clients;

    public ClientsController(IClientService clients)
        => _clients = clients;

    /// <summary>List all registered OAuth2 / OIDC applications.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _clients.GetAllAsync(ct));

    /// <summary>Get a single client by its client_id.</summary>
    [HttpGet("{clientId}")]
    public async Task<IActionResult> Get(string clientId, CancellationToken ct)
    {
        var client = await _clients.GetByClientIdAsync(clientId, ct);
        return client is null ? NotFound() : Ok(client);
    }

    /// <summary>Register a new OAuth2 / OIDC client application.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClientDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ClientId))
            return BadRequest("ClientId is required.");

        if (dto.ClientType == "confidential" && string.IsNullOrWhiteSpace(dto.ClientSecret))
            return BadRequest("ClientSecret is required for confidential clients.");

        var existing = await _clients.GetByClientIdAsync(dto.ClientId, ct);
        if (existing is not null)
            return Conflict($"A client with client_id '{dto.ClientId}' already exists.");

        var created = await _clients.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { clientId = created.ClientId }, created);
    }

    /// <summary>Update an existing client's metadata, permissions and redirect URIs.</summary>
    [HttpPut("{clientId}")]
    public async Task<IActionResult> Update(
        string clientId,
        [FromBody] UpdateClientDto dto,
        CancellationToken ct)
    {
        try
        {
            await _clients.UpdateAsync(clientId, dto, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Delete an OAuth2 / OIDC client registration.</summary>
    [HttpDelete("{clientId}")]
    public async Task<IActionResult> Delete(string clientId, CancellationToken ct)
    {
        try
        {
            await _clients.DeleteAsync(clientId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
