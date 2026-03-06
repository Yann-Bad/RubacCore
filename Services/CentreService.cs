using Microsoft.EntityFrameworkCore;
using RubacCore.Data;
using RubacCore.Dtos;
using RubacCore.Interfaces;
using RubacCore.Models;

namespace RubacCore.Services;

public class CentreService : ICentreService
{
    private readonly RubacDbContext _db;

    public CentreService(RubacDbContext db) => _db = db;

    // ── Queries ───────────────────────────────────────────────────────────

    public async Task<IEnumerable<CentreDto>> GetAllAsync() =>
        await _db.Centres
            .Include(c => c.Parent)
            .OrderBy(c => c.SubdivisionAdministrative)
            .ThenBy(c => c.Name)
            .Select(c => ToDto(c))
            .ToListAsync();

    public async Task<CentreTreeDto?> GetTreeAsync()
    {
        var all = await _db.Centres
            .AsNoTracking()
            .OrderBy(c => c.SubdivisionAdministrative)
            .ThenBy(c => c.Name)
            .ToListAsync();

        // Build root node(s) — CAPITAL-level centres have no parent
        var roots = all.Where(c => c.ParentId == null).ToList();
        if (roots.Count == 0) return null;

        // For forests with multiple roots, return a virtual root wrapping them
        if (roots.Count == 1)
            return BuildTree(roots[0], all);

        // Multiple CAPITALs: wrap in a virtual node
        return new CentreTreeDto(
            Id: 0, Code: null, Name: "All Centres", IsActive: true,
            SubdivisionAdministrative: CodeSubdivisionCentre.CAPITAL,
            ParentId: null,
            Children: roots.Select(r => BuildTree(r, all)).ToList());
    }

    public async Task<CentreDto?> GetByIdAsync(int id)
    {
        var c = await _db.Centres.Include(x => x.Parent).FirstOrDefaultAsync(x => x.Id == id);
        return c is null ? null : ToDto(c);
    }

    // ── Mutations ─────────────────────────────────────────────────────────

    public async Task<CentreDto> CreateAsync(CreateCentreRequest req)
    {
        var centre = new Centre
        {
            Code                     = req.Code,
            Name                     = req.Name,
            IsActive                 = req.IsActive,
            SubdivisionAdministrative = req.SubdivisionAdministrative,
            ParentId                 = req.ParentId,
        };
        _db.Centres.Add(centre);
        await _db.SaveChangesAsync();

        await _db.Entry(centre).Reference(c => c.Parent).LoadAsync();
        return ToDto(centre);
    }

    public async Task<CentreDto?> UpdateAsync(int id, UpdateCentreRequest req)
    {
        var centre = await _db.Centres.FindAsync(id);
        if (centre is null) return null;

        centre.Code                     = req.Code;
        centre.Name                     = req.Name;
        centre.IsActive                 = req.IsActive;
        centre.SubdivisionAdministrative = req.SubdivisionAdministrative;
        centre.ParentId                 = req.ParentId;

        await _db.SaveChangesAsync();
        await _db.Entry(centre).Reference(c => c.Parent).LoadAsync();
        return ToDto(centre);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var centre = await _db.Centres.FindAsync(id);
        if (centre is null) return false;

        _db.Centres.Remove(centre);
        await _db.SaveChangesAsync();
        return true;
    }

    // ── User–Centre assignments ───────────────────────────────────────────

    public async Task<PagedResult<CentreUserDto>> GetCentreUsersAsync(
        int centreId, int page, int pageSize, string? search)
    {
        var query = _db.UserCentres
            .Where(uc => uc.CentreId == centreId)
            .Include(uc => uc.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(uc =>
                (uc.User.UserName != null && uc.User.UserName.ToLower().Contains(s)) ||
                (uc.User.Email    != null && uc.User.Email.ToLower().Contains(s)));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(uc => uc.IsPrimary)
            .ThenBy(uc => uc.User.UserName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(uc => new CentreUserDto(
                uc.UserId,
                uc.User.UserName!,
                uc.User.Email,
                uc.IsPrimary))
            .ToListAsync();

        return new PagedResult<CentreUserDto>(items, totalCount, page, pageSize);
    }

    public async Task<IEnumerable<UserCentreAssignmentDto>> GetCentresForUserAsync(long userId)
    {
        return await _db.UserCentres
            .Where(uc => uc.UserId == userId)
            .Include(uc => uc.Centre)
            .OrderByDescending(uc => uc.IsPrimary)
            .ThenBy(uc => uc.Centre.Name)
            .Select(uc => new UserCentreAssignmentDto(
                uc.CentreId,
                uc.Centre.Code,
                uc.Centre.Name,
                uc.Centre.SubdivisionAdministrative,
                uc.IsPrimary))
            .ToListAsync();
    }

    public async Task<(string? Primary, IEnumerable<string> All)> GetUserCentresAsync(long userId)
    {
        var links = await _db.UserCentres
            .Where(uc => uc.UserId == userId)
            .Include(uc => uc.Centre)
            .ToListAsync();

        var primary = links.FirstOrDefault(uc => uc.IsPrimary)?.Centre?.Code;
        var all     = links.Select(uc => uc.Centre?.Code).OfType<string>().ToList();
        return (primary, all);
    }

    public async Task AssignUserCentreAsync(AssignUserCentreRequest req)
    {
        // If setting this as primary, clear any existing primary
        if (req.IsPrimary)
        {
            var existing = await _db.UserCentres
                .Where(uc => uc.UserId == req.UserId && uc.IsPrimary)
                .ToListAsync();
            existing.ForEach(uc => uc.IsPrimary = false);
        }

        var link = await _db.UserCentres
            .FirstOrDefaultAsync(uc => uc.UserId == req.UserId && uc.CentreId == req.CentreId);

        if (link is null)
        {
            _db.UserCentres.Add(new UserCentre
            {
                UserId    = req.UserId,
                CentreId  = req.CentreId,
                IsPrimary = req.IsPrimary,
            });
        }
        else
        {
            link.IsPrimary = req.IsPrimary;
        }

        await _db.SaveChangesAsync();
    }

    public async Task RemoveUserCentreAsync(long userId, int centreId)
    {
        var link = await _db.UserCentres
            .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.CentreId == centreId);
        if (link is null) return;

        _db.UserCentres.Remove(link);
        await _db.SaveChangesAsync();
    }

    public async Task TryAssignCentreByCodeAsync(long userId, string centreCode)
    {
        var centre = await _db.Centres
            .FirstOrDefaultAsync(c => c.Code == centreCode && c.IsActive);
        if (centre is null) return;

        await AssignUserCentreAsync(
            new AssignUserCentreRequest(userId, centre.Id, IsPrimary: true));
    }

    // ── Mapping helpers ───────────────────────────────────────────────────

    private static CentreDto ToDto(Centre c) =>
        new(c.Id, c.Code, c.Name, c.IsActive,
            c.SubdivisionAdministrative, c.ParentId, c.Parent?.Name);

    private static CentreTreeDto BuildTree(Centre node, List<Centre> all) =>
        new(node.Id, node.Code, node.Name, node.IsActive,
            node.SubdivisionAdministrative, node.ParentId,
            all.Where(c => c.ParentId == node.Id)
               .Select(c => BuildTree(c, all))
               .ToList());
}
