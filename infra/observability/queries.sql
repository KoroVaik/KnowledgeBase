-- Request storm windows, including requests still waiting for a response.
select @Timestamp, SessionId, Source, Trigger, Route, Count, Completed, Failed, InFlight
from stream
where Event = 'http.storm'
order by @Timestamp desc
limit 100

-- Request totals by initiator. Use summary counters during sampling/overload.
select Source, Trigger, Route, sum(Count) as Requests, sum(Failed) as Failures, max(MaxDurationMs) as SlowestMs
from stream
where Event in ['http.summary', 'http.storm']
group by Source, Trigger, Route
order by Requests desc
limit 100

-- Replace the identifier to follow one upload across processes.
select @Timestamp, Service, Event, @Message, Source, Trigger, AssetId, JobId, TraceId
from stream
where UploadId = 'replace-with-upload-id'
order by @Timestamp
limit 1000

-- Repeated signed-link requests for the same photo.
select AssetId, Source, Trigger, count(*) as Requests
from stream
where Event = 'http.completed' and Route like '%/link'
group by AssetId, Source, Trigger
order by Requests desc
limit 100

-- Slow API requests, excluding long-lived SSE streams.
select @Timestamp, Route, DurationMs, TraceId, ClientRequestId
from stream
where Service = 'api' and Method is not null and IsEventStream = false and DurationMs > 1000
order by DurationMs desc
limit 100
