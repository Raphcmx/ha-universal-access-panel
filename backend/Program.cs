using System.Net.Http.Headers;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Protocol;
using AccessPanel.Api;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://0.0.0.0:8099");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
builder.Services.AddHttpClient("supervisor", client => {
    client.BaseAddress = new Uri("http://supervisor/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton(new ConfigStore(builder.Configuration["DataDirectory"]
    ?? (OperatingSystem.IsLinux() ? "/data" : Path.Combine(builder.Environment.ContentRootPath, "data"))));
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions {
    OnPrepareResponse = context => {
        if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase)) {
            context.Context.Response.Headers.CacheControl = "no-cache";
        }
    }
});

app.MapGet("/api/config/last", async (ConfigStore store, HttpContext context) => {
    context.Response.Headers.CacheControl = "no-store";
    try {
        var saved = await store.ReadAsync(context.RequestAborted);
        return saved is null ? Results.NoContent() : Results.Ok(saved);
    } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) {
        app.Logger.LogError(ex, "Stored configuration could not be read");
        return Results.Json(new { error = "Die zuletzt gesendete Konfiguration konnte nicht gelesen werden. Bitte Add-on-Log prüfen." }, statusCode: 500);
    }
});
app.MapPost("/api/config/validate", (JsonElement configuration) => {
    var errors = ConfigValidator.Validate(configuration);
    return errors.Count == 0 ? Results.Ok(new { valid = true })
        : Results.Json(new { error = "Bitte die Konfiguration korrigieren.", errors }, statusCode: 400);
});
app.MapPost("/api/mqtt/test", (IHttpClientFactory clients, ConfigStore store, HttpContext context) =>
    PublishMqtt(clients, store, context, null));
app.MapPost("/api/mqtt/config", (JsonElement configuration, IHttpClientFactory clients, ConfigStore store, HttpContext context) =>
    PublishMqtt(clients, store, context, configuration));

async Task<IResult> PublishMqtt(IHttpClientFactory httpClients, ConfigStore store, HttpContext context, JsonElement? configuration) {
    if (configuration.HasValue) {
        var errors = ConfigValidator.Validate(configuration.Value);
        if (errors.Count > 0)
            return Results.Json(new { error = "Bitte die Konfiguration korrigieren.", errors }, statusCode: 400);
    }
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    timeout.CancelAfter(TimeSpan.FromSeconds(20));
    var stage = "Broker-Konfiguration";
    var locked = false;
    string? stagedFile = null;
    var brokerConfirmed = false;
    try {
        var token = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
            return Results.Json(new { error = "SUPERVISOR_TOKEN fehlt. Bitte im Home-Assistant-Add-on testen." }, statusCode: 503);
        if (configuration.HasValue) {
            await store.PublishLock.WaitAsync(timeout.Token);
            locked = true;
            stage = "Speicher vorbereiten";
            stagedFile = await store.StageAsync(configuration.Value, timeout.Token);
        }
        stage = "Broker-Konfiguration";
        using var request = new HttpRequestMessage(HttpMethod.Get, "services/mqtt");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClients.CreateClient("supervisor").SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
            return Results.Json(new { error = $"MQTT-Konfiguration nicht verfügbar (HTTP {(int)response.StatusCode}). Bitte Mosquitto und mqtt:need prüfen." }, statusCode: 503);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        if (!json.RootElement.TryGetProperty("result", out var result) || result.GetString() != "ok"
            || !json.RootElement.TryGetProperty("data", out var data))
            throw new InvalidOperationException("Invalid service response.");
        var host = data.GetProperty("host").GetString();
        if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("Missing MQTT host.");
        var port = data.TryGetProperty("port", out var p) ? p.GetInt32() : 1883;
        var username = data.TryGetProperty("username", out var u) ? u.GetString() : null;
        var password = data.TryGetProperty("password", out var pw) ? pw.GetString() : null;
        var options = new MqttClientOptionsBuilder()
            .WithClientId($"access-panel-{Guid.NewGuid():N}").WithTcpServer(host, port);
        if (!string.IsNullOrEmpty(username)) options.WithCredentials(username, password);
        if (data.TryGetProperty("ssl", out var ssl) && ssl.ValueKind == JsonValueKind.True)
            options.WithTlsOptions(tls => tls.UseTls());
        using var client = new MqttClientFactory().CreateMqttClient();
        stage = "Broker-Verbindung";
        await client.ConnectAsync(options.Build(), timeout.Token);
        var panelReply = new TaskCompletionSource<PanelStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acceptingReplies = 0;
        if (configuration.HasValue) {
            client.ApplicationMessageReceivedAsync += args => {
                // Ignore old retained subscription deliveries; fresh publishes use RETAIN=false.
                if (Volatile.Read(ref acceptingReplies) == 0 || args.ApplicationMessage.Retain
                    || args.ApplicationMessage.Topic != ConfigStore.Topic + "/status") return Task.CompletedTask;
                var status = PanelStatus.Parse(args.ApplicationMessage.ConvertPayloadToString());
                if (status is not null) panelReply.TrySetResult(status);
                return Task.CompletedTask;
            };
            stage = "Status-Topic abonnieren";
            var subscription = await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(filter => filter.WithTopic(ConfigStore.Topic + "/status")
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)).Build(), timeout.Token);
            if (subscription.Items.Any(item => (int)item.ResultCode >= 128))
                throw new InvalidOperationException("Status subscription rejected.");
        }
        var topic = configuration.HasValue ? ConfigStore.Topic : "access_panel/test";
        var payload = configuration.HasValue ? configuration.Value.GetRawText()
            : JsonSerializer.Serialize(new { source = "access_panel", message = "MQTT-Test", timestamp = DateTimeOffset.UtcNow });
        var message = new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(configuration.HasValue).Build();
        stage = "Veröffentlichen";
        Volatile.Write(ref acceptingReplies, 1);
        var published = await client.PublishAsync(message, timeout.Token);
        if ((int)published.ReasonCode >= 128) throw new InvalidOperationException("Publish rejected.");
        brokerConfirmed = true;
        if (configuration.HasValue) {
            stage = "Konfiguration speichern";
            // Once PUBACK arrives, persist even when the browser disconnects.
            var saved = await store.CommitAsync(stagedFile!, configuration.Value);
            stagedFile = null;
            // PUBACK is already persisted, even when panel feedback times out.
            PanelStatus? panelStatus = null;
            try {
                panelStatus = await panelReply.Task.WaitAsync(TimeSpan.FromSeconds(8), timeout.Token);
            } catch (TimeoutException) { }
              catch (OperationCanceledException) { }
            if (panelStatus is not null) {
                stage = "Panelantwort speichern";
                stagedFile = await store.StageAsync(configuration.Value, CancellationToken.None);
                saved = await store.CommitAsync(stagedFile, configuration.Value, panelStatus, saved.SentAt);
                stagedFile = null;
            }
            return Results.Ok(saved);
        }
        return Results.Ok(new { success = true, topic, payload });
    } catch (OperationCanceledException) {
        return Results.Json(new { error = $"Zeitüberschreitung bei: {stage}. Bei einer unterbrochenen Übertragung kann der Broker die Nachricht bereits erhalten haben; die gespeicherte Konfiguration bleibt unverändert." }, statusCode: 504);
    } catch (Exception ex) {
        app.Logger.LogError(ex, "MQTT operation failed during {Stage}", stage);
        var error = brokerConfirmed
            ? "Der Broker hat die Konfiguration bestätigt, aber das Speichern der Konfiguration oder Panelantwort ist fehlgeschlagen. Bitte den gespeicherten Stand aktualisieren und das Add-on-Log prüfen."
            : $"Fehler bei: {stage}. Bitte Broker und Add-on-Log prüfen.";
        return Results.Json(new { error, brokerConfirmed }, statusCode: 502);
    } finally {
        if (stagedFile is not null) store.Discard(stagedFile);
        if (locked) store.PublishLock.Release();
    }
}

