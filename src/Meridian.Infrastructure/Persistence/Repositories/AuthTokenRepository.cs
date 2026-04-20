using Meridian.Application.Ports;
using Meridian.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public class AuthTokenRepository : IAuthTokenRepository
{
    private readonly MeridianDbContext _db;

    public AuthTokenRepository(MeridianDbContext db) => _db = db;

    public Task<EmailVerificationToken?> FindVerificationAsync(string tokenHash, CancellationToken ct)
        => _db.EmailVerificationTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public Task<PasswordResetToken?> FindResetAsync(string tokenHash, CancellationToken ct)
        => _db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AddVerificationAsync(EmailVerificationToken token, CancellationToken ct)
        => await _db.EmailVerificationTokens.AddAsync(token, ct);

    public async Task AddResetAsync(PasswordResetToken token, CancellationToken ct)
        => await _db.PasswordResetTokens.AddAsync(token, ct);

    public async Task InvalidateVerificationTokensForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await _db.EmailVerificationTokens
            .Where(t => t.UserId == userId && t.ConsumedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, now), ct);
    }

    public async Task InvalidateResetTokensForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await _db.PasswordResetTokens
            .Where(t => t.UserId == userId && t.ConsumedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, now), ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
