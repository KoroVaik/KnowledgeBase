---
name: generate-test-data
description: Generate synthetic test files for exercising the KnowledgeBase AI pipeline (tag generation, tag-merge suggestions, synthesis grouping) - currently plain text notes via local Ollama, with images and PDFs planned. Use when asked to generate test data, test files, or sample notes/uploads for the pipeline.
---

# Generate test data

Produces files meant to be uploaded through the app so they go through the real
pipeline (`SourceNoteHandler` -> Ollama -> tags) - not hand-placed into
`backend/data/assets`, which is the debug bucket for real user uploads (see
[`architecture.md`](../../../docs/architecture.md)), and not committed to the repo.

## Decisions

- **One script per content kind, under `scripts/<kind>/`** - mirrors the project's own
  `ISourceExtractor` pattern in [`ai-pipeline.md`](../../../docs/ai-pipeline.md#decisions)
  ("new file type = new class, not a case"). Adding image or PDF generation later means a
  new script + topic file, not touching the text one.
- **Topics are data, not code** - each kind's `topics.json` lists `{cluster, file, topic}`.
  Files in the same `cluster` share a theme on purpose: the synthesis pipeline needs at
  least 2 Source notes carrying the same tag to group them (`ai-pipeline.md`, *Synthesis
  pipeline*), and varying the phrasing of the same subtopic within a cluster gives the
  still-unverified `GroupTags` pass (`ai-pipeline.md` Open list) something real to merge.
  Add topics by editing the JSON, not the script. For a one-off batch that isn't worth
  curating into `topics.json`, pass `-Topics`/`-CountPerTopic` instead (see below) - the
  model is asked to vary the angle itself.
- **Model/BaseUrl are read from `backend/KnowledgeBase.Worker/appsettings.json`** at run
  time (override with `-Model`/`-OllamaUrl`) - test content should always be generated
  with whatever model the worker actually tags with, not a value that can quietly drift
  out of sync.
- **Output goes to `<repo root>/test-data/<kind>/`**, gitignored - not
  `backend/data/assets` (that's the debug bucket for real uploads). Living under the repo
  root keeps it easy to find and to drag-and-drop into the running app; the gitignore
  entry keeps it out of commits.
- Generation only - this skill does not upload the files or start the worker/Ollama.
  Follow CLAUDE.md's "ask before testing": starting the worker or running a real upload
  through the pipeline needs the user's go-ahead first.

## Text notes (current)

```powershell
powershell -File .claude\skills\generate-test-data\scripts\text\generate-notes.ps1
```

Options:
- `-Clusters python,books` - only generate the given clusters (see `topics.json` for the
  full list: python, cooking, travel, books, fitness, gardening, movies).
- `-Count 5` - cap how many topics to generate (after the cluster filter).
- `-OutDir <path>` - default is `<repo root>/test-data/text`.
- `-Model` / `-OllamaUrl` - override the auto-detected worker settings.
- `-Topics "home coffee brewing","urban beekeeping"` - skip `topics.json` and generate
  ad hoc: each string becomes its own cluster/theme.
- `-CountPerTopic 4` - how many files per `-Topics` entry (default 5); each one asks the
  model for a distinct angle so they don't repeat.
- `-Files a.txt,b.txt` - re-roll only these `topics.json` entries (by `file` name) -
  handy when the model way over/undershot the target length on a few notes.

Requires Ollama running locally (`http://localhost:11434` by default) with the
configured model pulled. One failed generation does not abort the run - it's logged and
skipped, with a succeeded/failed summary at the end.

## Planned: other content kinds

Not implemented yet - listed here so a future session doesn't have to re-derive the
approach:

- **Images**, `scripts/image/` - synthetic JPEGs with embedded EXIF (`DateTimeOriginal`,
  GPS) via a small .NET/ImageSharp script. Directly exercises the still-unverified real
  photo upload in `ai-pipeline.md`'s Open list (*Photo capture metadata - full flow run
  pending*), which only synthetic-JPEG unit tests have covered so far.
- **PDFs**, `scripts/pdf/` - wrap generated text into a PDF, to exercise
  `PdfPigTextExtractor` (text-layer PDFs only, per `ai-pipeline.md` - a scanned/image-only
  PDF is a separate, still-open extractor problem, not something this skill should fake).

Each would get its own `topics.json` (reusing the same cluster names where it makes
sense, e.g. a `books` PDF alongside the existing `books` text notes) and follow the same
output convention (`<repo root>/test-data/<kind>/`).
