using Microsoft.AspNetCore.Mvc;

namespace SmartMedBox.Api.Errors;

public static class ApiProblemResults
{
    public static IActionResult Create(HttpContext httpContext, int statusCode, string code, string? detail = null) =>
        new ObjectResult(ApiProblems.Create(httpContext, statusCode, code, detail))
        {
            StatusCode = statusCode,
            ContentTypes = { ApiProblems.ContentType },
        };

    public static IActionResult Validation(HttpContext httpContext, string field, string message) =>
        new ObjectResult(ApiProblems.Create(
            httpContext,
            StatusCodes.Status400BadRequest,
            ErrorCodes.ValidationError,
            errors: new Dictionary<string, string[]> { [field] = [message] }))
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { ApiProblems.ContentType },
        };
}
