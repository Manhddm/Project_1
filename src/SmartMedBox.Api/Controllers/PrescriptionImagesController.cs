using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SmartMedBox.Api.Contracts;
using SmartMedBox.Api.Errors;
using SmartMedBox.Api.Security;
using SmartMedBox.Infrastructure.Prescriptions;

namespace SmartMedBox.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/prescriptions/images")]
public sealed class PrescriptionImagesController(PrescriptionImageService images) : ControllerBase
{
    // Chừa chỗ cho header multipart; body lớn hơn bị cắt khi đọc thay vì buffer hết.
    private const long MaxRequestBytes = PrescriptionImageService.MaxBytes + 64 * 1024;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Problem(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        var (outcome, items) = await images.ListAsync(userId, cancellationToken);
        return outcome switch
        {
            PrescriptionImageAccessOutcome.Ok => Ok(new PrescriptionImageListDto(
                items!.Select(ToDto).ToArray())),
            PrescriptionImageAccessOutcome.Forbidden => Problem(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
        };
    }

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Content(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Problem(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        var (outcome, content) = await images.OpenContentAsync(userId, id, cancellationToken);
        if (outcome == PrescriptionImageAccessOutcome.Forbidden)
        {
            return Problem(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden);
        }

        if (outcome == PrescriptionImageAccessOutcome.NotFound)
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.ImageNotFound);
        }

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentDisposition = BuildContentDisposition(content!.OriginalFileName);
        return File(content.Content, content.ContentType);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Problem(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        var outcome = await images.DeleteAsync(userId, id, cancellationToken);
        return outcome switch
        {
            PrescriptionImageAccessOutcome.Ok => NoContent(),
            PrescriptionImageAccessOutcome.Forbidden => Problem(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden),
            PrescriptionImageAccessOutcome.NotFound => Problem(StatusCodes.Status404NotFound, ErrorCodes.ImageNotFound),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
        };
    }

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Problem(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        // Từ chối người thân/người ngoài trước khi đọc body.
        if (await images.GetOwnedDeviceIdAsync(userId, cancellationToken) is null)
        {
            return Problem(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden);
        }

        if (!Request.HasFormContentType)
        {
            return ApiProblemResults.Validation(HttpContext, "file", "Cần multipart/form-data với trường file.");
        }

        IFormCollection form;
        try
        {
            form = await Request.ReadFormAsync(cancellationToken);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, ErrorCodes.FileTooLarge);
        }
        catch (InvalidDataException)
        {
            return ApiProblemResults.Validation(HttpContext, "file", "Dữ liệu multipart không hợp lệ.");
        }

        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return ApiProblemResults.Validation(HttpContext, "file", "Trường file là bắt buộc.");
        }

        if (file.Length > PrescriptionImageService.MaxBytes)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, ErrorCodes.FileTooLarge);
        }

        await using var stream = file.OpenReadStream();
        var (outcome, image) = await images.UploadAsync(userId, file.FileName, stream, cancellationToken);
        return outcome switch
        {
            PrescriptionUploadOutcome.Ok => StatusCode(
                StatusCodes.Status201Created,
                new PrescriptionImageDto(image!.Id, image.OriginalFileName, image.ContentType, image.SizeBytes, image.UploadedAt)),
            PrescriptionUploadOutcome.Forbidden => Problem(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden),
            PrescriptionUploadOutcome.TooLarge => Problem(StatusCodes.Status413PayloadTooLarge, ErrorCodes.FileTooLarge),
            PrescriptionUploadOutcome.TypeNotAllowed => Problem(StatusCodes.Status415UnsupportedMediaType, ErrorCodes.FileTypeNotAllowed),
            PrescriptionUploadOutcome.Invalid => Problem(StatusCodes.Status400BadRequest, ErrorCodes.ImageInvalid),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
        };
    }

    private IActionResult Problem(int status, string code) => ApiProblemResults.Create(HttpContext, status, code);

    private static PrescriptionImageDto ToDto(PrescriptionImageView image) =>
        new(image.Id, image.OriginalFileName, image.ContentType, image.SizeBytes, image.UploadedAt);

    private static string BuildContentDisposition(string fileName)
    {
        var safeName = Path.GetFileName(fileName.Replace('\0', '_'));
        safeName = new string(safeName.Select(character =>
            char.IsControl(character) || character is '"' or '\\' or '/' or ';' ? '_' : character).ToArray());
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "image";
        }

        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(safeName);
        return disposition.ToString();
    }
}
