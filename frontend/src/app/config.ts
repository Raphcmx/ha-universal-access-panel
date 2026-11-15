export interface Action {
  type: 'HOMEASSISTANT';
  name: string;
  entity_id: string;
  mode?: 'brightness' | 'service';
  service?: string;
  state?: string;
  stepSize?: number;
  defaultValue?: number;
  autoExecute?: boolean;
  [extra: string]: unknown;
}
export interface PanelFunction {
  type: 'HOMEASSISTANT' | 'COMPOSED';
  name: string;
  deviceName: string;
  entity_id?: string;
  mode?: 'brightness' | 'service';
  service?: string;
  state?: string;
  stepSize?: number;
  defaultValue?: number;
  autoExecute?: boolean;
  functions?: Action[];
  [extra: string]: unknown;
}
export interface FunctionRef { name: string; deviceName: string; }
export interface Group { name: string; functions: FunctionRef[]; [extra: string]: unknown; }
export interface Configuration { allFunctions: PanelFunction[]; groups: Group[]; [extra: string]: unknown; }
export interface PanelStatus {
  status: 'ok' | 'error';
  changed: boolean | null;
  configHash: string | null;
  groups: number | null;
  functions: number | null;
  error: string | null;
  receivedAt: string;
}
export interface SentConfiguration {
  panelStatus?: PanelStatus | null;
  configuration: Configuration;
  sentAt: string;
  topic: string;
  qos: number;
  retain: boolean;
}
export interface Entity { entityId: string; name: string; state: string; unit?: string; }
export const emptyConfiguration = (): Configuration => ({ allFunctions: [], groups: [] });
export const clone = <T>(value: T): T => structuredClone(value);
export const sameRef = (a: FunctionRef, b: FunctionRef): boolean => a.name === b.name && a.deviceName === b.deviceName;

export function renameFunction(config: Configuration, fn: PanelFunction, name: string, deviceName: string): void {
  const previous = { name: fn.name, deviceName: fn.deviceName };
  for (const group of config.groups) {
    for (const ref of group.functions) {
      if (sameRef(ref, previous)) { ref.name = name; ref.deviceName = deviceName; }
    }
  }
  fn.name = name;
  fn.deviceName = deviceName;
}
export function removeFunction(config: Configuration, fn: PanelFunction): void {
  for (const group of config.groups) group.functions = group.functions.filter(ref => !sameRef(ref, fn));
  config.allFunctions = config.allFunctions.filter(item => item !== fn);
}
export function move<T>(items: T[], index: number, offset: number): void {
  const target = index + offset;
  if (index < 0 || index >= items.length || target < 0 || target >= items.length) return;
  [items[index], items[target]] = [items[target], items[index]];
}
export function uniqueName(prefix: string, names: string[]): string {
  let result = prefix;
  for (let i = 2; names.includes(result); i++) result = `${prefix} ${i}`;
  return result;
}

