# Photo archive foundation

The photo archive is a separate domain inside the same application, worker, and Postgres
database as the knowledge base. It is not a separate service. The existing `Note` / `Tag`
model remains useful for knowledge-base navigation, but it is not the source of truth for
people, places, or events.

The purpose of this work is to produce reviewed, structured archive data. Narrative-book
generation is deliberately deferred until that data is reliable.

## Decisions

### Canonical archive records are separate from AI output

`Person`, `Location`, and `Event` are stable, user-curated records. An AI cluster or
candidate is not itself a canonical record: it is evidence that may lead to one. This keeps
an algorithm re-run from silently changing the archive's history.

### Every AI result is reproducible and reviewable

Each analysis has a `PhotoAnalysisRun` carrying the asset id, pipeline/configuration hash,
model keys and completion state. Model output is append-only rather than overwritten by a
later run.

Candidates retain their rank, aggregate ranking score, and the individual signals that
produced it (for example visual similarity, GPS distance, time proximity, and confirmed
person overlap). A score is a ranking measure, not a probability, until it has been
calibrated against enough human decisions.

Human review is stored separately and never replaces the candidate: accept, reject,
correct, merge, and the chosen canonical target are decisions with timestamps. This supplies
an audit trail and labelled examples for later threshold calibration and ranking improvements.

When a location suggestion is refreshed, only unreviewed candidates for that asset are marked
superseded. They remain in the audit trail; the review screen shows the newer run instead. A
reviewed decision is never superseded or changed by a later analysis.

### Face detection runs once per photo, whatever the queue does

A worker that stops mid-job leaves the row in `Running`, and the next start requeues it. Detection has
no memory of its own, so a second execution stored every face of that photo again and put it up for
review twice - three photos in the local archive ended up with duplicate faces this way. `AnalyzeFaces`
now skips an asset that already has stored face occurrences; refreshing a photo's identities is
`RescoreFaces`' job, which re-ranks what is already detected instead of detecting again.

The skip only counts faces from the current `PipelineVersion` (`face-analysis/v2`), so a detection fix
is applied to an old photo by requeueing its `AnalyzeFaces` job: the new run supersedes the photo's
unreviewed person candidates and leaves reviewed ones and reference faces alone. v2 detects on the
EXIF-rotated image - v1 detected on the raw sideways pixels of a phone photo, so its boxes pointed at
the wrong part of the picture the browser shows.

### Face candidates cover every plausible person, and a re-score rewrites only what moved

A face proposes every person whose best confirmed reference reaches 0.25, capped at twenty, plus its
own top guess even when that falls below the floor - dropping the top guess would take the face out of
review altogether. The picker beside a review item shows each of those scores, so a reviewer can pick
the third-best person without losing what the model thought of them.

`Rank` records where a candidate stood when it was computed, not the current order: one face's rows can
come from several re-scores, so the live ranking is derived from the scores.

A re-score writes a new row only for the people whose score moved by at least 0.03, and retires only the
rows it replaced plus the people who dropped out of range. The comparison is against the stored score
rather than the previous calculation, so a drift of one percent at a time still eventually crosses the
threshold. Rewriting a face's whole candidate set whenever anything changed was the earlier behaviour;
past a handful of people it turned every confirmation into a full rewrite of every open face, because a
one-per-mille move at the bottom of the list counted as a change.

### The system improves from references before it retrains models

Confirming a face adds a validated example to that person's reference set; confirming a
location adds a validated appearance to that location. Later matches compare against these
larger reference sets. This improves archive-specific results without fine-tuning ArcFace,
CLIP, or another foundation model.

Fine-tuning a foundation model is not a planned early step: it needs a large, clean labelled
dataset, dedicated training infrastructure, and a separate evaluation process. Review data
first improves thresholds and candidate ranking instead.

### Exact duplicate bytes share one analysis

An exact duplicate is defined by SHA-256 over the stored bytes, not by filename, file size, or
visual resemblance. The oldest asset in a hash group is its canonical representative; only it
receives face analysis and its candidates appear in review. Other copies remain real archive files
and are not deleted. Similar-but-different photos remain eligible because visual deduplication is
a later, reviewable decision rather than a safe automatic rule.

