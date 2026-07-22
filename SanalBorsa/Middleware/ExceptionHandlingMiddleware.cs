using Microsoft.AspNetCore.Mvc;
using SanalBorsa.Application.Common.Exceptions;
using System.Net;
using System.Text.Json;

namespace SanalBorsa.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
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
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Resource not found");
            await WriteErrorAsync(context, HttpStatusCode.NotFound, "Not Found", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized");
            await WriteErrorAsync(context, HttpStatusCode.Unauthorized, "Unauthorized", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Business rule violation");
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "Bad Request", ex.Message);
        }
        catch (Application.Common.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Validation failed: {@Errors}", ex.Errors);
            await WriteValidationErrorAsync(context, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteErrorAsync(context, HttpStatusCode.InternalServerError,
                "Internal Server Error", "An unexpected error occurred.");
        }
    }

    private static Task WriteErrorAsync(HttpContext ctx, HttpStatusCode status, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = (int)status,
            Title = title,
            Detail = detail,
            Instance = ctx.Request.Path
        };

        ctx.Response.ContentType = "application/problem+json";
        ctx.Response.StatusCode = (int)status;
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }

    private static Task WriteValidationErrorAsync(
        HttpContext ctx,
        Application.Common.Exceptions.ValidationException ex)
    {
        var problem = new ValidationProblemDetails(
            ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value))
        {
            Status = (int)HttpStatusCode.UnprocessableEntity,
            Title = "Validation Failed",
            Instance = ctx.Request.Path
        };

        ctx.Response.ContentType = "application/problem+json";
        ctx.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }
}
