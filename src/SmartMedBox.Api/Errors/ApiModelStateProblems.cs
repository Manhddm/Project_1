using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace SmartMedBox.Api.Errors;

public static class ApiModelStateProblems
{
    public static IActionResult Create(ActionContext context)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (key, entry) in context.ModelState)
        {
            if (entry.Errors.Count == 0)
            {
                continue;
            }

            var field = NormalizeFieldName(key);
            if (!errors.TryGetValue(field, out var messages))
            {
                messages = [];
                errors[field] = messages;
            }

            messages.AddRange(entry.Errors.Select(error =>
                string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Giá trị không hợp lệ." : error.ErrorMessage));
        }

        if (errors.Count == 0)
        {
            errors["request"] = ["Dữ liệu không hợp lệ."];
        }

        var problem = ApiProblems.Create(
            context.HttpContext,
            StatusCodes.Status400BadRequest,
            ErrorCodes.ValidationError,
            errors: errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { ApiProblems.ContentType },
        };
    }

    private static string NormalizeFieldName(string key)
    {
        var name = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        if (name is "$" or "")
        {
            return "request";
        }

        var segments = name.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = JsonNamingPolicy.CamelCase.ConvertName(segments[i]);
        }

        return string.Join('.', segments);
    }
}
