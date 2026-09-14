import { Pipe, PipeTransform } from '@angular/core';

/** Renders an absolute timestamp as a compact age: "just now", "42m ago", "3h ago", "2d ago". */
@Pipe({ name: 'relativeTime' })
export class RelativeTime implements PipeTransform {
  private static readonly Minute = 60;
  private static readonly Hour = 3600;
  private static readonly Day = 86_400;
  private static readonly Month = 2_592_000;

  transform(value: string | Date | null | undefined, now: Date = new Date()): string {
    if (!value) {
      return '';
    }

    const then = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(then.getTime())) {
      return '';
    }

    // Clamp at zero: clock skew between the browser and the feed must not read "in 3 minutes".
    const seconds = Math.max(0, Math.floor((now.getTime() - then.getTime()) / 1000));

    if (seconds < RelativeTime.Minute) {
      return 'just now';
    }
    if (seconds < RelativeTime.Hour) {
      return `${Math.floor(seconds / RelativeTime.Minute)}m ago`;
    }
    if (seconds < RelativeTime.Day) {
      return `${Math.floor(seconds / RelativeTime.Hour)}h ago`;
    }
    if (seconds < RelativeTime.Month) {
      return `${Math.floor(seconds / RelativeTime.Day)}d ago`;
    }

    return then.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
  }
}
