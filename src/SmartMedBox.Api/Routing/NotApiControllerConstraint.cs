using Microsoft.AspNetCore.Routing;

namespace SmartMedBox.Api.Routing;

public sealed class NotApiControllerConstraint : IRouteConstraint
{
    public bool Match(
        HttpContext? httpContext,
        IRouter? route,
        string routeKey,
        RouteValueDictionary values,
        RouteDirection routeDirection)
    {
        return values.TryGetValue(routeKey, out var value)
            && !string.Equals(value?.ToString(), "api", StringComparison.OrdinalIgnoreCase);
    }
}
