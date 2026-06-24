import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PublicShopComponent } from './public-shop.component';

describe('PublicShopComponent', () => {
  let component: PublicShopComponent;
  let fixture: ComponentFixture<PublicShopComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PublicShopComponent]
    })
    .compileComponents();
    
    fixture = TestBed.createComponent(PublicShopComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
