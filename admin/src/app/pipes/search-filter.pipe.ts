import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'searchFilter', standalone: true })
export class SearchFilterPipe implements PipeTransform {
  transform(items: string[], query: string): string[] {
    if (!query?.trim()) return items;
    const q = query.toLowerCase();
    return items.filter(i => i.toLowerCase().includes(q));
  }
}
