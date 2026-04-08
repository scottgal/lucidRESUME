using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace lucidRESUME.Ingestion.Layout;

/// <summary>
/// Detects document layout regions from page images using a YOLO DocLayNet ONNX model.
/// Returns bounding boxes with region types: Title, Section-header, Text, Table, List-item, etc.
///
/// Model: YOLOv10m trained on IBM DocLayNet (58MB ONNX).
/// Hosted at: scottgal/doclaynet-yolov10m-onnx on HuggingFace.
///
/// LAZY-LOADED: the model is only loaded when first needed, not at app startup.
/// </summary>
public sealed class DocumentLayoutDetector : IDisposable
{
    private static readonly string[] Labels =
        ["Caption", "Footnote", "Formula", "List-item", "Page-footer", "Page-header",
         "Picture", "Section-header", "Table", "Text", "Title"];

    private const string ModelUrl = "https://huggingface.co/scottgal/doclaynet-yolov10m-onnx/resolve/main/model.onnx";
    private const int InputSize = 640;
    private const float ConfidenceThreshold = 0.3f;

    private readonly ILogger<DocumentLayoutDetector> _logger;
    private readonly HttpClient _http;
    private InferenceSession? _session;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public DocumentLayoutDetector(ILogger<DocumentLayoutDetector> logger, HttpClient http)
    {
        _logger = logger;
        _http = http;
    }

    /// <summary>
    /// Detect layout regions from a page image file (PNG).
    /// Returns bounding boxes with region labels and confidence scores.
    /// Model is downloaded and loaded lazily on first call.
    /// </summary>
    public async Task<List<LayoutRegion>> DetectAsync(string imagePath, CancellationToken ct = default)
    {
        if (!File.Exists(imagePath)) return [];

        await EnsureModelLoadedAsync(ct);
        if (_session is null) return [];

        // Load and preprocess image using SkiaSharp (already a dependency)
        var imageData = PreprocessImage(imagePath);
        if (imageData is null) return [];

        // Run inference
        var inputTensor = new DenseTensor<float>(imageData, [1, 3, InputSize, InputSize]);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("images", inputTensor) };

        using var results = _session.Run(inputs);
        var output = results.First().AsTensor<float>();

