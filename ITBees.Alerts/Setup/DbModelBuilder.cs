using ITBees.Alerts.DbModels;
using ITBees.Notifications.DbModels;
using Microsoft.EntityFrameworkCore;

namespace ITBees.Alerts.Setup;

/// <summary>
/// Registers the alerting tables on the host's DbContext. Call from
/// <c>OnModelCreating</c> the same way as the other ITBees libraries.
/// </summary>
public class DbModelBuilder
{
    public static void Register(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AlertRule>().HasKey(x => x.Guid);
        modelBuilder.Entity<AlertRule>().Property(x => x.Discriminator).HasMaxLength(128).IsRequired();
        modelBuilder.Entity<AlertRule>().Property(x => x.ScopeKind).HasMaxLength(64).IsRequired();
        modelBuilder.Entity<AlertRule>().Property(x => x.AlertKey).HasMaxLength(128).IsRequired();
        modelBuilder.Entity<AlertRule>().Property(x => x.Description).HasMaxLength(512);
        modelBuilder.Entity<AlertRule>().Property(x => x.CustomMessage).HasMaxLength(512);
        // The routing lookup on every raised alert - keep it covered.
        modelBuilder.Entity<AlertRule>().HasIndex(x => new { x.Discriminator, x.AlertKey, x.ScopeKind });

        modelBuilder.Entity<AlertRuleTarget>().HasKey(x => x.Guid);
        modelBuilder.Entity<AlertRuleTarget>().HasIndex(x => new { x.AlertRuleGuid, x.ScopeId }).IsUnique();
        // Routing asks "which rules cover this parking?" - the other way round.
        modelBuilder.Entity<AlertRuleTarget>().HasIndex(x => x.ScopeId);
        modelBuilder.Entity<AlertRuleTarget>()
            .HasOne(x => x.AlertRule)
            .WithMany(x => x.Targets)
            .HasForeignKey(x => x.AlertRuleGuid)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AlertContact>().HasKey(x => x.Guid);
        modelBuilder.Entity<AlertContact>().Property(x => x.Discriminator).HasMaxLength(128).IsRequired();
        modelBuilder.Entity<AlertContact>().Property(x => x.OwnerKind).HasMaxLength(64).IsRequired();
        modelBuilder.Entity<AlertContact>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<AlertContact>().Property(x => x.Email).HasMaxLength(320);
        modelBuilder.Entity<AlertContact>().Property(x => x.Phone).HasMaxLength(32);
        modelBuilder.Entity<AlertContact>().HasIndex(x => new { x.Discriminator, x.OwnerKind, x.OwnerId });

        modelBuilder.Entity<AlertRuleRecipient>().HasKey(x => x.Guid);
        modelBuilder.Entity<AlertRuleRecipient>().HasIndex(x => new { x.AlertRuleGuid, x.AlertContactGuid }).IsUnique();
        modelBuilder.Entity<AlertRuleRecipient>()
            .HasOne(x => x.AlertRule)
            .WithMany(x => x.Recipients)
            .HasForeignKey(x => x.AlertRuleGuid)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AlertRuleRecipient>()
            .HasOne(x => x.AlertContact)
            .WithMany()
            .HasForeignKey(x => x.AlertContactGuid)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AlertOccurrence>().HasKey(x => x.Guid);
        modelBuilder.Entity<AlertOccurrence>().Property(x => x.AlertKey).HasMaxLength(128).IsRequired();
        modelBuilder.Entity<AlertOccurrence>().Property(x => x.ScopeKind).HasMaxLength(64).IsRequired();
        modelBuilder.Entity<AlertOccurrence>().Property(x => x.Title).HasMaxLength(400);
        modelBuilder.Entity<AlertOccurrence>().Property(x => x.SourceId).HasMaxLength(128);
        modelBuilder.Entity<AlertOccurrence>().Property(x => x.SourceName).HasMaxLength(200);
        // Deduplication runs on every raised alert, so this index carries the hot path.
        modelBuilder.Entity<AlertOccurrence>().HasIndex(x => new { x.ScopeKind, x.ScopeId, x.CreatedUtc });

        modelBuilder.Entity<AlertDelivery>().HasKey(x => x.Guid);
        modelBuilder.Entity<AlertDelivery>().Property(x => x.Discriminator).HasMaxLength(128).IsRequired();
        modelBuilder.Entity<AlertDelivery>().Property(x => x.Target).HasMaxLength(320);
        modelBuilder.Entity<AlertDelivery>().Property(x => x.Subject).HasMaxLength(400);
        modelBuilder.Entity<AlertDelivery>().Property(x => x.Error).HasMaxLength(500);
        // The outbox poll: pending rows that are due.
        modelBuilder.Entity<AlertDelivery>().HasIndex(x => new { x.Status, x.NotBeforeUtc, x.CreatedUtc });
        modelBuilder.Entity<AlertDelivery>()
            .HasOne(x => x.AlertOccurrence)
            .WithMany()
            .HasForeignKey(x => x.AlertOccurrenceGuid)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Notification>()
            .HasDiscriminator<string>("NotificationType")
            .HasValue<Notification>("notification")
            .HasValue<DiscriminatedNotification>("discriminated");
        modelBuilder.Entity<DiscriminatedNotification>().Property(x => x.Discriminator)
            .HasMaxLength(128).IsRequired();
        modelBuilder.Entity<DiscriminatedNotification>().Property(x => x.ScopeKind).HasMaxLength(64);
        modelBuilder.Entity<DiscriminatedNotification>()
            .HasIndex(x => new { x.Discriminator, x.ScopeKind, x.ScopeId });
    }
}