### Clusters are temporary; links are explicit

`VisualCluster` and `EventCluster` are versioned algorithm outputs. `LocationCandidate` and
`EventCandidate` make a proposed link to a canonical record explicit. A user can merge two
provisional locations or split a proposed event without rewriting raw observations.

## Target records

The first schema will evolve, but it should preserve these responsibilities:

| Record | Responsibility |
|---|---|
| `PhotoAnalysisRun` | Versioned execution and model/configuration provenance for one photo. |
| `FaceOccurrence` | One detected face: original-image bounding box, landmarks, quality and model-versioned embedding. |
| `Person` / `PersonReferenceFace` | Curated person and their confirmed face examples. |
| `FaceMatchCandidate` / `FaceReviewDecision` | A proposed identity and the separate human outcome. |
| `VisualEmbedding` / `VisualCluster` | Model-versioned scene vector and a temporary similarity grouping. |
| `Location` / `LocationObservation` | Curated physical or visual place, and one photo's observed appearance there. |
| `LocationCandidate` / `LocationReviewDecision` | Top ranked possible location, its score breakdown, and human outcome. |
| `SceneObservation` / `SceneObservationReviewDecision` | Cautious VLM output and its independent human confirmation or rejection. |
| `Event` / `EventPhoto` | Curated event and its included photos. |
| `EventCandidate` / `EventReviewDecision` | Proposed event grouping and human outcome. |

## Delivery plan

### Phase 1 — archive data contract and audit trail

Create the archive entities, migrations, analysis-run provenance, candidate score storage,
and append-only review decisions. Define which record is canonical versus derived. No model
needs to be introduced in this phase.

The implemented slice includes the canonical catalogue (people, physical/visual locations, and
events with explicit asset/person/location links) and the audit records. The review surface lists
only unreviewed candidates in their corresponding Person, Location, or Event subsection. A user
can accept, reject, or correct one; the original candidate remains unchanged and its decision is
stored separately. The worker now writes person candidates from face evidence and location
candidates from scene vectors; event review remains empty until its pipeline exists.

**Done when:** a manually created candidate and a human accept/reject/correct decision are
stored independently, remain visible after a second analysis run, and identify their model
and configuration version.

### Phase 2 — people and face evidence

Add local face detection, alignment and embeddings. Store every detected `FaceOccurrence`;
allow a user to enrol a `Person` from confirmed occurrences. Produce ranked identity
candidates with the raw similarity score and the gap to the next candidate. Ambiguous faces
remain unknown and reviewable rather than being silently assigned.

**Done when:** confirmed reference faces make later suggestions for the same person better,
and a wrong suggestion can be rejected without losing the original model result.

The first implementation uses local FaceONNX models: its embedded YOLOv5 face detector returns
a bounding box and five landmarks, and its ResNet27 embedder produces a 512-value vector. Every
image upload queues `FingerprintAsset` alongside `BuildSourceNote`; the fingerprint job then queues
`AnalyzeFaces` and `AnalyzeScenes` for the canonical asset of the duplicate group. The worker stores the
face occurrence and top-five cosine-similarity candidates per detected face. With no confirmed
reference faces, it creates an explicit unknown candidate so the reviewer can create a person and
correct the candidate to them. Accepting or correcting a face candidate creates a
`PersonReferenceFace`; a later run compares against all such references for that person.

Existing archives start with `FingerprintAsset` jobs through **Analyze unprocessed photos**. Once
fingerprints are known, exactly one `AnalyzeFaces` job is queued per duplicate group. No source
note is re-generated by this archive batch action.

### Phase 3 — visual embeddings and locations

Generate a model-versioned scene embedding per photo. Produce the top five location
candidates and expose every contributing signal and aggregate score. New visual locations
start as provisional records and can later be confirmed, merged, or rejected.

**Done when:** a reviewer can see why each of the five candidates ranked where it did and
can choose an existing location or create/confirm a new one.

