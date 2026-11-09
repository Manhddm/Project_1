using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Errors;

public sealed class ProblemDetailsTests
{
    [Fact]
    public async Task Get_UnknownApiRoute_ReturnsNotFoundProblemDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/khong-ton-tai");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal("NOT_FOUND", problem.GetProperty("code").GetString());
        Assert.Equal("Not Found", problem.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Get_WrongMethod_ReturnsMethodNotAllowedProblemDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/test-probe/validate");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(405, problem.GetProperty("status").GetInt32());
        Assert.Equal("METHOD_NOT_ALLOWED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Post_InvalidBody_ReturnsValidationProblemDetailsWithCamelCaseFields()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/test-probe/validate", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("name", out var nameErrors));
        Assert.NotEmpty(nameErrors.EnumerateArray());
    }

    [Fact]
    public async Task Post_MalformedJson_ReturnsValidationProblemDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent("{ \"name\": ", System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/test-probe/validate", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Post_UnsupportedContentType_ReturnsUnsupportedMediaTypeProblemDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent("name=x", System.Text.Encoding.UTF8, "text/plain");

        using var response = await client.PostAsync("/api/test-probe/validate", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("UNSUPPORTED_MEDIA_TYPE", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_UnhandledException_ReturnsInternalErrorWithoutDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/test-probe/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret internal detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("INTERNAL_ERROR", problem.GetProperty("code").GetString());
        Assert.Equal("Đã xảy ra lỗi hệ thống.", problem.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public void HttpLogging_DoesNotCaptureHeadersBodiesOrQuery()
    {
        using var factory = CreateFactory();

        var options = factory.Services.GetRequiredService<IOptions<HttpLoggingOptions>>().Value;

        var notAllowed = HttpLoggingFields.RequestHeaders
            | HttpLoggingFields.RequestBody
            | HttpLoggingFields.ResponseHeaders
            | HttpLoggingFields.ResponseBody
            | HttpLoggingFields.RequestQuery;

        Assert.Equal(HttpLoggingFields.None, options.LoggingFields & notAllowed);
        Assert.True(options.LoggingFields.HasFlag(HttpLoggingFields.RequestMethod));
        Assert.True(options.LoggingFields.HasFlag(HttpLoggingFields.RequestPath));
        Assert.True(options.LoggingFields.HasFlag(HttpLoggingFields.ResponseStatusCode));
        Assert.True(options.LoggingFields.HasFlag(HttpLoggingFields.Duration));
    }

    private static TestApplicationFactory CreateFactory() => new();

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }
}
