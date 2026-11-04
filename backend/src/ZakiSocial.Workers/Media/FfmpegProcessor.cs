using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace ZakiSocial.Workers.Media;

public sealed record MediaInfo(int Width, int Height, double? DurationSeconds);

/// <summary>FFmpeg على السيرفر: قراءة الأبعاد، توحيد الصور (JPEG أقصاه 2048) والفيديو (H.264/AAC بقياس أقصاه 1080p)، مصغّرات.</summary>
public sealed class FfmpegProcessor
{
    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken ct)
    {
        var output = await RunAsync("ffprobe", ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height:format=duration", "-of", "json", path], ct);
        using var doc = JsonDocument.Parse(output);
        var stream = doc.RootElement.GetProperty("streams")[0];
        double? duration = null;
        if (doc.RootElement.TryGetProperty("format", out var f) && f.TryGetProperty("duration", out var d)
            && double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            duration = seconds;
        return new MediaInfo(stream.GetProperty("width").GetInt32(), stream.GetProperty("height").GetInt32(), duration);
    }

    public Task ProcessImageAsync(string input, string output, CancellationToken ct) =>
        RunAsync("ffmpeg", ["-y", "-i", input, "-vf", "scale='min(2048,iw)':-2", "-frames:v", "1", "-q:v", "3", output], ct);

    public Task ThumbnailAsync(string input, string output, bool isVideo, CancellationToken ct) =>
        RunAsync("ffmpeg", isVideo
            ? ["-y", "-ss", "0.1", "-i", input, "-vf", "scale='min(480,iw)':-2", "-frames:v", "1", "-q:v", "4", output]
            : ["-y", "-i", input, "-vf", "scale='min(480,iw)':-2", "-frames:v", "1", "-q:v", "4", output], ct);

    public Task ProcessVideoAsync(string input, string output, CancellationToken ct) =>
        RunAsync("ffmpeg", ["-y", "-i", input,
            "-vf", "scale='min(1920,iw)':'min(1080,ih)':force_original_aspect_ratio=decrease,scale=trunc(iw/2)*2:trunc(ih/2)*2",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", output], ct);

    private static async Task<string> RunAsync(string tool, IReadOnlyList<string> args, CancellationToken ct)
    {
        var info = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"{tool} did not start");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new InvalidOperationException($"{tool} failed (exit {process.ExitCode})");
        _ = stderr;
        return await stdout;
    }
}
