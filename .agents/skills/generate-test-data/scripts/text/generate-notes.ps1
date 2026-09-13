<#
Generates short English text notes via local Ollama, for testing the KnowledgeBase
AI pipeline (tag generation, tag merging, synthesis grouping).

Topics live in topics.json next to this script, grouped by "cluster" - files in the
same cluster share a theme so generated notes end up with overlapping/related tags
(useful for the synthesis threshold, which needs 2+ notes per tag) while phrasing of
the same subtopic varies across files (useful for tag-merge-suggestion testing).

Model/BaseUrl default to whatever backend/KnowledgeBase.Worker/appsettings.json
actually configures, so this script never silently drifts from what the worker uses
for real. Override with -Model/-OllamaUrl if needed (e.g. testing against a
different local model).

-Topics generates ad hoc instead of reading topics.json: each entry becomes its own
cluster, with -CountPerTopic files asking the model for a distinct angle each time -
use this for a one-off batch instead of hand-editing topics.json.

-Files re-rolls specific topics.json entries by their "file" name - useful for
redoing the odd note the model way overshot or undershot on, without a full rerun.

Examples:
  generate-notes.ps1
  generate-notes.ps1 -Clusters python,books
  generate-notes.ps1 -Count 5 -OutDir C:\temp\quick-test
  generate-notes.ps1 -Topics "home coffee brewing","urban beekeeping" -CountPerTopic 4
  generate-notes.ps1 -Files trip-iceland-road-trip.txt,coffee-latte-art-practice.txt
#>

param(
    [string]$OutDir,
    [string]$TopicsFile = "$PSScriptRoot\topics.json",
    [string[]]$Clusters,
    [int]$Count,
    [string[]]$Files,
    [string[]]$Topics,
    [int]$CountPerTopic = 5,
    [string]$OllamaUrl,
    [string]$Model
)

$ErrorActionPreference = "Stop"

function ConvertTo-FlatArray([string[]]$values) {
    # Invoked as `powershell -File ...` from a non-PowerShell shell (e.g. Bash), a
    # comma-separated value arrives as a single string instead of being split into
    # an array - PowerShell only does that splitting when its own parser sees the
    # command line. Split defensively so -Clusters/-Files/-Topics work either way.
    if (-not $values) { return $values }
    return @($values | ForEach-Object { $_ -split ',' } | Where-Object { $_ -ne '' })
}
$Clusters = ConvertTo-FlatArray $Clusters
$Files = ConvertTo-FlatArray $Files
$Topics = ConvertTo-FlatArray $Topics

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..\..")
if (-not $OutDir) {
    $OutDir = Join-Path $repoRoot "test-data\text"
}

function Get-WorkerAiSettings {
    $appsettingsPath = Join-Path $repoRoot "backend\KnowledgeBase.Worker\appsettings.json"
    if (-not (Test-Path $appsettingsPath)) {
        return $null
    }
    try {
        # appsettings.json can contain "//" comments, which Windows PowerShell's
        # strict ConvertFrom-Json rejects (unlike ASP.NET Core's own JSON reader).
        $config = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
        return $config.Ai.Ollama
    } catch {
        Write-Warning "Could not parse $appsettingsPath ($($_.Exception.Message)); using defaults"
        return $null
    }
}

if (-not $OllamaUrl -or -not $Model) {
    $workerAi = Get-WorkerAiSettings
    if (-not $OllamaUrl) {
        $OllamaUrl = if ($workerAi) { $workerAi.BaseUrl } else { "http://localhost:11434" }
    }
    if (-not $Model) {
        $Model = if ($workerAi) { $workerAi.Model } else { "qwen2.5vl:7b" }
    }
}
$generateUrl = "$OllamaUrl/api/generate"

function ConvertTo-Slug([string]$text) {
    $slug = $text.ToLowerInvariant() -replace "[^a-z0-9]+", "-"
    return $slug.Trim('-')
}

if ($Topics) {
    $notes = foreach ($topic in $Topics) {
        $slug = ConvertTo-Slug $topic
        for ($n = 1; $n -le $CountPerTopic; $n++) {
            [PSCustomObject]@{
                cluster = $slug
                file    = "$slug-$n.txt"
                topic   = "a personal note about $topic - this is entry $n of $CountPerTopic on this theme, take a distinct specific angle, example, or personal experience so it doesn't repeat the others"
            }
        }
    }
} else {
    if (-not (Test-Path $TopicsFile)) {
        throw "Topics file not found: $TopicsFile"
    }
    $notes = Get-Content $TopicsFile -Raw | ConvertFrom-Json

    if ($Clusters) {
        $notes = $notes | Where-Object { $_.cluster -in $Clusters }
    }
    if ($Files) {
        $notes = $notes | Where-Object { $_.file -in $Files }
    }
    if ($Count -gt 0) {
        $notes = $notes | Select-Object -First $Count
    }
}
if (-not $notes -or $notes.Count -eq 0) {
    throw "No topics matched (Clusters filter: $($Clusters -join ', '))"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Write-Host "Model: $Model  ($generateUrl)"
Write-Host "Writing $($notes.Count) notes to $OutDir"

$succeeded = 0
$failed = @()
$i = 0

foreach ($note in $notes) {
    $i++
    $path = Join-Path $OutDir $note.file
    Write-Host "[$i/$($notes.Count)] $($note.cluster)/$($note.file)"

    $prompt = @"
Write $($note.topic).

Write it as a real personal note a person keeps in their own knowledge base app -
first-person, casual, specific, long and detailed, around 1000-1300 words
(roughly 5000-7000 characters). Plain text only: no markdown headers, no bullet
asterisks, no code fences, just prose (several paragraphs, plain hyphen lists
are fine). Do not mention that you are an AI or that this is a generated
example.
"@

    $body = @{
        model   = $Model
        prompt  = $prompt
        stream  = $false
        options = @{ temperature = 0.7 }
    } | ConvertTo-Json -Depth 5

    try {
        $response = Invoke-RestMethod -Uri $generateUrl -Method Post -Body $body -ContentType "application/json"
        $text = $response.response.Trim()
        [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  -> $($text.Length) chars"
        $succeeded++
    } catch {
        Write-Warning "  failed: $($_.Exception.Message)"
        $failed += $note.file
    }
}

Write-Host ""
Write-Host "Done: $succeeded succeeded, $($failed.Count) failed."
if ($failed.Count -gt 0) {
    Write-Host "Failed: $($failed -join ', ')"
}
