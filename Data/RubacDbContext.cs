using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RubacCore.Models;

namespace RubacCore.Data;

public class RubacDbContext : IdentityDbContext<
    ApplicationUser,
    ApplicationRole,
    long,
    IdentityUserClaim<long>,
    ApplicationUserRole,
    IdentityUserLogin<long>,
    IdentityRoleClaim<long>,
    IdentityUserToken<long>>
{
    public RubacDbContext(DbContextOptions<RubacDbContext> options) : base(options) { }

    public DbSet<AuditLog>         AuditLogs        { get; set; } = null!;
    public DbSet<Centre>           Centres          { get; set; } = null!;
    public DbSet<UserCentre>       UserCentres      { get; set; } = null!;
    public DbSet<UserApplication>  UserApplications { get; set; } = null!;
    public DbSet<Permission>       Permissions      { get; set; } = null!;
    public DbSet<RolePermission>   RolePermissions  { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ── Table names ────────────────────────────────────────────
        builder.Entity<ApplicationUser>().ToTable("Users");
        builder.Entity<ApplicationRole>().ToTable("Roles");
        builder.Entity<ApplicationUserRole>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<long>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<long>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<long>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<long>>().ToTable("UserTokens");
        builder.Entity<AuditLog>().ToTable("AuditLogs")
            .HasIndex(a => a.OccurredAt);

        // ── UserRole navigations ───────────────────────────────────
        builder.Entity<ApplicationUserRole>(b =>
        {
            b.HasOne(ur => ur.User)
             .WithMany(u => u.UserRoles)
             .HasForeignKey(ur => ur.UserId);

            b.HasOne(ur => ur.Role)
             .WithMany(r => r.UserRoles)
             .HasForeignKey(ur => ur.RoleId);
        });

        // ── Centre hierarchy (self-referencing tree) ───────────────
        builder.Entity<Centre>(b =>
        {
            b.ToTable("Centres");
            b.HasIndex(c => c.Code).IsUnique();
            b.Property(c => c.SubdivisionAdministrative)
             .HasConversion<string>();

            b.HasOne(c => c.Parent)
             .WithMany(c => c.Children)
             .HasForeignKey(c => c.ParentId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── UserCentre junction ────────────────────────────────────
        builder.Entity<UserCentre>(b =>
        {
            b.ToTable("UserCentres");
            b.HasKey(uc => new { uc.UserId, uc.CentreId });

            b.HasOne(uc => uc.User)
             .WithMany(u => u.UserCentres)
             .HasForeignKey(uc => uc.UserId);

            b.HasOne(uc => uc.Centre)
             .WithMany(c => c.UserCentres)
             .HasForeignKey(uc => uc.CentreId);
        });

        // ── UserApplication junction ───────────────────────────────
        builder.Entity<UserApplication>(b =>
        {
            b.ToTable("UserApplications");
            b.HasKey(ua => new { ua.UserId, ua.ApplicationClientId });

            b.HasOne(ua => ua.User)
             .WithMany(u => u.UserApplications)
             .HasForeignKey(ua => ua.UserId);
        });

        // ── Permissions ────────────────────────────────────────────
        builder.Entity<Permission>(b =>
        {
            b.ToTable("Permissions");
            b.HasIndex(p => p.Name).IsUnique();
        });

        // ── RolePermission junction ────────────────────────────────
        builder.Entity<RolePermission>(b =>
        {
            b.ToTable("RolePermissions");
            b.HasKey(rp => new { rp.RoleId, rp.PermissionId });

            b.HasOne(rp => rp.Role)
             .WithMany(r => r.RolePermissions)
             .HasForeignKey(rp => rp.RoleId);

            b.HasOne(rp => rp.Permission)
             .WithMany(p => p.RolePermissions)
             .HasForeignKey(rp => rp.PermissionId);
        });
    }
}
