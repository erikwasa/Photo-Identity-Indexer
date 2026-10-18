using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PhotoIdentity.PhotoQualityEvaluation;

internal static class PhotoQualityBenchmarkReviewCommand
{
    private const string ProxyRootEnvironmentVariable = "PhotoIdentity__ReviewProxyRoot";

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Any(arg => arg is "-h" or "--help"))
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            Options options = Parse(args);
            string proxyRoot = string.IsNullOrWhiteSpace(options.ProxyRoot)
                ? Environment.GetEnvironmentVariable(ProxyRootEnvironmentVariable)
                    ?? throw new InvalidOperationException(
                        $"Set {ProxyRootEnvironmentVariable} or pass --proxy-root <path>.")
                : options.ProxyRoot;

            PhotoQualityBenchmarkReviewStore store =
                await PhotoQualityBenchmarkReviewStore.OpenAsync(
                    options.InputPath,
                    proxyRoot);

            string token = Convert.ToHexString(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))
                .ToLowerInvariant();
            string url = $"http://127.0.0.1:{options.Port}";
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls(url);
            WebApplication app = builder.Build();

            app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run(async context =>
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "The review request could not be completed.",
                    });
                });
            });

            app.MapGet(
                "/",
                () => Results.Content(
                    ReviewerHtml.Replace("__TOKEN__", token, StringComparison.Ordinal),
                    "text/html; charset=utf-8"));

            app.MapGet(
                "/api/state",
                (HttpRequest request) =>
                {
                    RequireToken(request, token, queryAllowed: false);
                    int index = 0;
                    if (request.Query.TryGetValue("index", out var values) &&
                        !int.TryParse(values.FirstOrDefault(), out index))
                    {
                        return Results.BadRequest(new { error = "index must be an integer." });
                    }

                    return Results.Json(store.GetState(index));
                });

            app.MapGet(
                "/api/image/{revisionId}",
                async (string revisionId, HttpRequest request) =>
                {
                    RequireToken(request, token, queryAllowed: true);
                    byte[] content = await store.ReadProxyAsync(revisionId);
                    return Results.Bytes(content, "image/jpeg");
                });

            app.MapPost(
                "/api/review/{revisionId}",
                async (string revisionId, HttpRequest request) =>
                {
                    RequireToken(request, token, queryAllowed: false);
                    PhotoQualityBlindReviewUpdate? update =
                        await request.ReadFromJsonAsync<PhotoQualityBlindReviewUpdate>();
                    if (update is null)
                    {
                        return Results.BadRequest(new { error = "Review body is required." });
                    }

                    PhotoQualityBlindReviewState state =
                        await store.SaveReviewAsync(revisionId, update);
                    return Results.Json(state);
                });

            app.MapPost(
                "/api/clear/{revisionId}",
                async (string revisionId, HttpRequest request) =>
                {
                    RequireToken(request, token, queryAllowed: false);
                    PhotoQualityBlindReviewState state =
                        await store.ClearReviewAsync(revisionId);
                    return Results.Json(state);
                });

            app.Lifetime.ApplicationStarted.Register(() =>
            {
                Console.WriteLine($"Blinded photo-quality reviewer: {url}");
                Console.WriteLine("Press Ctrl+C in this terminal when review is finished.");
                if (!options.NoOpen)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true,
                        });
                    }
                    catch
                    {
                        Console.WriteLine(
                            "Could not open the browser automatically; open the URL above.");
                    }
                }
            });

            await app.RunAsync();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            PrintUsage();
            return 1;
        }
    }

    private static void RequireToken(
        HttpRequest request,
        string token,
        bool queryAllowed)
    {
        string? supplied =
            request.Headers["X-PhotoIdentity-Review"].FirstOrDefault();
        if (queryAllowed && string.IsNullOrWhiteSpace(supplied))
        {
            supplied = request.Query["token"].FirstOrDefault();
        }

        if (!string.Equals(supplied, token, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "Invalid local review session token.");
        }
    }

    private static Options Parse(string[] args)
    {
        string input = string.Empty;
        string? proxyRoot = null;
        int port = 5182;
        bool noOpen = false;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--input":
                    input = NextValue(args, ref index, "--input");
                    break;
                case "--proxy-root":
                    proxyRoot = NextValue(args, ref index, "--proxy-root");
                    break;
                case "--port":
                    if (!int.TryParse(
                            NextValue(args, ref index, "--port"),
                            out port) ||
                        port is < 1024 or > 65535)
                    {
                        throw new ArgumentException(
                            "--port must be between 1024 and 65535.");
                    }
                    break;
                case "--no-open":
                    noOpen = true;
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown review argument: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("--input is required.");
        }

        return new Options(
            Path.GetFullPath(input),
            proxyRoot,
            port,
            noOpen);
    }

    private static string NextValue(
        string[] args,
        ref int index,
        string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index];
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark review --input private/photo-quality-benchmark.json [--proxy-root <path>] [--port 5182] [--no-open]");
    }

    private sealed record Options(
        string InputPath,
        string? ProxyRoot,
        int Port,
        bool NoOpen);

    private const string ReviewerHtml = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Photo Identity — blinded quality review</title>
