namespace Meridian.Application.Demo;

public record DemoProvisionRequest(
    string Slug,
    string TenantName,
    string Email,
    string FullName,
    string Password);
