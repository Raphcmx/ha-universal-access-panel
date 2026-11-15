import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FunctionFieldsComponent } from './function-fields';
import { clone, emptyConfiguration, isConfiguration, move, removeFunction, renameFunction, sameRef, uniqueName, validate } from './config';
import type { Action, Configuration, Entity, FunctionRef, Group, PanelFunction, SentConfiguration } from './config';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [FormsModule, FunctionFieldsComponent],
  templateUrl: './app.html'
})
export class AppComponent {
  darkMode = signal(document.documentElement.dataset['theme'] !== 'light');
  config: Configuration = emptyConfiguration();
  selectedGroup: Group | null = null;
  selectedFunction: PanelFunction | null = null;
  view = signal<'editor' | 'last'>('editor');
  entities = signal<Entity[]>([]);
  entitiesLoading = signal(false);
  entityError = signal('');
  latest = signal<SentConfiguration | null>(null);
  latestError = signal('');
  loading = signal(true);
  publishing = signal(false);
  testing = signal(false);
  notice = signal('');
  noticeError = signal(false);
  serverErrors = signal<string[]>([]);
  showImport = false;
  importText = '';
  revision = signal(0);
  draftStorageMessage = signal('Entwurf wird in diesem Browser gesichert.');
  readonly errors = computed(() => { this.revision(); return validate(this.config); });
  readonly hasChanges = computed(() => {
    this.revision();
    return JSON.stringify(this.config) !== JSON.stringify(this.latest()?.configuration ?? emptyConfiguration());
  });
  private readonly storageKey = `access-panel:panel1:draft:v1:${new URL('.', document.baseURI).pathname}`;

  constructor() { void this.initialize(); void this.loadEntities(); }

  toggleTheme(): void {
    const theme = this.darkMode() ? 'light' : 'dark';
    this.darkMode.set(theme === 'dark');
    document.documentElement.dataset['theme'] = theme;
    try { localStorage.setItem('access-panel:theme', theme); }
    catch { /* Switching still works without browser storage. */ }
  }

