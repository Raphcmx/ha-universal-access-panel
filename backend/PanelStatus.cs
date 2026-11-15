using System.Text.Json;
namespace AccessPanel.Api;

public sealed record PanelStatus(string Status, bool? Changed, string? ConfigHash,
    int? Groups, int? Functions, string? Error, DateTimeOffset ReceivedAt) {
    public static PanelStatus? Parse(string payload) {
        try {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var status)
                || status.ValueKind != JsonValueKind.String || status.GetString() is not ("ok" or "error")) return null;
            string? Text(string key) => root.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            int? Count(string key) => root.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) && count >= 0 ? count : null;
            bool? changed = root.TryGetProperty("changed", out var change)
                && change.ValueKind is JsonValueKind.True or JsonValueKind.False ? change.GetBoolean() : null;
            var hash = Text("config_hash");
            var groups = Count("groups");
            var functions = Count("functions");
            if (status.GetString() == "ok" && (changed is null || string.IsNullOrWhiteSpace(hash)
                || groups is null || functions is null)) return null;
            return new(status.GetString()!, changed, hash, groups, functions, Text("error"), DateTimeOffset.UtcNow);
        } catch (JsonException) { return null; }
    }
}