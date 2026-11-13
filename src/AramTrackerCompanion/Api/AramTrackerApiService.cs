using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AramTrackerCompanion.Api;

internal sealed class AramTrackerApiService : IDisposable
{
    public const int MaxMatchesPerRequest = 10;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly Uri _baseUri;

    public AramTrackerApiService(string baseUrl, HttpMessageHandler? handler = null)
    {
        _baseUri = ValidateBaseUrl(baseUrl);
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Uri WebsiteSettingsUrl => new(_baseUri, "/settings?tab=companion");

    public Task<PairResponse> PairAsync(string code, string installationName, CancellationToken cancellation) =>
        SendAsync<PairResponse>(HttpMethod.Post, "pair", null, new PairRequest(code, installationName, AppInfo.Version), cancellation);

    public Task<MeResponse> GetMeAsync(string token, CancellationToken cancellation) =>
        SendAsync<MeResponse>(HttpMethod.Get, "me", token, null, cancellation);

    public async Task RevokeAsync(string token, CancellationToken cancellation)
    {
        using var response = await SendAsync(HttpMethod.Delete, "installation", token, null, cancellation);
    }

    public Task<SubmitResponse> SubmitMatchesAsync(string token, IReadOnlyList<MatchSubmission> matches, CancellationToken cancellation) =>
        SendAsync<SubmitResponse>(HttpMethod.Post, "matches", token, new SubmitRequest(matches), cancellation);

    public void Dispose() => _http.Dispose();

    internal static Uri ValidateBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"The API address '{baseUrl}' is not a valid URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            throw new ArgumentException("The API address must use HTTPS.");
        }

        return uri;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, string? token, object? body, CancellationToken cancellation)
    {
        using var response = await SendAsync(method, path, token, body, cancellation);

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellation)
                ?? throw new ApiException(ApiFailure.Server, "ARAM Tracker sent an empty answer.");
        }
        catch (JsonException exception)
        {
            throw new ApiException(ApiFailure.Server, "ARAM Tracker sent an answer that could not be read.", inner: exception);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, object? body, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        HttpResponseMessage response;

        try
        {
            response = await _http.SendAsync(request, cancellation);
        }
        catch (HttpRequestException exception)
        {
            throw new ApiException(ApiFailure.Network, "ARAM Tracker could not be reached.", inner: exception);
        }
        catch (OperationCanceledException exception) when (!cancellation.IsCancellationRequested)
        {
            throw new ApiException(ApiFailure.Network, "ARAM Tracker took too long to answer.", inner: exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            throw await ToExceptionAsync(response, cancellation);
        }
    }

    private static async Task<ApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        var (code, message) = await ReadErrorAsync(response, cancellation);
        var status = (int)response.StatusCode;

        if (status >= 500)
        {
            return new ApiException(ApiFailure.Server, $"ARAM Tracker had a problem ({status}).", statusCode: status);
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new ApiException(ApiFailure.Unauthorized, message ?? "This companion is no longer connected.", statusCode: status),
            HttpStatusCode.TooManyRequests => new ApiException(
                ApiFailure.RateLimited,
                "Too many requests. Trying again shortly.",
                response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60),
                statusCode: status),
            _ => new ApiException(
                code switch
                {
                    "invalid_code" => ApiFailure.InvalidCode,
                    "too_many_installations" => ApiFailure.TooManyInstallations,
                    _ => ApiFailure.Refused,
                },
                message ?? $"ARAM Tracker refused the request ({status}).",
                statusCode: status),
        };
    }

    private static async Task<(string? Code, string? Message)> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                return (ReadString(error, "code"), ReadString(error, "message"));
            }

            return (null, root.ValueKind == JsonValueKind.Object ? ReadString(root, "message") : null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
