import { Marked } from 'marked'
import type { TokenizerAndRendererExtension, Tokens } from 'marked'
import type { NoteLinkState } from '../api/notes'
import './renderNoteBody.css'

interface WikiLinkToken extends Tokens.Generic {
  type: 'wikiLink'
  raw: string
  title: string
}

// A marked extension, not a raw-text replace: the tokenizer knows what is code, so a
// [[title]] inside a fenced block stays literal text.
// `linkable: false` renders a resolved [[link]] as a highlighted span, not a button - for
// places that show a note without the accordion behind the click (the file panel).
export function renderNoteBody(
  markdown: string,
  links: NoteLinkState[],
  { linkable = true }: { linkable?: boolean } = {},
): string {
  const states = new Map(links.map((link) => [link.title.toLowerCase(), link]))

  const wikiLink: TokenizerAndRendererExtension = {
    name: 'wikiLink',
    level: 'inline',

    // Without this, other inline rules consume the brackets before the extension runs.
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
        if (!linkable) {
          return `<span class="wiki-link wiki-link-inert">${text}</span>`
        }

        // A button, not an anchor: a note has no URL, the list expands it in place.
        return `<button type="button" class="wiki-link" data-note-id="${escapeHtml(link.targetId)}">${text}</button>`
      }

      if (link?.state === 'deleted') {
        // Strike on the inner span, not the wrapper: it inherits down and would cross the tag too.
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
