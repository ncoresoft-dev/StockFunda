using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StockLens_Infrastructure.ExternalServices.BharatStock.Exceptions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_API.Middleware
{
    /// <summary>
    /// Global exception handler that catches unhandled exceptions across all API endpoints,
    /// logs the error, and returns a consistent JSON problem response compatible with Angular frontend.
    /// </summary>
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;
        private readonly IWebHostEnvironment _env;

        public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "Unhandled exception occurred during request execution: {Message}", exception.Message);

            var (statusCode, title, message) = exception switch
            {
                ArgumentNullException nullEx => (StatusCodes.Status400BadRequest, "Bad Request", nullEx.Message),
                ArgumentException argEx => (StatusCodes.Status400BadRequest, "Bad Request", argEx.Message),
                KeyNotFoundException notFoundEx => (StatusCodes.Status404NotFound, "Not Found", notFoundEx.Message),
                ProviderNotFoundException provNotFound => (StatusCodes.Status404NotFound, "Not Found", provNotFound.Message),
                ProviderRateLimitException rateLimitEx => (StatusCodes.Status429TooManyRequests, "Too Many Requests", rateLimitEx.Message),
                ProviderApiException provEx => (provEx.StatusCode ?? StatusCodes.Status502BadGateway, "Provider Error", provEx.Message),
                OperationCanceledException => (499, "Request Cancelled", "Client cancelled the request."),
                _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", "An error occurred while processing your request.")
            };

            if (exception is ProviderRateLimitException rateLimit && rateLimit.RetryAfter.HasValue)
            {
                httpContext.Response.Headers["Retry-After"] = ((int)rateLimit.RetryAfter.Value.TotalSeconds).ToString();
            }

            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "application/json";

            var errorResponse = new
            {
                status = statusCode,
                title,
                message,
                detail = _env.IsDevelopment() ? exception.Message : null
            };

            await httpContext.Response.WriteAsJsonAsync(errorResponse, cancellationToken);
            return true;
        }
    }
}

