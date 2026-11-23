using System.IO;
using System.Text.Json;

namespace NumenX;

public class Settings
{
    public string Resolution { get; set; } = "1080p";   // 720p, 1080p, 1440p, 2160p, Native
    public int Fps { get; set; } = 60;                   // 30 or 60
    public string Quality { get; set; } = "Balanced";    // Performance, Balanced, Maximum
    public string Encoder { get; set; } = "Auto";
    public string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "NumenX");
    public bool Mp4 { get; set; } = true;
    public string Accent { get; set; } = "#7C5CFF";
    public string Mic { get; set; } = "None";
    public string Desktop { get; set; } = "None";

    static readonly string PathFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NumenX", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(PathFile))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathFile)) ?? new Settings();
        }
        catch (Exception ex) { Logger.Write("Settings load failed: " + ex.Message); }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathFile)!);
            File.WriteAllText(PathFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Logger.Write("Settings save failed: " + ex.Message); }
    }
}
