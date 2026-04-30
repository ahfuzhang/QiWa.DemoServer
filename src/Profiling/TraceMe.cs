// 注意：本功能在 AOT 模式下会导致进程发生 exit code 139 崩溃。

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO.Compression;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing.Stacks.Formats;
using Microsoft.Diagnostics.Symbols;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

public class TraceMe
{
    private const string TraceOutputDir = "/tmp/";

    /// <summary>
    /// 注册路径 /traceme?seconds=30
    /// </summary>
    /// <param name="app"></param>
    /// <param name="generatedProfiles"></param>
    public static void MapTraceMe(WebApplication app, ConcurrentDictionary<string, string> generatedProfiles)
    {
        app.MapGet("/traceme", async context =>
        {
            var seconds = ParseSeconds(context.Request.Query["seconds"]);

            context.Response.Headers.CacheControl = "no-store";
            context.Response.ContentType = "text/html; charset=utf-8";
            // 页面上显示正在 profiling 的信息
            await WriteHtmlChunk(
                context,
                "<!doctype html><meta charset=\"utf-8\"><title>TraceMeV2</title>" +
                "<h2>Trace collecting...</h2><p id=\"status\"></p><pre id=\"log\"></pre>" +
                "<script>" +
                "const s=document.getElementById('status');" +
                "const l=document.getElementById('log');" +
                "function setStatus(t){s.textContent=t;}" +
                "function appendLog(t){if(t){l.textContent+=(t+'\\n');}}" +
                "</script>");
            // 创建一个独立的物理线程
            var traceTask = StartTraceOnDedicatedThread(seconds);
            var targetTime = DateTime.UtcNow.AddSeconds(seconds);

            // 每秒输出一行，显示倒计时
            while (!traceTask.IsCompleted)
            {
                var remain = (int)Math.Max(0, Math.Ceiling((targetTime - DateTime.UtcNow).TotalSeconds));
                var status = remain > 0
                    ? $"collecting... {remain}s left"
                    : "converting trace to speedscope...";

                await WriteHtmlChunk(context, $"<script>setStatus({ToJs(status)});</script>");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            TraceArtifact artifact;
            try
            {
                artifact = await traceTask;
            }
            catch (Exception ex)
            {
                await WriteHtmlChunk(context, $"<script>setStatus({ToJs($"trace failed: {ex.Message}")});</script>");
                return;
            }
            // 记录  cpu profile 的文件名
            generatedProfiles[artifact.ProfileFileName] = artifact.ProfileFilePath;
            var redirect = $"/speedscope/index.html#profileURL=/profile/{artifact.ProfileFileName}";
            // todo: 这里考虑使用官网的 UI
            await WriteHtmlChunk(context, $"<script>setStatus('done, redirecting...');location.href={ToJs(redirect)};</script>");
        });
    }

    /// <summary>
    /// 提供 cpu profile 的 JSON 文件的下载.  /profile/{name}.json
    /// </summary>
    /// <param name="app"></param>
    /// <param name="generatedProfiles"></param>
    public static void MapProfile(WebApplication app, ConcurrentDictionary<string, string> generatedProfiles)
    {
        app.MapGet("/profile/{name}.json", async (HttpContext context, string name) =>
        {
            var safeName = Path.GetFileName(name);
            if (!string.Equals(safeName, name, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var fileName = $"{safeName}.json";
            if (!generatedProfiles.TryGetValue(fileName, out var profilePath) || !File.Exists(profilePath))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "application/json; charset=utf-8";
            var acceptsGzip = context.Request.Headers.AcceptEncoding.ToString()
                .Contains("gzip", StringComparison.OrdinalIgnoreCase);
            if (acceptsGzip)
            {
                context.Response.Headers.ContentEncoding = "gzip";
                await using var fileStream = File.OpenRead(profilePath);
                await using var gzip = new GZipStream(context.Response.Body, CompressionLevel.SmallestSize);
                await fileStream.CopyToAsync(gzip, context.RequestAborted);
            }
            else
            {
                await context.Response.SendFileAsync(profilePath, context.RequestAborted);
            }
        });
    }

    /// <summary>
    /// 对嵌入的 web 界面进行映射. /speedscope/index.html 以及 /speedscope/*
    /// </summary>
    /// <param name="app"></param>
    public static void ConfigureSpeedscope(WebApplication app, System.Type res)
    {
        var assembly = res.Assembly;
        var provider = new ManifestEmbeddedFileProvider(assembly, "speedscope");

        app.MapMethods("/speedscope/index.html", ["GET", "HEAD"], async context =>
        {
            var file = provider.GetFileInfo("index.html");
            if (!file.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            var acceptsGzip = context.Request.Headers.AcceptEncoding.ToString()
                .Contains("gzip", StringComparison.OrdinalIgnoreCase);
            if (acceptsGzip)
            {
                context.Response.Headers.ContentEncoding = "gzip";
                await using var fileStream = file.CreateReadStream();
                await using var gzip = new GZipStream(context.Response.Body, CompressionLevel.SmallestSize);
                await fileStream.CopyToAsync(gzip, context.RequestAborted);
            }
            else
            {
                await context.Response.SendFileAsync(file, context.RequestAborted);
            }
        });

        var contentTypeProvider = new FileExtensionContentTypeProvider();
        app.MapGet("/speedscope/{**path}", async (HttpContext context, string? path) =>
        {
            var filePath = path ?? string.Empty;
            var file = provider.GetFileInfo(filePath);
            if (!file.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!contentTypeProvider.TryGetContentType(filePath, out var contentType))
                contentType = "application/octet-stream";

            context.Response.ContentType = contentType;
            var acceptsGzip = context.Request.Headers.AcceptEncoding.ToString()
                .Contains("gzip", StringComparison.OrdinalIgnoreCase);
            if (acceptsGzip)
            {
                context.Response.Headers.ContentEncoding = "gzip";
                await using var fileStream = file.CreateReadStream();
                await using var gzip = new GZipStream(context.Response.Body, CompressionLevel.SmallestSize);
                await fileStream.CopyToAsync(gzip, context.RequestAborted);
            }
            else
            {
                await context.Response.SendFileAsync(file, context.RequestAborted);
            }
        });
    }

    private static int ParseSeconds(string? secondsRaw)
    {
        const int defaultSeconds = 10;
        if (!int.TryParse(secondsRaw, out var seconds))
        {
            return defaultSeconds;
        }

        return Math.Clamp(seconds, 1, 60);
    }

    private static Task<TraceArtifact> StartTraceOnDedicatedThread(int seconds)
    {
        // 把 task 的完成权交给别的线程
        var completion = new TaskCompletionSource<TraceArtifact>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(CollectTrace(seconds));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "TraceMeCollector"
        };

        thread.Start();
        return completion.Task;
    }

    private static TraceArtifact CollectTrace(int seconds)
    {
        Directory.CreateDirectory(TraceOutputDir);

        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss_fff");
        var processId = Environment.ProcessId;
        var nettracePath = Path.Combine(TraceOutputDir, $"{stamp}.nettrace");
        var etlxPath = $"{nettracePath}.etlx";
        var profileFileName = $"{stamp}.speedscope.json";
        var profilePath = Path.Combine(TraceOutputDir, profileFileName);

        CollectCpuNettrace(processId, TimeSpan.FromSeconds(seconds), nettracePath);
        ConvertNettraceToSpeedscope(nettracePath, etlxPath, profilePath);
        TryDeleteFile(nettracePath);
        TryDeleteFile(etlxPath);

        return new TraceArtifact(profileFileName, profilePath);
    }

    private static void CollectCpuNettrace(int processId, TimeSpan duration, string outFile)
    {
        var providers = new List<EventPipeProvider>
        {
            new("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, (long)ClrTraceEventParser.Keywords.Default),
            new("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational, 0)
        };

        var client = new DiagnosticsClient(processId);
        using var session = client.StartEventPipeSession(providers, requestRundown: true);
        using var stream = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.Read);

        var copyTask = session.EventStream.CopyToAsync(stream);
        Thread.Sleep(duration);
        session.Stop();

        try
        {
            copyTask.GetAwaiter().GetResult();
        }
        catch
        {
            // Stop() may race with the read loop; this is expected on shutdown.
        }
    }

    private static void ConvertNettraceToSpeedscope(string nettracePath, string etlxPath, string speedscopePath)
    {
        var options = new TraceLogOptions
        {
            ConversionLog = TextWriter.Null,
            ShouldResolveSymbols = _ => false
        };

        using var traceLog = new TraceLog(TraceLog.CreateFromEventPipeDataFile(nettracePath, etlxPath, options));
        var symbolReader = new SymbolReader(TextWriter.Null, SymbolPath.MicrosoftSymbolServerPath, null)
        {
            SecurityCheck = _ => true
        };

        var stackSource = new MutableTraceEventStackSource(traceLog);
        var computer = new SampleProfilerThreadTimeComputer(traceLog, symbolReader);
        // EventPipe traces from DiagnosticsClient are already scoped to the attached process.
        // Filtering by Environment.ProcessId can drop all samples when PID namespaces/remapping are involved.
        computer.GenerateThreadTimeStacks(stackSource, traceLog.Events);

        SpeedScopeStackSourceWriter.WriteStackViewAsJson(stackSource, speedscopePath);
    }

    private static void TryDeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Keep temporary files if cleanup fails.
        }
    }

    private static async Task WriteHtmlChunk(HttpContext context, string htmlChunk)
    {
        await context.Response.WriteAsync(htmlChunk, context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }

    private static string ToJs(string value)
    {
        return $"\"{JavaScriptEncoder.Default.Encode(value)}\"";
    }

    private readonly record struct TraceArtifact(string ProfileFileName, string ProfileFilePath);
    private readonly record struct StartupOptions(int Port, int? Cores, string[] ForwardedArgs);
}