  private async request<T>(path: string, body?: unknown): Promise<T> {
    const response = await fetch(new URL(`api/${path}`, document.baseURI), {
      method: body === undefined ? 'GET' : 'POST', cache: 'no-store',
      headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: AbortSignal.timeout(30000)
    });
    if (response.status === 204) return null as T;
    const data = await response.json().catch(() => ({ error: `Unerwartete Serverantwort (HTTP ${response.status}).` }));
    if (!response.ok) {
      if (Array.isArray(data.errors)) this.serverErrors.set(data.errors);
      throw new Error(data.error ?? `Anfrage fehlgeschlagen (HTTP ${response.status}).`);
    }
    return data as T;
  }
  private async initialize(): Promise<void> {
    let draft: Configuration | null = null;
    try {
      const raw = localStorage.getItem(this.storageKey);
      if (raw) {
        const parsed: unknown = JSON.parse(raw);
        if (isConfiguration(parsed)) draft = parsed;
        else this.message('Der lokale Entwurf hat ein unbekanntes Format und wurde nicht geladen.', true);
      }
    } catch { this.draftStorageMessage.set('Lokale Entwurfssicherung ist nicht verfügbar. JSON regelmäßig herunterladen.'); }
    try {
      const latest = await this.request<SentConfiguration | null>('config/last');
      this.latest.set(latest);
      this.config = draft ?? clone(latest?.configuration ?? emptyConfiguration());
    } catch (error) {
      this.latestError.set(this.errorText(error));
      this.config = draft ?? emptyConfiguration();
    } finally {
      this.selectInitial();
      this.revision.update(value => value + 1);
      this.loading.set(false);
    }
  }
  async refreshLatest(): Promise<void> {
    this.latestError.set('');
    try { this.latest.set(await this.request<SentConfiguration | null>('config/last')); }
    catch (error) { this.latestError.set(this.errorText(error)); }
  }
  async loadEntities(): Promise<void> {
    this.entitiesLoading.set(true);
    this.entityError.set('');
    try { this.entities.set(await this.request<Entity[]>('entities')); }
    catch (error) { this.entityError.set(this.errorText(error)); }
    finally { this.entitiesLoading.set(false); }
  }
  private selectInitial(): void {
    this.selectedGroup = this.config.groups[0] ?? null;
    this.selectedFunction = this.selectedGroup?.functions.map(ref => this.resolve(ref)).find(fn => fn !== undefined)
      ?? this.config.allFunctions[0] ?? null;
  }
  changed(): void {
    this.revision.update(value => value + 1);
    this.serverErrors.set([]);
    try {
      localStorage.setItem(this.storageKey, JSON.stringify(this.config));
      this.draftStorageMessage.set('Entwurf in diesem Browser gesichert.');
    } catch { this.draftStorageMessage.set('Entwurf konnte lokal nicht gesichert werden. JSON herunterladen.'); }
  }
  resolve(ref: FunctionRef): PanelFunction | undefined { return this.config.allFunctions.find(fn => sameRef(fn, ref)); }
  selectGroup(group: Group): void {
    this.selectedGroup = group;
    this.selectedFunction = group.functions.map(ref => this.resolve(ref)).find(fn => fn !== undefined) ?? null;
  }
  selectFunction(fn: PanelFunction | undefined): void { this.selectedFunction = fn ?? null; }
  addGroup(): void {
    const group: Group = { name: uniqueName('Neue Gruppe', this.config.groups.map(item => item.name)), functions: [] };
    this.config.groups.push(group);
    this.selectedGroup = group;
    this.selectedFunction = null;
    this.changed();
  }
  deleteGroup(group: Group): void {
    if (!window.confirm(`Gruppe „${group.name}“ löschen? Die Funktionen bleiben in der Funktionsbibliothek erhalten.`)) return;
    this.config.groups = this.config.groups.filter(item => item !== group);
    if (this.selectedGroup === group) this.selectInitial();
    this.changed();
  }
  addFunction(kind: 'brightness' | 'service' | 'composed'): void {
    if (!this.selectedGroup) this.addGroup();
    const deviceName = this.selectedGroup!.name;
    const names = this.config.allFunctions.filter(fn => fn.deviceName === deviceName).map(fn => fn.name);
    const fn: PanelFunction = {
      type: kind === 'composed' ? 'COMPOSED' : 'HOMEASSISTANT',
      name: uniqueName(kind === 'brightness' ? 'Helligkeit' : kind === 'composed' ? 'Aktionsauswahl' : 'Neue Aktion', names),
      deviceName, autoExecute: false
    };
    if (kind === 'composed') fn.functions = [
      { type: 'HOMEASSISTANT', name: 'aus', entity_id: '', service: 'light.turn_off', state: 'off' },
      { type: 'HOMEASSISTANT', name: 'ein', entity_id: '', service: 'light.turn_on', state: 'on' }
    ];
    else {
      fn.mode = kind;
      fn.entity_id = '';
      if (kind === 'brightness') { fn.stepSize = 10; fn.defaultValue = 50; }
      else fn.service = '';
    }
    this.config.allFunctions.push(fn);
    this.selectedGroup!.functions.push({ name: fn.name, deviceName: fn.deviceName });
    this.selectedFunction = fn;
    this.changed();
  }
  rename(fn: PanelFunction, field: 'name' | 'deviceName', value: string): void {
    const name = field === 'name' ? value : fn.name;
    const deviceName = field === 'deviceName' ? value : fn.deviceName;
    if (this.config.allFunctions.some(other => other !== fn && other.name === name && other.deviceName === deviceName)) {
      this.message('Diese Kombination aus Funktionsname und Gerätekennung ist bereits vergeben.', true);
      return;
    }
    renameFunction(this.config, fn, name, deviceName);
    this.changed();
  }
  deleteFunction(fn: PanelFunction): void {
    if (!window.confirm(`Funktion „${fn.name}“ aus der Bibliothek und allen Gruppen löschen?`)) return;
    removeFunction(this.config, fn);
    this.selectedFunction = null;
    this.changed();
  }
  duplicateFunction(fn: PanelFunction): void {
    const copy = clone(fn);
    copy.name = uniqueName(`${fn.name} Kopie`, this.config.allFunctions.filter(item => item.deviceName === fn.deviceName).map(item => item.name));
    this.config.allFunctions.push(copy);
    if (this.selectedGroup) this.selectedGroup.functions.push({ name: copy.name, deviceName: copy.deviceName });
    this.selectedFunction = copy;
    this.changed();
  }
  assigned(group: Group, fn: PanelFunction): boolean { return group.functions.some(ref => sameRef(ref, fn)); }
  assign(group: Group, fn: PanelFunction, checked: boolean): void {
    if (checked && !this.assigned(group, fn)) group.functions.push({ name: fn.name, deviceName: fn.deviceName });
    if (!checked) group.functions = group.functions.filter(ref => !sameRef(ref, fn));
    this.changed();
  }
  addAction(fn: PanelFunction): void {
    fn.functions ??= [];
    fn.functions.push({ type: 'HOMEASSISTANT', name: uniqueName('Neue Aktion', fn.functions.map(item => item.name)), entity_id: '', mode: 'service', service: '' });
    this.changed();
  }
  removeAction(fn: PanelFunction, index: number): void {
    if (!window.confirm(`Aktion „${fn.functions?.[index].name}“ löschen?`)) return;
    fn.functions?.splice(index, 1);
    this.changed();
  }
  reorder<T>(items: T[], index: number, offset: number): void { move(items, index, offset); this.changed(); }
  label(fn: PanelFunction): string {
    return fn.type === 'COMPOSED' ? `${fn.functions?.length ?? 0} Aktionen` : fn.mode === 'brightness' ? 'Helligkeit' : 'HA-Aktion';
  }
  json(config: Configuration): string { return JSON.stringify(config, null, 2); }
  formatDate(value: string): string { return new Date(value).toLocaleString('de-AT'); }
  private message(text: string, error = false): void { this.noticeError.set(error); this.notice.set(text); }
  private errorText(error: unknown): string { return error instanceof Error ? error.message : 'Anfrage fehlgeschlagen. Bitte Add-on und Netzwerk prüfen.'; }
  async publish(): Promise<void> {
    if (this.publishing() || this.errors().length) return;
    const snapshot = clone(this.config);
    if (!window.confirm('Die gesamte Konfiguration an Panel 1 senden? Sie ersetzt die bisherige MQTT-Konfiguration.')) return;
    this.publishing.set(true);
    this.serverErrors.set([]);
    try {
      this.latest.set(await this.request<SentConfiguration>('mqtt/config', snapshot));
      this.latestError.set('');
      const status = this.latest()?.panelStatus;
      this.message(status?.status === 'ok'
        ? `Vom Broker bestätigt und gespeichert. Panel meldet OK (${status.changed ? 'Konfiguration geändert' : 'Konfiguration unverändert'}).`
        : status?.status === 'error'
          ? `Vom Broker bestätigt und gespeichert. Panel meldet Fehler: ${status.error || 'Keine Fehlerbeschreibung.'}`
          : 'Vom Broker bestätigt und gespeichert. Innerhalb der Wartezeit kam keine neue gültige Panelantwort.', status?.status !== 'ok');
    } catch (error) { this.message(this.errorText(error), true); }
    finally { this.publishing.set(false); }
  }
  async testMqtt(): Promise<void> {
    this.testing.set(true);
    try { await this.request('mqtt/test', {}); this.message('MQTT-Testnachricht erfolgreich an access_panel/test gesendet.'); }
    catch (error) { this.message(this.errorText(error), true); }
    finally { this.testing.set(false); }
  }
  restoreLast(): void {
    const latest = this.latest();
    if (!latest || !window.confirm('Den aktuellen Entwurf durch die zuletzt erfolgreich gesendete Konfiguration ersetzen?')) return;
    this.config = clone(latest.configuration);
    this.selectInitial();
    this.changed();
    this.view.set('editor');
    this.message('Zuletzt gesendete Konfiguration als Entwurf übernommen.');
  }
  async importJson(): Promise<void> {
    this.serverErrors.set([]);
    try {
      if (new TextEncoder().encode(this.importText).length > 1_048_576) throw new Error('Die Konfiguration darf höchstens 1 MiB groß sein.');
      const parsed: unknown = JSON.parse(this.importText);
      if (!isConfiguration(parsed)) throw new Error('Unbekannte JSON-Struktur. allFunctions, groups und gültige Funktionen werden erwartet.');
      const errors = validate(parsed);
      if (errors.length) { this.serverErrors.set(errors); throw new Error('Der Import enthält ungültige Angaben. Dein Entwurf bleibt erhalten.'); }
      await this.request('config/validate', parsed);
      if (!window.confirm('Den aktuellen Entwurf durch das importierte JSON ersetzen? Es wird nichts gesendet.')) return;
      this.config = clone(parsed);
      this.selectInitial();
      this.changed();
      this.showImport = false;
      this.importText = '';
      this.message('Konfiguration als Entwurf importiert.');
    } catch (error) { this.message(this.errorText(error), true); }
  }
  download(config: Configuration, name: string): void {
    const url = URL.createObjectURL(new Blob([this.json(config)], { type: 'application/json' }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = name;
    anchor.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