app.MapGet("/api/entities", async (IHttpClientFactory httpClients, HttpContext context) => {
    context.Response.Headers.CacheControl = "no-store";
    try {
        var token = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
            return Results.Json(new { error = "SUPERVISOR_TOKEN fehlt. Bitte im Home-Assistant-Add-on öffnen." }, statusCode: 503);
        using var request = new HttpRequestMessage(HttpMethod.Get, "core/api/states");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClients.CreateClient("supervisor").SendAsync(request, context.RequestAborted);
        if (!response.IsSuccessStatusCode)
            return Results.Json(new { error = $"Home Assistant antwortet mit HTTP {(int)response.StatusCode}. Bitte homeassistant_api: true prüfen und das Add-on neu bauen." }, statusCode: 502);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.RequestAborted));
        var entities = json.RootElement.EnumerateArray().Select(entity => {
            var entityId = entity.GetProperty("entity_id").GetString()!;
            var attributes = entity.GetProperty("attributes");
            var name = attributes.TryGetProperty("friendly_name", out var friendlyName) && friendlyName.ValueKind == JsonValueKind.String
                ? friendlyName.GetString() : entityId;
            var unit = attributes.TryGetProperty("unit_of_measurement", out var unitValue) && unitValue.ValueKind == JsonValueKind.String
                ? unitValue.GetString() : null;
            return new { entityId, name = name ?? entityId, state = entity.GetProperty("state").GetString(), unit };
        }).OrderBy(entity => entity.name, StringComparer.OrdinalIgnoreCase).ThenBy(entity => entity.entityId).ToArray();
        return Results.Ok(entities);
    } catch (OperationCanceledException) {
        return Results.Json(new { error = "Zeitüberschreitung beim Laden der Entitäten. Bitte Home Assistant prüfen." }, statusCode: 504);
    } catch (Exception ex) {
        app.Logger.LogWarning("Loading entities failed ({ErrorType})", ex.GetType().Name);
        return Results.Json(new { error = "Entitäten konnten nicht geladen werden. Bitte Home Assistant und Add-on-Log prüfen." }, statusCode: 502);
    }
});

app.Run();
