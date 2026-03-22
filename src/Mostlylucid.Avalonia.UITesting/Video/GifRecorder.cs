using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace Mostlylucid.Avalonia.UITesting.Video;

public sealed class GifRecorder : IAsyncDisposable
{
    private readonly List<(byte[] PngData, int DelayMs)> _frames = new();
    private readonly int _fps;
    private readonly int _frameDelayMs;
    private Window? _window;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private bool _recording;
    private readonly Action<string>? _log;

    public bool IsRecording => _recording;
    public int FrameCount => _frames.Count;

    public GifRecorder(int fps = 5, Action<string>? log = null)
    {
        _fps = Math.Clamp(fps, 1, 30);
        _frameDelayMs = 1000 / _fps;
        _log = log;
    }

    public void StartRecording(Window window)
    {
        if (_recording) return;

        _window = window;
        _frames.Clear();
        _recording = true;
        _cts = new CancellationTokenSource();

        _captureTask = Task.Run(() => CaptureLoopAsync(_cts.Token));
        _log?.Invoke($"GIF recording started at {_fps} fps");
    }

    public async Task StopRecordingAsync()
    {
        if (!_recording) return;

        _recording = false;
        _cts?.Cancel();

        if (_captureTask != null)
        {
            try { await _captureTask; }
            catch (OperationCanceledException) { }
        }

        _log?.Invoke($"GIF recording stopped. {_frames.Count} frames captured");
    }

    public async Task<string> SaveAsync(string filePath)
    {
        if (_frames.Count == 0)
            throw new InvalidOperationException("No frames captured");

        await Task.Run(() => EncodeGif(filePath));
        _log?.Invoke($"GIF saved: {filePath} ({new FileInfo(filePath).Length / 1024}KB)");
        return filePath;
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var frameData = await CaptureFrameAsync();
                if (frameData != null)
                {
                    _frames.Add((frameData, _frameDelayMs));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Frame capture error: {ex.Message}");
            }

            try
            {
                await Task.Delay(_frameDelayMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<byte[]?> CaptureFrameAsync()
    {
        if (_window == null) return null;

        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _window.UpdateLayout();

            var width = Math.Max(100, (int)_window.Bounds.Width);
            var height = Math.Max(100, (int)_window.Bounds.Height);

            var size = new PixelSize(width, height);
            var dpi = new Vector(96, 96);

            using var bitmap = new RenderTargetBitmap(size, dpi);
            bitmap.Render(_window);

            using var ms = new MemoryStream();
            bitmap.Save(ms);
            return ms.ToArray();
        });
    }

    private void EncodeGif(string filePath)
    {
        if (_frames.Count == 0) return;

        // Decode the first frame to get dimensions
        using var firstImage = SKBitmap.Decode(_frames[0].PngData);
        var width = firstImage.Width;
        var height = firstImage.Height;

        using var stream = File.Create(filePath);
        using var writer = new BinaryWriter(stream);

        // GIF89a header
        writer.Write("GIF89a"u8);

        // Logical Screen Descriptor
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((byte)0x70); // no global color table, 8 bits color resolution
        writer.Write((byte)0);    // background color index
        writer.Write((byte)0);    // pixel aspect ratio

        // Netscape Application Extension (for looping)
        writer.Write((byte)0x21); // extension introducer
        writer.Write((byte)0xFF); // application extension
        writer.Write((byte)11);   // block size
        writer.Write("NETSCAPE2.0"u8);
        writer.Write((byte)3);    // sub-block size
        writer.Write((byte)1);    // sub-block ID
        writer.Write((ushort)0);  // loop count (0 = infinite)
        writer.Write((byte)0);    // block terminator

        foreach (var (pngData, delayMs) in _frames)
        {
            using var bitmap = SKBitmap.Decode(pngData);
            if (bitmap == null) continue;

            var resized = bitmap;
            if (bitmap.Width != width || bitmap.Height != height)
            {
                resized = bitmap.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Nearest));
            }

            // Build local color table via color quantization
            var (indexedPixels, colorTable, transparentIndex) = QuantizeFrame(resized, 256);

            if (resized != bitmap) resized.Dispose();

            var colorTableSize = colorTable.Length / 3;
            var tableSizeBits = (int)Math.Ceiling(Math.Log2(Math.Max(2, colorTableSize)));
            var tableEntries = 1 << tableSizeBits;

            // Graphic Control Extension
            writer.Write((byte)0x21); // extension introducer
            writer.Write((byte)0xF9); // graphic control label
            writer.Write((byte)4);    // block size
            var disposalMethod = transparentIndex >= 0 ? (byte)0x09 : (byte)0x08; // restore to bg if transparent
            writer.Write(disposalMethod);
            writer.Write((ushort)(delayMs / 10)); // delay in centiseconds
            writer.Write((byte)(transparentIndex >= 0 ? transparentIndex : 0)); // transparent color index
            writer.Write((byte)0); // block terminator

            // Image Descriptor
            writer.Write((byte)0x2C); // image separator
            writer.Write((ushort)0);  // left
            writer.Write((ushort)0);  // top
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            writer.Write((byte)(0x80 | (tableSizeBits - 1))); // local color table flag + size

            // Local Color Table
            var paddedTable = new byte[tableEntries * 3];
            Array.Copy(colorTable, paddedTable, Math.Min(colorTable.Length, paddedTable.Length));
            writer.Write(paddedTable);

            // LZW compressed image data
            var minCodeSize = Math.Max(2, tableSizeBits);
            writer.Write((byte)minCodeSize);
            var lzwData = LzwEncode(indexedPixels, minCodeSize);
            WriteSubBlocks(writer, lzwData);
            writer.Write((byte)0); // block terminator
        }

