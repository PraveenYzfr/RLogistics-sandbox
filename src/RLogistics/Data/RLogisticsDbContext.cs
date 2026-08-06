using Microsoft.EntityFrameworkCore;
using RLogistics.Domain;

namespace RLogistics.Data;

public class RLogisticsDbContext(DbContextOptions<RLogisticsDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<DisposalRequest> Requests => Set<DisposalRequest>();
    public DbSet<AssetLine> Assets => Set<AssetLine>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Clarification> Clarifications => Set<Clarification>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<AppConfig> AppConfigs => Set<AppConfig>();
    public DbSet<EmailOutbox> EmailOutbox => Set<EmailOutbox>();
    public DbSet<TeamsOutbox> TeamsOutbox => Set<TeamsOutbox>();
    public DbSet<Vendor> Vendors => Set<Vendor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.DisplayName).HasMaxLength(200);
        });

        modelBuilder.Entity<Vendor>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.ServiceArea).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(256);
        });

        modelBuilder.Entity<DisposalRequest>(e =>
        {
            e.HasIndex(x => x.RequestNumber).IsUnique();
            e.Property(x => x.RequestNumber).HasMaxLength(32);
            e.Property(x => x.Site).HasMaxLength(200);
            e.Property(x => x.ContactName).HasMaxLength(200);
            e.Property(x => x.ContactEmail).HasMaxLength(256);
            e.Property(x => x.ContactPhone).HasMaxLength(40);
            e.Property(x => x.ContactDepartment).HasMaxLength(120);
            e.Property(x => x.FacilityCode).HasMaxLength(64);
            e.Property(x => x.Building).HasMaxLength(100);
            e.Property(x => x.Floor).HasMaxLength(40);
            e.Property(x => x.Room).HasMaxLength(40);
            e.Property(x => x.PickupAddressLine1).HasMaxLength(200);
            e.Property(x => x.PickupAddressLine2).HasMaxLength(200);
            e.Property(x => x.PickupCity).HasMaxLength(100);
            e.Property(x => x.PickupState).HasMaxLength(50);
            e.Property(x => x.PickupPostalCode).HasMaxLength(20);
            e.Property(x => x.PickupCountry).HasMaxLength(60);
            e.Property(x => x.ScheduledPickupSlot).HasMaxLength(80);
            e.HasOne(x => x.Requestor)
                .WithMany(u => u.OwnedRequests)
                .HasForeignKey(x => x.RequestorUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AssignedCoordinator)
                .WithMany(u => u.AssignedRequests)
                .HasForeignKey(x => x.AssignedCoordinatorUserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.TransportVendor)
                .WithMany()
                .HasForeignKey(x => x.TransportVendorId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ProcessingVendor)
                .WithMany()
                .HasForeignKey(x => x.ProcessingVendorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AssetLine>(e =>
        {
            e.Property(x => x.AssetType).HasMaxLength(100);
            e.Property(x => x.SerialNumber).HasMaxLength(100);
            e.Property(x => x.DeviceGuid).HasMaxLength(100);
            e.Property(x => x.Manufacturer).HasMaxLength(100);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.AssetTag).HasMaxLength(100);
            e.Property(x => x.Condition).HasMaxLength(80);
        });

        modelBuilder.Entity<EmailTemplate>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64);
        });

        modelBuilder.Entity<AppConfig>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(100);
        });

        modelBuilder.Entity<EmailOutbox>(e =>
        {
            e.Property(x => x.ToAddress).HasMaxLength(256);
            e.Property(x => x.Subject).HasMaxLength(500);
            e.Property(x => x.TemplateCode).HasMaxLength(64);
        });

        modelBuilder.Entity<TeamsOutbox>(e =>
        {
            e.Property(x => x.Channel).HasMaxLength(40);
            e.Property(x => x.ToHint).HasMaxLength(256);
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.ProviderResult).HasMaxLength(500);
        });
    }
}
