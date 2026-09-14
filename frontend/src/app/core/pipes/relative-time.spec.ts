import { RelativeTime } from './relative-time';

describe('RelativeTime', () => {
  const pipe = new RelativeTime();
  const now = new Date('2026-09-12T12:00:00Z');

  function ago(seconds: number): string {
    return pipe.transform(new Date(now.getTime() - seconds * 1000).toISOString(), now);
  }

  it('describes the last minute as "just now"', () => {
    expect(ago(0)).toBe('just now');
    expect(ago(59)).toBe('just now');
  });

  it('counts minutes up to an hour', () => {
    expect(ago(60)).toBe('1m ago');
    expect(ago(42 * 60)).toBe('42m ago');
    expect(ago(59 * 60)).toBe('59m ago');
  });

  it('counts hours up to a day', () => {
    expect(ago(3600)).toBe('1h ago');
    expect(ago(23 * 3600)).toBe('23h ago');
  });

  it('counts days up to a month', () => {
    expect(ago(86_400)).toBe('1d ago');
    expect(ago(29 * 86_400)).toBe('29d ago');
  });

  it('falls back to a date beyond a month', () => {
    const result = ago(90 * 86_400);

    expect(result).not.toContain('ago');
    expect(result).toContain('2026');
  });

  it('never reports a future time, so clock skew cannot read "in 3 minutes"', () => {
    const future = new Date(now.getTime() + 5 * 60_000).toISOString();

    expect(pipe.transform(future, now)).toBe('just now');
  });

  it('returns an empty string for missing or unparseable values', () => {
    expect(pipe.transform(null, now)).toBe('');
    expect(pipe.transform(undefined, now)).toBe('');
    expect(pipe.transform('', now)).toBe('');
    expect(pipe.transform('not a date', now)).toBe('');
  });

  it('accepts a Date as well as a string', () => {
    expect(pipe.transform(new Date(now.getTime() - 3600_000), now)).toBe('1h ago');
  });
});
