namespace TokenObservability.Api;

internal static class TokenObservabilityCorrelationId
{
    private const int MaxCorrelationIdLength = 128;

    public static string Resolve(HttpContext httpContext)
    {
        var values = httpContext.Request.Headers["X-Correlation-Id"];

        if (values.Count != 1)
        {
            return httpContext.TraceIdentifier;
        }

        var value = values[0]?.Trim() ?? string.Empty;

        return IsSafeCorrelationId(value)
            ? value
            : httpContext.TraceIdentifier;
    }

    private static bool IsSafeCorrelationId(string value)
    {
        return value.Length is > 0 and <= MaxCorrelationIdLength &&
            value.All(static character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.');
    }
}
