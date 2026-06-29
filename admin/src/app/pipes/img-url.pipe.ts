import { Pipe, PipeTransform } from '@angular/core';
import { resolveImageUrl } from '../utils/image-url.util';

@Pipe({ name: 'imgUrl', standalone: true })
export class ImgUrlPipe implements PipeTransform {
  transform(url: string | undefined | null): string {
    return resolveImageUrl(url);
  }
}
