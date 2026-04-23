using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Infrastructure.Outreach.Graph;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Infrastructure.Outreach.Graph;

public class GraphTokenProviderTests
{
    private static MeridianGraphOptions DefaultOptions() => new()
    {
        TenantId = "tenant-123",
        ClientId = "client-abc",
        ClientSecret = "secret-xyz",
        TokenExpirySafetySeconds = 60
    };

    private static HttpResponseMessage TokenResponse(string token, int expiresIn) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                access_token = token,
                expires_in = expiresIn,
                token_type = "Bearer"
            }), System.Text.Encoding.UTF8, "application/json")
        };

    [Fact]
    public async Task First_call_acquires_token_and_caches_it()
    {
        var calls = 0;
        var handler = new FakeHandler(req =>
        {
            calls++;
            req.RequestUri!.AbsoluteUri.Should().Contain("/tenant-123/oauth2/v2.0/token");
            return TokenResponse("token-1", 3600);
        });
        var provider = new GraphTokenProvider(new HttpClient(handler), DefaultOptions(),
            NullLogger<GraphTokenProvider>.Instance, () => DateTimeOffset.UtcNow);

        var first = await provider.GetAccessTokenAsync(CancellationToken.None);
        var second = await provider.GetAccessTokenAsync(CancellationToken.None);

        first.Should().Be("token-1");
        second.Should().Be("token-1");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Refreshes_when_within_safety_window()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => TokenResponse($"token-{++calls}", 3600));

        var now = new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);
        var provider = new GraphTokenProvider(new HttpClient(handler), DefaultOptions(),
            NullLogger<GraphTokenProvider>.Instance, () => now);

        var first = await provider.GetAccessTokenAsync(CancellationToken.None);

        // Simulate clock advancing past expiry-safety window (1hr - 60s = ~59 min in)
        now = now.AddMinutes(59).AddSeconds(30);
        var second = await provider.GetAccessTokenAsync(CancellationToken.None);

        first.Should().Be("token-1");
        second.Should().Be("token-2");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Token_endpoint_failure_propagates_as_exception()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var provider = new GraphTokenProvider(new HttpClient(handler), DefaultOptions(),
            NullLogger<GraphTokenProvider>.Instance, () => DateTimeOffset.UtcNow);

        var act = () => provider.GetAccessTokenAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Concurrent_callers_only_trigger_one_token_request()
    {
        var calls = 0;
        var handler = new FakeHandler(_ =>
        {
            Interlocked.Increment(ref calls);
            Thread.Sleep(20);
            return TokenResponse("token-once", 3600);
        });
        var provider = new GraphTokenProvider(new HttpClient(handler), DefaultOptions(),
            NullLogger<GraphTokenProvider>.Instance, () => DateTimeOffset.UtcNow);

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => provider.GetAccessTokenAsync(CancellationToken.None))
            .ToArray();
        await Task.WhenAll(tasks);

        tasks.Select(t => t.Result).Should().OnlyContain(s => s == "token-once");
        calls.Should().Be(1);
    }
}
