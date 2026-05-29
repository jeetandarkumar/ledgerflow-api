using System.Net;
using System.Text.Json;
using ledgerflowApi.Application.Common.Exceptions;
using ledgerflowApi.Domain.Exceptions;

namespace ledgerflowApi.API.Middleware;

public class GlobalExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;

    public GlobalExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception. TraceId: {TraceId}", context.TraceIdentifier);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, errors) = exception switch
        {
            ValidationException ve => (
                HttpStatusCode.UnprocessableEntity,
                "Validation Failed",
                ve.Errors as object),

            NotFoundException nfe => (
                HttpStatusCode.NotFound,
                "Resource Not Found",
                new Dictionary<string, string[]> { ["detail"] = [nfe.Message] } as object),

            ForbiddenAccessException => (
                HttpStatusCode.Forbidden,
                "Forbidden",
                new Dictionary<string, string[]> { ["detail"] = ["You do not have permission to access this resource."] } as object),

            DomainException de => (
                HttpStatusCode.BadRequest,
                "Business Rule Violation",
                new Dictionary<string, string[]> { ["detail"] = [de.Message] } as object),

            _ => (
                HttpStatusCode.InternalServerError,
                "An unexpected error occurred.",
                new Dictionary<string, string[]> { ["detail"] = ["Please try again later or contact support."] } as object)
        };

        var response = new
        {
            type = $"https://httpstatuses.io/{(int)statusCode}",
            title,
            status = (int)statusCode,
            traceId = context.TraceIdentifier,
            errors
        };

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(JsonSerializer.Serialize(response,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
