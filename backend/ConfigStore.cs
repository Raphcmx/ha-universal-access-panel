using System.Text.Json;

namespace AccessPanel.Api;

public sealed record SentConfiguration(JsonElement Configuration, DateTimeOffset SentAt,
    string Topic, int Qos = 1, bool Retain = true, PanelStatus? PanelStatus = null);

public sealed class ConfigStore(string directory) {
    public const string Topic = "bedienpanel/panel1/config";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string path = Path.Combine(directory, "last-sent.json");
    public SemaphoreSlim PublishLock { get; } = new(1, 1);

    public async Task<SentConfiguration?> ReadAsync(CancellationToken cancellationToken = default) {
        if (!File.Exists(path)) return null;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var saved = await JsonSerializer.DeserializeAsync<SentConfiguration>(file, JsonOptions, cancellationToken)
            ?? throw new JsonException("Empty configuration snapshot.");
        if (ConfigValidator.Validate(saved.Configuration).Count > 0)
            throw new JsonException("Invalid configuration snapshot.");
        return saved;
    }

    public async Task<string> StageAsync(JsonElement configuration, CancellationToken cancellationToken) {
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".pending-{Guid.NewGuid():N}.json");
        try {
            await File.WriteAllTextAsync(temporary, configuration.GetRawText(), cancellationToken);
            return temporary;
        } catch { Discard(temporary); throw; }
    }

    public async Task<SentConfiguration> CommitAsync(string temporary, JsonElement configuration, PanelStatus? panelStatus = null, DateTimeOffset? sentAt = null) {
        var saved = new SentConfiguration(configuration.Clone(), sentAt ?? DateTimeOffset.UtcNow, Topic, PanelStatus: panelStatus);
        await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough | FileOptions.Asynchronous)) {
            await JsonSerializer.SerializeAsync(file, saved, JsonOptions);
            await file.FlushAsync();
            file.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
        return saved;
    }

    public void Discard(string temporary) {
        try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}