        // GIF Trailer
        writer.Write((byte)0x3B);
    }

    private static (byte[] Pixels, byte[] ColorTable, int TransparentIndex) QuantizeFrame(SKBitmap bitmap, int maxColors)
    {
        // Simple median-cut-ish quantization: collect unique colors, pick top N
        var colorCounts = new Dictionary<int, int>();
        var pixels = new byte[bitmap.Width * bitmap.Height];

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                // Reduce precision slightly for better grouping
                var key = ((c.Red >> 2) << 16) | ((c.Green >> 2) << 8) | (c.Blue >> 2);
                colorCounts.TryGetValue(key, out var count);
                colorCounts[key] = count + 1;
            }
        }

        var palette = colorCounts
            .OrderByDescending(kv => kv.Value)
            .Take(maxColors)
            .Select(kv => kv.Key)
            .ToList();

        var paletteIndex = new Dictionary<int, byte>();
        for (int i = 0; i < palette.Count; i++)
            paletteIndex[palette[i]] = (byte)i;

        var colorTable = new byte[palette.Count * 3];
        for (int i = 0; i < palette.Count; i++)
        {
            colorTable[i * 3] = (byte)((palette[i] >> 16) << 2);
            colorTable[i * 3 + 1] = (byte)(((palette[i] >> 8) & 0xFF) << 2);
            colorTable[i * 3 + 2] = (byte)((palette[i] & 0xFF) << 2);
        }

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                var key = ((c.Red >> 2) << 16) | ((c.Green >> 2) << 8) | (c.Blue >> 2);

                if (paletteIndex.TryGetValue(key, out var idx))
                {
                    pixels[y * bitmap.Width + x] = idx;
                }
                else
                {
                    // Find nearest color
                    pixels[y * bitmap.Width + x] = FindNearest(key, palette);
                }
            }
        }

        return (pixels, colorTable, -1);
    }

    private static byte FindNearest(int key, List<int> palette)
    {
        int r = (key >> 16) & 0xFF;
        int g = (key >> 8) & 0xFF;
        int b = key & 0xFF;
        int bestDist = int.MaxValue;
        byte bestIdx = 0;

        for (int i = 0; i < palette.Count; i++)
        {
            int pr = (palette[i] >> 16) & 0xFF;
            int pg = (palette[i] >> 8) & 0xFF;
            int pb = palette[i] & 0xFF;
            int dist = (r - pr) * (r - pr) + (g - pg) * (g - pg) + (b - pb) * (b - pb);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = (byte)i;
            }
        }

        return bestIdx;
    }

    private static byte[] LzwEncode(byte[] pixels, int minCodeSize)
    {
        var clearCode = 1 << minCodeSize;
        var eoiCode = clearCode + 1;

        using var output = new MemoryStream();
        var bitWriter = new LzwBitWriter(output);

        var codeSize = minCodeSize + 1;
        var nextCode = eoiCode + 1;
        var maxCode = (1 << codeSize) - 1;

        // Initialize code table
        var codeTable = new Dictionary<string, int>();
        for (int i = 0; i < clearCode; i++)
            codeTable[((char)i).ToString()] = i;

        bitWriter.WriteBits(clearCode, codeSize);

        if (pixels.Length == 0)
        {
            bitWriter.WriteBits(eoiCode, codeSize);
            bitWriter.Flush();
            return output.ToArray();
        }

        var buffer = ((char)pixels[0]).ToString();

        for (int i = 1; i < pixels.Length; i++)
        {
            var c = (char)pixels[i];
            var test = buffer + c;

            if (codeTable.ContainsKey(test))
            {
                buffer = test;
            }
            else
            {
                bitWriter.WriteBits(codeTable[buffer], codeSize);

                if (nextCode <= 4095)
                {
                    codeTable[test] = nextCode++;
                    if (nextCode > maxCode + 1 && codeSize < 12)
                    {
                        codeSize++;
                        maxCode = (1 << codeSize) - 1;
                    }
                }
                else
                {
                    // Reset
                    bitWriter.WriteBits(clearCode, codeSize);
                    codeSize = minCodeSize + 1;
                    nextCode = eoiCode + 1;
                    maxCode = (1 << codeSize) - 1;
                    codeTable.Clear();
                    for (int j = 0; j < clearCode; j++)
                        codeTable[((char)j).ToString()] = j;
                }

                buffer = c.ToString();
            }
        }

        bitWriter.WriteBits(codeTable[buffer], codeSize);
        bitWriter.WriteBits(eoiCode, codeSize);
        bitWriter.Flush();

        return output.ToArray();
    }

    private static void WriteSubBlocks(BinaryWriter writer, byte[] data)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            var blockSize = Math.Min(255, data.Length - offset);
            writer.Write((byte)blockSize);
            writer.Write(data, offset, blockSize);
            offset += blockSize;
        }
    }

    public async Task<string?> TryExportMp4Async(string filePath, string? ffmpegPath = null)
    {
        if (_frames.Count == 0) return null;

        var ffmpeg = ffmpegPath ?? "ffmpeg";

        // Write frames as temp PNGs
        var tempDir = Path.Combine(Path.GetTempPath(), $"uitest_mp4_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            for (int i = 0; i < _frames.Count; i++)
            {
                var framePath = Path.Combine(tempDir, $"frame_{i:D5}.png");
                await File.WriteAllBytesAsync(framePath, _frames[i].PngData);
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-y -framerate {_fps} -i \"{Path.Combine(tempDir, "frame_%05d.png")}\" -c:v libx264 -pix_fmt yuv420p \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return null;

            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && File.Exists(filePath))
            {
                _log?.Invoke($"MP4 saved: {filePath}");
                return filePath;
            }

            var error = await process.StandardError.ReadToEndAsync();
            _log?.Invoke($"FFmpeg failed: {error}");
            return null;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"MP4 export failed: {ex.Message}");
            return null;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopRecordingAsync();
        _cts?.Dispose();
    }
}

internal sealed class LzwBitWriter
{
    private readonly Stream _output;
    private int _bitBuffer;
    private int _bitsInBuffer;

    public LzwBitWriter(Stream output)
    {
        _output = output;
    }

    public void WriteBits(int code, int numBits)
    {
        _bitBuffer |= code << _bitsInBuffer;
        _bitsInBuffer += numBits;

        while (_bitsInBuffer >= 8)
        {
            _output.WriteByte((byte)(_bitBuffer & 0xFF));
            _bitBuffer >>= 8;
            _bitsInBuffer -= 8;
        }
    }

    public void Flush()
    {
        if (_bitsInBuffer > 0)
        {
            _output.WriteByte((byte)(_bitBuffer & 0xFF));
        }
        _bitBuffer = 0;
        _bitsInBuffer = 0;
    }
}
