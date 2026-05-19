using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Meridian.Infrastructure.Health;

public record HealthResponse(
    string Status,
    string Version,
    string Description,
    string BuildDate,
    string Timestamp);

public class HealthInfo
{
    public const string DefaultDescription = "Meridian Worker";
    public const string ConfigKey = "Health:Description";

    private readonly string _version;
    private readonly string _buildDate;
    private readonly string _description;

    public HealthInfo(IConfiguration configuration)
    {
        var entry = Assembly.GetEntryAssembly() ?? typeof(HealthInfo).Assembly;
        var assemblyPath = entry.Location;
        var buildUtc = !string.IsNullOrEmpty(assemblyPath) && File.Exists(assemblyPath)
            ? File.GetLastWriteTimeUtc(assemblyPath)
            : DateTime.UtcNow;

        _version = buildUtc.ToString("yyyyMMdd.HHmm");
        _buildDate = new DateTimeOffset(buildUtc, TimeSpan.Zero).ToString("O");
        _description = configuration[ConfigKey] ?? DefaultDescription;
    }

    public HealthResponse Build() => new(
        Status: "healthy",
        Version: _version,
        Description: _description,
        BuildDate: _buildDate,
        Timestamp: DateTimeOffset.UtcNow.ToString("O"));
}