        return ParseDetections(output, imagePath);
    }

    /// <summary>
    /// Detect layout from raw image bytes (PNG/JPEG).
    /// </summary>
    public async Task<List<LayoutRegion>> DetectFromBytesAsync(byte[] imageBytes, int imageWidth, int imageHeight, CancellationToken ct = default)
    {
        await EnsureModelLoadedAsync(ct);
        if (_session is null) return [];

        var imageData = PreprocessBytes(imageBytes, imageWidth, imageHeight);
        if (imageData is null) return [];

        var inputTensor = new DenseTensor<float>(imageData, [1, 3, InputSize, InputSize]);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("images", inputTensor) };

        using var results = _session.Run(inputs);
        var output = results.First().AsTensor<float>();

        return ParseYolov10Detections(output);
    }

    private async Task EnsureModelLoadedAsync(CancellationToken ct)
    {
        if (_session is not null) return;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_session is not null) return;

            var modelDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "lucidRESUME", "models", "doclaynet");
            var modelPath = Path.Combine(modelDir, "model.onnx");

            if (!File.Exists(modelPath))
            {
                _logger.LogInformation("Downloading DocLayNet layout model (~59MB)...");
                Directory.CreateDirectory(modelDir);
                var response = await _http.GetAsync(ModelUrl, ct);
                response.EnsureSuccessStatusCode();
                await using var fs = File.Create(modelPath);
                await response.Content.CopyToAsync(fs, ct);
                _logger.LogInformation("DocLayNet model downloaded.");
            }

            var opts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            opts.InterOpNumThreads = 1;
            _session = new InferenceSession(modelPath, opts);
            _logger.LogInformation("DocLayNet layout model loaded ({Labels} classes)", Labels.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load DocLayNet model — layout detection disabled");
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private static float[]? PreprocessImage(string imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        using var bitmap = SKBitmap.Decode(stream);
        if (bitmap is null) return null;
        return BitmapToTensor(bitmap);
    }

    private static float[]? PreprocessBytes(byte[] imageBytes, int width, int height)
    {
        using var bitmap = SKBitmap.Decode(imageBytes);
        if (bitmap is null) return null;
        return BitmapToTensor(bitmap);
    }

    /// <summary>
    /// Resize bitmap to InputSize x InputSize and convert to CHW float tensor normalised [0,1].
    /// YOLO expects: [1, 3, 640, 640] with channels = RGB, values in [0, 1].
    /// </summary>
    private static float[] BitmapToTensor(SKBitmap original)
    {
        // Resize with letterboxing (maintain aspect ratio, pad with grey)
        using var resized = new SKBitmap(InputSize, InputSize);
        var scaleX = (float)InputSize / original.Width;
        var scaleY = (float)InputSize / original.Height;
        var scale = Math.Min(scaleX, scaleY);
        var newW = (int)(original.Width * scale);
        var newH = (int)(original.Height * scale);
        var padX = (InputSize - newW) / 2;
        var padY = (InputSize - newH) / 2;

        using var canvas = new SKCanvas(resized);
        canvas.Clear(new SKColor(114, 114, 114)); // YOLO standard grey padding
        var destRect = new SKRect(padX, padY, padX + newW, padY + newH);
        canvas.DrawBitmap(original, destRect);

        // Convert to CHW float array normalised [0,1]
        var tensor = new float[3 * InputSize * InputSize];
        var pixels = resized.Pixels;

        for (var y = 0; y < InputSize; y++)
        {
            for (var x = 0; x < InputSize; x++)
            {
                var pixel = pixels[y * InputSize + x];
                var idx = y * InputSize + x;
                tensor[0 * InputSize * InputSize + idx] = pixel.Red / 255f;   // R channel
                tensor[1 * InputSize * InputSize + idx] = pixel.Green / 255f; // G channel
                tensor[2 * InputSize * InputSize + idx] = pixel.Blue / 255f;  // B channel
            }
        }

        return tensor;
    }

    private List<LayoutRegion> ParseDetections(Tensor<float> output, string imagePath)
    {
        return ParseYolov10Detections(output);
    }

    private List<LayoutRegion> ParseYolov10Detections(Tensor<float> output)
    {
        var regions = new List<LayoutRegion>();
        var dims = output.Dimensions;

        // YOLOv10 output: [1, N, 6] where N is number of detections
        // Each detection: [x1, y1, x2, y2, confidence, class_id]
        if (dims.Length == 3)
        {
            var numDetections = dims[1];
            for (var i = 0; i < numDetections; i++)
            {
                var confidence = output[0, i, 4];
                if (confidence < ConfidenceThreshold) continue;

                var classId = (int)output[0, i, 5];
                if (classId < 0 || classId >= Labels.Length) continue;

                regions.Add(new LayoutRegion
                {
                    Label = Labels[classId],
                    ClassId = classId,
                    Confidence = confidence,
                    X1 = output[0, i, 0] / InputSize,
                    Y1 = output[0, i, 1] / InputSize,
                    X2 = output[0, i, 2] / InputSize,
                    Y2 = output[0, i, 3] / InputSize,
                });
            }
        }

        return regions.OrderBy(r => r.Y1).ThenBy(r => r.X1).ToList();
    }

    public void Dispose()
    {
        _session?.Dispose();
        _loadLock.Dispose();
    }
}

/// <summary>
/// A detected region in a document page image.
/// Coordinates are normalised [0,1] relative to image dimensions.
/// </summary>
public sealed class LayoutRegion
{
    public string Label { get; init; } = "";
    public int ClassId { get; init; }
    public float Confidence { get; init; }
    public float X1 { get; init; } // top-left X (normalised)
    public float Y1 { get; init; } // top-left Y (normalised)
    public float X2 { get; init; } // bottom-right X (normalised)
    public float Y2 { get; init; } // bottom-right Y (normalised)

    public float Width => X2 - X1;
    public float Height => Y2 - Y1;
    public float CenterX => (X1 + X2) / 2;
    public float CenterY => (Y1 + Y2) / 2;
    public float Area => Width * Height;
}

/// <summary>
/// Structural hash of a document layout — used for template matching.
/// Two documents with the same visual layout (same heading positions, column structure)
/// produce the same hash, even if the text content differs.
/// </summary>
public static class DocumentLayoutHash
{
    /// <summary>
    /// Compute a structural hash from layout regions.
    /// Position-invariant within tolerance — small shifts don't change the hash.
    /// </summary>
    public static string Compute(IReadOnlyList<LayoutRegion> regions)
    {
        if (regions.Count == 0) return "empty";

        // Quantise positions to grid cells (10x10 grid)
        // and create a string of (label, gridX, gridY) tuples
        var parts = regions
            .OrderBy(r => r.Y1).ThenBy(r => r.X1)
            .Select(r =>
            {
                var gx = (int)(r.CenterX * 10);
                var gy = (int)(r.CenterY * 10);
                return $"{r.Label[0]}{gx}{gy}";
            });

        var layoutString = string.Join("|", parts);

        // Hash the layout string
        var bytes = System.Text.Encoding.UTF8.GetBytes(layoutString);
        var hash = System.IO.Hashing.XxHash64.Hash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
