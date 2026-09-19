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
now skips an asset that already has stored face occurrences; regrouping what is already detected is
`ClusterFaces`' job, which detects nothing.

The skip only counts faces from the current `PipelineVersion` (`face-analysis/v2`), so a detection fix
is applied to an old photo by requeueing its `AnalyzeFaces` job; the next grouping replaces the photo's
unreviewed person candidates and leaves reviewed ones and reference faces alone. Grouping considers
only the current version's detections for the same reason: an old version's occurrence must not come
back into review as a fresh proposal. v2 detects on the
EXIF-rotated image - v1 detected on the raw sideways pixels of a phone photo, so its boxes pointed at
the wrong part of the picture the browser shows.

### Faces are reviewed as rows of people, grouped by comparing faces with each other

Scoring each face alone against confirmed references (the earlier `FaceCandidateRanking`) never compared
faces with each other: with no references every face was "Unknown face", and with a few references the
top guess was kept even below the 0.25 floor, so random approved people were suggested. Now `ClusterFaces`
groups every open face of the archive (current detection version, with an identity, on a canonical photo,
not settled) and the review screen shows one row per person:

1. A face joins the confirmed person whose best reference scores highest, only if that reaches
   `PersonJoinThreshold`; a below-threshold guess is never kept, and a person on the face's negative
   list is skipped.
2. The rest join an ignored group the same way (best cosine to the faces the user filed into it), so an
   ignored stranger does not come back as a new row on every upload.
3. What is left is clustered agglomeratively (average linkage on cosine, merged while the linkage
   reaches `ClusterThreshold`). Single faces go to Unsorted.
4. An anonymous row or ignored group gets a "Looks like" hint only when its average best score for one
   person lies between `HintThreshold` and `PersonJoinThreshold`, never for a person any member is
   negative for.

Rows are ordered by the face's score to the person (person rows) or its average cosine to the other
members (groups), most similar first. The three thresholds (0.45 / 0.45 / 0.30, section `FaceClustering`
in the worker config) are raw ArcFace `w600k_r50` cosines and **unmeasured placeholders**, like the score
calibration below.

Each run writes a `FaceClusteringRun` and its `FaceCluster` rows, supersedes every undecided person
candidate and writes one fresh candidate per open face, pointing at its cluster. `PhotoAnalysisCandidate`
still needs a per-asset `PhotoAnalysisRun`, so each grouping writes one per photo it touches. The full
rewrite is the simple choice at hundreds of faces. `ClusterFaces` is queued by every detection job, every
review action (submit, delete, ignore, revoke) and the end of `MigrateFaceModels`, at most one pending
at a time; it finishes without work while any `FingerprintAsset` or `AnalyzeFaces` job is still active,
because the last detection job queues another. Old `RescoreFaces` rows still in the queue run the same
grouping - nothing queues that kind any more.

### The system improves from references before it retrains models

Confirming a face adds a validated example to that person's reference set; confirming a
location adds a validated appearance to that location. Later matches compare against these
larger reference sets. This improves archive-specific results without fine-tuning ArcFace,
CLIP, or another foundation model.

Fine-tuning a foundation model is not a planned early step: it needs a large, clean labelled
dataset, dedicated training infrastructure, and a separate evaluation process. Review data
first improves thresholds and candidate ranking instead.

### A face's identity survives re-detection

Detection names a physical face on a photo with a `FaceIdentity` row, not with its `FaceOccurrence`
row: every occurrence any run stores for that face points at the same identity. When a run stores a
face, it is matched against what earlier runs of the same photo already assigned - the best pair
scoring at least a 0.5 embedding cosine wins, one-to-one, so a person appearing twice on one photo
keeps two identities. The comparison is embedding cosine, not box overlap: a re-detection crops
nearly the same pixels, so the match survives a detector change, which near-identical boxes cannot
promise.

Review states attach to the identity, not an occurrence, and `FaceIdentityState` is the one place that
answers what state an identity is in (the latest decision per identity wins):

- **Settled** - the identity has a confirmed reference face (Submit). It is never grouped or shown
  again; a re-detection is stored as evidence only.
