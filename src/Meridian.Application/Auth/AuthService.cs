using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Meridian.Domain.Users;

namespace Meridian.Application.Auth;

public class AuthService
{
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly IUserTenantRepository _memberships;
    private readonly IAuthTokenRepository _authTokens;
    private readonly IPasswordHasher _passwords;
    private readonly ITotpService _totp;
    private readonly ITokenHasher _tokenHasher;
    private readonly IEmailSender _email;
    private readonly AuthEmailOptions _emailOptions;

    public AuthService(
        ITenantRepository tenants,
        IUserRepository users,
        IUserTenantRepository memberships,
        IAuthTokenRepository authTokens,
        IPasswordHasher passwords,
        ITotpService totp,
        ITokenHasher tokenHasher,
        IEmailSender email,
        AuthEmailOptions emailOptions)
    {
        _tenants = tenants;
        _users = users;
        _memberships = memberships;
        _authTokens = authTokens;
        _passwords = passwords;
        _totp = totp;
        _tokenHasher = tokenHasher;
        _email = email;
        _emailOptions = emailOptions;
    }

    public async Task<ServiceResult<SignupResult>> SignupAsync(SignupRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 12)
            return ServiceResult<SignupResult>.Fail("Password must be at least 12 characters.");

        if (await _users.EmailExistsAsync(request.Email, ct))
            return ServiceResult<SignupResult>.Fail("An account already exists for that email.");

        if (await _tenants.SlugExistsAsync(request.TenantSlug, ct))
            return ServiceResult<SignupResult>.Fail("That workspace URL is already taken.");

        Tenant tenant;
        User user;
        try
        {
            tenant = Tenant.Create(request.TenantName, request.TenantSlug);
            user = User.CreateWithPassword(request.Email, request.FullName,
                _passwords.Hash(request.Password));
        }
        catch (ArgumentException ex)
        {
            return ServiceResult<SignupResult>.Fail(ex.Message);
        }

        var membership = UserTenant.CreateOwner(user.Id, tenant.Id);

        await _tenants.AddAsync(tenant, ct);
        await _users.AddAsync(user, ct);
        await _memberships.AddAsync(membership, ct);

        var rawToken = _tokenHasher.GenerateToken();
        var verification = EmailVerificationToken.Issue(user.Id, _tokenHasher.Hash(rawToken));
        await _authTokens.AddVerificationAsync(verification, ct);

        await _users.SaveChangesAsync(ct);

        await SendVerificationEmailAsync(user, rawToken, ct);

        return ServiceResult<SignupResult>.Ok(new SignupResult(user.Id, tenant.Id, tenant.Slug));
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(request.Email, ct);
        if (user is null)
            return LoginResult.Fail(LoginOutcome.InvalidCredentials);

        if (user.Status == UserStatus.Disabled)
            return LoginResult.Fail(LoginOutcome.Disabled);

        if (user.IsLockedOut)
            return LoginResult.Fail(LoginOutcome.LockedOut);

        if (user.PasswordHash is null ||
            !_passwords.Verify(request.Password, user.PasswordHash))
        {
            user.RecordFailedLogin();
            await _users.SaveChangesAsync(ct);
            return LoginResult.Fail(user.IsLockedOut ? LoginOutcome.LockedOut : LoginOutcome.InvalidCredentials);
        }

        if (!user.EmailVerified)
            return LoginResult.Fail(LoginOutcome.EmailNotVerified);

