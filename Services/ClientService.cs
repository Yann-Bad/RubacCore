using OpenIddict.Abstractions;
using RubacCore.Dtos;
using RubacCore.Interfaces;

namespace RubacCore.Services;

public class ClientService : IClientService
{
    private readonly IOpenIddictApplicationManager _manager;

    public ClientService(IOpenIddictApplicationManager manager)
        => _manager = manager;

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<ClientDto> ToDto(
        IOpenIddictApplicationManager manager,
        object app,
        CancellationToken ct)
    {
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, app, ct);

        return new ClientDto(
            ClientId:    descriptor.ClientId ?? string.Empty,
            DisplayName: descriptor.DisplayName,
            ClientType:  descriptor.ClientType ?? "public",
            Permissions: descriptor.Permissions.ToArray(),
            RedirectUris: descriptor.RedirectUris.Select(u => u.ToString()).ToArray()
        );
    }

    // ── Public methods ───────────────────────────────────────────────────────

    public async Task<IEnumerable<ClientDto>> GetAllAsync(CancellationToken ct = default)
    {
        var result = new List<ClientDto>();
        await foreach (var app in _manager.ListAsync(cancellationToken: ct))
        {
            result.Add(await ToDto(_manager, app, ct));
        }
        return result;
    }

    public async Task<ClientDto?> GetByClientIdAsync(string clientId, CancellationToken ct = default)
    {
        var app = await _manager.FindByClientIdAsync(clientId, ct);
        if (app is null) return null;
        return await ToDto(_manager, app, ct);
    }

    public async Task<ClientDto> CreateAsync(CreateClientDto dto, CancellationToken ct = default)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId    = dto.ClientId,
            DisplayName = dto.DisplayName,
            ClientType  = dto.ClientType,
        };

        if (!string.IsNullOrWhiteSpace(dto.ClientSecret))
            descriptor.ClientSecret = dto.ClientSecret;

        foreach (var perm in dto.Permissions)
            descriptor.Permissions.Add(perm);

        foreach (var uri in dto.RedirectUris)
            if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
                descriptor.RedirectUris.Add(parsed);

        var app = await _manager.CreateAsync(descriptor, ct);
        return await ToDto(_manager, app, ct);
    }

    public async Task UpdateAsync(string clientId, UpdateClientDto dto, CancellationToken ct = default)
    {
        var app = await _manager.FindByClientIdAsync(clientId, ct)
            ?? throw new KeyNotFoundException($"Client '{clientId}' not found.");

        var descriptor = new OpenIddictApplicationDescriptor();
        await _manager.PopulateAsync(descriptor, app, ct);

        if (dto.DisplayName is not null)
            descriptor.DisplayName = dto.DisplayName;

        if (!string.IsNullOrWhiteSpace(dto.ClientSecret))
            descriptor.ClientSecret = dto.ClientSecret;

        descriptor.Permissions.Clear();
        foreach (var perm in dto.Permissions)
            descriptor.Permissions.Add(perm);

        descriptor.RedirectUris.Clear();
        foreach (var uri in dto.RedirectUris)
            if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
                descriptor.RedirectUris.Add(parsed);

        await _manager.UpdateAsync(app, descriptor, ct);
    }

    public async Task DeleteAsync(string clientId, CancellationToken ct = default)
    {
        var app = await _manager.FindByClientIdAsync(clientId, ct)
            ?? throw new KeyNotFoundException($"Client '{clientId}' not found.");

        await _manager.DeleteAsync(app, ct);
    }
}
