namespace Meridian.Domain.Users;

public class UserTenant
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public UserRole Role { get; private set; }
    public MembershipStatus Status { get; private set; }
    public Guid? InvitedByUserId { get; private set; }
    public DateTimeOffset InvitedAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }

    private UserTenant() { }

    public static UserTenant CreateOwner(Guid userId, Guid tenantId)
    {
        var m = Create(userId, tenantId, UserRole.Owner, invitedBy: null);
        m.Status = MembershipStatus.Active;
        m.AcceptedAt = m.InvitedAt;
        return m;
    }

    public static UserTenant Invite(Guid userId, Guid tenantId, UserRole role, Guid invitedByUserId)
    {
        if (invitedByUserId == Guid.Empty)
            throw new ArgumentException("Inviter is required.", nameof(invitedByUserId));
        return Create(userId, tenantId, role, invitedByUserId);
    }

    // Used by OIDC auto-provisioning: when a user signs in via a tenant's configured
    // IdP for the first time, we trust the IdP's authorization and create them as an
    // already-active member rather than a pending invite.
    public static UserTenant CreateActive(Guid userId, Guid tenantId, UserRole role)
    {
        var m = Create(userId, tenantId, role, invitedBy: null);
        m.Status = MembershipStatus.Active;
        m.AcceptedAt = m.InvitedAt;
        return m;
    }

    public void Accept()
    {
        if (Status != MembershipStatus.Pending)
            throw new InvalidOperationException("Only pending memberships can be accepted.");
        Status = MembershipStatus.Active;
        AcceptedAt = DateTimeOffset.UtcNow;
    }

    public void ChangeRole(UserRole role)
    {
        if (Status == MembershipStatus.Removed)
            throw new InvalidOperationException("Cannot change role on a removed membership.");
        Role = role;
    }

    public void Remove()
    {
        Status = MembershipStatus.Removed;
        RemovedAt = DateTimeOffset.UtcNow;
    }

    private static UserTenant Create(Guid userId, Guid tenantId, UserRole role, Guid? invitedBy)
    {
        if (userId == Guid.Empty) throw new ArgumentException("UserId is required.", nameof(userId));
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));

        return new UserTenant
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            Role = role,
            Status = MembershipStatus.Pending,
            InvitedByUserId = invitedBy,
            InvitedAt = DateTimeOffset.UtcNow
        };
    }
}
