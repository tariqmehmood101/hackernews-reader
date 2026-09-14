import { Highlight, TextSegment } from './highlight';

describe('Highlight', () => {
  const pipe = new Highlight();

  /** Compact view of a result: matched runs wrapped in brackets. */
  function shape(segments: TextSegment[]): string {
    return segments.map(s => (s.match ? `[${s.text}]` : s.text)).join('');
  }

  it('marks a single match inside the text', () => {
    expect(shape(pipe.transform('Learning Rust today', 'Rust'))).toBe('Learning [Rust] today');
  });

  it('matches regardless of case but keeps the original casing', () => {
    expect(shape(pipe.transform('Learning RUST today', 'rust'))).toBe('Learning [RUST] today');
    expect(shape(pipe.transform('Learning Rust today', 'RUST'))).toBe('Learning [Rust] today');
  });

  it('marks every occurrence', () => {
    expect(shape(pipe.transform('rust and more rust', 'rust'))).toBe('[rust] and more [rust]');
  });

  it('handles a match at the very start and the very end', () => {
    expect(shape(pipe.transform('rust wins', 'rust'))).toBe('[rust] wins');
    expect(shape(pipe.transform('all about rust', 'rust'))).toBe('all about [rust]');
  });

  it('marks the whole string when the term is the whole string', () => {
    expect(shape(pipe.transform('rust', 'rust'))).toBe('[rust]');
  });

  it('marks nothing when the term does not appear', () => {
    const segments = pipe.transform('Learning Go today', 'rust');

    expect(shape(segments)).toBe('Learning Go today');
    expect(segments.every(s => !s.match)).toBeTrue();
  });

  it('treats a blank or missing term as no search at all', () => {
    for (const term of ['', '   ', null, undefined]) {
      const segments = pipe.transform('Learning Rust', term);

      expect(segments.length).toBe(1);
      expect(segments[0].match).toBeFalse();
    }
  });

  it('trims the term, matching what the API is sent', () => {
    expect(shape(pipe.transform('Learning Rust today', '  rust  '))).toBe('Learning [Rust] today');
  });

  it('returns nothing for empty or missing text', () => {
    expect(pipe.transform('', 'rust')).toEqual([]);
    expect(pipe.transform(null, 'rust')).toEqual([]);
    expect(pipe.transform(undefined, 'rust')).toEqual([]);
  });

  it('treats regex metacharacters as literal text', () => {
    // indexOf, not a RegExp — so ".*" matches the characters, never everything, and a term like
    // "(a+)+$" is inert rather than a backtracking hazard.
    expect(shape(pipe.transform('a .* b', '.*'))).toBe('a [.*] b');
    expect(shape(pipe.transform('literal (a+)+$ here', '(a+)+$'))).toBe('literal [(a+)+$] here');
    expect(shape(pipe.transform('anything at all', '.*'))).toBe('anything at all');
  });

  it('never loses or duplicates characters', () => {
    const title = 'Rust, rust and RUSTY things';

    expect(pipe.transform(title, 'rust').map(s => s.text).join('')).toBe(title);
  });

  it('degrades to no highlighting when lower-casing would shift the indices', () => {
    // "İ" lower-cases to two code units, so slicing by the lowered index would corrupt the text.
    const title = 'İstanbul rust';

    const segments = pipe.transform(title, 'rust');

    expect(segments.length).toBe(1);
    expect(segments[0].match).toBeFalse();
    expect(segments[0].text).toBe(title);
  });

  it('keeps hostile text as inert data', () => {
    // The pipe must never build markup; it only ever slices the original string.
    const title = '<img src=x onerror="alert(1)"> rust';

    const segments = pipe.transform(title, 'rust');

    expect(segments.map(s => s.text).join('')).toBe(title);
    expect(segments.some(s => s.text.includes('<img'))).toBeTrue();
  });
});
