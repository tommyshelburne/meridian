using Meridian.Application.Auth;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Meridian.Domain.Users;
using Meridian.Infrastructure.Auth;

namespace Meridian.Unit.Application.Auth;

public class AuthServiceTestFixture
{
    public FakeTenantRepository Tenants { get; } = new();
    public FakeUserRepository Users { get; } = new();
    public FakeUserTenantRepository Memberships { get; } = new();
    public FakeAuthTokenRepository AuthTokens { get; } = new();
    public FakeEmailSender Emails { get; } = new();
    public IPasswordHasher PasswordHasher { get; } = new BCryptPasswordHasher();
    public ITotpService Totp { get; } = new TotpService();
    public ITokenHasher TokenHasher { get; } = new TokenHasher();

    public AuthService BuildService()
    {
        var options = new AuthEmailOptions { BaseUrl = "https://portal.test" };
        return new AuthService(Tenants, Users, Memberships, AuthTokens,
            PasswordHasher, Totp, TokenHasher, Emails, options);
    }
}

public class FakeTenantRepository : ITenantRepository
{
    public List<Tenant> Items { get; } = new();
    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(t => t.Id == id));
    public Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(t => t.Slug == slug.ToLowerInvariant()));
    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) =>
        Task.FromResult(Items.Any(t => t.Slug == slug.ToLowerInvariant()));
    public Task<IReadOnlyList<Tenant>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var set = ids.ToHashSet();
        return Task.FromResult<IReadOnlyList<Tenant>>(Items.Where(t => set.Contains(t.Id)).ToList());
    }
    public Task<IReadOnlyList<Tenant>> GetActiveTenantsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Tenant>>(Items.Where(t => t.Status != TenantStatus.Suspended).ToList());
    public Task AddAsync(Tenant tenant, CancellationToken ct) { Items.Add(tenant); return Task.CompletedTask; }
    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}

public class FakeUserRepository : IUserRepository
{
    public List<User> Items { get; } = new();
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(u => u.Id == id));
    public Task<User?> GetByEmailAsync(string email, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(u => u.Email == email.Trim().ToLowerInvariant()));
    public Task<bool> EmailExistsAsync(string email, CancellationToken ct) =>
        Task.FromResult(Items.Any(u => u.Email == email.Trim().ToLowerInvariant()));
    public Task AddAsync(User user, CancellationToken ct) { Items.Add(user); return Task.CompletedTask; }
    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}

public class FakeUserTenantRepository : IUserTenantRepository
{
    public List<UserTenant> Items { get; } = new();
    public Task<UserTenant?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(m => m.Id == id));
    public Task<UserTenant?> GetAsync(Guid userId, Guid tenantId, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(m => m.UserId == userId && m.TenantId == tenantId));
    public Task<IReadOnlyList<UserTenant>> GetForUserAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UserTenant>>(Items.Where(m => m.UserId == userId).ToList());
    public Task<IReadOnlyList<UserTenant>> GetForTenantAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UserTenant>>(Items.Where(m => m.TenantId == tenantId).ToList());
    public Task AddAsync(UserTenant membership, CancellationToken ct) { Items.Add(membership); return Task.CompletedTask; }
    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}

public class FakeAuthTokenRepository : IAuthTokenRepository
{
    public List<EmailVerificationToken> Verifications { get; } = new();
    public List<PasswordResetToken> Resets { get; } = new();

    public Task<EmailVerificationToken?> FindVerificationAsync(string tokenHash, CancellationToken ct) =>
        Task.FromResult(Verifications.FirstOrDefault(t => t.TokenHash == tokenHash));
    public Task<PasswordResetToken?> FindResetAsync(string tokenHash, CancellationToken ct) =>
        Task.FromResult(Resets.FirstOrDefault(t => t.TokenHash == tokenHash));
    public Task AddVerificationAsync(EmailVerificationToken token, CancellationToken ct) { Verifications.Add(token); return Task.CompletedTask; }
    public Task AddResetAsync(PasswordResetToken token, CancellationToken ct) { Resets.Add(token); return Task.CompletedTask; }
    public Task InvalidateVerificationTokensForUserAsync(Guid userId, CancellationToken ct)
    {
        foreach (var t in Verifications.Where(t => t.UserId == userId && !t.IsConsumed && !t.IsExpired))
            try { t.Consume(); } catch { }
        return Task.CompletedTask;
    }
    public Task InvalidateResetTokensForUserAsync(Guid userId, CancellationToken ct)
    {
        foreach (var t in Resets.Where(t => t.UserId == userId && !t.IsConsumed && !t.IsExpired))
            try { t.Consume(); } catch { }
        return Task.CompletedTask;
    }
    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}

public class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = new();
    public Task<ServiceResult<SendResult>> SendAsync(EmailMessage message, CancellationToken ct)
    {
        Sent.Add(message);
        return Task.FromResult(ServiceResult<SendResult>.Ok(new SendResult(Guid.NewGuid().ToString("N"))));
    }
}
