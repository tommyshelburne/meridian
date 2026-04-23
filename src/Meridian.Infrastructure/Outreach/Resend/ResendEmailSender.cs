using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Outreach.Resend;

public class ResendOptions
{
    public const string SectionName = "Resend";
    public string BaseUrl { get; set; } = "https://api.resend.com";
}

public class ResendEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly ResendOptions _options;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(
        HttpClient httpClient,
        IOptions<ResendOptions> options,
        ILogger<ResendEmailSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<SendResult>> SendAsync(
        EmailMessage message,
        string apiKey,
        string? replyToAddress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return ServiceResult<SendResult>.Fail("Resend API key is missing.");

        var url = $"{_options.BaseUrl.TrimEnd('/')}/emails";
        var payload = new ResendSendRequest(
            From: $"{message.DisplayName} <{message.From}>",
            To: new[] { message.To },
            Subject: message.Subject,
            Html: message.BodyHtml,
            ReplyTo: replyToAddress);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Resend send failed: {Status} {Body}", response.StatusCode, body);
                return ServiceResult<SendResult>.Fail($"Resend returned {(int)response.StatusCode}: {body}");
            }

            var result = await response.Content.ReadFromJsonAsync<ResendSendResponse>(cancellationToken: ct);
            return ServiceResult<SendResult>.Ok(new SendResult(result?.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resend send threw");
            return ServiceResult<SendResult>.Fail($"Resend send failed: {ex.Message}");
        }
    }

    private record ResendSendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] IReadOnlyList<string> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("reply_to")] string? ReplyTo);

    private class ResendSendResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }
}
