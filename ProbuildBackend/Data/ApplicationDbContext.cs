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
            .HasOne(j => j.User)
            .WithMany()
            .HasForeignKey(j => j.UserId)
            .IsRequired();

        base.OnModelCreating(modelBuilder);
    }
}
