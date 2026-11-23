using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace NumenX;

public static class Hardware
{
    public static readonly Dictionary<string, string> EncoderNames = new()
    {
        ["h264_nvenc"] = "NVIDIA NVENC",
        ["h264_amf"] = "AMD AMF",
        ["h264_qsv"] = "Intel Quick Sync",
        ["libx264"] = "Software (CPU)",
    };

    public static string? FindFfmpeg()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                var p = Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe");
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    public static async Task<(int Code, string Out, string Err)> RunAsync(string exe, IEnumerable<string> args, int timeoutMs = 20000)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            return (-1, "", "timeout");
        }
        return (p.ExitCode, await o, await e);
    }

    /// <summary>Runs a tiny real encode per encoder: only hardware that actually works is listed.</summary>
    public static async Task<List<string>> DetectEncodersAsync(string ffmpeg)
    {
        var found = new List<string>();
        foreach (var enc in new[] { "h264_nvenc", "h264_amf", "h264_qsv" })
        {
            try
            {
                var r = await RunAsync(ffmpeg, new[]
                {
                    "-hide_banner", "-f", "lavfi", "-i", "color=c=black:s=640x360:r=30",
                    "-frames:v", "3", "-c:v", enc, "-pix_fmt", "yuv420p", "-f", "null", "-"
                });
                if (r.Code == 0) found.Add(enc);
            }
            catch (Exception ex) { Logger.Write($"Probe {enc} failed: {ex.Message}"); }
        }
        found.Add("libx264");
        return found;
    }

    public static async Task<bool> HasFilterAsync(string ffmpeg, string name)
    {
        try { return (await RunAsync(ffmpeg, new[] { "-hide_banner", "-filters" })).Out.Contains(name); }
        catch { return false; }
    }

    public static async Task<List<string>> ListAudioDevicesAsync(string ffmpeg)
    {
        var devs = new List<string>();
        try
        {
            var r = await RunAsync(ffmpeg, new[] { "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy" });
            bool inAudio = false;
            foreach (var line in r.Err.Split('\n'))
            {
                if (line.Contains("DirectShow audio devices")) inAudio = true;
                else if (line.Contains("DirectShow video devices")) inAudio = false;
                if (line.Contains("Alternative name")) continue;
                var m = Regex.Match(line, "\"([^\"]+)\"");
                if (!m.Success) continue;
                if (line.Contains("(audio)") || (inAudio && !line.Contains("(video)")))
                    if (!devs.Contains(m.Groups[1].Value)) devs.Add(m.Groups[1].Value);
            }
        }
        catch (Exception ex) { Logger.Write("Audio device scan failed: " + ex.Message); }
        return devs;
    }
}
