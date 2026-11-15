# Universal Access Panel (UAP)

Home-Assistant-Add-on mit ASP.NET Core (.NET 10), Angular 22, HTML, CSS und TypeScript. Aktuell wird genau ein Panel über `bedienpanel/panel1/config` konfiguriert.

## Installation in Home Assistant

Im Add-on-Store unter Repositorys die URL https://github.com/Raphcmx/ha-universal-access-panel hinzufügen. Danach Universal Access Panel installieren, bauen und starten. MQTT muss in Home Assistant verfügbar sein.

Das Beispiel in `examples/panel1.json` verwendet fiktive Entitäts-IDs. Diese vor dem Senden durch eigene Entitäten ersetzen. Zugangsdaten werden zur Laufzeit vom Supervisor bezogen und gehören nicht in das Repository.

## Im Add-on verwenden

1. Das Add-on nach der Aktualisierung auf Version `0.4.3` neu bauen und starten. Der Docker-Build baut das Angular-Frontend automatisch und integriert es in das .NET-Backend.
2. Über Home Assistant die Weboberfläche öffnen.
3. Eine Gruppe anlegen oder unter „JSON importieren“ eine vorhandene Konfiguration einfügen. `examples/panel1.json` enthält das bereitgestellte Firmware-Beispiel. Es wird nicht automatisch als aktuelle Konfiguration übernommen.
4. Funktionen anlegen, konfigurieren und einer oder mehreren Gruppen zuordnen. „Aktionsauswahl“ erzeugt eine `COMPOSED`-Funktion; darin lassen sich weitere Aktionen ergänzen und sortieren.
5. Die JSON-Vorschau prüfen und „An Panel senden“ wählen. Die Übertragung verwendet QoS 1 und Retain.

Gruppen referenzieren Funktionen über `name` und `deviceName`. Die Gerätekennung ist unabhängig vom Gruppennamen. Umbenennen aktualisiert alle Verweise; Löschen einer Gruppe erhält ihre Funktionen in der Bibliothek. Löschen einer Funktion entfernt sie aus allen Gruppen.

## Entwurf und gesendeter Stand

- Der Entwurf wird bei Änderungen im Local Storage dieses Browsers gesichert. Die Sicherung gehört zur jeweiligen Ingress-Adresse; ein anderer Browser oder eine andere Adresse hat einen eigenen Entwurf. Falls die Sicherung nicht verfügbar ist, zeigt die Oberfläche einen Hinweis. JSON-Downloads ermöglichen eine zusätzliche Sicherung.
- „Zuletzt erfolgreich gesendet“ zeigt ausschließlich eine Konfiguration, die die Backend-Prüfung bestanden hat und vom MQTT-Broker bestätigt wurde. Sie liegt samt Zeitpunkt und MQTT-Metadaten unter `/data/last-sent.json` und überlebt Add-on-Neustarts und -Updates.
- Validierungsfehler und fehlgeschlagene Übertragungen überschreiben den gespeicherten Stand nicht. Falls nach der Brokerbestätigung das Speichern scheitert, weist die Oberfläche ausdrücklich darauf hin.
- Vor dem Senden wird `bedienpanel/panel1/config/status` abonniert. Alte Retain-Zustellungen werden ignoriert. Nach PUBACK wird bis zu acht Sekunden (innerhalb des gesamten Anfragezeitlimits) auf eine neue gültige Statusantwort gewartet. `ok` und `error`, Änderungsflag, Config-Hash, Zähler und Fehlertext werden beim Sendestand gespeichert und angezeigt. `changed: false` ist ein Erfolg ohne Änderung. Ohne neue Antwort bleibt der brokerbestätigte Stand gespeichert. Spätere Antworten werden derzeit nicht nachgeladen. Die Zuordnung erfolgt zeitlich; ohne bekannte Firmware-Hashberechnung oder Request-ID ist kein eindeutiger Abgleich mit dem gesendeten Inhalt möglich.
- Bereits vor Version 0.4.0 gesendete MQTT-Konfigurationen werden nicht automatisch eingelesen. Vorhandenes JSON als Entwurf importieren und bei Bedarf erneut senden.
- „Als Entwurf übernehmen“ kopiert den gesendeten Stand in den Editor, ohne ihn erneut zu senden.

