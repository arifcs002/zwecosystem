import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PosManagementComponent } from './pos-management.component';

describe('PosManagementComponent', () => {
  let component: PosManagementComponent;
  let fixture: ComponentFixture<PosManagementComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PosManagementComponent]
    })
    .compileComponents();
    
    fixture = TestBed.createComponent(PosManagementComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
