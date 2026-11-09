using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SmartMedBox.Api.Errors;

namespace SmartMedBox.Api.Security;

public sealed class ApiAntiforgeryFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method)
            || HttpMethods.IsTrace(method))
        {
            return;
        }

        if (context.ActionDescriptor.EndpointMetadata.OfType<IgnoreAntiforgeryTokenAttribute>().Any())
        {
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = ApiProblemResults.Create(
                context.HttpContext,
                StatusCodes.Status400BadRequest,
                ErrorCodes.CsrfInvalid);
        }
    }
}
