namespace DemoServer;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using CmdlineArgs;

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
    public static WebApplication Build(int http1Port, int? http2Port, int? grpcPort, bool useTraceMe, System.Type res, Func<HttpContext, Task> callback)
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
                http1Port,
                listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
            Console.WriteLine($"listen port {http1Port}");    

            // HTTP/2 端口（可选）
            if (http2Port.HasValue)
            {
                kestrelOptions.ListenAnyIP(
                    http2Port.Value,
                    listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
                kestrelOptions.Limits.Http2.MaxStreamsPerConnection = 200;
                Console.WriteLine($"listen port {http2Port.Value}");
            }

            // gRPC 端口（可选，gRPC 基于 HTTP/2）
            if (grpcPort.HasValue)
            {
                kestrelOptions.ListenAnyIP(
                    grpcPort.Value,
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
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddPrometheusExporter();
                metrics.AddProcessInstrumentation();
                metrics.AddRuntimeInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddMeter(
                    "System.Runtime",
                    "System.Net.Http",
                    "System.Net.Sockets",
                    "Microsoft.AspNetCore.Hosting",
                    "Microsoft.AspNetCore.Server.Kestrel");
            });
        // if (grpcPort.HasValue)
        // {
        //     //builder.Services.AddGrpc();
        //     builder.Services.AddGrpc(options =>
        //         {
        //             // 替换掉真正的 gzip provider，用透传版本
        //             options.CompressionProviders.Add(new DemoLoginServer.GrpcUtils.PassthroughCompressionProvider("gzip"));
        //             options.CompressionProviders.Add(new DemoLoginServer.GrpcUtils.PassthroughCompressionProvider("zstd"));

        //             // 全局默认用 gzip（框架会设 compressed-flag=1 并写 grpc-encoding: gzip）
        //             options.ResponseCompressionAlgorithm = "gzip";
        //             options.ResponseCompressionLevel = System.IO.Compression.CompressionLevel.NoCompression; // 无意义，但保持一致
        //         }
        //     );

        // }
        var app = builder.Build();

        // 对 /metrics 强制注入 Accept-Encoding: gzip，使压缩中间件无论客户端是否声明都生效
        // 同时过滤掉 Prometheus 注释行（# HELP / # TYPE）
        // app.Use(async (context, next) =>
        // {
        //     if (!context.Request.Path.StartsWithSegments("/metrics"))
        //     {
        //         await next();
        //         return;
        //     }
        //     context.Request.Headers["Accept-Encoding"] = "gzip";

        //     var originalBody = context.Response.Body;
        //     using var buffered = new MemoryStream();
        //     context.Response.Body = buffered;
        //     await next();

        //     buffered.Seek(0, SeekOrigin.Begin);
        //     using var reader = new StreamReader(buffered, leaveOpen: true);
        //     var raw = await reader.ReadToEndAsync();

        //     var filtered = string.Concat(
        //         raw.Split('\n')
        //            .Where(line => !line.StartsWith('#'))
        //            .Select(line => line + '\n'));

        //     context.Response.Body = originalBody;
        //     context.Response.ContentLength = System.Text.Encoding.UTF8.GetByteCount(filtered);
        //     await context.Response.WriteAsync(filtered);
        // });

        // 必须显式调用，避免 macOS 下 WebApplication 自动插入时机偏后，
        // 导致 fallback 中间件里 ctx.GetEndpoint() 始终为 null，/healthz 返回 404。
        app.UseRouting();

        // 启用响应压缩中间件（需在路由之前）
        app.UseResponseCompression();

        // 注册路由
        // /metrics、/healthz、/ready 仅在 HTTP/1 端口上生效，避免暴露到 gRPC/HTTP2 端口
        var http1HostFilter = $"*:{http1Port}";
        // /metrics - Prometheus 格式的 metrics 数据
        app.MapPrometheusScrapingEndpoint().RequireHost(http1HostFilter);
        // /healthz - k8s 健康检查
        // todo: 配合内部逻辑，做得更复杂
        // 不加 RequireHost：HTTP2/gRPC 端口已由 HttpProtocols.Http2 限制，HTTP1 请求物理上无法到达那些端口。
        // 且 .NET 10 的 HostMatcherPolicy 对 *:port 通配符匹配行为改变，会导致 macOS 下返回 404。
        app.MapGet("/healthz", () => "OK");
        // /ready - k8s 就绪检查
        app.MapGet("/ready", () => "OK");
        if (useTraceMe)
        {
            TraceMe.ConfigureSpeedscope(app, res);
            var generatedProfiles = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
            TraceMe.MapTraceMe(app, generatedProfiles);
            TraceMe.MapProfile(app, generatedProfiles);
            Console.WriteLine("visit /traceme?seconds=30 will collect cpu profile");
        }


        // /login、/biz_logic 仅在 HTTP/1 和 HTTP/2 端口上生效，不暴露到 gRPC 端口
        var bizHostFilters = new List<string> { $"*:{http1Port}" };
        if (http2Port.HasValue)
            bizHostFilters.Add($"*:{http2Port.Value}");
        var bizHostFilterArray = bizHostFilters.ToArray();
        // /login - 用户登录
        // app.MapPost("/login", LoginHandler.HandleAsync).RequireHost(bizHostFilterArray);
        // // /biz_logic - 业务接口（需要鉴权）
        // app.MapPost("/biz_logic", BizHandler.HandleAsync).RequireHost(bizHostFilterArray);
        // // 所有 HTTP/1.1 未匹配路由的请求，统一走 Http1Handler 兜底处理
        // app.MapFallback(Http1Handler.HandleAsync).RequireHost(http1HostFilter);
        // 所有 HTTP/2 未匹配路由的请求，统一走 Http2Handler 兜底处理
        // if (http2Port.HasValue)
        // {
        //     app.MapFallback(Http2Handler.HandleAsync).RequireHost($"*:{http2Port.Value}");
        // }
        // //
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
