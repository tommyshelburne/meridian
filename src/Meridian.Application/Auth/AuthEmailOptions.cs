namespace Meridian.Application.Auth;

public class AuthEmailOptions
{
    public const string SectionName = "AuthEmail";

    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string FromAddress { get; set; } = "no-reply@meridian.local";
    public string FromName { get; set; } = "Meridian";
}