- **Pinned to Unsorted** - a face left unchecked when its row is submitted or ignored gets a `Rejected`
  decision, in the same save as the row's other decisions. The face stays out of automatic
  grouping until it is named or ignored, and if its row proposed a person, that person becomes a
  negative for the face: never auto-joined or hinted for it again.
- **Ignored into group X** - Ignore creates an `IgnoredFaceGroup` and writes an `Ignored` decision
  (target = the group) per face. Ignored faces are *not* settled: the group stays reviewable and can
  still be named.

Before clustering, `Rejected`/`Merged` on a person proposal meant "close this face". The
`AddFaceClustering` migration moves such faces (latest decision Rejected/Merged, no reference) into one
ignored group through an `Ignored` decision on a superseded copy of the decided proposal; the old
decision stays in the audit trail. Revoking a reference unsettles the face and queues a regrouping.
`PersonReferenceFace` still points at the specific confirmed occurrence, so ranking keeps comparing
against the crop the reviewer actually confirmed, and re-embedding keeps that reference's vector
current.

### Face model migrations are self-healing; scores stay raw, percents are calibrated

Every `FaceOccurrence` records the `EmbeddingModelKey` of the embedder that produced its vector
(the current one is insightface ArcFace `w600k_r50`), and detection runs are versioned. The
worker closes the gap between stored data and current code itself: on start it counts stale
embeddings and canonical photos without a run of the current detection version, and if either is
non-zero it queues a `MigrateFaceModels` job for itself. That job re-crops and re-embeds every
stale face (references included, in place, per-photo resumable), requeues detection for the
outdated photos, and regroups the open faces. Detection fixes ride the same path — no separate procedure.

A model swap is therefore: put the .onnx file in place, change the code, restart the worker.
The migration is idempotent (each step touches only rows not matching current code) and
deduplicated (one pending/running job at a time); a converged archive queues nothing. Reviewed
decisions are never touched by any of this — a re-embed rewrites embeddings, not history, and
re-detected faces belonging to an already-settled identity store their occurrence but do not re-open
the settled identity for review. The migration also assigns identities to occurrences that predate
the identity system, replaying each photo's runs oldest first.

A cosine score is not a probability, and the raw value stays the stored evidence. The API adds a
calibrated `confidence` at read time for person candidates: a logistic over the raw score with
`FaceAnalysis:Center` and `FaceAnalysis:Scale` (defaults 0.28 / 0.05, halfway between the expected
same-person and different-person ranges for this embedder). The review screen shows that as the
percent; the raw score remains in the Model evidence section. Once score distributions have been
measured on real labelled data, the two numbers move to config - no re-run needed. Since the
cluster-based review no screen shows this percent any more (`FaceConfidenceCalibration` is unused).

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

The first implementation detects with the local FaceONNX YOLOv5s-face model (bounding box plus
five landmarks) and embeds with the local insightface `w600k_r50.onnx` ArcFace model, a 512-value
vector of the 112×112 crop that a 5-point similarity transform aligns onto the standard template
(FaceONNX's own ResNet27 embedder was replaced 2026-09-18: same-person and different-person
cosines sat so close together that a confirmed face could score 37%). Every image upload queues
`FingerprintAsset` alongside `BuildSourceNote`; the fingerprint job then queues `AnalyzeFaces`
and `AnalyzeScenes` for the canonical asset of the duplicate group. The worker stores the face
occurrences and their identities, then `ClusterFaces` groups them into review rows (see the
Decision above). Submitting a row creates a `PersonReferenceFace` per face; later groupings
compare against all such references for that person.

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
- [ ] **The picker's grey notes are unverified.** The person picker is gone (cluster-based review); the
      location picker's `no reference photos` / `score too low` notes were never seen on screen, nor the
      green end of the score colour scale. Re-check with a location that has no confirmed photos.
- [ ] **Compute `ContentSha256` while the upload streams to storage.** Hashing is cheap and needs no
      AI, but it goes through the FIFO job queue, so a new photo waits behind slow AI jobs before its
      analysis can even be queued. Hashing in the upload stream would leave `FingerprintAsset` needed
      only for the existing archive.
