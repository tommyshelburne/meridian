using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Infrastructure.Health;
using Microsoft.Extensions.Configuration;

namespace Meridian.Unit.Infrastructure.Health;

public class HealthInfoTests
{
    private static HealthInfo Build(string? description = null)
    {
        var dict = new Dictionary<string, string?>();
        if (description is not null)
            dict[HealthInfo.ConfigKey] = description;
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        return new HealthInfo(config);
    }

    [Fact]
    public void Build_returns_status_healthy()
    {
        Build().Build().Status.Should().Be("healthy");
    }

    [Fact]
    public void Build_version_matches_yyyymmdd_dot_hhmm_pattern()
    {
        var resp = Build().Build();
        Regex.IsMatch(resp.Version, @"^\d{8}\.\d{4}$")
            .Should().BeTrue($"version was '{resp.Version}', expected YYYYMMDD.HHMM");
    }

    [Fact]
    public void Build_buildDate_is_valid_iso8601_not_in_future()
    {
        var resp = Build().Build();
        var parsed = DateTimeOffset.Parse(resp.BuildDate);
        parsed.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(5));
    }

    [Fact]
    public void Build_timestamp_is_current_within_a_few_seconds()
    {
        var resp = Build().Build();
        var parsed = DateTimeOffset.Parse(resp.Timestamp);
        parsed.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Build_description_defaults_to_meridian_worker_when_config_unset()
    {
        Build().Build().Description.Should().Be(HealthInfo.DefaultDescription);
    }

    [Fact]
    public void Build_description_uses_configured_value_when_set()
    {
        Build(description: "Meridian Worker — prod-east").Build()
            .Description.Should().Be("Meridian Worker — prod-east");
    }

    [Fact]
    public async Task Build_subsequent_calls_return_advancing_timestamp()
    {
        var info = Build();
        var first = DateTimeOffset.Parse(info.Build().Timestamp);
        await Task.Delay(15);
        var second = DateTimeOffset.Parse(info.Build().Timestamp);
        second.Should().BeAfter(first);
    }
}
