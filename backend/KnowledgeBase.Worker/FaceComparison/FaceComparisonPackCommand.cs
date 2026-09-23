using System.Globalization;
using System.Text;
using System.Text.Json;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Worker.FaceAnalysis;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace KnowledgeBase.Worker.FaceComparison;

public static class FaceComparisonPackCommand
{
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff"];

    public static async Task RunAsync(string inputDirectory, string? outputDirectory)
    {
        var input = Path.GetFullPath(inputDirectory);
        if (!Directory.Exists(input)) throw new DirectoryNotFoundException($"Input directory was not found: {input}");

        var output = Path.GetFullPath(outputDirectory ?? Path.Combine(input, "results", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)));
        if (Directory.Exists(output)) throw new IOException($"Output directory already exists: {output}");
        Directory.CreateDirectory(output);

        var files = Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(Path.Combine(input, "results") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0) throw new InvalidOperationException("No supported image files were found.");

        var variants = Variants();
        var rows = new List<PackResult>();
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(input, file);
            try
            {
                using var image = Image.Load<Rgb24>(file);
                image.Mutate(context => context.AutoOrient());
                foreach (var variant in variants)
                {
                    try
                    {
                        var result = await variant.RunAsync(image, CancellationToken.None);
                        rows.Add(new PackResult(relativePath, image.Width, image.Height, variant.Name, result.ConfigurationJson,
                            result.ElapsedMilliseconds, result.Detections, null));
                    }
                    catch (Exception error)
                    {
                        rows.Add(new PackResult(relativePath, image.Width, image.Height, variant.Name, variant.ConfigurationJson,
                            null, [], error.Message));
                    }
                }
            }
            catch (Exception error)
            {
                foreach (var variant in variants)
                    rows.Add(new PackResult(relativePath, null, null, variant.Name, variant.ConfigurationJson, null, [], error.Message));
            }
        }

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        {
            createdAtUtc = DateTime.UtcNow,
            inputDirectory = input,
            fileCount = files.Length,
            variants = variants.Select(variant => new { variant.Name, configuration = JsonSerializer.Deserialize<JsonElement>(variant.ConfigurationJson) }),
            results = rows.Select(row => new
            {
                row.File, row.Width, row.Height, row.Variant, elapsedMilliseconds = row.ElapsedMilliseconds,
                configuration = JsonSerializer.Deserialize<JsonElement>(row.ConfigurationJson), row.Error,
                faces = row.Detections.Select(face => new { face.Bounds, face.Score, face.Landmarks,
                    warnings = row.Width is not null && row.Height is not null ? FaceComparisonQuality.Warnings(face, row.Width.Value, row.Height.Value) : [] }),
            }),
        }, jsonOptions));

        await File.WriteAllTextAsync(Path.Combine(output, "detections.csv"), DetectionsCsv(rows));
        await File.WriteAllTextAsync(Path.Combine(output, "summary.csv"), SummaryCsv(rows));
        await File.WriteAllTextAsync(Path.Combine(output, "report.html"), ReportHtml(input, output, rows));
        await File.WriteAllTextAsync(Path.Combine(output, "README.md"), Readme(input, files.Length, variants));
        Console.WriteLine($"Compared {files.Length} images with {variants.Count} configurations.");
        Console.WriteLine($"Results: {output}");
    }

    public static async Task RunReportAsync(string resultsPath)
    {
        var path = Path.GetFullPath(resultsPath);
        var output = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The results file has no parent directory.");
        var input = Directory.GetParent(output)?.Parent?.FullName
            ?? throw new InvalidOperationException("Could not infer the input directory from the results path.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var rows = document.RootElement.GetProperty("results").EnumerateArray().Select(result =>
        {
            var faces = result.GetProperty("faces").EnumerateArray().Select(face =>
            {
                var bounds = face.GetProperty("Bounds");
                return new ComparisonDetection(
                    new(bounds.GetProperty("X").GetSingle(), bounds.GetProperty("Y").GetSingle(),
                        bounds.GetProperty("Width").GetSingle(), bounds.GetProperty("Height").GetSingle()),
                    face.GetProperty("Score").GetDouble(),
                    face.GetProperty("Landmarks").EnumerateArray().Select(point => new ComparisonLandmark(
                        point.GetProperty("X").GetSingle(), point.GetProperty("Y").GetSingle())).ToArray());
            }).ToArray();
            var error = result.GetProperty("Error");
            return new PackResult(
                result.GetProperty("File").GetString()!, result.GetProperty("Width").GetInt32(), result.GetProperty("Height").GetInt32(),
                result.GetProperty("Variant").GetString()!, result.GetProperty("configuration").GetRawText(),
                result.GetProperty("elapsedMilliseconds").GetDouble(), faces,
                error.ValueKind is JsonValueKind.Null ? null : error.GetString());
        }).ToArray();
        await File.WriteAllTextAsync(Path.Combine(output, "report.html"), ReportHtml(input, output, rows));
        Console.WriteLine($"Report updated: {Path.Combine(output, "report.html")}");
    }

    private static List<Variant> Variants() =>
    [
        Variant.Create("yolov5s-face_current", "yolov5s-face", new FaceComparisonOptions(), new FaceAnalysisOptions()),
        Variant.Create("yolov5s-face_strict", "yolov5s-face", new FaceComparisonOptions(), new FaceAnalysisOptions
        {
            DetectionThreshold = 0.5f, ConfidenceThreshold = 0.9f, NonMaximumSuppressionThreshold = 0.3f,
        }),
        Variant.Create("scrfd-10g_0.7", "scrfd-10g", new FaceComparisonOptions { ScrfdThreshold = 0.7f }, new FaceAnalysisOptions()),
        Variant.Create("scrfd-10g_0.5", "scrfd-10g", new FaceComparisonOptions { ScrfdThreshold = 0.5f }, new FaceAnalysisOptions()),
        Variant.Create("scrfd-10g_0.4", "scrfd-10g", new FaceComparisonOptions { ScrfdThreshold = 0.4f }, new FaceAnalysisOptions()),
        Variant.Create("yunet_0.9", "yunet", new FaceComparisonOptions { YuNetThreshold = 0.9f }, new FaceAnalysisOptions()),
        Variant.Create("yunet_0.8", "yunet", new FaceComparisonOptions { YuNetThreshold = 0.8f }, new FaceAnalysisOptions()),
        Variant.Create("yunet_0.7", "yunet", new FaceComparisonOptions { YuNetThreshold = 0.7f }, new FaceAnalysisOptions()),
        Variant.Create("yunet_0.5", "yunet", new FaceComparisonOptions { YuNetThreshold = 0.5f }, new FaceAnalysisOptions()),
    ];

    private static string DetectionsCsv(IEnumerable<PackResult> rows)
    {
        var csv = new StringBuilder("file,width,height,variant,elapsed_ms,detection_count,ordinal,x,y,box_width,box_height,score,warnings,error\n");
        foreach (var row in rows)
        {
            if (row.Detections.Count == 0)
            {
                Append(csv, row.File, row.Width, row.Height, row.Variant, row.ElapsedMilliseconds, 0, null, null, null, null, null, null, null, row.Error);
                continue;
            }

            for (var ordinal = 0; ordinal < row.Detections.Count; ordinal++)
            {
                var face = row.Detections[ordinal];
                var warnings = row.Width is not null && row.Height is not null
                    ? string.Join("; ", FaceComparisonQuality.Warnings(face, row.Width.Value, row.Height.Value)) : null;
                Append(csv, row.File, row.Width, row.Height, row.Variant, row.ElapsedMilliseconds, row.Detections.Count, ordinal + 1,
                    face.Bounds.X, face.Bounds.Y, face.Bounds.Width, face.Bounds.Height, face.Score, warnings, row.Error);
            }
        }
        return csv.ToString();
    }

    private static string SummaryCsv(IEnumerable<PackResult> rows)
    {
        var csv = new StringBuilder("variant,files,successful_files,failed_files,files_with_detections,total_detections,median_elapsed_ms\n");
        foreach (var group in rows.GroupBy(row => row.Variant))
        {
            var times = group.Where(row => row.ElapsedMilliseconds is not null).Select(row => row.ElapsedMilliseconds!.Value).Order().ToArray();
            double? median = times.Length == 0 ? null : times[times.Length / 2];
            Append(csv, group.Key, group.Count(), group.Count(row => row.Error is null), group.Count(row => row.Error is not null),
                group.Count(row => row.Detections.Count > 0), group.Sum(row => row.Detections.Count), median);
        }
        return csv.ToString();
    }

    private static void Append(StringBuilder csv, params object?[] values) =>
        csv.AppendLine(string.Join(',', values.Select(value => Csv(value))));

    private static string Csv(object? value)
    {
        if (value is null) return string.Empty;
        var text = value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString()!;
        return '"' + text.Replace("\"", "\"\"") + '"';
    }

    private static string Readme(string input, int fileCount, IReadOnlyList<Variant> variants) => $"""
        # Face detector pack comparison

        Input: `{input}`
        Files: {fileCount}
        Configurations: {string.Join(", ", variants.Select(variant => variant.Name))}

        `results.json` keeps complete boxes and five landmarks. `detections.csv` has one row per
        detection; an empty detection row means the model found no face or failed for that file.
        `summary.csv` aggregates counts and the median of each model's per-image median time.
        Open `report.html` locally to review the three candidate configurations side by side:
        current YOLO, SCRFD 0.4 and YuNet 0.7. Each detection has Approve/Reject controls;
        Approve all confirms an exact match and zero missed faces. The report calculates precision,
        recall and F1 after every photo is reviewed. It stores labels in the browser and can
        export/import them as JSON; it does not contact a server.

        Scores are model-specific and must not be compared as a shared confidence percentage.
        This command reads files directly and does not start the worker host, access Garage/Postgres,
        enqueue jobs, or create photo/archive records.
        """;

    private sealed record PackResult(string File, int? Width, int? Height, string Variant, string ConfigurationJson,
        double? ElapsedMilliseconds, IReadOnlyList<ComparisonDetection> Detections, string? Error);

    private static string ReportHtml(string input, string output, IReadOnlyList<PackResult> rows)
    {
        var selected = new[]
        {
            new { Name = "yolov5s-face_current", Label = "YOLOv5s-face · current" },
            new { Name = "scrfd-10g_0.4", Label = "SCRFD-10GF · threshold 0.4" },
            new { Name = "yunet_0.7", Label = "YuNet · threshold 0.7" },
        };
        var data = rows.GroupBy(row => row.File).Select(group => new
        {
            file = group.Key,
            source = Path.GetRelativePath(output, Path.Combine(input, group.Key)).Replace('\\', '/'),
            width = group.First().Width,
            height = group.First().Height,
            variants = selected.Select(selection =>
            {
                var row = group.Single(row => row.Variant == selection.Name);
                return new
                {
                    name = row.Variant,
                    label = selection.Label,
                    error = row.Error,
                    faces = row.Detections.Select(face => new { x = face.Bounds.X, y = face.Bounds.Y, width = face.Bounds.Width, height = face.Bounds.Height, score = face.Score }),
                };
            }),
        });
        var json = JsonSerializer.Serialize(data).Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
        return $$$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Face detector comparison</title>
            <style>
            :root { color-scheme: dark; font-family: system-ui, sans-serif; background:#111827; color:#e5e7eb; }
            body { margin:0; padding:14px; max-width:1250px; margin:auto; font-size:13px; } h1 { margin:0 0 4px; font-size:22px; } p { color:#aab3c4; margin:5px 0; }
            .controls,.actions,.navigation { display:flex; flex-wrap:wrap; gap:7px; align-items:center; margin:12px 0; } select,button,input { font:inherit; padding:4px 6px; border-radius:5px; border:1px solid #43506a; background:#1f2937; color:inherit; } button { cursor:pointer; } button:hover { background:#334155; } button:disabled { opacity:.45; cursor:default; }
            #stage { position:relative; line-height:0; width:100%; background:#020617; border:1px solid #43506a; border-radius:6px; overflow:hidden; } .photo { display:block; width:100%; max-height:57vh; object-fit:contain; } .overlay { position:absolute; inset:0; width:100%; height:100%; } .prediction { fill:none; stroke:#f8fafc; stroke-width:2; cursor:pointer; } .prediction.highlight { stroke:#22c55e; stroke-width:4; fill:#22c55e30; } .group-label { fill:#f8fafc; font-size:15px; font-weight:800; paint-order:stroke; stroke:#020617; stroke-width:3px; cursor:pointer; }
            .group-grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(165px,1fr)); gap:7px; margin-top:8px; } .group { display:grid; grid-template-columns:58px 1fr; gap:7px; border:1px solid #43506a; border-radius:6px; padding:6px; background:#172033; } .group.highlight { border-color:#22c55e; box-shadow:0 0 0 2px #22c55e55; } .group.approved { border-color:#15803d; } .group.rejected { border-color:#be123c; } .group.partial { border-color:#d97706; } .crop { width:58px; height:58px; border-radius:4px; background:#020617; } .group-title { font-weight:700; } .chips { display:flex; flex-wrap:wrap; gap:3px; margin:4px 0; } .chip { border-radius:4px; padding:1px 4px; font-size:11px; color:#020617; font-weight:700; } .model-0 { background:#38bdf8; } .model-1 { background:#c084fc; } .model-2 { background:#fbbf24; } .approve { border-color:#15803d; } .reject { border-color:#be123c; } .mark-partial { border-color:#d97706; }
            .review-summary { color:#93c5fd; } .storage-warning { color:#facc15; } .reviewed-section { margin:8px 0; color:#aab3c4; } .reviewed-section summary { cursor:pointer; } .reviewed-section label { display:inline-block; margin:7px 0 0; } .conclusion { margin:14px 0 8px; padding:10px 12px; border:1px solid #2563eb; border-radius:6px; background:#172554; line-height:1.5; } .conclusion strong { color:#bfdbfe; } .navigation { justify-content:space-between; } .navigation button:nth-child(2) { border-color:#22c55e; } table { width:100%; border-collapse:collapse; margin-top:14px; font-size:12px; } th,td { border-bottom:1px solid #374151; padding:5px; text-align:right; } th:first-child,td:first-child { text-align:left; }
            </style></head><body>
            <h1>Face detector comparison</h1>
            <p>One box means one candidate face. The crop card below shows which models found it. Approving a group scores all participating models automatically.</p>
            <div class="controls"><label>To review <select id="photoSelect"></select></label><button id="skipInvalid">Skip invalid photo</button><button id="clear">Clear this photo</button><button id="export">Export labels</button><label>Import labels <input id="import" type="file" accept="application/json"></label><span id="storageWarning" class="storage-warning"></span></div>
            <details class="reviewed-section"><summary>Reviewed photos (<span id="reviewedCount">0</span>)</summary><label>Photo <select id="reviewedSelect"></select></label></details>
            <div id="stage"></div><div class="actions"><button id="approveAgreed">Approve agreed groups</button><button id="approveAllGroups">Approve all candidates</button><button id="noMissed">No missed</button><button id="missed">+1 Missed</button><button id="toggleGroups">Show all groups</button><span id="reviewSummary" class="review-summary"></span></div><div id="groups" class="group-grid"></div><div class="navigation"><button id="previous">← Previous</button><button id="next">Next →</button></div>
            <section id="conclusion" class="conclusion"></section><table><thead><tr><th>Configuration</th><th>Reviewed photos</th><th>Detected</th><th>Usable</th><th>Partial</th><th>Rejected</th><th>Missed</th><th>Detection F1</th><th>Usable rate</th></tr></thead><tbody id="metrics"></tbody></table>
            <script id="data" type="application/json">{{{json}}}</script><script>
            const data=JSON.parse(document.querySelector('#data').textContent), key='face-comparison-group-reviews-v4:'+location.pathname;
            const photoSelect=document.querySelector('#photoSelect'), reviewedSelect=document.querySelector('#reviewedSelect'), reviewedCount=document.querySelector('#reviewedCount'), stage=document.querySelector('#stage'), groupsElement=document.querySelector('#groups'), metrics=document.querySelector('#metrics'), conclusion=document.querySelector('#conclusion'), previous=document.querySelector('#previous'), next=document.querySelector('#next'), approveAgreed=document.querySelector('#approveAgreed'), approveAllGroups=document.querySelector('#approveAllGroups'), noMissed=document.querySelector('#noMissed'), missed=document.querySelector('#missed'), toggleGroups=document.querySelector('#toggleGroups'), reviewSummary=document.querySelector('#reviewSummary'), storageWarning=document.querySelector('#storageWarning'), skipInvalid=document.querySelector('#skipInvalid'); let labels={reviews:{}},showAll=false,currentFile=data[0]?.file,view='pending';
            try { labels=JSON.parse(localStorage.getItem(key)||'{"reviews":{}}') } catch { storageWarning.textContent='Browser storage is unavailable; export labels before refreshing.' } if(!labels.reviews) labels={reviews:{}};
            const current=()=>data.find(image=>image.file===currentFile); function save(){try{localStorage.setItem(key,JSON.stringify(labels))}catch{storageWarning.textContent='Browser storage is unavailable; export labels before refreshing.'}}
            function review(image){return labels.reviews[image.file]??={groups:{},missed:null}} function status(value){return value===true?'approved':value===false?'rejected':value==='partial'?'partial':''} function percent(n,d){return d?`${(100*n/d).toFixed(1)}%`:'—'}
            function completed(image){const entry=review(image),found=groupedFaces(image);return entry.skipped===true||(found.every(group=>entry.groups[group.id]!==undefined)&&entry.missed!==null)} function activeImages(){return data.filter(image=>view==='pending'?!completed(image):completed(image))} function populate(select,images,empty){select.innerHTML='';if(!images.length)select.add(new Option(empty,''));else images.forEach(image=>select.add(new Option(image.file,image.file)))} function refreshPhotoSelectors(){const pending=data.filter(image=>!completed(image)),reviewed=data.filter(completed);if(view==='pending'&&!pending.some(image=>image.file===currentFile)){if(pending.length)currentFile=pending[0].file;else if(reviewed.length){currentFile=reviewed[0].file;view='reviewed'}}if(view==='reviewed'&&!reviewed.some(image=>image.file===currentFile)){if(pending.length){currentFile=pending[0].file;view='pending'}else if(reviewed.length)currentFile=reviewed[0].file}populate(photoSelect,pending,'All photos are reviewed');populate(reviewedSelect,reviewed,'No reviewed photos');photoSelect.disabled=!pending.length;reviewedSelect.disabled=!reviewed.length;photoSelect.value=view==='pending'?currentFile:'';reviewedSelect.value=view==='reviewed'?currentFile:'';reviewedCount.textContent=reviewed.length}
            function modelStats(){const eligible=data.filter(image=>!review(image).skipped).length;return data[0].variants.map((sample,index)=>{let usable=0,partial=0,rejected=0,missedCount=0,reviewed=0;data.forEach(image=>{const entry=review(image),found=groupedFaces(image);if(!completed(image)||entry.skipped)return;reviewed++;found.forEach(group=>{const copies=group.items.filter(item=>item.variantIndex===index).length,decision=entry.groups[group.id];if(decision===true||decision==='partial'){if(copies){if(decision===true)usable++;else partial++;rejected+=copies-1}else missedCount++}else rejected+=copies});missedCount+=entry.missed});const detected=usable+partial,precision=detected+rejected?detected/(detected+rejected):0,recall=detected+missedCount?detected/(detected+missedCount):0;return {sample,usable,partial,rejected,missedCount,reviewed,eligible,detected,f1:precision+recall?2*precision*recall/(precision+recall):0,usableRate:detected?usable/detected:0}})}
            function renderMetrics(){const stats=modelStats(),complete=stats.every(result=>result.reviewed===result.eligible),skipped=data.length-(stats[0]?.eligible??data.length);metrics.innerHTML=stats.map(result=>`<tr><td>${result.sample.label}</td><td>${result.reviewed}/${result.eligible}</td><td>${result.detected}</td><td>${result.usable}</td><td>${result.partial}</td><td>${result.rejected}</td><td>${result.missedCount}</td><td>${complete?`${(100*result.f1).toFixed(1)}%`:'—'}</td><td>${complete?percent(result.usable,result.detected):'—'}</td></tr>`).join('');if(!complete){conclusion.innerHTML=`<strong>Висновок ще не готовий.</strong> Заверши ${stats[0]?.eligible-stats[0]?.reviewed??0} фото; пропущено як невалідні: ${skipped}.`}else{const winner=[...stats].sort((a,b)=>b.f1-a.f1||b.usableRate-a.usableRate||a.missedCount-b.missedCount)[0];conclusion.innerHTML=`<strong>Рекомендація: ${winner.sample.label}.</strong> Найкращий Detection F1: ${(winner.f1*100).toFixed(1)}%. Виявлено ${winner.detected} облич, з них ${winner.usable} придатні для розпізнавання; пропущено ${winner.missedCount}, хибних кандидатів ${winner.rejected}. Пропущено як невалідні: ${skipped}.`}}
            function iou(a,b){const width=Math.max(0,Math.min(a.x+a.width,b.x+b.width)-Math.max(a.x,b.x)),height=Math.max(0,Math.min(a.y+a.height,b.y+b.height)-Math.max(a.y,b.y)),intersection=width*height;return intersection/(a.width*a.height+b.width*b.height-intersection)||0}
            function groupedFaces(image){const groups=[];image.variants.forEach((variant,variantIndex)=>variant.faces.forEach((face,faceIndex)=>{const item={face,faceIndex,variantIndex};const group=groups.find(candidate=>candidate.items.some(other=>iou(face,other.face)>=.4));if(group)group.items.push(item);else groups.push({items:[item]})}));return groups.sort((left,right)=>groupBox(left).y-groupBox(right).y||groupBox(left).x-groupBox(right).x).map((group,id)=>({...group,id}))}
            function groupBox(group){const left=Math.min(...group.items.map(item=>item.face.x)),top=Math.min(...group.items.map(item=>item.face.y)),right=Math.max(...group.items.map(item=>item.face.x+item.face.width)),bottom=Math.max(...group.items.map(item=>item.face.y+item.face.height));return{x:left,y:top,width:right-left,height:bottom-top}}
            function box(group){const face=groupBox(group);return `<rect class="prediction" data-face-group="${group.id}" x="${face.x}" y="${face.y}" width="${face.width}" height="${face.height}"></rect><text class="group-label" data-face-group="${group.id}" x="${face.x+3}" y="${Math.max(15,face.y-4)}">${group.id+1}</text>`}
            function agreed(group){return new Set(group.items.map(item=>item.variantIndex)).size===3&&group.items.length===3} function crop(group,image){const box=groupBox(group),pad=Math.max(12,Math.max(box.width,box.height)*.3),x=Math.max(0,box.x-pad),y=Math.max(0,box.y-pad),width=Math.min(image.width-x,box.width+pad*2),height=Math.min(image.height-y,box.height+pad*2);return `<svg class="crop" viewBox="${x} ${y} ${width} ${height}"><image href="${image.source}" width="${image.width}" height="${image.height}"></image></svg>`}
            function groupCard(group,image){const entry=review(image),decision=entry.groups[group.id],chips=group.items.map(item=>`<span class="chip model-${item.variantIndex}">${['Y','S','U'][item.variantIndex]} #${item.faceIndex+1}</span>`).join(''),caption=decision===true?'· Usable':decision==='partial'?'· Partial / excluded':decision===false?'· Rejected':'';return `<article class="group ${status(decision)}" data-face-group="${group.id}">${crop(group,image)}<div><div class="group-title">Face ${group.id+1} ${caption}</div><div class="chips">${chips}</div><button class="approve" data-group="${group.id}" data-decision="approve">Approve</button><button class="mark-partial" data-group="${group.id}" data-decision="partial">Partial</button><button class="reject" data-group="${group.id}" data-decision="reject">Reject</button></div></article>`}
            function render(){refreshPhotoSelectors();const image=current(),entry=review(image),found=groupedFaces(image),visible=showAll?found:found.filter(group=>!agreed(group)),active=activeImages(),position=active.findIndex(candidate=>candidate.file===image.file);stage.innerHTML=`<img class="photo" src="${image.source}" alt="${image.file}"><svg class="overlay" viewBox="0 0 ${image.width} ${image.height}">${found.map(box).join('')}</svg>`;groupsElement.innerHTML=visible.length?visible.map(group=>groupCard(group,image)).join(''):'<p>All candidate groups are found by every model. Use “Approve agreed groups” after checking the photo.</p>';reviewSummary.textContent=entry.skipped?'Skipped: invalid photo':`${found.length} groups · ${found.filter(group=>!agreed(group)).length} need attention · missed: ${entry.missed??'not reviewed'}`;toggleGroups.textContent=showAll?'Show only disagreements':'Show all groups';skipInvalid.disabled=view==='reviewed';previous.disabled=position<=0;next.disabled=position<0||position===active.length-1;renderMetrics()}
            groupsElement.onclick=event=>{const button=event.target.closest('button[data-group]');if(!button)return;review(current()).groups[button.dataset.group]=button.dataset.decision==='approve'?true:button.dataset.decision==='partial'?'partial':false;save();render()};
            function highlight(groupId){document.querySelectorAll(`[data-face-group="${groupId}"]`).forEach(element=>element.classList.add('highlight'))} function clearHighlight(){document.querySelectorAll('.highlight').forEach(element=>element.classList.remove('highlight'))} function hoverTarget(event){return event.target.closest('[data-face-group]')?.dataset.faceGroup}
            function hoverIn(event){const groupId=hoverTarget(event);if(groupId!==undefined){clearHighlight();highlight(groupId)}} function hoverOut(event){const groupId=hoverTarget(event),next=event.relatedTarget?.closest?.('[data-face-group]')?.dataset.faceGroup;if(groupId!==next)clearHighlight()}
            groupsElement.onmouseover=hoverIn;groupsElement.onmouseout=hoverOut;stage.onmouseover=hoverIn;stage.onmouseout=hoverOut;
            photoSelect.onchange=()=>{view='pending';currentFile=photoSelect.value;render()};reviewedSelect.onchange=()=>{view='reviewed';currentFile=reviewedSelect.value;render()};skipInvalid.onclick=()=>{review(current()).skipped=true;save();render()};document.querySelector('#clear').onclick=()=>{delete labels.reviews[current().file];save();render()};
            approveAgreed.onclick=()=>{const image=current(),entry=review(image);groupedFaces(image).filter(agreed).forEach(group=>entry.groups[group.id]=true);save();render()};approveAllGroups.onclick=()=>{const image=current(),entry=review(image);groupedFaces(image).forEach(group=>entry.groups[group.id]=true);entry.missed=0;save();render()};noMissed.onclick=()=>{review(current()).missed=0;save();render()};missed.onclick=()=>{const entry=review(current());entry.missed=(entry.missed??0)+1;save();render()};toggleGroups.onclick=()=>{showAll=!showAll;render()};
            previous.onclick=()=>{const active=activeImages(),index=active.findIndex(image=>image.file===currentFile);currentFile=active[Math.max(0,index-1)].file;render()};next.onclick=()=>{const active=activeImages(),index=active.findIndex(image=>image.file===currentFile);currentFile=active[Math.min(active.length-1,index+1)].file;render()};
            document.querySelector('#export').onclick=()=>{const blob=new Blob([JSON.stringify(labels,null,2)],{type:'application/json'}),link=Object.assign(document.createElement('a'),{href:URL.createObjectURL(blob),download:'face-detector-reviews.json'});link.click();URL.revokeObjectURL(link.href)};
            document.querySelector('#import').onchange=event=>{const file=event.target.files[0];if(!file)return;const reader=new FileReader;reader.onload=()=>{const next=JSON.parse(reader.result);if(!next.reviews)throw Error('Invalid review file');labels=next;save();render()};reader.readAsText(file)};render();
            </script></body></html>
            """;
    }

    private sealed class Variant(string name, string configurationJson, Func<Image<Rgb24>, CancellationToken, Task<DetectorComparisonOutput>> runAsync)
    {
        public string Name { get; } = name;
        public string ConfigurationJson { get; } = configurationJson;
        public Task<DetectorComparisonOutput> RunAsync(Image<Rgb24> image, CancellationToken cancellationToken) => runAsync(image, cancellationToken);

        public static Variant Create(string name, string modelId, FaceComparisonOptions comparisonOptions, FaceAnalysisOptions faceOptions)
        {
            var runner = new FaceComparisonRunner(new ComparisonModelFiles(Options.Create(comparisonOptions)), Options.Create(comparisonOptions), Options.Create(faceOptions));
            var config = JsonSerializer.Serialize(new { modelId, comparisonOptions.ScrfdThreshold, comparisonOptions.YuNetThreshold,
                comparisonOptions.NmsThreshold, faceOptions.DetectionThreshold, faceOptions.ConfidenceThreshold, faceOptions.NonMaximumSuppressionThreshold });
            return new Variant(name, config, (image, cancellationToken) => runner.RunAsync(modelId, image, cancellationToken));
        }
    }
}
