namespace DemoServer;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DemoServer.Metrics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using CmdlineArgs;
using Microsoft.Extensions.Options;

/// <summary>
/// 负责构建并配置 Kestrel WebApplication，包括端口监听、OpenTelemetry Metrics 和路由注册。
/// </summary>
internal static class KestrelInit
{
    /// <summary>
    /// 构建并配置 WebApplication。
    /// </summary>
    /// <param name="http1Port">HTTP/1.1 监听端口（必须）。</param>
    /// <param name="http2Port">HTTP/2 监听端口（可选）。</param>
    /// <param name="grpcPort">gRPC 监听端口（可选，基于 HTTP/2）。</param>
    /// <param name="callback">Task HandleAsync(HttpContext context)</param>
    /// <returns>已注册路由、尚未启动的 WebApplication 实例。</returns>
    public static WebApplication Build(ServerCommandLineOptions options, System.Type res, Func<HttpContext, Task> callback)
    {
        var builder = WebApplication.CreateBuilder();

        // 关闭 ASP.NET Core 内置日志，避免干扰自定义日志库；启用 JSON 格式控制台输出
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
            logging.AddJsonConsole(options =>
            {
                options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
                {
                    Indented = false,
                };
            });
        });

        // 配置监听端口
        builder.WebHost.ConfigureKestrel(kestrelOptions =>
        {
            // HTTP/1.1 端口（必须）
            kestrelOptions.ListenAnyIP(
                options.Http1Port,
                listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
            Console.WriteLine($"listen port {options.Http1Port}");

            // HTTP/2 端口（可选）
            if (options.Http2Port.HasValue)
            {
                kestrelOptions.ListenAnyIP(
                    options.Http2Port.Value,
                    listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
                kestrelOptions.Limits.Http2.MaxStreamsPerConnection = 200;
                // todo: 这里的 200 做成可配置的
                Console.WriteLine($"listen port {options.Http2Port.Value}");
            }

            // gRPC 端口（可选，gRPC 基于 HTTP/2）
            if (options.GrpcPort.HasValue)
            {
                kestrelOptions.ListenAnyIP(
                    options.GrpcPort.Value,
                    listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
            }
        });

        // 配置响应压缩：对 Prometheus metrics 的两种内容类型启用 gzip
        builder.Services.AddResponseCompression(opts =>
        {
            opts.EnableForHttps = false;
            opts.Providers.Add<GzipCompressionProvider>();
            opts.MimeTypes = new[]
            {
                "text/plain",
                "application/openmetrics-text",
            };
        });
        builder.Services.Configure<GzipCompressionProviderOptions>(opts =>
        {
            opts.Level = System.IO.Compression.CompressionLevel.Fastest;
        });

        // 配置 OpenTelemetry Metrics（含 Kestrel/Runtime 指标上报）
        var err = SnapshotMetricExporter.Init(
            builder,
            exportIntervalMilliseconds: options.MetricIntervalMs,
            pushAddr: options.MetricPushAddr ?? ""
        );
        if (err.Err())
        {
            Console.WriteLine($"metrics init fail: code={err.Code}, message={err.Message}");
            Environment.Exit(-1);
        }
        // 把输出 metrics 的方法注册进去
        SnapshotMetricExporter.Singleton!.AddCountersGenerator<QiWa.KestrelWrap.Counters>(QiWa.KestrelWrap.ContextBase.SumCounters);
        SnapshotMetricExporter.Singleton!.AddCountersGenerator<Generated.Demo.DemoCounters>(Generated.Demo.Demo.SumCounters);

        var app = builder.Build();

        // 必须显式调用，避免 macOS 下 WebApplication 自动插入时机偏后，
        // 导致 fallback 中间件里 ctx.GetEndpoint() 始终为 null，/healthz 返回 404。
        app.UseRouting();

        // 启用响应压缩中间件（需在路由之前）
        app.UseResponseCompression();

        // 注册路由
        // /metrics - 自定义 Prometheus 格式输出，由 SnapshotMetricExporter 定期刷新
        app.MapGet("/metrics", SnapshotMetricExporter.Singleton!.HandleMetricsAsync);

        // todo: 配合内部逻辑，做得更复杂
        app.MapGet("/healthz", () => "OK");
        // /ready - k8s 就绪检查
        app.MapGet("/ready", () => "OK");
        if (options.WithCpuProfiling)
        {
            TraceMe.ConfigureSpeedscope(app, res);
            var generatedProfiles = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
            TraceMe.MapTraceMe(app, generatedProfiles);
            TraceMe.MapProfile(app, generatedProfiles);
            Console.WriteLine("visit /traceme?seconds=30 will collect cpu profile");
        }

        // if (grpcPort.HasValue)
        // {
        //     // todo: 使用全局的拦截器
        //     //app.MapGrpcService<EchoService>().RequireHost($"*:{grpcPort.Value}");
        // }

        // MapFallback(Delegate) 在 AOT 下会触发 IL2026/IL3050/RDG002。
        // 改用 RequestDelegate 中间件：路由已匹配时继续，否则交给 callback 兜底。
        app.Use(next => ctx => ctx.GetEndpoint() != null ? next(ctx) : callback(ctx));

        return app;
    }
}
