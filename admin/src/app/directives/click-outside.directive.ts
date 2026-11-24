import { Directive, ElementRef, EventEmitter, HostListener, Output } from '@angular/core';

@Directive({ selector: '[clickOutside]', standalone: true })
export class ClickOutsideDirective {
  @Output() clickOutside = new EventEmitter<void>();

  constructor(private el: ElementRef) {}

  @HostListener('document:click', ['$event.target'])
  onClick(target: EventTarget | null) {
    if (!this.el.nativeElement.contains(target as Node | null)) {
      this.clickOutside.emit();
    }
  }
}
