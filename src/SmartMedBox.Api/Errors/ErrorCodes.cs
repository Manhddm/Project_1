namespace SmartMedBox.Api.Errors;

public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string TokenInvalid = "TOKEN_INVALID";
    public const string TokenExpired = "TOKEN_EXPIRED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string EmailNotVerified = "EMAIL_NOT_VERIFIED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string DeviceNotFound = "DEVICE_NOT_FOUND";
    public const string DeviceNotPaired = "DEVICE_NOT_PAIRED";
    public const string DeviceUnauthorized = "DEVICE_UNAUTHORIZED";
    public const string EmailTaken = "EMAIL_TAKEN";
    public const string PairingCodeInvalid = "PAIRING_CODE_INVALID";
    public const string PairingCodeUsed = "PAIRING_CODE_USED";
    public const string PairingCodeExpired = "PAIRING_CODE_EXPIRED";
    public const string UserAlreadyOwnsDevice = "USER_ALREADY_OWNS_DEVICE";
    public const string DeviceAlreadyPaired = "DEVICE_ALREADY_PAIRED";
    public const string InviteAlreadyPending = "INVITE_ALREADY_PENDING";
    public const string InviteNotFound = "INVITE_NOT_FOUND";
    public const string InviteNotPending = "INVITE_NOT_PENDING";
    public const string InviteExpired = "INVITE_EXPIRED";
    public const string InviteEmailMismatch = "INVITE_EMAIL_MISMATCH";
    public const string RelationshipExists = "RELATIONSHIP_EXISTS";
    public const string RelationshipNotFound = "RELATIONSHIP_NOT_FOUND";
    public const string ScheduleNotFound = "SCHEDULE_NOT_FOUND";
    public const string ScheduleInvalidTimezone = "SCHEDULE_INVALID_TIMEZONE";
    public const string ScheduleOverlap = "SCHEDULE_OVERLAP";
    public const string ScheduleVersionConflict = "SCHEDULE_VERSION_CONFLICT";
    public const string CompartmentInvalid = "COMPARTMENT_INVALID";
    public const string EventTimeInvalid = "EVENT_TIME_INVALID";
    public const string EventTypeInvalid = "EVENT_TYPE_INVALID";
    public const string AlertNotFound = "ALERT_NOT_FOUND";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string FileTypeNotAllowed = "FILE_TYPE_NOT_ALLOWED";
    public const string ImageInvalid = "IMAGE_INVALID";
    public const string ImageNotFound = "IMAGE_NOT_FOUND";
    public const string CsrfInvalid = "CSRF_INVALID";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string RateLimited = "RATE_LIMITED";
    public const string Conflict = "CONFLICT";
    public const string InternalError = "INTERNAL_ERROR";
}
