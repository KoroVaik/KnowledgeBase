import type { AssetSummary } from '../../api/assets'
import type { RecognitionComparisonResult, RecognitionComparisonRun, RecognitionEvidence } from '../../api/faceRecognitionComparisons'
import { useCollapsibleSection } from '../../hooks/useCollapsibleSection'
import { FaceCropPreview } from '../FacePreview/FacePreview'
import { Select } from '../Select/Select'
import { useFaceRecognitionComparison } from './useFaceRecognitionComparison'
import './FaceRecognitionComparisonSection.css'

function percent(value: number) { return `${Math.round(value * 1000) / 10}%` }

function Metrics({ result }: { result: RecognitionComparisonResult }) {
  const precision = result.truePositives + result.falsePositives === 0 ? 0 : result.truePositives / (result.truePositives + result.falsePositives)
  const recall = result.truePositives + result.falseNegatives === 0 ? 0 : result.truePositives / (result.truePositives + result.falseNegatives)
  const f1 = precision + recall === 0 ? 0 : 2 * precision * recall / (precision + recall)
  return <dl className="recognition-metrics">
    <div><dt>F1</dt><dd>{percent(f1)}</dd></div>
    <div><dt>Precision</dt><dd>{percent(precision)}</dd></div>
    <div><dt>Recall</dt><dd>{percent(recall)}</dd></div>
    <div><dt>Threshold</dt><dd>{result.threshold?.toFixed(3) ?? '—'}</dd></div>
  </dl>
}

function RecognizerCard({ result, runStatus }: { result: RecognitionComparisonResult; runStatus: string }) {
  return <article className="recognition-model">
    <h4>{result.modelName}</h4>
    {result.error ? <p className="notes-error">This recognizer failed: {result.error}</p> : result.completedAtUtc === null
      ? <p role="status">{runStatus === 'Failed' ? 'Not processed because the run failed.' : 'Preparing recognizer…'}</p>
      : <>
        {result.threshold === null
          ? <p className="recognition-model-meta">Embeddings saved · no confirmed-face ground truth yet · {result.elapsedMilliseconds?.toFixed(0)} ms</p>
          : <><Metrics result={result} /><p className="recognition-model-meta">{result.samePersonPairs + result.differentPersonPairs} labelled pairs · {result.elapsedMilliseconds?.toFixed(0)} ms</p></>}
        <details><summary>Model settings</summary><pre>{JSON.stringify(result.configuration, null, 2)}</pre></details>
      </>}
  </article>
}

function EvidenceCard({ evidence, run, assetsById }: { evidence: RecognitionEvidence; run: RecognitionComparisonRun; assetsById: Map<string, AssetSummary> }) {
  const results = new Map(run.results.map(result => [result.id, result]))
  return <article className="recognition-evidence">
    <div className="recognition-evidence-faces">
      <FaceCropPreview asset={assetsById.get(evidence.first.assetId)} faceBounds={evidence.first.bounds} size={80} />
      <span aria-hidden="true">↔</span>
      <FaceCropPreview asset={assetsById.get(evidence.second.assetId)} faceBounds={evidence.second.bounds} size={80} />
    </div>
    <div>
      <strong>{evidence.isSamePerson ? 'Confirmed: same person' : 'Confirmed: different people'}</strong>
      <div className="recognition-evidence-scores">{evidence.scores.map(score => {
        const result = results.get(score.resultId)
        return <span key={score.resultId} className={score.isMatch === evidence.isSamePerson ? 'recognition-score-correct' : 'recognition-score-wrong'}>
          {result?.modelName ?? 'Model'} {score.score.toFixed(3)} · {score.isMatch ? 'match' : 'separate'}
        </span>
      })}</div>
    </div>
  </article>
}

function RecognitionComparisonContent({ assets }: { assets: AssetSummary[] }) {
  const review = useFaceRecognitionComparison()
  const assetsById = new Map(assets.map(asset => [asset.id, asset]))
  const run = review.run
  return <div className="recognition-comparison-content">
    <p>Run every recognizer for detected faces that do not have all three results yet. It uses the existing face detections and never changes person suggestions.</p>
    <div className="recognition-comparison-controls">
      <button type="button" className="btn btn-primary" disabled={review.busy || review.pending || !review.history?.pendingPhotoCount} onClick={() => void review.compare()}>Analyze {review.history?.pendingPhotoCount ?? 0} photos</button>
      <small>Each model is saved once per detected face. New face detections appear here automatically.</small>
    </div>
    {review.error && <p className="notes-error" role="alert">{review.error} <button type="button" className="btn btn-xs" onClick={review.reload}>Reload</button></p>}
    {review.history === null ? <p>Loading recognition comparison history…</p> : <>
      <div className="recognition-comparison-history">
        <Select value={review.runId} onChange={review.chooseRun} ariaLabel="Recognition comparison history" placeholder="No recognition comparisons yet"
          options={review.history.runs.map(item => ({ id: item.id, label: `${new Date(item.createdAtUtc).toLocaleString()} · ${item.photoCount} photos · ${item.referenceFaceCount} references · ${item.personCount} people · ${item.status}` }))} />
        {review.page > 0 && <button type="button" className="btn btn-xs" onClick={() => review.changePage(review.page - 1)}>Newer</button>}
        {review.history.hasMore && <button type="button" className="btn btn-xs" onClick={() => review.changePage(review.page + 1)}>Older</button>}
      </div>
      {review.history.runs.length === 0 && !review.runId && <p>No recognition analysis yet. Run it when there are detected faces to analyze.</p>}
    </>}
    {review.runId && !run && <p>Loading results…</p>}
    {run && <>
      <div className="recognition-run-head"><strong>{run.photoCount} photos {run.status === 'Done' ? 'analyzed' : 'selected'} · {run.referenceFaceCount} confirmed references · {run.personCount} people</strong><span>{new Date(run.createdAtUtc).toLocaleString()} · {run.status}</span></div>
      {run.error && <p className="notes-error">{run.error}</p>}
      <div className="recognition-models">{run.results.map(result => <RecognizerCard key={result.id} result={result} runStatus={run.status} />)}</div>
      {run.evidence.length > 0 && <div className="recognition-evidence-list">
        <h4>Disagreements and closest calls</h4>
        <p>Confirmed people label these pairs automatically. They explain the scores; no review is required.</p>
        {run.evidence.map(evidence => <EvidenceCard key={evidence.id} evidence={evidence} run={run} assetsById={assetsById} />)}
      </div>}
    </>}
  </div>
}

export function FaceRecognitionComparisonSection({ assets }: { assets: AssetSummary[] }) {
  const { collapsed, toggle } = useCollapsibleSection('photo-analysis:face-recognition-comparison')
  return <section className="subsection-panel photo-analysis-subsection face-recognition-comparison-section">
    <h3 className="tags-subhead"><button type="button" className="subsection-toggle" aria-expanded={!collapsed} onClick={toggle}><span className="section-toggle-caret" aria-hidden="true">▾</span>Face recognizer comparison</button></h3>
    {!collapsed && <RecognitionComparisonContent assets={assets} />}
  </section>
}
