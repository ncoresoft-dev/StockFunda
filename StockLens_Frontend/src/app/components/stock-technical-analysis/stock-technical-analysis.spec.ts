import { ComponentFixture, TestBed } from '@angular/core/testing';

import { StockTechnicalAnalysis } from './stock-technical-analysis';

describe('StockTechnicalAnalysis', () => {
  let component: StockTechnicalAnalysis;
  let fixture: ComponentFixture<StockTechnicalAnalysis>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StockTechnicalAnalysis],
    }).compileComponents();

    fixture = TestBed.createComponent(StockTechnicalAnalysis);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
