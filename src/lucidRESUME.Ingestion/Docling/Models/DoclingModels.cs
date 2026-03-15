using System.Text.Json;
using System.Text.Json.Serialization;

namespace lucidRESUME.Ingestion.Docling.Models;

internal record DoclingTaskSubmission(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("task_status")] string TaskStatus
);

internal record DoclingTaskStatus(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("task_status")] string TaskStatus,
    [property: JsonPropertyName("task_position")] int? TaskPosition
);

internal record DoclingApiResult(
    [property: JsonPropertyName("document")] DoclingApiDocument Document,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("errors")] List<string>? Errors,
    [property: JsonPropertyName("processing_time")] double? ProcessingTime
);

internal record DoclingApiDocument(
    [property: JsonPropertyName("md_content")] string? MdContent,
    [property: JsonPropertyName("text_content")] string? TextContent,
    // JsonElement lets us navigate the Docling document tree to extract page images
    [property: JsonPropertyName("json_content")] JsonElement? JsonContent
);
