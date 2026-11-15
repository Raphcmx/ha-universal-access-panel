# Universal Access Panel (UAP)

Mit diesem Home-Assistant-Add-on stellst du zusammen, welche Geräte und Aktionen dein Universal Access Panel bedienen soll. Du ordnest Funktionen in Gruppen, wählst die passenden Home-Assistant-Entitäten aus und sendest die Konfiguration per MQTT an das Panel. Aktuell wird ein Panel über `bedienpanel/panel1/config` unterstützt.

## Installation

1. Im Home-Assistant-Add-on-Store unter **Repositorys** die URL https://github.com/Raphcmx/ha-universal-access-panel hinzufügen.
2. **Universal Access Panel** installieren und starten. MQTT muss in Home Assistant verfügbar sein.
3. Die Weboberfläche des Add-ons öffnen. Nach Updates das Add-on aktualisieren und neu starten; bei einer lokalen Installation neu bauen.

Die MQTT-Zugangsdaten bezieht das Add-on automatisch vom Home-Assistant-Supervisor. Du musst keine Tokens oder Passwörter im Editor eintragen.

## Gruppen und Funktionen

**Gruppen** bilden das Menü auf deinem Panel, beispielsweise „Beleuchtung“ oder „Wohnzimmer“. Ihre Reihenfolge im Editor entspricht der Reihenfolge auf dem Panel.

In einer Gruppe legst du die gewünschten Funktionen an:

- **Helligkeit:** Die Helligkeit einer Licht-Entität über den Regler ändern. Schrittweite und Startwert sind einstellbar. Beim Auswählen übernimmt die Firmware den tatsächlichen Home-Assistant-Wert.
- **HA-Aktion:** Einen Home-Assistant-Dienst für eine Entität ausführen, beispielsweise ein Licht einschalten oder einen Rollladen stoppen.
- **Aktionsauswahl:** Mehrere benannte Aktionen zusammenfassen. Der Regler wählt zwischen ihnen, beispielsweise „aus“ und „ein“. Die Reihenfolge der Aktionen lässt sich ändern.

Eine Funktion kann mehreren Gruppen zugeordnet werden. Änderungen an ihr gelten dann in allen zugeordneten Gruppen. Name und Gerätekennung identifizieren die Funktion; beim Umbenennen aktualisiert der Editor die Gruppenverweise automatisch.

Die standardmäßig eingeklappte **Funktionsbibliothek** enthält alle Funktionen, auch solche ohne Gruppenzuordnung. Beim Löschen einer Gruppe bleiben ihre Funktionen dort erhalten und können wieder zugeordnet werden. „Funktion löschen“ entfernt die Funktion dagegen aus der Bibliothek und allen Gruppen.

## Nach Bestätigung automatisch ausführen

Diese Option bestimmt, ob du Änderungen am Panel jedes Mal mit der Aktionstaste bestätigen musst.

**Mit aktivierter Option:**

1. Am Panel das gewünschte Gerät und die Funktion auswählen.
2. Den gewünschten Wert beziehungsweise die Aktion einstellen und **einmal mit der Aktionstaste bestätigen**.
3. Danach wird jede weitere Regleränderung **sofort ausgeführt**, ohne erneut die Aktionstaste zu drücken.

Beispiel: Du bestätigst die Helligkeit eines Lichts einmal. Anschließend wird das Licht beim weiteren Verstellen des Reglers direkt heller oder dunkler.

**Nach einem Geräte- oder Funktionswechsel ist wieder eine erste Bestätigung nötig.** Die Automatik gilt also für die aktuell bestätigte Auswahl, nicht dauerhaft für alle Funktionen.

Bei deaktivierter Option musst du jede Änderung mit der Aktionstaste bestätigen. Die Einstellung betrifft die Bedienung am Panel; Änderungen im Konfigurator überträgst du weiterhin über **„An Panel senden“**.

## Konfiguration senden

Fehlende oder ungültige Angaben zeigt der Editor automatisch an. Solange Fehler bestehen, ist „An Panel senden“ gesperrt. Beim Senden prüft das Backend die Konfiguration nochmals; ein separater Prüfschritt ist nicht nötig.

Mit **„An Panel senden“** überträgst du die gesamte Konfiguration und ersetzt die bisherige MQTT-Konfiguration. Die Übertragung verwendet QoS 1 und Retain. Eine gültige Konfiguration garantiert noch nicht, dass das Panel sie erfolgreich anwenden kann: Beachte deshalb die Rückmeldung nach dem Senden.

Unter **„Zuletzt erfolgreich gesendet“** findest du den vom MQTT-Broker bestätigten und gespeicherten Stand. Die Panel-Rückmeldung wird gesondert angezeigt: OK, Fehler oder keine neue Antwort innerhalb der Wartezeit. Eine Brokerbestätigung allein bestätigt noch nicht die Übernahme am Panel. Später eintreffende Panelantworten werden derzeit nicht nachgeladen.

## Entwürfe, Import und Sicherung

- Änderungen werden automatisch als Entwurf im Local Storage dieses Browsers gesichert. Ein anderer Browser oder eine andere Ingress-Adresse hat einen eigenen Entwurf.
- Der bestätigte Sendestand bleibt unter `/data/last-sent.json` auch nach Add-on-Neustarts und -Updates erhalten. Validierungsfehler und fehlgeschlagene Übertragungen überschreiben ihn nicht. Falls die Speicherung nach der Brokerbestätigung scheitert, zeigt die App einen Fehler.
- **„Als Entwurf übernehmen“** lädt den zuletzt gesendeten Stand zum Bearbeiten, ohne ihn erneut zu senden.
- **„JSON herunterladen“** sichert eine Konfiguration als Datei. **„JSON importieren“** prüft vorhandenes JSON und übernimmt es als Entwurf; gesendet wird erst über „An Panel senden“.
- `examples/panel1.json` enthält ein Beispiel mit fiktiven Entitäts-IDs. Ersetze diese vor dem Senden durch eigene Entitäten. Das Beispiel wird nicht automatisch geladen.

## Grenzen

Rollladen-Öffnen, -Schließen und -Stopp lassen sich als HA-Aktionen anlegen. Ein Positionsregler ist derzeit kein unterstützter Firmware-Modus. Verschachtelte Aktionsauswahlen werden nicht angeboten.

Der Editor hat keine feste Anzahlbegrenzung für Gruppen, Funktionen oder Aktionen. Die gesamte Konfiguration darf höchstens 1 MiB groß sein; zusätzlich gelten die Grenzen der Panel-Firmware.