// Shape checking also accepts unfinished drafts; semantic validation runs separately.
export function isConfiguration(value: unknown): value is Configuration {
  if (!isObject(value) || !Array.isArray(value['allFunctions']) || !Array.isArray(value['groups'])) return false;
  return value['allFunctions'].every(fn => isObject(fn) && typeof fn['name'] === 'string'
    && typeof fn['deviceName'] === 'string' && hasSafeFields(fn) && (fn['type'] === 'HOMEASSISTANT' || fn['type'] === 'COMPOSED')
    && (fn['functions'] === undefined || (Array.isArray(fn['functions']) && fn['functions'].every(child =>
      isObject(child) && hasSafeFields(child) && child['type'] === 'HOMEASSISTANT' && typeof child['name'] === 'string' && typeof child['entity_id'] === 'string'))))
    && value['groups'].every(group => isObject(group) && typeof group['name'] === 'string'
      && Array.isArray(group['functions']) && group['functions'].every(ref => isObject(ref)
        && typeof ref['name'] === 'string' && typeof ref['deviceName'] === 'string'));
}
function hasSafeFields(fn: Record<string, unknown>): boolean {
  for (const name of ['entity_id', 'service', 'state', 'mode']) {
    if (fn[name] !== undefined && typeof fn[name] !== 'string') return false;
  }
  if (fn['mode'] !== undefined && fn['mode'] !== 'brightness' && fn['mode'] !== 'service') return false;
  if (fn['autoExecute'] !== undefined && typeof fn['autoExecute'] !== 'boolean') return false;
  // Number inputs produce null while the user clears the field; keep that draft recoverable.
  for (const name of ['stepSize', 'defaultValue']) {
    if (fn[name] !== undefined && fn[name] !== null && typeof fn[name] !== 'number') return false;
  }
  return true;
}
function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function validate(config: Configuration): string[] {
  const errors: string[] = [];
  const text = (value: unknown, label: string): void => {
    if (typeof value !== 'string' || !value.trim() || value.length > 255) errors.push(`${label}: Text erforderlich (höchstens 255 Zeichen).`);
  };
  if (!config.allFunctions.length) errors.push('Mindestens eine Funktion hinzufügen.');
  if (!config.groups.length) errors.push('Mindestens eine Gruppe hinzufügen.');
  const keys = new Set<string>();
  const key = (ref: FunctionRef) => JSON.stringify([ref.name, ref.deviceName]);
  const check = (fn: PanelFunction | Action, label: string): void => {
    text(fn.name, `${label} · Name`);
    if (fn.autoExecute !== undefined && typeof fn.autoExecute !== 'boolean') errors.push(`${label}: Automatik muss ein Wahrheitswert sein.`);
    if (fn.type === 'COMPOSED') {
      const children = fn.functions as Action[] | undefined;
      if (!children?.length) errors.push(`${label}: Mindestens eine Aktion hinzufügen.`);
      const names = new Set<string>();
      for (const child of children ?? []) {
        if (names.has(child.name)) errors.push(`${label}: Aktionsname „${child.name}“ ist doppelt.`);
        names.add(child.name);
        check(child, `${label} / ${child.name || 'Aktion'}`);
      }
      return;
    }
    if (typeof fn.entity_id !== 'string' || !/^[a-z_]+\.[a-z0-9_]+$/.test(fn.entity_id)) errors.push(`${label}: Gültige Entität auswählen.`);
    const mode = fn.mode ?? 'service';
    if (mode === 'brightness') {
      if (!fn.entity_id?.startsWith('light.')) errors.push(`${label}: Helligkeit benötigt eine light.-Entität.`);
    } else if (mode === 'service') {
      if (typeof fn.service !== 'string' || !/^[a-z_]+\.[a-z_][a-z0-9_]*$/.test(fn.service)) errors.push(`${label}: Gültigen Dienst eingeben, z. B. light.turn_on.`);
    } else errors.push(`${label}: Nicht unterstützter Modus.`);
    for (const [field, min, max] of [['stepSize', 1, 100], ['defaultValue', 0, 100]] as const) {
      const value = fn[field];
      if (value !== undefined && (!Number.isInteger(value) || value < min || value > max)) errors.push(`${label}: ${field} muss eine ganze Zahl von ${min} bis ${max} sein.`);
    }
    if (fn.state !== undefined && (typeof fn.state !== 'string' || fn.state.length > 255)) errors.push(`${label}: Zustand muss Text sein (höchstens 255 Zeichen).`);
  };
  for (const fn of config.allFunctions) {
    const label = fn.name || 'Neue Funktion';
    text(fn.deviceName, `${label} · Gerätekennung`);
    if (keys.has(key(fn))) errors.push(`${label}: Name und Gerätekennung sind bereits vergeben.`);
    keys.add(key(fn));
    check(fn, label);
  }
  const groups = new Set<string>();
  for (const group of config.groups) {
    text(group.name, 'Gruppenname');
    if (groups.has(group.name)) errors.push(`Gruppe „${group.name}“ ist doppelt.`);
    groups.add(group.name);
    if (!group.functions.length) errors.push(`Gruppe „${group.name}“: Mindestens eine Funktion zuordnen.`);
    const refs = new Set<string>();
    for (const ref of group.functions) {
      if (!keys.has(key(ref))) errors.push(`Gruppe „${group.name}“: Funktion „${ref.name}“ existiert nicht.`);
      if (refs.has(key(ref))) errors.push(`Gruppe „${group.name}“: Funktion „${ref.name}“ ist doppelt zugeordnet.`);
      refs.add(key(ref));
    }
  }
  return errors;
}
