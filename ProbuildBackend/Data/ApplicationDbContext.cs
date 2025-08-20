using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;

public class ApplicationDbContext : DbContext, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
    public DbSet<UserModel> Users { get; set; }
    public DbSet<ClientDetailsModel> ClientDetails { get; set; }
    public DbSet<ProjectModel> Projects { get; set; }
    public DbSet<JobModel> Jobs { get; set; }
    public DbSet<JobsTermsAgreement> JobsTermsAgreement { get; set; }
    public DbSet<BidModel> Bids { get; set; }
    public DbSet<NotificationModel> Notifications { get; set; }
    public DbSet<NotificationView> NotificationViews { get; set; }
    public DbSet<JobAssignmentModel> JobAssignments { get; set; }
    public DbSet<SubtaskNoteDocumentModel> SubtaskNoteDocument { get; set; }
    public DbSet<ProfileDocuments> ProfileDocuments { get; set; }
    public DbSet<DocumentProcessingLogModel> DocumentProcessingLog { get; set; }
    public DbSet<UserAddressModel> UserAddress { get; set; }
    public DbSet<PaymentRecord> PaymentRecords { get; set; }
    public DbSet<PaymentRecordHistoryModel> PaymentRecordsHistory { get; set; }
    public DbSet<UserTermsAgreementModel> UserTermsAgreement { get; set; }
    public DbSet<SubtaskNoteModel> SubtaskNote { get; set; }
    public DbSet<SubtaskNoteUserModel> SubtaskNoteUser { get; set; }
    public DbSet<StripeModel> Subscriptions { get; set; }
    public DbSet<JobSubtasksModel> JobSubtasks { get; set; }
    public DbSet<DocumentProcessingResult> DocumentProcessingResults { get; set; }
    public DbSet<AddressModel> JobAddresses { get; set; }
    public DbSet<JobDocumentModel> JobDocuments { get; set; }
    public DbSet<LogosModel> Logos { get; set; }
    public DbSet<Quote> Quotes { get; set; }
    public DbSet<QuoteRow> QuoteRows { get; set; }
    public DbSet<QuoteExtraCost> QuoteExtraCosts { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<TeamMember> TeamMembers { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<TeamMemberPermission> TeamMemberPermissions { get; set; }
    public DbSet<Conversation> Conversations { get; set; }
    public DbSet<ConversationPrompt> ConversationPrompts { get; set; }

      protected override void OnModelCreating(ModelBuilder modelBuilder)
      {
        modelBuilder.Entity<NotificationView>().HasNoKey().ToView("vw_Notifications");

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
            .HasOne(n => n.Job)
            .WithMany(j => j.Notifications)
            .HasForeignKey(n => n.JobId);

        // Keep the existing UserId relationship
        modelBuilder.Entity<NotificationModel>()
            .HasOne(n => n.User)
            .WithMany(u => u.Notifications)
            .HasForeignKey(n => n.UserId);

        modelBuilder.Entity<NotificationModel>()
            .HasOne(n => n.Sender)
            .WithMany()  // No collection needed on the User side for sent notifications
            .HasForeignKey(n => n.SenderId)
            .OnDelete(DeleteBehavior.Restrict);  // Prevent cascade delete conflicts

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
        modelBuilder.Entity<UserAddressModel>(entity =>
        {
            entity.ToTable("UserAddress");
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
            entity.Property(e => e.UserId).HasColumnName("UserId").IsRequired();
        });

        modelBuilder.Entity<JobAssignmentModel>()
            .HasKey(ja => new { ja.UserId, ja.JobId });

        modelBuilder.Entity<JobAssignmentModel>()
            .HasOne(ja => ja.Job)
            .WithMany()
            .HasForeignKey(ja => ja.JobId)
            .IsRequired();

        modelBuilder.Entity<JobAssignmentModel>()
            .Property(ja => ja.JobRole)
            .HasMaxLength(450);

        modelBuilder.Entity<Quote>()
            .HasMany(q => q.ExtraCosts)
            .WithOne(ec => ec.Quote)
            .HasForeignKey(ec => ec.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Quote>()
           .HasOne(q => q.Logo)
           .WithMany()
           .HasForeignKey(q => q.LogoId)
           .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<TeamMember>()
            .HasIndex(t => new { t.InviterId, t.Email })
            .IsUnique();

        modelBuilder.Entity<TeamMemberPermission>()
           .HasKey(tp => new { tp.TeamMemberId, tp.PermissionId });

        modelBuilder.Entity<TeamMemberPermission>()
           .HasOne(tp => tp.TeamMember)
           .WithMany(t => t.TeamMemberPermissions)
           .HasForeignKey(tp => tp.TeamMemberId);

        modelBuilder.Entity<TeamMemberPermission>()
           .HasOne(tp => tp.Permission)
           .WithMany(p => p.TeamMemberPermissions)
           .HasForeignKey(tp => tp.PermissionId);

        modelBuilder.Entity<PaymentRecordHistoryModel>()
            .HasKey(tp => new { tp.PaymentRecordHistoryId });

 
        modelBuilder.Entity<Conversation>()
            .HasMany<JobDocumentModel>()
            .WithOne()
            .HasForeignKey(d => d.ConversationId);

       modelBuilder.Entity<Conversation>()
           .HasMany(c => c.PromptKeys)
           .WithOne(cp => cp.Conversation)
           .HasForeignKey(cp => cp.ConversationId);

        modelBuilder.Entity<ConversationPrompt>()
            .ToTable("ConversationPrompts");

       base.OnModelCreating(modelBuilder);
      }
}
