namespace AramTrackerCompanion.Api;

internal enum ApiFailure
{
    Network,
    Server,
    Unauthorized,
    RateLimited,
    InvalidCode,
    TooManyInstallations,
    Refused,
}

internal sealed class ApiException(ApiFailure failure, string message, TimeSpan? retryAfter = null, Exception? inner = null, int? statusCode = null)
    : Exception(message, inner)
{
    public ApiFailure Failure { get; } = failure;

    public TimeSpan? RetryAfter { get; } = retryAfter;

    public int? StatusCode { get; } = statusCode;

    public bool IsTransient => Failure is ApiFailure.Network or ApiFailure.Server or ApiFailure.RateLimited;

    public bool IsFinalRefusal => Failure == ApiFailure.Refused && StatusCode is 400 or 413 or 422;
}
