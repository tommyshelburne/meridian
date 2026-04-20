using Meridian.Domain.Users;

namespace Meridian.Application.Ports;

public interface IAuthTokenRepository
{
    Task<EmailVerificationToken?> FindVerificationAsync(string tokenHash, CancellationToken ct);
    Task<PasswordResetToken?> FindResetAsync(string tokenHash, CancellationToken ct);
    Task AddVerificationAsync(EmailVerificationToken token, CancellationToken ct);
    Task AddResetAsync(PasswordResetToken token, CancellationToken ct);
    Task InvalidateVerificationTokensForUserAsync(Guid userId, CancellationToken ct);
    Task InvalidateResetTokensForUserAsync(Guid userId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
