import { Marked } from 'marked'
import type { TokenizerAndRendererExtension, Tokens } from 'marked'
import type { NoteLinkState } from '../api/notes'

interface WikiLinkToken extends Tokens.Generic {
  type: 'wikiLink'
  raw: string
  title: string
}

/**
 * Renders a note body, turning every [[title]] into the state the server resolved it to:
 * a working link, a note that was deleted, or a note that does not exist yet.
 *
 * A marked extension rather than a replace over the raw text: the tokenizer knows what is
 * code, so a [[title]] inside a fenced block stays the literal text it is meant to be.
 */
export function renderNoteBody(markdown: string, links: NoteLinkState[]): string {
  const states = new Map(links.map((link) => [link.title.toLowerCase(), link]))

  const wikiLink: TokenizerAndRendererExtension = {
    name: 'wikiLink',
    level: 'inline',

    // Without this, marked scans for the extension only after other inline rules have had
    // their say, and the brackets are already gone.
    start: (source: string) => source.indexOf('[['),

    tokenizer(source: string) {
      const match = /^\[\[([^[\]\r\n]+)\]\]/.exec(source)

      if (match === null) {
        return undefined
      }

      return {
        type: 'wikiLink',
        raw: match[0],
        title: match[1].split('|')[0].trim(),
      } satisfies WikiLinkToken
    },

    renderer(token) {
      const { title } = token as WikiLinkToken
      const link = states.get(title.toLowerCase())
      const text = escapeHtml(title)

      if (link?.state === 'resolved' && link.targetId !== null) {
        // A button, not an anchor: there is no URL for a note - the list expands it in place.
        return `<button type="button" class="wiki-link" data-note-id="${escapeHtml(link.targetId)}">${text}</button>`
      }

      if (link?.state === 'deleted') {
        // The strike sits on an inner span, not the wrapper: text-decoration inherits down
        // and a descendant cannot switch it off, so a wrapper-level strike would cross out
        // the "deleted" tag as well.
        return (
          `<span class="wiki-link wiki-link-deleted">` +
          `<span class="wiki-link-text">${text}</span>` +
          `<span class="wiki-link-tag"> · deleted</span>` +
          `</span>`
        )
      }

      return `<span class="wiki-link wiki-link-missing" title="No note with this title">${text}</span>`
    },
  }

  return new Marked({ extensions: [wikiLink] }).parse(markdown, { async: false })
}

function escapeHtml(value: string): string {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
}
