using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Infrastructure.Outreach.Graph;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure.Outreach.Graph;

public class GraphReplyMonitorTests
{
    private const string LoginUrl = "https://login.microsoftonline.com";
    private const string GraphUrl = "https://graph.microsoft.com/v1.0";
    private const string Mailbox = "outreach@kombea.com";

    private static MeridianGraphOptions Options() => new()
    {
        TenantId = "tenant-1",
        ClientId = "client-1",
        ClientSecret = "secret-1",
        Mailbox = Mailbox,
        LoginBaseUrl = LoginUrl,
        GraphBaseUrl = GraphUrl,
        MessagePageSize = 50,
        TokenExpirySafetySeconds = 60
    };

    private static HttpResponseMessage TokenResponse() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                access_token = "test-token",
                expires_in = 3600,
                token_type = "Bearer"
            }), System.Text.Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage GraphResponse(object body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8, "application/json")
        };

    private static GraphReplyMonitor Build(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var sharedHandler = new FakeHandler(handler);
        var options = Options();
        var tokenProvider = new GraphTokenProvider(new HttpClient(sharedHandler), options,
            NullLogger<GraphTokenProvider>.Instance, () => DateTimeOffset.UtcNow);
        return new GraphReplyMonitor(new HttpClient(sharedHandler), tokenProvider,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<GraphReplyMonitor>.Instance);
    }

    [Fact]
    public async Task Maps_messages_with_in_reply_to_header_to_detected_replies()
    {
        HttpRequestMessage? graphRequest = null;
        var monitor = Build(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("/oauth2/v2.0/token"))
                return TokenResponse();

            graphRequest = req;
            return GraphResponse(new
            {
                value = new[]
                {
                    new
                    {
                        id = "graph-id-1",
                        internetMessageId = "<reply-1@vendor.com>",
                        subject = "Re: Contact Center RFP",
                        receivedDateTime = "2026-04-22T14:30:00Z",
                        from = new { emailAddress = new { address = "rep@vendor.com", name = "Rep" } },
                        internetMessageHeaders = new[]
                        {
                            new { name = "In-Reply-To", value = "<original-1@kombea.com>" },
                            new { name = "References", value = "<original-1@kombea.com>" }
                        }
                    }
                }
            });
        });

        var since = DateTimeOffset.Parse("2026-04-22T10:00:00Z");
        var result = await monitor.CheckForRepliesAsync(since, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var reply = result.Value.Should().ContainSingle().Subject;
        reply.MessageId.Should().Be("<original-1@kombea.com>");
        reply.Subject.Should().Be("Re: Contact Center RFP");
        reply.FromAddress.Should().Be("rep@vendor.com");

        graphRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        graphRequest.Headers.Authorization.Parameter.Should().Be("test-token");
        var graphUri = graphRequest.RequestUri!.AbsoluteUri;
        graphUri.Should().Contain($"/users/{Uri.EscapeDataString(Mailbox)}/messages");
        graphUri.Should().Contain("receivedDateTime");
        graphUri.Should().Contain("2026-04-22");
    }

    [Fact]
    public async Task Falls_back_to_internet_message_id_when_no_in_reply_to_header()
    {
        var monitor = Build(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("/oauth2/v2.0/token"))
                return TokenResponse();

            return GraphResponse(new
            {
                value = new[]
                {
                    new
                    {
                        id = "graph-id-2",
                        internetMessageId = "<no-headers@vendor.com>",
                        subject = "Inbound Message",
                        receivedDateTime = "2026-04-22T14:30:00Z",
                        from = new { emailAddress = new { address = "rep@vendor.com" } },
                        internetMessageHeaders = (object?)null
                    }
                }
            });
        });

        var result = await monitor.CheckForRepliesAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None);

        result.Value.Should().ContainSingle()
            .Which.MessageId.Should().Be("<no-headers@vendor.com>");
    }

    [Fact]
    public async Task Skips_messages_with_no_from_address()
    {
        var monitor = Build(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("/oauth2/v2.0/token"))
                return TokenResponse();

            return GraphResponse(new
            {
                value = new[]
                {
                    new
                    {
                        id = "ghost",
                        internetMessageId = "<no-from@x.com>",
                        subject = "Anon",
                        receivedDateTime = "2026-04-22T14:30:00Z",
                        from = (object?)null,
                        internetMessageHeaders = (object?)null
                    }
                }
            });
        });

        var result = await monitor.CheckForRepliesAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None);

        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_value_array_yields_empty_result()
    {
        var monitor = Build(req =>
            req.RequestUri!.AbsoluteUri.Contains("/oauth2/v2.0/token")
                ? TokenResponse()
                : GraphResponse(new { value = Array.Empty<object>() }));

        var result = await monitor.CheckForRepliesAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Token_failure_returns_failure_result()
    {
        var monitor = Build(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await monitor.CheckForRepliesAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().StartWith("Token acquisition failed");
    }

    [Fact]
    public async Task Graph_failure_after_successful_token_returns_failure_result()
    {
        var monitor = Build(req =>
            req.RequestUri!.AbsoluteUri.Contains("/oauth2/v2.0/token")
                ? TokenResponse()
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await monitor.CheckForRepliesAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().StartWith("Graph fetch failed");
    }

    [Fact]
    public async Task Missing_mailbox_returns_failure_without_calling_graph()
    {
        var options = Options();
        options.Mailbox = string.Empty;
        var tokenHandler = new FakeHandler(_ => TokenResponse());
        var tokenProvider = new GraphTokenProvider(new HttpClient(tokenHandler), options,
            NullLogger<GraphTokenProvider>.Instance, () => DateTimeOffset.UtcNow);
        var graphHandler = new FakeHandler(_ => throw new InvalidOperationException("Should not call Graph"));
        var monitor = new GraphReplyMonitor(new HttpClient(graphHandler), tokenProvider,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<GraphReplyMonitor>.Instance);

        var result = await monitor.CheckForRepliesAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Mailbox");
    }
}