<style>
:root { color-scheme: dark; font-family: system-ui, sans-serif; }
body { margin:0; background:#111; color:#eee; }
main { max-width:1200px; margin:auto; padding:16px; }
header { display:flex; gap:16px; align-items:center; justify-content:space-between; flex-wrap:wrap; }
#stage { min-height:58vh; display:flex; align-items:center; justify-content:center; background:#080808; border-radius:12px; overflow:hidden; margin:12px 0; }
#photo { max-width:100%; max-height:68vh; object-fit:contain; }
.controls { display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:8px; }
button { padding:12px; font-size:16px; border-radius:8px; border:1px solid #555; background:#222; color:#eee; cursor:pointer; }
button.selected { outline:3px solid #ddd; }
.nav { display:flex; gap:8px; margin-top:10px; flex-wrap:wrap; }
textarea { width:100%; min-height:70px; box-sizing:border-box; margin-top:10px; background:#181818; color:#eee; border:1px solid #555; border-radius:8px; padding:8px; }
.small { color:#bbb; font-size:13px; }
#status { font-variant-numeric:tabular-nums; }
label { display:flex; gap:8px; align-items:center; margin-top:10px; }
@media (max-width:700px){ .controls{grid-template-columns:repeat(2,minmax(0,1fr));} }
</style>
</head>
<body>
<main>
<header>
  <div>
    <strong>Blinded photo-quality review</strong>
    <div class="small">No source path, stratum, partition, model score, candidate reason, or representative/challenge membership is shown.</div>
  </div>
  <div id="status">Loading…</div>
</header>
<div id="stage"><img id="photo" alt="Review proxy"></div>
<div class="controls" id="levels">
  <button data-level="very-low">1 · Very Low</button>
  <button data-level="low">2 · Low</button>
  <button data-level="normal">3 · Normal</button>
  <button data-level="high">4 · High</button>
  <button data-level="very-high">5 · Very High</button>
  <button data-level="unsure">6 · Unsure</button>
</div>
<label><input id="important" type="checkbox"> Personally important (separate from quality)</label>
<textarea id="notes" maxlength="2000" placeholder="Optional note. Ctrl+Enter saves the current label with updated note."></textarea>
<div class="nav">
  <button id="prev">← Previous</button>
  <button id="next">Next →</button>
  <button id="next-unreviewed">Next unreviewed</button>
  <button id="clear">Clear review</button>
</div>
<p class="small">
Very Low = technically/presentationally poor enough that you normally would not want it selected automatically.
Low = clearly inferior but potentially usable.
Normal = ordinary acceptable archive photo.
High = clearly better than normal.
Very High = exceptional presentation quality.
Unsure = cannot confidently place it.
Judge the photo itself, not the people/event or personal importance.
</p>
</main>
<script>
const token="__TOKEN__";
const apiHeaders={"X-PhotoIdentity-Review":token,"Content-Type":"application/json"};
let state=null;
const photo=document.getElementById("photo");
const notes=document.getElementById("notes");
const important=document.getElementById("important");
const status=document.getElementById("status");

async function load(index){
  const r=await fetch("/api/state?index="+index,{headers:{"X-PhotoIdentity-Review":token}});
  if(!r.ok) throw new Error(await r.text());
  state=await r.json();
  photo.src="/api/image/"+encodeURIComponent(state.revisionId)+"?token="+encodeURIComponent(token)+"&v="+Date.now();
  notes.value=state.notes||"";
  important.checked=state.personallyImportant===true;
  document.querySelectorAll("[data-level]").forEach(function(b){
    b.classList.toggle("selected",b.dataset.level===state.qualityLevel);
  });
  status.textContent=(state.index+1)+"/"+state.total+" · reviewed "+state.reviewed+" · remaining "+state.remaining;
}

async function save(level,advance){
  if(advance===undefined) advance=true;
  const r=await fetch("/api/review/"+encodeURIComponent(state.revisionId),{
    method:"POST",
    headers:apiHeaders,
    body:JSON.stringify({qualityLevel:level,personallyImportant:important.checked,notes:notes.value})
  });
  if(!r.ok) throw new Error(await r.text());
  const saved=await r.json();
  if(advance && saved.remaining>0){
    await load(saved.nextUnreviewedIndex===null ? Math.min(saved.index+1,saved.total-1) : saved.nextUnreviewedIndex);
  }else{
    await load(saved.index);
  }
}

document.querySelectorAll("[data-level]").forEach(function(b){
  b.addEventListener("click",function(){ save(b.dataset.level); });
});
document.getElementById("prev").onclick=function(){ load(Math.max(0,state.index-1)); };
document.getElementById("next").onclick=function(){ load(Math.min(state.total-1,state.index+1)); };
document.getElementById("next-unreviewed").onclick=function(){ load(state.nextUnreviewedIndex===null ? state.index : state.nextUnreviewedIndex); };
document.getElementById("clear").onclick=async function(){
  const r=await fetch("/api/clear/"+encodeURIComponent(state.revisionId),{method:"POST",headers:apiHeaders});
  if(!r.ok) throw new Error(await r.text());
  await load(state.index);
};
document.addEventListener("keydown",async function(e){
  if(e.target===notes && !(e.ctrlKey&&e.key==="Enter")) return;
  if(e.key>="1"&&e.key<="6"){
    const levels=["very-low","low","normal","high","very-high","unsure"];
    e.preventDefault();
    await save(levels[Number(e.key)-1]);
    return;
  }
  if(e.key==="ArrowLeft"){ e.preventDefault(); await load(Math.max(0,state.index-1)); }
  if(e.key==="ArrowRight"){ e.preventDefault(); await load(Math.min(state.total-1,state.index+1)); }
  if(e.key.toLowerCase()==="p"){ e.preventDefault(); important.checked=!important.checked; }
  if(e.ctrlKey&&e.key==="Enter"&&state.qualityLevel){ e.preventDefault(); await save(state.qualityLevel,false); }
});
load(0).catch(function(e){ status.textContent=e.message; });
</script>
</body>
</html>
""";
}