## Unterstützte Funktionen

- `HOMEASSISTANT` / `brightness`: Licht-Entität, Schrittweite 1–100, Startwert 0–100 und `autoExecute`. Beim Auswählen übernimmt die Firmware den tatsächlichen HA-Wert.
- `HOMEASSISTANT` / `service`: Entität, Dienst, optionaler `state`-Text und `autoExecute`. Fehlendes `mode` bedeutet `service`; der Import erhält diese Schreibweise.
- `COMPOSED`: benannte Auswahl aus beliebig ergänzbaren `HOMEASSISTANT`-Aktionen, jeweils mit eigenen Einstellungen. Verschachtelte `COMPOSED`-Funktionen werden derzeit nicht angeboten.
- `autoExecute`: Nach einmaliger manueller Bestätigung werden weitere Regleränderungen automatisch ausgeführt. Ein Geräte- oder Funktionswechsel pausiert die Automatik.

Rollo-Öffnen/-Schließen/-Stopp können als Dienste angelegt werden. Ein Positionsregler ist noch kein unterstützter Firmware-Modus. Der Editor erzeugt dafür keinen erfundenen Modus. Das optionale `state`-Feld wird unverändert übertragen; die Auswertung liegt bei der Firmware.

Es gibt keine feste Anzahlbegrenzung für Gruppen, Funktionen oder Aktionen im Editor. Die gesamte API-Anfrage ist auf 1 MiB begrenzt. Speicher- und Payload-Grenzen der Panel-Firmware gelten zusätzlich.

## Lokal entwickeln

Voraussetzungen: .NET SDK 10 und Node.js 24.15 oder neuer innerhalb der 24er-Reihe. Installierte Versionen sind in `frontend/package-lock.json` festgehalten.

Backend, im Projektverzeichnis:

```powershell
dotnet run --project backend/AccessPanel.Api.csproj
```

Frontend, in einem zweiten Terminal:

```powershell
cd frontend
npm ci
npm start
```

Das Frontend läuft auf `http://127.0.0.1:4200`; API-Anfragen werden an Port 8099 weitergereicht. Unter Windows speichert das Backend standardmäßig in `backend/data`, unter Linux in `/data`. Für einen anderen Ort `--DataDirectory <Pfad>` übergeben. Die Adresse lässt sich mit `--urls http://127.0.0.1:8099` konfigurieren.

Ohne Home-Assistant-Supervisor und `SUPERVISOR_TOKEN` funktionieren Editor, JSON-Import und Validierung; Entitätsabruf und MQTT melden einen kontrollierten Fehler. Entitäts-IDs können manuell eingegeben werden. Supervisor-Tokens gehören ausschließlich ins Backend.

Produktions-Build des Frontends:

```powershell
cd frontend
npm run build
```

Im Add-on liefert ASP.NET die gebauten Dateien aus `wwwroot` aus. Ein zusätzlicher Node-Server läuft dort nicht. Relative Asset- und API-Adressen unterstützen Home-Assistant-Ingress, auch bei einem Aufruf ohne abschließenden Schrägstrich.

## Prüfungen

```powershell
cd frontend
npm test
npm run build
```

Im Projektverzeichnis:

```powershell
dotnet run --project backend.tests/AccessPanel.Tests.csproj -c Release
```

Die Tests prüfen unter anderem das Firmware-Beispiel, Gruppenverweise beim Umbenennen und Löschen, Aktionsreihenfolge, ungültige Felder und die Wiederherstellung des gespeicherten Stands. Die Backend-Prüfungen benötigen keine zusätzliche Testframework-Abhängigkeit.

API-Endpunkte:

| Endpunkt | Verhalten |
| --- | --- |
| `GET api/entities` | Home-Assistant-Entitäten laden |
| `POST api/config/validate` | Vollständige Konfiguration prüfen, ohne zu senden |
| `POST api/mqtt/config` | Vollständige Konfiguration prüfen, senden und nach Brokerbestätigung speichern |
| `GET api/config/last` | Gespeicherten Stand mit `configuration`, `sentAt`, `topic`, `qos`, `retain` lesen; HTTP 204 bei noch fehlendem Stand |
| `POST api/mqtt/test` | Separate Testnachricht an `access_panel/test`, ohne die Panel-Konfiguration zu verändern |