- [ ] **Check whether CLIP scene vectors see phone photos sideways.** `ClipSceneEmbedder` hands the raw
      bytes to `ClipImageEncoder`; if that library ignores EXIF orientation like ImageSharp does, a
      rotated phone photo gets a vector for the sideways picture and location matching suffers.
- [ ] **Face identities — run pending.** Identity assignment at detection (`FaceIdentityMatcher`,
      0.5 embedding cosine, one-to-one per run pair), the identity-based settled filter (now used by
      face grouping and the people review), and the migration pass that assigns identities to occurrences
      stored before identities existed are all in code; the build is green, nothing ran against live
      data yet. On the next worker start the gap check should queue one `MigrateFaceModels` job, which
      assigns identities and retires open proposals on settled identities; the review queue must not
      show re-detected copies of confirmed or rejected faces afterwards. The 0.5 matching cosine is
      as unmeasured as the score calibration below.
- [ ] **Cluster-based people review - run pending.** `ClusterFaces` (worker), migration
      `AddFaceClustering` (tables `FaceClusteringRuns`, `FaceClusters`, `IgnoredFaceGroups`, column
      `PhotoAnalysisCandidates.FaceClusterId`, legacy Rejected/Merged into one ignored group), the
      `people-review` API and the row-based UI are in code; build and lint are green, nothing ran live:
      the migration was not applied, the worker never grouped, the UI was never opened. Check: apply the
      migration (API start) and confirm the one local legacy rejection lands in an ignored group; the
      worker start queues the first `ClusterFaces`; rows appear in the Persons subsection; Submit
      (existing and new name) and Ignore with some faces unchecked (those land in Unsorted), Ignore and a 409 reload behave; no horizontal overflow on a phone.
- [ ] **Face-clustering threshold calibration.** `PersonJoinThreshold`, `ClusterThreshold` and
      `HintThreshold` are placeholders; measure same-person / different-person cosines on labelled faces
      and set them (together with `FaceAnalysis:Center`/`Scale`).
- [ ] **`FaceConfidenceCalibration` is unused.** The API still binds it and computes `confidence` for
      person candidates, but no person candidate reaches `PhotoAnalysisController`'s list any more.
      Remove it, or reuse it if people review ever shows a percent again.
- [ ] **Audit rows grow with every regrouping.** Each `ClusterFaces` run supersedes and rewrites every
      open person candidate, plus one `PhotoAnalysisRun` per touched photo and a set of `FaceClusters`.
      Fine at hundreds of faces; later either write only what moved or prune superseded clustering
      output (the candidate-to-cluster FK is `SET NULL` for that reason).
- [ ] **Anonymous row ids are not stable.** A row's `clusterId` is new on every run, so a name typed
      into an anonymous row is lost when a regrouping lands meanwhile (the row key changes). Match new
      clusters to old ones by membership if this bites.
- [ ] **ArcFace model on the home PC.** The worker container expects `w600k_r50.onnx` at
      `/models/arcface/` in the `models` volume (compose already sets
      `FaceAnalysis__RecognitionModelPath`); download instructions are in
      [`../infra/worker/README.md`](../infra/worker/README.md). Until then face analysis fails
      with a "model was not found" error listing the probed paths.
- [ ] **Phase 2 — face-quality calibration.** Validate the detector confidence and similarity
      behaviour on a small manually labelled archive subset before introducing acceptance
      thresholds or any automatic decision. First step after the ArcFace swap: measure the
      same-person and different-person cosine distributions on real data and set
      `FaceAnalysis:Center`/`Scale` from them (defaults are placeholders from typical ArcFace
      behaviour, not measurements).
- [ ] **Phase 3 — location-score calibration.** Review a manually labelled archive subset before
      interpreting cosine scores as a confidence or introducing any automatic location decision.
- [ ] **Phase 4 — scene-observation calibration.** Review a manually labelled archive subset to
      assess VLM evidence quality and decide whether any per-kind confidence guidance is useful.
- [ ] **Phase 5 — event-candidate review verification.** Use an archive subset with at least two
      related, reviewed photos to browser-test candidate evidence, membership adjustment and both
      Create/Attach review outcomes.
- [ ] **Phase 6 — narrative generation.** Revisit only after reviewed events are useful.
