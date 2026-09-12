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

Examples:
  generate-notes.ps1
  generate-notes.ps1 -Clusters python,books
  generate-notes.ps1 -Count 5 -OutDir C:\temp\quick-test
#>

param(
    [string]$OutDir,
    [string]$TopicsFile = "$PSScriptRoot\topics.json",
    [string[]]$Clusters,
    [int]$Count,
    [string]$OllamaUrl,
    [string]$Model
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..\..")
if (-not $OutDir) {
    $OutDir = Join-Path $repoRoot "test-data\text"
}

function Get-WorkerAiSettings {
    $appsettingsPath = Join-Path $repoRoot "backend\KnowledgeBase.Worker\appsettings.json"
    if (-not (Test-Path $appsettingsPath)) {
        return $null
    }
    $config = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
    return $config.Ai.Ollama
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

if (-not (Test-Path $TopicsFile)) {
    throw "Topics file not found: $TopicsFile"
}
$notes = Get-Content $TopicsFile -Raw | ConvertFrom-Json

if ($Clusters) {
    $notes = $notes | Where-Object { $_.cluster -in $Clusters }
}
if ($Count -gt 0) {
    $notes = $notes | Select-Object -First $Count
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
first-person, casual, specific, 250-400 words. Plain text only: no markdown
headers, no bullet asterisks, no code fences, just prose (a couple of short
paragraphs, plain hyphen lists are fine). Do not mention that you are an AI or
that this is a generated example.
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
