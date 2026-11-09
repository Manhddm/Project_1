namespace SmartMedBox.Api.Errors;

public static class ErrorCodeMap
{
    public static string? ForStatus(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ErrorCodes.ValidationError,
        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthorized,
        StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ErrorCodes.NotFound,
        StatusCodes.Status405MethodNotAllowed => ErrorCodes.MethodNotAllowed,
        StatusCodes.Status409Conflict => ErrorCodes.Conflict,
        StatusCodes.Status413PayloadTooLarge => ErrorCodes.FileTooLarge,
        StatusCodes.Status415UnsupportedMediaType => ErrorCodes.UnsupportedMediaType,
        StatusCodes.Status429TooManyRequests => ErrorCodes.RateLimited,
        >= 500 => ErrorCodes.InternalError,
        _ => null,
    };
}
