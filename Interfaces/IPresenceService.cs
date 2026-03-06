using RubacCore.Models;

namespace RubacCore.Interfaces;

public interface IPresenceService
{
    void                         AddOrUpdate(UserSession session);
    void                         Remove(string connectionId);
    IReadOnlyCollection<UserSession> GetAll();
    UserSession?                 GetByConnectionId(string connectionId);
}