        if (user.IsTwoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.TotpCode))
                return LoginResult.Fail(LoginOutcome.TwoFactorRequired);
            if (!_totp.VerifyCode(user.TotpSecret!, request.TotpCode))
            {
                user.RecordFailedLogin();
                await _users.SaveChangesAsync(ct);
                return LoginResult.Fail(LoginOutcome.InvalidTotp);
            }
        }

        var memberships = await LoadMembershipsAsync(user.Id, ct);
        if (memberships.Count == 0)
            return LoginResult.Fail(LoginOutcome.NoActiveMembership);

        user.RecordSuccessfulLogin();
        await _users.SaveChangesAsync(ct);

        return new LoginResult(LoginOutcome.Success, user.Id, user.Email, user.FullName, memberships);
    }

    // Called by the OIDC callback flow after the IdP has verified the user's identity.
    // Trusts the email + name claims as authoritative — the IdP wouldn't have issued
    // the token without authenticating the user. Auto-provisions a User row and an
    // active UserTenant membership when missing, since the tenant admin granted access
    // by configuring this OIDC provider for their workspace in the first place.
    public async Task<LoginResult> SignInWithOidcAsync(
        Guid tenantId, string email, string? fullName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return LoginResult.Fail(LoginOutcome.InvalidCredentials);

        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null || tenant.Status == TenantStatus.Suspended)
            return LoginResult.Fail(LoginOutcome.NoActiveMembership);

        var user = await _users.GetByEmailAsync(email, ct);
        if (user is null)
        {
            try
            {
                user = User.CreateOidcOnly(
                    email, string.IsNullOrWhiteSpace(fullName) ? email : fullName);
            }
            catch (ArgumentException)
            {
                return LoginResult.Fail(LoginOutcome.InvalidCredentials);
            }
            await _users.AddAsync(user, ct);
        }

        if (user.Status == UserStatus.Disabled)
            return LoginResult.Fail(LoginOutcome.Disabled);

        var membership = await _memberships.GetAsync(user.Id, tenantId, ct);
        if (membership is null)
        {
            membership = UserTenant.CreateActive(user.Id, tenantId, UserRole.Operator);
            await _memberships.AddAsync(membership, ct);
        }
        else if (membership.Status == MembershipStatus.Pending)
        {
            membership.Accept();
        }
        else if (membership.Status == MembershipStatus.Removed)
        {
            return LoginResult.Fail(LoginOutcome.NoActiveMembership);
        }

        user.RecordSuccessfulLogin();
        await _users.SaveChangesAsync(ct);

        var loginMembership = new LoginTenantMembership(
            tenantId, tenant.Slug, tenant.Name, membership.Role);
        return new LoginResult(LoginOutcome.Success, user.Id, user.Email, user.FullName,
            new[] { loginMembership });
    }

    public async Task<ServiceResult> VerifyEmailAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return ServiceResult.Fail("Token is required.");

        var hash = _tokenHasher.Hash(rawToken);
        var token = await _authTokens.FindVerificationAsync(hash, ct);
        if (token is null || !token.IsUsable)
            return ServiceResult.Fail("This link is invalid or has expired.");

        var user = await _users.GetByIdAsync(token.UserId, ct);
        if (user is null)
            return ServiceResult.Fail("This link is invalid or has expired.");

        token.Consume();
        user.VerifyEmail();
        await _users.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RequestPasswordResetAsync(string email, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(email ?? string.Empty, ct);
        if (user is null || user.Status == UserStatus.Disabled)
            return ServiceResult.Ok(); // Don't leak existence

        await _authTokens.InvalidateResetTokensForUserAsync(user.Id, ct);

        var rawToken = _tokenHasher.GenerateToken();
        var reset = PasswordResetToken.Issue(user.Id, _tokenHasher.Hash(rawToken));
        await _authTokens.AddResetAsync(reset, ct);
        await _users.SaveChangesAsync(ct);

        await SendPasswordResetEmailAsync(user, rawToken, ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ResetPasswordAsync(string rawToken, string newPassword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 12)
            return ServiceResult.Fail("Password must be at least 12 characters.");

        var hash = _tokenHasher.Hash(rawToken);
        var token = await _authTokens.FindResetAsync(hash, ct);
        if (token is null || !token.IsUsable)
            return ServiceResult.Fail("This link is invalid or has expired.");

        var user = await _users.GetByIdAsync(token.UserId, ct);
        if (user is null)
            return ServiceResult.Fail("This link is invalid or has expired.");

        token.Consume();
        user.ChangePassword(_passwords.Hash(newPassword));
        // Successful reset proves control of the email, so consider it verified.
        user.VerifyEmail();
        await _users.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    private async Task<IReadOnlyList<LoginTenantMembership>> LoadMembershipsAsync(Guid userId, CancellationToken ct)
    {
        var memberships = await _memberships.GetForUserAsync(userId, ct);
        var active = memberships
            .Where(m => m.Status == MembershipStatus.Active)
            .ToList();
        if (active.Count == 0) return Array.Empty<LoginTenantMembership>();

        var tenantIds = active.Select(m => m.TenantId);
        var tenants = (await _tenants.GetByIdsAsync(tenantIds, ct))
            .ToDictionary(t => t.Id);

        return active
            .Where(m => tenants.ContainsKey(m.TenantId) &&
                        tenants[m.TenantId].Status != TenantStatus.Suspended)
            .Select(m => new LoginTenantMembership(
                m.TenantId, tenants[m.TenantId].Slug, tenants[m.TenantId].Name, m.Role))
            .ToList();
    }

    private Task SendVerificationEmailAsync(User user, string rawToken, CancellationToken ct)
    {
        var link = $"{_emailOptions.BaseUrl.TrimEnd('/')}/verify-email?token={Uri.EscapeDataString(rawToken)}";
        var body = $"<p>Hi {WebEncode(user.FullName)},</p>" +
                   $"<p>Confirm your email to finish setting up your Meridian account:</p>" +
                   $"<p><a href=\"{link}\">Verify email</a></p>" +
                   $"<p>This link expires in 24 hours.</p>";
        return _email.SendAsync(new EmailMessage(
            user.Email, _emailOptions.FromAddress, _emailOptions.FromName,
            "Verify your Meridian email", body), ct);
    }

    private Task SendPasswordResetEmailAsync(User user, string rawToken, CancellationToken ct)
    {
        var link = $"{_emailOptions.BaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(rawToken)}";
        var body = $"<p>Hi {WebEncode(user.FullName)},</p>" +
                   $"<p>We received a request to reset your Meridian password.</p>" +
                   $"<p><a href=\"{link}\">Reset password</a></p>" +
                   $"<p>This link expires in 1 hour. Ignore this message if you did not request it.</p>";
        return _email.SendAsync(new EmailMessage(
            user.Email, _emailOptions.FromAddress, _emailOptions.FromName,
            "Reset your Meridian password", body), ct);
    }

    private static string WebEncode(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
