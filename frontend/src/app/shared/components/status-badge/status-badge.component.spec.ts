import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatusBadgeComponent } from './status-badge.component';

describe('StatusBadgeComponent', () => {
  let fixture: ComponentFixture<StatusBadgeComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [StatusBadgeComponent] }).compileComponents();
    fixture = TestBed.createComponent(StatusBadgeComponent);
  });

  it('renders the given value as text', () => {
    fixture.componentRef.setInput('value', 'Flagged');
    fixture.detectChanges();

    const span: HTMLElement = fixture.nativeElement.querySelector('span');
    expect(span.textContent?.trim()).toBe('Flagged');
  });

  it('applies a lowercased status CSS class matching the value', () => {
    fixture.componentRef.setInput('value', 'Critical');
    fixture.detectChanges();

    const span: HTMLElement = fixture.nativeElement.querySelector('span');
    expect(span.classList).toContain('status-critical');
  });
});
