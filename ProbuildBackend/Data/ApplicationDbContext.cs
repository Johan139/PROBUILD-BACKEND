using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
    public DbSet<UserModel> Users { get; set; }
    public DbSet<ProjectModel> Projects { get; set; }
    public DbSet<JobModel> Jobs { get; set; }
    public DbSet<BidModel> Bids { get; set; }
    public DbSet<NotificationModel> Notifications { get; set; }

    public DbSet<DocumentProcessingResult> DocumentProcessingResults { get; set; }
    public DbSet<AddressModel> JobAddresses { get; set; }
    public DbSet<JobDocumentModel> JobDocuments { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {   
        modelBuilder.Entity<ProjectModel>()
            .HasOne(p => p.Foreman)
            .WithMany()
            .HasForeignKey(p => p.ForemanId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProjectModel>()
            .HasOne(p => p.Contractor)
            .WithMany()
            .HasForeignKey(p => p.ContractorId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProjectModel>()
            .HasOne(p => p.SubContractorWallStructure)
            .WithMany()
            .HasForeignKey(p => p.SubContractorWallStructureId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<ProjectModel>()
            .HasOne(p => p.SubContractorWallInsulation)
            .WithMany()
            .HasForeignKey(p => p.SubContractorWallInsulationId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<ProjectModel>()
            .HasOne(p => p.SubContractorRoofStructure)
            .WithMany()
            .HasForeignKey(p => p.SubContractorRoofStructureId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<ProjectModel>()
            .HasOne(p => p.SubContractorRoofType)
            .WithMany()
            .HasForeignKey(p => p.SubContractorRoofTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProjectModel>()
            .HasOne(p => p.SubContractorRoofInsulation)
            .WithMany()
            .HasForeignKey(p => p.SubContractorRoofInsulationId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<ProjectModel>()
            .HasOne(p => p.SubContractorFoundation)
            .WithMany()
            .HasForeignKey(p => p.SubContractorFoundationId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<ProjectModel>()
            .HasOne(p => p.SubContractorFinishes)
            .WithMany()
            .HasForeignKey(p => p.SubContractorFinishesId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<ProjectModel>()
            .HasOne(p => p.SubContractorElectricalSupplyNeeds)
            .WithMany()
            .HasForeignKey(p => p.SubContractorElectricalSupplyNeedsId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<BidModel>()
               .HasOne(b => b.Job)
               .WithMany(p => p.Bids)
               .HasForeignKey(b => b.JobId);

        modelBuilder.Entity<BidModel>()
            .HasOne(b => b.User)
            .WithMany(u => u.Bids)
            .HasForeignKey(b => b.UserId);


        modelBuilder.Entity<NotificationModel>()
            .HasOne(n => n.Project)
            .WithMany(p => p.Notifications)
            .HasForeignKey(n => n.ProjectId);

        modelBuilder.Entity<NotificationModel>()
            .HasOne(n => n.User)
            .WithMany(u => u.Notifications)
            .HasForeignKey(n => n.UserId);

        modelBuilder.Entity<JobModel>()
                    .HasMany(j => j.Documents)
                    .WithOne()
                    .HasForeignKey(d => d.JobId)
                    .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<JobModel>()
            .HasOne(j => j.User)
            .WithMany()
            .HasForeignKey(j => j.UserId);

        modelBuilder.Entity<JobModel>()
            .HasMany(j => j.Bids)
            .WithOne()
            .HasForeignKey(b => b.JobId);

        modelBuilder.Entity<AddressModel>(entity =>
        {
            entity.ToTable("JobAddress");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Latitude).HasColumnType("decimal(10,8)").IsRequired(false);
            entity.Property(e => e.Longitude).HasColumnType("decimal(11,8)").IsRequired(false);
            // Explicitly map properties to snake_case column names
            entity.Property(e => e.StreetNumber).HasColumnName("street_number");
            entity.Property(e => e.StreetName).HasColumnName("street_name");
            entity.Property(e => e.City).HasColumnName("city");
            entity.Property(e => e.State).HasColumnName("state");
            entity.Property(e => e.PostalCode).HasColumnName("postal_code");
            entity.Property(e => e.Country).HasColumnName("country");
            entity.Property(e => e.Latitude).HasColumnName("latitude");
            entity.Property(e => e.Longitude).HasColumnName("longitude");
            entity.Property(e => e.FormattedAddress).HasColumnName("formatted_address").HasMaxLength(255).IsRequired(false);
            entity.Property(e => e.GooglePlaceId).HasColumnName("google_place_id").HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.JobId).HasColumnName("JobId").IsRequired();
        });


        base.OnModelCreating(modelBuilder);
    }
}
