using System.Text.Json;
using System.Text.Json.Nodes;
using AccessPanel.Api;

var exampleText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "panel1.json"));
var example = JsonDocument.Parse(exampleText).RootElement.Clone();
var count = 0;
void Check(bool condition, string name) {
    if (!condition) throw new Exception($"FAIL: {name}");
    Console.WriteLine($"PASS: {name}"); count++;
}
void Invalid(string name, Action<JsonNode> mutate) {
    var node = JsonNode.Parse(exampleText)!;
    mutate(node);
    Check(ConfigValidator.Validate(JsonSerializer.SerializeToElement(node)).Count > 0, name);
}
Check(ConfigValidator.Validate(example).Count == 0, "Firmware example with implicit service mode is valid");
Invalid("Dangling group references rejected", root => root["groups"]![0]!["functions"]![0]!["name"] = "missing");
Invalid("Duplicate function identities rejected", root => root["allFunctions"]!.AsArray().Add(root["allFunctions"]![0]!.DeepClone()));
Invalid("Duplicate group references rejected", root => root["groups"]![0]!["functions"]!.AsArray().Add(root["groups"]![0]!["functions"]![0]!.DeepClone()));
Invalid("Empty composed actions rejected", root => root["allFunctions"]![2]!["functions"] = new JsonArray());
Invalid("Unknown firmware modes rejected", root => root["allFunctions"]![0]!["mode"] = "position");
Invalid("Non-light brightness rejected", root => root["allFunctions"]![0]!["entity_id"] = "cover.kitchen");
Invalid("Out-of-range brightness rejected", root => root["allFunctions"]![0]!["defaultValue"] = 101);
Invalid("Cleared numeric values rejected", root => root["allFunctions"]![0]!["stepSize"] = null);
Invalid("Non-boolean autoExecute rejected", root => root["allFunctions"]![0]!["autoExecute"] = "false");
Invalid("Missing action service rejected", root => root["allFunctions"]![2]!["functions"]![0]!.AsObject().Remove("service"));
Invalid("Non-object child rejected", root => root["allFunctions"]![2]!["functions"]![0] = 12);
Invalid("Nested composed rejected", root => root["allFunctions"]![2]!["functions"]![0]!["type"] = "COMPOSED");
Invalid("Duplicate action names rejected", root => root["allFunctions"]![2]!["functions"]![1]!["name"] = "aus");
foreach (var text in new[] { "null", "[]", "{}", "{\"allFunctions\":[],\"groups\":null}" })
    Check(ConfigValidator.Validate(JsonDocument.Parse(text).RootElement).Count > 0, $"Malformed root rejected: {text}");

var panelOk = PanelStatus.Parse("""{"status":"ok","changed":false,"config_hash":"5c4906579f0b34fbc8427f1054d26b35","groups":2,"functions":4}""");
Check(panelOk is { Status: "ok", Changed: false, Groups: 2, Functions: 4 }, "Unchanged panel response is successful");
Check(panelOk!.ConfigHash == "5c4906579f0b34fbc8427f1054d26b35", "Panel config hash is preserved");
Check(PanelStatus.Parse("""{"status":"error","error":"Invalid configuration"}""") is { Status: "error", Error: "Invalid configuration" }, "Panel errors preserve firmware text");
foreach (var invalidReply in new[] { "null", "[]", "broken", "{}", "{\"status\":\"ok\"}", "{\"status\":\"unknown\"}" })
    Check(PanelStatus.Parse(invalidReply) is null, "Malformed or incomplete status is ignored: " + invalidReply);

var directory = Path.Combine(Path.GetTempPath(), $"access-panel-store-tests-{Guid.NewGuid():N}");
try {
    var store = new ConfigStore(directory);
    Check(await store.ReadAsync() is null, "Fresh installation has no last-sent configuration");
    var stage = await store.StageAsync(example, CancellationToken.None);
    Check(await store.ReadAsync() is null, "Unconfirmed publish does not create last-sent state");
    var saved = await store.CommitAsync(stage, example);
    Check(saved.Topic == "bedienpanel/panel1/config" && saved.Qos == 1 && saved.Retain, "Snapshot records MQTT metadata");
    var restarted = new ConfigStore(directory);
    var restored = await restarted.ReadAsync();
    Check(restored is not null && JsonElement.DeepEquals(example, restored.Configuration), "Confirmed configuration survives store restart exactly");
    Check(restored!.SentAt == saved.SentAt, "Confirmation timestamp survives restart");
    var changed = JsonNode.Parse(exampleText)!;
    changed["allFunctions"]![0]!["defaultValue"] = 25;
    var replacement = JsonSerializer.SerializeToElement(changed);
    var failedStage = await store.StageAsync(replacement, CancellationToken.None);
    store.Discard(failedStage);
    Check(JsonElement.DeepEquals(example, (await store.ReadAsync())!.Configuration), "Aborted publish preserves previous confirmed snapshot");
    var nextStage = await store.StageAsync(replacement, CancellationToken.None);
    await store.CommitAsync(nextStage, replacement);
    Check(JsonElement.DeepEquals(replacement, (await store.ReadAsync())!.Configuration), "Next confirmed publish replaces snapshot");
    var panelStage = await store.StageAsync(replacement, CancellationToken.None);
    var withPanel = await store.CommitAsync(panelStage, replacement, panelOk, saved.SentAt);
    var withPanelRestored = await new ConfigStore(directory).ReadAsync();
    Check(withPanelRestored!.PanelStatus == panelOk, "Panel response survives store restart");
    Check(withPanel.SentAt == saved.SentAt, "Adding panel feedback preserves publish timestamp");
    await File.WriteAllTextAsync(Path.Combine(directory, "last-sent.json"), "corrupt");
    var detected = false;
    try { await store.ReadAsync(); } catch (JsonException) { detected = true; }
    Check(detected, "Corrupt stored snapshot is reported rather than treated as empty");
} finally {
    var resolved = Path.GetFullPath(directory);
    if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(resolved).StartsWith("access-panel-store-tests-", StringComparison.Ordinal))
        Directory.Delete(resolved, recursive: true);
}
Console.WriteLine($"{count} backend checks passed.");
