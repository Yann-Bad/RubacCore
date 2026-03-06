using RubacCore.Dtos;

namespace RubacCore.Interfaces;

public interface IClientService
{
    Task<IEnumerable<ClientDto>> GetAllAsync(CancellationToken ct = default);
    Task<ClientDto?>             GetByClientIdAsync(string clientId, CancellationToken ct = default);
    Task<ClientDto>              CreateAsync(CreateClientDto dto, CancellationToken ct = default);
    Task                        UpdateAsync(string clientId, UpdateClientDto dto, CancellationToken ct = default);
    Task                        DeleteAsync(string clientId, CancellationToken ct = default);
}
