---
name: sync-schema
description: Compare schema.puml (repo root) with the C# entity classes it documents and update only what changed - add or remove fields, enum values and relations, never regenerate the whole file. Use when asked to sync, update, refresh or check the entity diagram / schema.puml / class diagram, or after entity, enum or relation changes in backend/KnowledgeBase.Core/Persistence.
---

# Sync schema.puml with the entity code

[`schema.puml`](../../../schema.puml) at the repo root is a hand-maintained PlantUML
class diagram of the generation-related entities. It is a **diff target, not a generated
artifact**: run this skill after touching entities and it applies only the delta.

## Scope

The diagram covers the AI generation pipeline entities only
([`docs/ai-pipeline.md`](../../../docs/ai-pipeline.md)): `AssetRecord`, `ProcessingJob`,
`Note`, `NoteLink`, `NoteTag`, `SynthesisSource`, `Tag`, `TagParent`,
`TagParentSuggestion` and the enums `JobKind`, `ProcessingStatus`, `NoteKind`,
`SuggestionConfidence`. Photo-archive analysis entities
([`docs/photo-archive.md`](../../../docs/photo-archive.md) - `PhotoAnalysis*`,
`FaceOccurrence`, `EventCluster*`, `SceneObservation`, `Person`, `Location`,
`VisualEmbedding`, `ArchiveEvent*`) are **deliberately out of scope** - do not add them.

## Sources of truth

`backend/KnowledgeBase.Core/Persistence/*.cs`, excluding `*Configuration.cs`,
`*DbContext.cs`, `*Registration.cs`, the `Migrations/` folder and anything under `obj/`.
The entity class files are the only source for fields and enums; navigation-style links
between entities are plain string FK columns on the classes, so relations are read off
the FK properties (e.g. `NoteTag.NoteId`/`TagId`), not from a DbContext.

## Procedure

1. Read `schema.puml` and all in-scope entity files (step above).
2. Build the delta, nothing more:
   - field added / removed / renamed / retyped / nullability flipped;
   - enum value added / removed / renamed;
   - relation added (new FK property) or removed (property gone) - including the
     cardinality the nullability implies;
   - entity added **in scope** or removed from the code.
3. Apply each delta as a surgical edit to `schema.puml`. **Never rewrite the whole file.**
   Preserve the header comments, package grouping, member order within a class (append
   new fields where they read naturally - usually at the end), and the relation section's
   grouping comments.
4. If nothing changed, say exactly that and touch nothing.

## Diagram conventions (keep the file consistent)

- Fields as `Name : Type`, nullable as `Type?`; no methods, no visibility markers.
- Enums as PlantUML `enum` blocks, one value per line, no comments.
- Relations: arrow + cardinality + the FK property as the label, e.g.
  `NoteTag "0..*" --> "1" Tag : TagId`. Composition `*--` for rows owned by their parent
  (`NoteLink`, `NoteTag` are owned by `Note`); plain `-->` otherwise. `string?` FK =>
  target cardinality `0..1`; `string` FK => `1`.
- Join rows with two FKs to the same class (`SynthesisSource`, `TagParent`,
  `TagParentSuggestion`) get one arrow per FK, each labelled. `Tag --> Tag` for the
  self-referencing `SuggestedMergeIntoId`.
- Grouping: one outer package `KnowledgeBase.Core.Persistence`, inner packages by domain
  (`Jobs & assets`, `Notes`, `Tags`) - the code has no subfolders, so this grouping is a
  diagram-level choice, and a new in-scope entity goes into the matching group.

## Reporting

Summarise the diff in Ukrainian in chat: what was added / removed / changed, and what was
checked. If a change looks deliberate-but-unmapped (e.g. a new FK with no clear target
entity), surface it as a question instead of guessing a relation.
