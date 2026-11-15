import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { clone, isConfiguration, move, removeFunction, renameFunction, validate } from '../src/app/config.ts';
import type { Configuration } from '../src/app/config.ts';

const example = JSON.parse(readFileSync(new URL('../../examples/panel1.json', import.meta.url), 'utf8')) as Configuration;

test('firmware example imports and roundtrips without adding service mode or changing device names', () => {
  assert.equal(isConfiguration(example), true);
  assert.deepEqual(validate(example), []);
  assert.deepEqual(JSON.parse(JSON.stringify(clone(example))), example);
  assert.equal(example.allFunctions[2].functions![0].mode, undefined);
  assert.notEqual(example.groups[1].name, example.groups[1].functions[0].deviceName);
});
test('rename updates every group reference without touching another function', () => {
  const config = clone(example);
  config.groups[1].functions.push({ ...config.groups[0].functions[0] });
  renameFunction(config, config.allFunctions[0], 'Küche', 'Lichter');
  assert.deepEqual(config.groups[0].functions[0], { name: 'Küche', deviceName: 'Lichter' });
  assert.deepEqual(config.groups[1].functions[2], { name: 'Küche', deviceName: 'Lichter' });
  assert.equal(config.groups[0].functions[1].name, 'Bürolicht');
  assert.deepEqual(validate(config), []);
});
test('deleting a shared function removes its references from every group', () => {
  const config = clone(example);
  config.groups[1].functions.push({ ...config.groups[0].functions[0] });
  removeFunction(config, config.allFunctions[0]);
  assert.equal(config.allFunctions.length, 3);
  assert.equal(config.groups[0].functions.length, 1);
  assert.equal(config.groups[1].functions.length, 2);
  assert.deepEqual(validate(config), []);
});
test('sorting composed actions preserves their payloads and respects boundaries', () => {
  const config = clone(example);
  const actions = config.allFunctions[2].functions!;
  move(actions, 0, -1);
  assert.equal(actions[0].name, 'aus');
  move(actions, 1, -1);
  assert.equal(actions[0].service, 'light.turn_on');
  assert.equal(actions[1].state, 'off');
  assert.deepEqual(validate(config), []);
});
test('invalid references, duplicate identities and unsupported modes block publishing', () => {
  const badRef = clone(example);
  badRef.groups[0].functions[0].name = 'Fehlt';
  assert.ok(validate(badRef).some(error => error.includes('existiert nicht')));
  const duplicate = clone(example);
  duplicate.allFunctions.push(clone(duplicate.allFunctions[0]));
  assert.ok(validate(duplicate).some(error => error.includes('bereits vergeben')));
  const future = clone(example) as any;
  future.allFunctions[0].mode = 'position';
  assert.equal(isConfiguration(future), false);
  assert.ok(validate(future).some(error => error.includes('Modus')));
});
test('malformed imported field types are rejected before editing', () => {
  for (const [field, value] of [['entity_id', 123], ['autoExecute', 'false'], ['stepSize', '10']]) {
    const bad = clone(example) as any;
    bad.allFunctions[0][field] = value;
    assert.equal(isConfiguration(bad), false);
  }
  assert.equal(isConfiguration({ allFunctions: [null], groups: [] }), false);
});
test('unfinished numeric inputs survive draft restoration but cannot be sent', () => {
  const draft = clone(example) as any;
  draft.allFunctions[0].stepSize = null;
  assert.equal(isConfiguration(draft), true);
  assert.ok(validate(draft).some(error => error.includes('stepSize')));
});
test('brightness rejects non-light entities, out-of-range values and empty composed actions', () => {
  const invalid = clone(example);
  invalid.allFunctions[0].entity_id = 'cover.kueche';
  invalid.allFunctions[0].defaultValue = 101;
  invalid.allFunctions[2].functions = [];
  const errors = validate(invalid);
  assert.ok(errors.some(error => error.includes('light.')));
  assert.ok(errors.some(error => error.includes('defaultValue')));
  assert.ok(errors.some(error => error.includes('Mindestens eine Aktion')));
});