The first implementation uses a local CLIP vision ONNX encoder that produces a normalized
512-value scene vector. `AnalyzeScenes` runs only for the canonical member of an exact-duplicate
group. It compares that vector with `LocationObservation` reference vectors confirmed by earlier
reviews, records top-five cosine-ranked candidates, and includes the model, vector size, reference
asset and reference count in the immutable evidence. Correcting or accepting a location candidate
creates the observation. **Refresh location suggestions** re-runs only photos that still lack a
confirmed location; it supersedes their old unreviewed proposals without deleting the audit trail.

### Phase 4 — scene observations

Use the VLM only after confirmed people and location context are available. Store structured,
evidence-bound observations: visible actions, interactions, objects, text, and cautious mood
signals. Do not turn uncertain inference into a canonical fact.

**Done when:** each observation identifies its photo, analysis run, confidence, and review
outcome.

The first implementation manually queues `AnalyzeSceneObservations` only for canonical images
that already have a reviewed person or location. Ollama receives those names and locations as
limited context, returning at most ten structured observations of five kinds: `Action`,
`Interaction`, `Object`, `Text`, or `Mood`. The worker preserves the actual model key and a
configuration hash in `PhotoAnalysisRun`; an observation stores its visible evidence and cautious
0–1 ranking separately from a `Confirmed` or `Rejected` human decision. A refresh supersedes only
unreviewed observations for the same image, preserving both earlier model output and every review.

### Phase 5 — event candidates

Cluster photos using reviewed people and locations, time proximity, visual similarity, and
scene observations. Keep clusters and event candidates separate from curated events, with
merge/split decisions recorded explicitly.

**Done when:** a user can review a proposed event, adjust its membership, and preserve the
reasoning behind the original grouping.

The first implementation manually queues a deterministic event-clustering run over canonical
photos. An edge needs a transparent combination of capture-time proximity, shared reviewed people
or locations, compatible CLIP vectors, and confirmed scene-observation kinds. Each temporary
cluster stores its complete edge evidence, then produces one event candidate. A reviewer selects
which cluster photos to keep and either creates a named canonical event or attaches them to an
existing one; the independent review decision retains the selected photo ids. The worker never
creates an event itself.

### Phase 6 — narrative generation (deferred)

Generate chapters from reviewed events and their structured evidence, not from raw image
descriptions. This phase is intentionally out of scope until the preceding data foundation is
usable.

## Open

- [ ] **The job queue has no lease.** A claim is a plain status write, and a starting worker requeues
      everything left in `Running` - including a job another worker is still executing. Harmless with one
      worker, wrong with two (the local worker and the container one already exist side by side, against
      different databases). A lease with an expiry would replace both the claim and the stuck-job reset.
- [ ] **The picker's two grey notes are unverified.** `no reference faces` and `not ranked` were never
      seen on screen: in the test archive every person already had a reference face, and the picker
      only appears for low-confidence candidates, so the green end of the score colour scale was not
      observed either. Re-check once a person without confirmed faces exists.
- [ ] **Compute `ContentSha256` while the upload streams to storage.** Hashing is cheap and needs no
      AI, but it goes through the FIFO job queue, so a new photo waits behind slow AI jobs before its
      analysis can even be queued. Hashing in the upload stream would leave `FingerprintAsset` needed
      only for the existing archive.
- [ ] **Check whether CLIP scene vectors see phone photos sideways.** `ClipSceneEmbedder` hands the raw
      bytes to `ClipImageEncoder`; if that library ignores EXIF orientation like ImageSharp does, a
      rotated phone photo gets a vector for the sideways picture and location matching suffers.
- [ ] **Phase 2 — face-quality calibration.** Validate the detector confidence and similarity
      behaviour on a small manually labelled archive subset before introducing acceptance
      thresholds or any automatic decision.
- [ ] **Phase 3 — location-score calibration.** Review a manually labelled archive subset before
      interpreting cosine scores as a confidence or introducing any automatic location decision.
- [ ] **Phase 4 — scene-observation calibration.** Review a manually labelled archive subset to
      assess VLM evidence quality and decide whether any per-kind confidence guidance is useful.
- [ ] **Phase 5 — event-candidate review verification.** Use an archive subset with at least two
      related, reviewed photos to browser-test candidate evidence, membership adjustment and both
      Create/Attach review outcomes.
- [ ] **Phase 6 — narrative generation.** Revisit only after reviewed events are useful.
