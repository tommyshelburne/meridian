using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Meridian.Application.Ports;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Meridian.Infrastructure.Auth;

public class JwtTokenIssuer : ITokenIssuer
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public JwtTokenIssuer(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured with at least 32 characters.");

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _signingCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
    }

    public IssuedToken IssueAccessToken(AccessTokenRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, request.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (request.TenantId is { } tid)
            claims.Add(new Claim("tenant_id", tid.ToString()));
        if (!string.IsNullOrEmpty(request.TenantSlug))
            claims.Add(new Claim("tenant_slug", request.TenantSlug));
        if (!string.IsNullOrEmpty(request.Role))
            claims.Add(new Claim(ClaimTypes.Role, request.Role));
        if (request.AdditionalClaims is { Count: > 0 })
            claims.AddRange(request.AdditionalClaims);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: _signingCredentials);

        return new IssuedToken(_handler.WriteToken(token), expires);
    }

    public IssuedToken IssueRefreshToken(Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddDays(_options.RefreshTokenLifetimeDays);

        var bytes = RandomNumberGenerator.GetBytes(32);
        var refresh = Base64UrlEncoder.Encode(bytes);
        return new IssuedToken($"{userId:N}.{refresh}", expires);
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        try
        {
            return _handler.ValidateToken(token, parameters, out _);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }
}
