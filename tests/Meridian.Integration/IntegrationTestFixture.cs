using Meridian.Application.Auth;
using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Auth;
using Meridian.Infrastructure.Persistence;
using Meridian.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Integration;

public sealed class IntegrationTestFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<MeridianDbContext> _options;

    public TenantContext TenantContext { get; } = new();
    public IPasswordHasher Passwords { get; } = new BCryptPasswordHasher();
    public ITokenHasher TokenHasher { get; } = new TokenHasher();
    public ITotpService Totp { get; } = new TotpService();
    public InMemoryEmailSender Emails { get; } = new();
    public AuthEmailOptions EmailOptions { get; } = new()
    {
        BaseUrl = "https://portal.test",
        FromAddress = "noreply@meridian.test",
        FromName = "Meridian"
    };

    public IntegrationTestFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<MeridianDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = NewDbContext();
        ctx.Database.EnsureCreated();
    }

    public MeridianDbContext NewDbContext() => new(_options, TenantContext);

    public AuthService BuildAuthService(MeridianDbContext db) =>
        new(
            new TenantRepository(db),
            new UserRepository(db),
            new UserTenantRepository(db),
            new AuthTokenRepository(db),
            Passwords, Totp, TokenHasher, Emails, EmailOptions);

    public MembershipService BuildMembershipService(MeridianDbContext db) =>
        new(
            new UserRepository(db),
            new UserTenantRepository(db),
            new AuthTokenRepository(db),
            Passwords, TokenHasher, Emails, EmailOptions);

    public void Dispose()
    {
        _connection.Dispose();
    }
}

public sealed class InMemoryEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = new();

    public Task<Meridian.Application.Common.ServiceResult<SendResult>> SendAsync(
        EmailMessage message, CancellationToken ct)
    {
        Sent.Add(message);
        return Task.FromResult(Meridian.Application.Common.ServiceResult<SendResult>.Ok(
            new SendResult(Guid.NewGuid().ToString())));
    }
}
