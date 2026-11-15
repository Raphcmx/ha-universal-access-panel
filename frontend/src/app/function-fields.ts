import { Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { Action, Entity, PanelFunction } from './config';

let nextFieldsId = 0;
@Component({
  selector: 'function-fields',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './function-fields.html'
})
export class FunctionFieldsComponent {
  model = input.required<Action | PanelFunction>();
  entities = input<Entity[]>([]);
  changed = output<void>();
  readonly id = `function-fields-${++nextFieldsId}`;
  get isBrightness(): boolean { return this.model().mode === 'brightness'; }
  get suggestions(): Entity[] {
    return this.entities().filter(entity => !this.isBrightness || entity.entityId.startsWith('light.'));
  }
  changeMode(mode: 'brightness' | 'service'): void {
    const fn = this.model();
    fn.mode = mode;
    if (mode === 'brightness') {
      fn.stepSize ??= 10;
      fn.defaultValue ??= 50;
      delete fn.service;
      delete fn.state;
    } else {
      fn.service ??= '';
      delete fn.stepSize;
      delete fn.defaultValue;
    }
    this.changed.emit();
  }
}
