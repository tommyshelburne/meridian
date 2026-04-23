using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Outreach.Graph;

public class GraphReplyMonitor : IInboxMonitor
{
    private readonly HttpClient _httpClient;
    private readonly GraphTokenProvider _tokenProvider;
    private readonly MeridianGraphOptions _options;
    private readonly ILogger<GraphReplyMonitor> _logger;

    public GraphReplyMonitor(
        HttpClient httpClient,
        GraphTokenProvider tokenProvider,
        IOptions<MeridianGraphOptions> options,
        ILogger<GraphReplyMonitor> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<DetectedReply>>> CheckForRepliesAsync(
        DateTimeOffset since, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Mailbox))
            return ServiceResult<IReadOnlyList<DetectedReply>>.Fail("MeridianGraph:Mailbox is not configured.");

        string accessToken;
        try
        {
            accessToken = await _tokenProvider.GetAccessTokenAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Graph access token");
            return ServiceResult<IReadOnlyList<DetectedReply>>.Fail($"Token acquisition failed: {ex.Message}");
        }

        var sinceIso = since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var url = $"{_options.GraphBaseUrl.TrimEnd('/')}/users/{Uri.EscapeDataString(_options.Mailbox)}/messages"
                  + $"?$filter=receivedDateTime ge {sinceIso}"
                  + "&$select=id,internetMessageId,subject,from,receivedDateTime,internetMessageHeaders"
                  + $"&$top={_options.MessagePageSize}"
                  + "&$orderby=receivedDateTime desc";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<GraphMessagesResponse>(cancellationToken: ct);
            if (payload?.Value is null)
                return ServiceResult<IReadOnlyList<DetectedReply>>.Ok(Array.Empty<DetectedReply>());

            var replies = new List<DetectedReply>();
            foreach (var message in payload.Value)
            {
                var reply = MapMessage(message);
                if (reply is not null) replies.Add(reply);
            }

            _logger.LogInformation("GraphReplyMonitor fetched {Count} messages from {Mailbox} since {Since}",
                replies.Count, _options.Mailbox, sinceIso);

            return ServiceResult<IReadOnlyList<DetectedReply>>.Ok(replies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch messages from Graph");
            return ServiceResult<IReadOnlyList<DetectedReply>>.Fail($"Graph fetch failed: {ex.Message}");
        }
    }

    private static DetectedReply? MapMessage(GraphMessage message)
    {
        var fromAddress = message.From?.EmailAddress?.Address;
        if (string.IsNullOrWhiteSpace(fromAddress)) return null;

        var inReplyTo = ExtractInReplyTo(message.InternetMessageHeaders)
                        ?? message.InternetMessageId
                        ?? message.Id
                        ?? string.Empty;

        return new DetectedReply(
            MessageId: inReplyTo,
            Subject: message.Subject ?? string.Empty,
            ReceivedAt: message.ReceivedDateTime ?? DateTimeOffset.UtcNow,
            FromAddress: fromAddress);
    }

    private static string? ExtractInReplyTo(List<GraphHeader>? headers)
    {
        if (headers is null) return null;

        var header = headers.FirstOrDefault(h =>
            string.Equals(h.Name, "In-Reply-To", StringComparison.OrdinalIgnoreCase));
        return header?.Value;
    }

    private class GraphMessagesResponse
    {
        [JsonPropertyName("value")]
        public List<GraphMessage>? Value { get; set; }
    }

    private class GraphMessage
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("internetMessageId")]
        public string? InternetMessageId { get; set; }

        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("receivedDateTime")]
        public DateTimeOffset? ReceivedDateTime { get; set; }

        [JsonPropertyName("from")]
        public GraphRecipient? From { get; set; }

        [JsonPropertyName("internetMessageHeaders")]
        public List<GraphHeader>? InternetMessageHeaders { get; set; }
    }

    private class GraphRecipient
    {
        [JsonPropertyName("emailAddress")]
        public GraphEmailAddress? EmailAddress { get; set; }
    }

    private class GraphEmailAddress
    {
        [JsonPropertyName("address")]
        public string? Address { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class GraphHeader
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }
}
