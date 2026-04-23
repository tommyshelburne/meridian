using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Ports;
using Meridian.Infrastructure.Outreach.Resend;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure.Outreach.Resend;

public class ResendEmailSenderTests
{
    private const string BaseUrl = "https://api.resend.com";

    private static ResendEmailSender Build(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var httpClient = new HttpClient(new FakeHandler(handler));
        return new ResendEmailSender(
            httpClient,
            Options.Create(new ResendOptions { BaseUrl = BaseUrl }),
            NullLogger<ResendEmailSender>.Instance);
    }

    private static EmailMessage Msg() =>
        new("recipient@dest.com", "from@vendor.com", "Vendor Name", "Hi", "<p>Body</p>");

    private static HttpResponseMessage JsonResponse(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json")
        };

    [Fact]
    public async Task Posts_to_emails_endpoint_with_bearer_auth()
    {
        HttpRequestMessage? captured = null;
        var sender = Build(req =>
        {
            captured = req;
            return JsonResponse(new { id = "msg-123" });
        });

        var result = await sender.SendAsync(Msg(), "rsk_live_abc", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MessageId.Should().Be("msg-123");
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.AbsoluteUri.Should().Be($"{BaseUrl}/emails");
        captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be("rsk_live_abc");
    }

    [Fact]
    public async Task Body_serializes_with_resend_field_names_and_combined_from()
    {
        string? sentJson = null;
        var sender = Build(req =>
        {
            sentJson = req.Content!.ReadAsStringAsync().Result;
            return JsonResponse(new { id = "msg-1" });
        });

        await sender.SendAsync(Msg(), "rsk", "reply@vendor.com", CancellationToken.None);

        sentJson.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(sentJson!);
        var root = doc.RootElement;
        root.GetProperty("from").GetString().Should().Be("Vendor Name <from@vendor.com>");
        root.GetProperty("to")[0].GetString().Should().Be("recipient@dest.com");
        root.GetProperty("subject").GetString().Should().Be("Hi");
        root.GetProperty("html").GetString().Should().Be("<p>Body</p>");
        root.GetProperty("reply_to").GetString().Should().Be("reply@vendor.com");
    }

    [Fact]
    public async Task Reply_to_omitted_when_null_serializes_as_null()
    {
        string? sentJson = null;
        var sender = Build(req =>
        {
            sentJson = req.Content!.ReadAsStringAsync().Result;
            return JsonResponse(new { id = "msg-1" });
        });

        await sender.SendAsync(Msg(), "rsk", null, CancellationToken.None);

        using var doc = JsonDocument.Parse(sentJson!);
        doc.RootElement.GetProperty("reply_to").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Missing_api_key_fails_without_calling_resend()
    {
        var called = false;
        var sender = Build(_ => { called = true; return JsonResponse(new { id = "x" }); });

        var result = await sender.SendAsync(Msg(), "", null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("API key");
        called.Should().BeFalse();
    }

    [Fact]
    public async Task Non_2xx_response_returns_failure_with_status_and_body()
    {
        var sender = Build(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"message\":\"invalid from\"}",
                System.Text.Encoding.UTF8, "application/json")
        });

        var result = await sender.SendAsync(Msg(), "rsk", null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("422");
        result.Error.Should().Contain("invalid from");
    }

    [Fact]
    public async Task Network_exception_is_caught_and_returned_as_failure()
    {
        var sender = Build(_ => throw new HttpRequestException("connection refused"));

        var result = await sender.SendAsync(Msg(), "rsk", null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("connection refused");
    }
}
