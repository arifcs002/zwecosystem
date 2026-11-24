
import { Component, Input } from '@angular/core';
import { NgModel } from '@angular/forms';

@Component({
    selector: 'app-required-error',
    imports: [],
    template: `
    @if (control?.invalid && (control?.touched || control?.dirty)) {
      <p class="field-error">
        This field is required.
      </p>
    }
    `,
    styles: [`
    .field-error {
      margin-top: 0.35rem;
      font-size: 0.78rem;
      line-height: 1.1rem;
      color: #fb7185;
    }
  `]
})
export class RequiredErrorComponent {
  @Input() control?: NgModel | null;
}
