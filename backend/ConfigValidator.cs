using System.Text.Json;
using System.Text.RegularExpressions;

namespace AccessPanel.Api;

public static partial class ConfigValidator {
    [GeneratedRegex(@"^[a-z_]+\.[a-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityPattern();
    [GeneratedRegex(@"^[a-z_]+\.[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ServicePattern();

    public static List<string> Validate(JsonElement root) {
        var errors = new List<string>();
        if (root.ValueKind != JsonValueKind.Object) { errors.Add("Die Konfiguration muss ein JSON-Objekt sein."); return errors; }
        if (!root.TryGetProperty("allFunctions", out var functions) || functions.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array) {
            errors.Add("allFunctions und groups müssen Arrays sein."); return errors;
        }
        if (functions.GetArrayLength() == 0) errors.Add("Mindestens eine Funktion ist erforderlich.");
        if (groups.GetArrayLength() == 0) errors.Add("Mindestens eine Gruppe ist erforderlich.");
        var keys = new HashSet<(string, string)>();
        var index = 0;
        foreach (var function in functions.EnumerateArray()) {
            var label = $"Funktion {++index}";
            if (function.ValueKind != JsonValueKind.Object) { errors.Add($"{label}: Objekt erwartet."); continue; }
            var name = RequiredText(function, "name", label, errors);
            var device = RequiredText(function, "deviceName", label, errors);
            if (!keys.Add((name, device))) errors.Add($"{label}: Name und Gerätekennung müssen zusammen eindeutig sein.");
            ValidateFunction(function, label, errors, allowComposed: true);
        }
        var groupNames = new HashSet<string>();
        index = 0;
        foreach (var group in groups.EnumerateArray()) {
            var label = $"Gruppe {++index}";
            if (group.ValueKind != JsonValueKind.Object) { errors.Add($"{label}: Objekt erwartet."); continue; }
            var name = RequiredText(group, "name", label, errors);
            if (!groupNames.Add(name)) errors.Add($"{label}: Gruppenname ist bereits vergeben.");
            if (!group.TryGetProperty("functions", out var refs) || refs.ValueKind != JsonValueKind.Array || refs.GetArrayLength() == 0) {
                errors.Add($"{label}: Mindestens eine Funktionszuordnung ist erforderlich."); continue;
            }
            var references = new HashSet<(string, string)>();
            foreach (var reference in refs.EnumerateArray()) {
                if (reference.ValueKind != JsonValueKind.Object) { errors.Add($"{label}: Ungültige Funktionszuordnung."); continue; }
                var key = (RequiredText(reference, "name", label, errors), RequiredText(reference, "deviceName", label, errors));
                if (!keys.Contains(key)) errors.Add($"{label}: Die referenzierte Funktion „{key.Item1}“ existiert nicht.");
                if (!references.Add(key)) errors.Add($"{label}: Die Funktion „{key.Item1}“ ist doppelt zugeordnet.");
            }
        }
        return errors;
    }

    private static void ValidateFunction(JsonElement function, string label, List<string> errors, bool allowComposed) {
        var type = RequiredText(function, "type", label, errors);
        if (function.TryGetProperty("autoExecute", out var auto) && auto.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            errors.Add($"{label}: autoExecute muss true oder false sein.");
        if (type == "COMPOSED" && allowComposed) {
            if (!function.TryGetProperty("functions", out var children) || children.ValueKind != JsonValueKind.Array || children.GetArrayLength() == 0) {
                errors.Add($"{label}: Eine Aktionsauswahl benötigt mindestens eine Aktion."); return;
            }
            var names = new HashSet<string>();
            var i = 0;
            foreach (var child in children.EnumerateArray()) {
                var childLabel = $"{label}, Aktion {++i}";
                if (child.ValueKind != JsonValueKind.Object) { errors.Add($"{childLabel}: Objekt erwartet."); continue; }
                var name = RequiredText(child, "name", childLabel, errors);
                if (!names.Add(name)) errors.Add($"{childLabel}: Aktionsname ist bereits vergeben.");
                ValidateFunction(child, childLabel, errors, allowComposed: false);
            }
            return;
        }
        if (type != "HOMEASSISTANT") { errors.Add($"{label}: Nicht unterstützter Funktionstyp „{type}“."); return; }
        var entity = RequiredText(function, "entity_id", label, errors);
        if (!EntityPattern().IsMatch(entity)) errors.Add($"{label}: Ungültige Entitäts-ID.");
        var mode = "service";
        if (function.TryGetProperty("mode", out var modeElement)) {
            if (modeElement.ValueKind == JsonValueKind.String) mode = modeElement.GetString()!;
            else mode = "";
        }
        if (mode == "brightness") {
            if (!entity.StartsWith("light.", StringComparison.Ordinal)) errors.Add($"{label}: Helligkeit benötigt eine light.-Entität.");
        } else if (mode == "service") {
            var service = RequiredText(function, "service", label, errors);
            if (!ServicePattern().IsMatch(service)) errors.Add($"{label}: Ungültiger Dienst, zum Beispiel light.turn_on verwenden.");
        } else errors.Add($"{label}: Der Modus „{mode}“ wird noch nicht unterstützt.");
        CheckNumber(function, "stepSize", 1, 100, label, errors);
        CheckNumber(function, "defaultValue", 0, 100, label, errors);
        if (function.TryGetProperty("state", out var state) && (state.ValueKind != JsonValueKind.String || state.GetString()!.Length > 255))
            errors.Add($"{label}: state muss ein Text mit höchstens 255 Zeichen sein.");
    }

    private static string RequiredText(JsonElement item, string property, string label, List<string> errors) {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > 255) {
            errors.Add($"{label}: {property} ist erforderlich (höchstens 255 Zeichen)."); return "";
        }
        return value.GetString()!;
    }
    private static void CheckNumber(JsonElement item, string property, int min, int max, string label, List<string> errors) {
        if (item.TryGetProperty(property, out var value) && (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < min || number > max))
            errors.Add($"{label}: {property} muss eine ganze Zahl zwischen {min} und {max} sein.");
    }
}