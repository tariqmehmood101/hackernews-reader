import { Pipe, PipeTransform } from '@angular/core';

/** A run of text, flagged if it matches the current search term. */
export interface TextSegment {
  readonly text: string;
  readonly match: boolean;
}

/**
 * Splits text into matched and unmatched runs so the template can wrap the matches in
 * <c>&lt;mark&gt;</c> elements.
 *
 * It returns <em>segments</em> rather than marked-up HTML on purpose. Story titles and authors are
 * submitted by Hacker News users, so building a string of tags and feeding it to
 * <c>innerHTML</c> would hand them script execution. Rendering segments through ordinary
 * interpolation keeps Angular's escaping in force.
 *
 * Matching uses <c>indexOf</c> rather than a regular expression, so a term full of regex
 * metacharacters needs no escaping and cannot become a catastrophic-backtracking input.
 */
@Pipe({ name: 'highlight' })
export class Highlight implements PipeTransform {
  transform(text: string | null | undefined, term: string | null | undefined): TextSegment[] {
    const source = text ?? '';
    const needle = (term ?? '').trim();

    if (source.length === 0) {
      return [];
    }

    if (needle.length === 0) {
      return [{ text: source, match: false }];
    }

    const haystack = source.toLowerCase();
    const lowered = needle.toLowerCase();

    // Lower-casing is not always length-preserving (Turkish "İ" becomes two code units), and a
    // shifted index would slice the original text in the wrong place. Rather than risk mangling
    // the title, fall back to showing it unhighlighted.
    if (haystack.length !== source.length) {
      return [{ text: source, match: false }];
    }

    const segments: TextSegment[] = [];
    let cursor = 0;

    for (;;) {
      const hit = haystack.indexOf(lowered, cursor);
      if (hit < 0) {
        break;
      }

      if (hit > cursor) {
        segments.push({ text: source.slice(cursor, hit), match: false });
      }

      segments.push({ text: source.slice(hit, hit + needle.length), match: true });
      cursor = hit + needle.length;
    }

    if (cursor < source.length) {
      segments.push({ text: source.slice(cursor), match: false });
    }

    return segments;
  }
}
