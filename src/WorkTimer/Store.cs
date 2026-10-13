using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkTimer;

public class Running
{
    [JsonPropertyName("task")] public string Task { get; set; } = "";
    [JsonPropertyName("start")] public long Start { get; set; }
}

public class SessionDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("task")] public string Task { get; set; } = "";
    [JsonPropertyName("start")] public long Start { get; set; }
    [JsonPropertyName("end")] public long End { get; set; }
}

public class Data
{
    [JsonPropertyName("sessions")] public List<SessionDto> Sessions { get; set; } = new();
    [JsonPropertyName("running")] public Running? Running { get; set; }

    public IEnumerable<Session> ToSessions() => Sessions.Select(s => new Session(s.Id, s.Task, s.Start, s.End));
}

// 旧Electron版と同じ %APPDATA%\work-timer\records.json を読み書きする(記録を引き継ぐ)
public static class Store
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DataFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "work-timer", "records.json");

    public static Data Load()
    {
        try { return JsonSerializer.Deserialize<Data>(File.ReadAllText(DataFile)) ?? new Data(); }
        catch { return new Data(); }
    }

    public static void Save(Data data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DataFile)!);
        var tmp = DataFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
        File.Move(tmp, DataFile, overwrite: true);
    }
}
