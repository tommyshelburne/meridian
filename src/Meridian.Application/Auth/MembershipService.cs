using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Users;

namespace Meridian.Application.Auth;

public record MemberSummary(
    Guid UserId,
    string Email,
    string FullName,
    UserRole Role,
    MembershipStatus Status,
    DateTimeOffset InvitedAt,
    DateTimeOffset? AcceptedAt);

public class MembershipService
{
    private readonly IUserRepository _users;
    private readonly IUserTenantRepository _memberships;
    private readonly IAuthTokenRepository _authTokens;
    private readonly IPasswordHasher _passwords;
    private readonly ITokenHasher _tokenHasher;
    private readonly IEmailSender _email;
    private readonly AuthEmailOptions _emailOptions;

    public MembershipService(
        IUserRepository users,
        IUserTenantRepository memberships,
        IAuthTokenRepository authTokens,
        IPasswordHasher passwords,
        ITokenHasher tokenHasher,
        IEmailSender email,
        AuthEmailOptions emailOptions)
    {
        _users = users;
        _memberships = memberships;
        _authTokens = authTokens;
        _passwords = passwords;
        _tokenHasher = tokenHasher;
        _email = email;
        _emailOptions = emailOptions;
    }

    public async Task<IReadOnlyList<MemberSummary>> GetMembersAsync(Guid tenantId, CancellationToken ct)
    {
        var memberships = await _memberships.GetForTenantAsync(tenantId, ct);
        var active = memberships
            .Where(m => m.Status != MembershipStatus.Removed)
            .ToList();
        var result = new List<MemberSummary>(active.Count);
        foreach (var m in active)
        {
            var user = await _users.GetByIdAsync(m.UserId, ct);
            if (user is null) continue;
            result.Add(new MemberSummary(
                user.Id, user.Email, user.FullName, m.Role, m.Status, m.InvitedAt, m.AcceptedAt));
        }
        return result.OrderBy(r => r.FullName).ToList();
    }

    public async Task<ServiceResult> InviteAsync(
        Guid tenantId, string email, string fullName, UserRole role,
        Guid invitedByUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return ServiceResult.Fail("Email is required.");
        if (string.IsNullOrWhiteSpace(fullName))
            return ServiceResult.Fail("Name is required.");

        var user = await _users.GetByEmailAsync(email, ct);
        var isNewUser = user is null;

        string? inviteToken = null;
        if (user is null)
        {
            var placeholder = _tokenHasher.GenerateToken();
            try
            {
                user = User.CreateWithPassword(email, fullName, _passwords.Hash(placeholder));
            }
            catch (ArgumentException ex)
            {
                return ServiceResult.Fail(ex.Message);
            }
            await _users.AddAsync(user, ct);

            inviteToken = _tokenHasher.GenerateToken();
            var reset = PasswordResetToken.Issue(user.Id, _tokenHasher.Hash(inviteToken));
            await _authTokens.AddResetAsync(reset, ct);
        }
        else
        {
            var existing = await _memberships.GetAsync(user.Id, tenantId, ct);
            if (existing is { Status: not MembershipStatus.Removed })
                return ServiceResult.Fail("This user is already a member of the workspace.");
        }

        var membership = UserTenant.Invite(user.Id, tenantId, role, invitedByUserId);
        if (isNewUser) membership.Accept(); // New users become active on signup
        await _memberships.AddAsync(membership, ct);

        await _memberships.SaveChangesAsync(ct);

        await SendInviteEmailAsync(user, inviteToken, ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ChangeRoleAsync(
        Guid tenantId, Guid userId, UserRole role, CancellationToken ct)
    {
        var m = await _memberships.GetAsync(userId, tenantId, ct);
        if (m is null || m.Status == MembershipStatus.Removed)
            return ServiceResult.Fail("Member not found.");
        try { m.ChangeRole(role); }
        catch (InvalidOperationException ex) { return ServiceResult.Fail(ex.Message); }
        await _memberships.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RemoveAsync(
        Guid tenantId, Guid userId, CancellationToken ct)
    {
        var m = await _memberships.GetAsync(userId, tenantId, ct);
        if (m is null || m.Status == MembershipStatus.Removed)
            return ServiceResult.Fail("Member not found.");
        m.Remove();
        await _memberships.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    private Task SendInviteEmailAsync(User user, string? inviteToken, CancellationToken ct)
    {
        var subject = "You've been invited to Meridian";
        string body;
        if (inviteToken is null)
        {
            var link = $"{_emailOptions.BaseUrl.TrimEnd('/')}/login";
            body = $"<p>Hi {WebEncode(user.FullName)},</p>" +
                   $"<p>You've been added to a Meridian workspace. Sign in to get started:</p>" +
                   $"<p><a href=\"{link}\">Sign in</a></p>";
        }
        else
        {
            var link = $"{_emailOptions.BaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(inviteToken)}";
            body = $"<p>Hi {WebEncode(user.FullName)},</p>" +
                   $"<p>You've been invited to a Meridian workspace. Set your password to accept the invitation:</p>" +
                   $"<p><a href=\"{link}\">Accept invitation</a></p>" +
                   $"<p>This link expires in 1 hour.</p>";
        }
        return _email.SendAsync(new EmailMessage(
            user.Email, _emailOptions.FromAddress, _emailOptions.FromName, subject, body), ct);
    }

    private static string WebEncode(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
