import { Pipe, PipeTransform, inject } from '@angular/core';
import { I18nService } from './i18n.service';

/** {{ 'Add to Cart' | t }} — impure so the whole page re-renders the moment the language is switched. */
@Pipe({ name: 't', standalone: true, pure: false })
export class TranslatePipe implements PipeTransform {
  private i18n = inject(I18nService);
  transform(text: string | null | undefined, params?: Record<string, string | number>): string { return this.i18n.t(text ?? '', params); }
}
