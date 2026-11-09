using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;

namespace SmartMedBox.Api.Errors;

public sealed class ApiProblemDetailsFactory : ProblemDetailsFactory
{
    public override ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        int? statusCode = null,
        string? title = null,
        string? type = null,
        string? detail = null,
        string? instance = null)
    {
        var status = statusCode ?? StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title ?? ReasonPhrases.GetReasonPhrase(status),
            Type = type ?? "about:blank",
            Detail = detail,
            Instance = instance,
        };

        AddCommonExtensions(problem, httpContext, ErrorCodeMap.ForStatus(status));
        return problem;
    }

    public override ValidationProblemDetails CreateValidationProblemDetails(
        HttpContext httpContext,
        ModelStateDictionary modelState,
        int? statusCode = null,
        string? title = null,
        string? type = null,
        string? detail = null,
        string? instance = null)
    {
        var status = statusCode ?? StatusCodes.Status400BadRequest;
        var problem = new ValidationProblemDetails(modelState)
        {
            Status = status,
            Title = title ?? ReasonPhrases.GetReasonPhrase(status),
            Type = type ?? "about:blank",
            Detail = detail,
            Instance = instance,
        };

        AddCommonExtensions(problem, httpContext, ErrorCodes.ValidationError);
        return problem;
    }

    private static void AddCommonExtensions(ProblemDetails problem, HttpContext httpContext, string? code)
    {
        if (code is not null)
        {
            problem.Extensions["code"] = code;
        }

        problem.Extensions["traceId"] = ApiProblems.GetTraceId(httpContext);
    }
}
