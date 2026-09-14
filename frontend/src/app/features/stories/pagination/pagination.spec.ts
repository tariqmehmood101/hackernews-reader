import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { Pagination } from './pagination';

describe('Pagination', () => {
  let fixture: ComponentFixture<Pagination>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Pagination] }).compileComponents();
    fixture = TestBed.createComponent(Pagination);
  });

  function render(page: number, totalPages: number, disabled = false): void {
    fixture.componentRef.setInput('page', page);
    fixture.componentRef.setInput('totalPages', totalPages);
    fixture.componentRef.setInput('disabled', disabled);
    fixture.detectChanges();
  }

  function pageButtons(): HTMLButtonElement[] {
    return fixture.debugElement.queryAll(By.css('.pager__page')).map(el => el.nativeElement);
  }

  it('renders nothing when there is only one page', () => {
    render(1, 1);

    expect(fixture.debugElement.query(By.css('.pager'))).toBeNull();
  });

  it('renders nothing when there are no pages at all', () => {
    render(1, 0);

    expect(fixture.debugElement.query(By.css('.pager'))).toBeNull();
    expect(fixture.componentInstance.pages()).toEqual([]);
  });

  it('jumps straight to the first page', () => {
    render(7, 10);
    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe(p => emitted.push(p));

    fixture.componentInstance.first();

    expect(emitted).toEqual([1]);
  });

  it('jumps straight to the last page', () => {
    render(3, 10);
    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe(p => emitted.push(p));

    fixture.componentInstance.last();

    expect(emitted).toEqual([10]);
  });

  it('first() does nothing when already on page 1', () => {
    render(1, 10);
    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe(p => emitted.push(p));

    fixture.componentInstance.first();

    expect(emitted).toEqual([]);
  });

  it('last() does nothing when already on the last page', () => {
    render(10, 10);
    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe(p => emitted.push(p));

    fixture.componentInstance.last();

    expect(emitted).toEqual([]);
  });

  it('renders First and Last rather than step buttons', () => {
    render(5, 10);

    const steps = fixture.debugElement
      .queryAll(By.css('.pager__step'))
      .map(el => el.nativeElement.textContent.trim());

    expect(steps).toEqual(['First', 'Last']);
  });

  it('caps the number of page buttons regardless of total pages', () => {
    render(1, 167);

    expect(pageButtons().length).toBe(5);
  });

  it('windows the buttons around the current page', () => {
    render(10, 20);

    expect(pageButtons().map(b => b.textContent!.trim())).toEqual(['8', '9', '10', '11', '12']);
  });

  it('keeps a full window at the start and end of the range', () => {
    render(1, 20);
    expect(pageButtons().map(b => b.textContent!.trim())).toEqual(['1', '2', '3', '4', '5']);

    render(20, 20);
    expect(pageButtons().map(b => b.textContent!.trim())).toEqual(['16', '17', '18', '19', '20']);
  });

  it('keeps each page button on its own page number as the window slides', () => {
    render(10, 20);
    const before = new Map(pageButtons().map(b => [b.textContent!.trim(), b]));

    render(11, 20);
    const after = new Map(pageButtons().map(b => [b.textContent!.trim(), b]));

    // The two windows are 8..12 and 9..13, overlapping on 9..12. Tracking by page number keeps
    // each of those on its own DOM node; tracking by index would instead reuse the same five
    // nodes and shuffle the labels between them.
    for (const label of ['9', '10', '11', '12']) {
      expect(after.get(label)).toBe(before.get(label));
    }
  });

  it('does not move keyboard focus onto a different page', () => {
    render(10, 20);
    const ten = pageButtons().find(b => b.textContent!.trim() === '10')!;
    ten.focus();

    render(11, 20);

    // The user aimed at page 10 and is still on page 10. With index tracking the focused node
    // would have been relabelled underneath them, so Enter would navigate somewhere else.
    expect((document.activeElement as HTMLElement).textContent!.trim()).toBe('10');
  });

  it('marks the current page for assistive technology', () => {
    render(3, 10);

    const current = pageButtons().find(b => b.getAttribute('aria-current') === 'page');
    expect(current?.textContent?.trim()).toBe('3');
  });

  it('emits the chosen page', () => {
    render(1, 10);
    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe(p => emitted.push(p));

    pageButtons()[2].click();

    expect(emitted).toEqual([3]);
  });

  it('does not emit for the page already shown', () => {
    render(3, 10);
    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe(p => emitted.push(p));

    fixture.componentInstance.goTo(3);

    expect(emitted).toEqual([]);
  });

  it('does not emit outside the valid range', () => {
    render(1, 10);
    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe(p => emitted.push(p));

    fixture.componentInstance.goTo(0);
    fixture.componentInstance.goTo(11);

    expect(emitted).toEqual([]);
  });

  it('disables First on the first page and Last on the last', () => {
    render(1, 10);
    expect(fixture.componentInstance.canGoFirst()).toBeFalse();
    expect(fixture.componentInstance.canGoLast()).toBeTrue();

    render(10, 10);
    expect(fixture.componentInstance.canGoFirst()).toBeTrue();
    expect(fixture.componentInstance.canGoLast()).toBeFalse();
  });

  it('reflects those states on the rendered buttons', () => {
    render(1, 10);

    const [first, last] = fixture.debugElement
      .queryAll(By.css('.pager__step'))
      .map(el => el.nativeElement as HTMLButtonElement);

    expect(first.disabled).toBeTrue();
    expect(last.disabled).toBeFalse();
  });

  it('emits nothing while disabled', () => {
    render(2, 10, true);
    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe(p => emitted.push(p));

    fixture.componentInstance.last();
    fixture.componentInstance.first();

    expect(emitted).toEqual([]);
  });
});
