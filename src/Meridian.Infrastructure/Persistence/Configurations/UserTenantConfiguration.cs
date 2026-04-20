using Meridian.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class UserTenantConfiguration : IEntityTypeConfiguration<UserTenant>
{
    public void Configure(EntityTypeBuilder<UserTenant> builder)
    {
        builder.ToTable("user_tenants");
        builder.HasKey(ut => ut.Id);
        builder.Property(ut => ut.Id).HasColumnName("id");
        builder.Property(ut => ut.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(ut => ut.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(ut => ut.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(30);
        builder.Property(ut => ut.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        builder.Property(ut => ut.InvitedByUserId).HasColumnName("invited_by_user_id");
        builder.Property(ut => ut.InvitedAt).HasColumnName("invited_at");
        builder.Property(ut => ut.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(ut => ut.RemovedAt).HasColumnName("removed_at");

        builder.HasIndex(ut => new { ut.UserId, ut.TenantId }).IsUnique();
        builder.HasIndex(ut => new { ut.TenantId, ut.Status });
    }
}
