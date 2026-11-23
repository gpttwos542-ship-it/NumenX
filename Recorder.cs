using System.Diagnostics;
using System.IO;

namespace NumenX;

/// <summary>
/// Screen recording engine. Drives FFmpeg: GPU desktop capture (ddagrab) or GDI fallback,
/// hardware H.264 encoding, optional microphone + desktop audio. Records to MKV first
/// (survives crashes / power loss) and remuxes to MP4 on a clean stop.
/// </summary>
public sealed class Recorder
{
    static readonly Dictionary<string, int?> Scales = new()
    {
        ["Native"] = null, ["2160p"] = 2160, ["1440p"] = 1440, ["1080p"] = 1080, ["720p"] = 720,
    };
    static readonly Dictionary<string, int> Quality = new()
    {
        ["Performance"] = 26, ["Balanced"] = 22, ["Maximum"] = 18,
    };

    Process? _p;
    readonly Queue<string> _log = new();
    bool _stopping;

    public string OutputPath { get; private set; } = "";
    public bool IsRecording => _p is { HasExited: false };
    public Process? Proc => _p;

    /// <summary>Raised if FFmpeg exits on its own (error). Argument = last FFmpeg messages.</summary>
    public event Action<string>? Failed;

    public static List<string> BuildArgs(Settings s, string enc, bool ddagrab, string outPath)
    {
        int fps = s.Fps;
        int q = Quality.GetValueOrDefault(s.Quality, 22);
        int? scale = Scales.GetValueOrDefault(s.Resolution);
        var a = new List<string> { "-hide_banner", "-y", "-loglevel", "warning", "-nostats" };

        // video input
        if (ddagrab)
            a.AddRange(new[] { "-f", "lavfi", "-i", $"ddagrab=output_idx=0:framerate={fps}:draw_mouse=1" });
        else
            a.AddRange(new[] { "-f", "gdigrab", "-framerate", fps.ToString(), "-draw_mouse", "1", "-i", "desktop" });

        // audio inputs
        var auds = new List<string>();
        if (s.Mic != "None") auds.Add(s.Mic);
        if (s.Desktop != "None" && s.Desktop != s.Mic) auds.Add(s.Desktop);
        foreach (var d in auds)
            a.AddRange(new[] { "-thread_queue_size", "1024", "-f", "dshow", "-i", $"audio={d}" });

        // filters
        var fc = new List<string>();
        string vmap = "0:v";
        string? amap = null;
        bool direct = ddagrab && enc == "h264_nvenc" && scale == null;   // zero-copy GPU path
        if (!direct)
        {
            var chain = new List<string>();
            if (ddagrab) { chain.Add("hwdownload"); chain.Add("format=bgra"); }
            if (scale != null) chain.Add($"scale=-2:{scale}:flags=bicubic");
            chain.Add(enc == "libx264" ? "format=yuv420p" : "format=nv12");
            fc.Add("[0:v]" + string.Join(",", chain) + "[v]");
            vmap = "[v]";
        }
        if (auds.Count == 2) { fc.Add("[1:a][2:a]amix=inputs=2:duration=longest[a]"); amap = "[a]"; }
        else if (auds.Count == 1) amap = "1:a";

        if (fc.Count > 0) a.AddRange(new[] { "-filter_complex", string.Join(";", fc) });
        a.AddRange(new[] { "-map", vmap });
        if (amap != null) a.AddRange(new[] { "-map", amap });

        // encoder (quality mode; streaming comes in a later stage)
        string gop = (fps * 2).ToString();
        switch (enc)
        {
            case "h264_nvenc":
                a.AddRange(new[] { "-c:v", enc, "-preset", "p5", "-tune", "hq", "-bf", "2", "-g", gop,
                                   "-profile:v", "high", "-rc", "vbr", "-cq", q.ToString(), "-b:v", "0" });
                break;
            case "h264_amf":
                a.AddRange(new[] { "-c:v", enc, "-quality", "balanced", "-g", gop, "-profile:v", "high",
                                   "-rc", "cqp", "-qp_i", q.ToString(), "-qp_p", (q + 2).ToString(), "-qp_b", (q + 3).ToString() });
                break;
            case "h264_qsv":
                a.AddRange(new[] { "-c:v", enc, "-preset", "medium", "-g", gop, "-profile:v", "high",
                                   "-global_quality", (q + 2).ToString() });
                break;
            default:
                a.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-g", gop, "-profile:v", "high",
                                   "-crf", (q + 1).ToString() });
                break;
        }
        if (amap != null) a.AddRange(new[] { "-c:a", "aac", "-b:a", "160k", "-ar", "48000" });

        a.AddRange(new[] { "-f", "matroska", outPath });
        return a;
    }

    public void Start(string ffmpeg, Settings s, string enc, bool ddagrab)
    {
        if (IsRecording) return;
        Directory.CreateDirectory(s.Folder);
        OutputPath = Path.Combine(s.Folder, $"NumenX {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mkv");

        var psi = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in BuildArgs(s, enc, ddagrab, OutputPath)) psi.ArgumentList.Add(arg);

        lock (_log) _log.Clear();
        _stopping = false;
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (_log) { _log.Enqueue(e.Data); while (_log.Count > 12) _log.Dequeue(); }
        };
        p.Exited += (_, _) =>
        {
            if (_stopping) return;
            Logger.Write("FFmpeg exited unexpectedly: " + Tail());
            Failed?.Invoke(Tail());
        };
        Logger.Write($"Recording started: encoder={enc} ddagrab={ddagrab} res={s.Resolution} fps={s.Fps} q={s.Quality}");
        p.Start();
        p.BeginErrorReadLine();
        _p = p;
    }

    public string Tail()
    {
        lock (_log) return _log.Count == 0 ? "No details from FFmpeg." : string.Join("\n", _log.TakeLast(6));
    }

    /// <summary>Stops cleanly (so the file is finalized) and returns the final file path.</summary>
    public async Task<string?> StopAsync(string ffmpeg, bool toMp4)
    {
        var p = _p;
        if (p == null) return null;
        _stopping = true;
        try { if (!p.HasExited) { p.StandardInput.Write("q"); p.StandardInput.Flush(); } } catch { }
        try
        {
            using var cts = new CancellationTokenSource(15000);
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch { try { p.Kill(true); } catch { } }
        _p = null;

        if (!File.Exists(OutputPath)) return null;
        if (!toMp4) return OutputPath;

        var mp4 = Path.ChangeExtension(OutputPath, ".mp4");
        var r = await Hardware.RunAsync(ffmpeg, new[]
        {
            "-hide_banner", "-y", "-loglevel", "error", "-i", OutputPath,
            "-c", "copy", "-movflags", "+faststart", mp4
        }, 120000).ConfigureAwait(false);
        if (r.Code == 0 && File.Exists(mp4))
        {
            try { File.Delete(OutputPath); } catch { }
            return mp4;
        }
        Logger.Write("MP4 remux failed, keeping MKV: " + r.Err);
        return OutputPath;   // never lose the recording
    }
}
