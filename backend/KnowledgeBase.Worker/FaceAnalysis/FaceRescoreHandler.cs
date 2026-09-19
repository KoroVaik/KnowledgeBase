using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Worker.FaceAnalysis;

// Nothing queues RescoreFaces any more, but rows from before clustering can still be waiting in
// the queue: they regroup the archive, which is what a re-score has become.
public sealed class FaceRescoreHandler(KnowledgeBaseDbContext database, IOptions<FaceClusteringOptions> options) : IPipelineHandler
{
    public JobKind Kind => JobKind.RescoreFaces;

    public bool RequiresContentAnalyzer => false;

    public Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken) =>
        new ClusterFacesHandler(database, options).HandleAsync(job, cancellationToken);
}
