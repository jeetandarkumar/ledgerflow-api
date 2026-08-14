using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ledgerflowApi.API.HealthChecks;

/// <summary>
/// Formats health check results as JSON, one entry per dependency, instead of the
/// framework's bare 200/503 with an empty body. This is what makes the difference
/// between "the endpoint says healthy" and "an operator can see which dependency is down".
/// </summary>
public static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = e.Value.Duration.TotalMilliseconds,
                error = e.Value.Exception?.Message
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
