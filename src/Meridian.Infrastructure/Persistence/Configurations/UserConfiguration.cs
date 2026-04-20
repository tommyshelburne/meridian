using Meridian.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");
        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        builder.Property(u => u.FullName).HasColumnName("full_name").HasMaxLength(200).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(256);
        builder.Property(u => u.PasswordUpdatedAt).HasColumnName("password_updated_at");
        builder.Property(u => u.EmailVerified).HasColumnName("email_verified");
        builder.Property(u => u.EmailVerifiedAt).HasColumnName("email_verified_at");
        builder.Property(u => u.TotpSecret).HasColumnName("totp_secret").HasMaxLength(128);
        builder.Property(u => u.TotpEnrolledAt).HasColumnName("totp_enrolled_at");
        builder.Property(u => u.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        builder.Property(u => u.FailedLoginAttempts).HasColumnName("failed_login_attempts");
        builder.Property(u => u.LockedUntil).HasColumnName("locked_until");
        builder.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(u => u.Email).IsUnique();
    }
}
