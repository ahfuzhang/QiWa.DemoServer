namespace CmdlineArgs;

using System.CommandLine;

/// <summary>
/// 登录服务的命令行配置。
/// 提示词意图：把命令行参数定义为一个 struct 的成员，并由当前结构体统一负责参数解析。
/// </summary>
internal readonly struct ServerCommandLineOptions
{
    /// <summary>控制 ConsoleLogger 最低输出级别。</summary>
    public string LogLevel { get; }

    /// <summary>控制日志缓冲区 flush 的时间间隔，单位毫秒。</summary>
    public int LogFlushIntervalMs { get; }

    /// <summary>控制日志缓冲区大小，支持带单位后缀的字符串。</summary>
    public string LogBufferSize { get; }

    /// <summary>指定日志通过 HTTP POST 推送到的目标地址。</summary>
    public string? LogPushAddr { get; }

    /// <summary>指定日志上报时附带的全局 tags。</summary>
    public string? LogGlobalTags { get; }

    /// <summary>指定 HTTP/1.1 服务监听端口。</summary>
    public int Http1Port { get; }

    /// <summary>指定 HTTP/2 服务监听端口。</summary>
    public int? Http2Port { get; }

    /// <summary>指定 gRPC 服务监听端口。</summary>
    public int? GrpcPort { get; }

    /// <summary>指定线程池最大线程数。</summary>
    public int? Cores { get; }

    /// <summary>启用 CPU profiling（speedscope）端点。</summary>
    public bool WithCpuProfiling { get; }

    /// <summary>metrics export 的时间间隔，单位毫秒。</summary>
    public int MetricIntervalMs { get; }

    /// <summary>metrics 的 push 地址。</summary>
    public string? MetricPushAddr { get; }

    /// <summary>
    /// 构造命令行解析后的服务配置对象。
    /// </summary>
    private ServerCommandLineOptions(
        string logLevel,
        int logFlushIntervalMs,
        string logBufferSize,
        string? logPushAddr,
        string? logGlobalTags,
        int http1Port,
        int? http2Port,
        int? grpcPort,
        int? cores,
        bool withCpuProfiling,
        int metricIntervalMs,
        string? metricPushAddr)
    {
        LogLevel = logLevel;
        LogFlushIntervalMs = logFlushIntervalMs;
        LogBufferSize = logBufferSize;
        LogPushAddr = logPushAddr;
        LogGlobalTags = logGlobalTags;
        Http1Port = http1Port;
        Http2Port = http2Port;
        GrpcPort = grpcPort;
        Cores = cores;
        WithCpuProfiling = withCpuProfiling;
        MetricIntervalMs = metricIntervalMs;
        MetricPushAddr = metricPushAddr;
    }

    /// <summary>
    /// 创建根命令并在解析完成后把参数绑定到当前结构体。
    /// </summary>
    public static Task<int> InvokeAsync(string[] args, Func<ServerCommandLineOptions, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateRootCommand(handler).InvokeAsync(args);
    }

    /// <summary>
    /// 生成 DemoLoginServer 使用的根命令和所有选项定义。
    /// </summary>
    private static RootCommand CreateRootCommand(Func<ServerCommandLineOptions, Task> handler)
    {
        var logLevelOption = new Option<string>("-log.level", () => "warn",
            "Log level (error/warn/info/debug)");
        var logFlushIntervalOption = new Option<int>("-log.flush.interval.ms", () => 1000,
            "log flush interval(ms)");
        var logBufferSizeOption = new Option<string>("-log.buffer.size", () => "64k",
            "log buffer size, default 64kb. support suffix: k/kb/m/mb/g/gb, maximum value is 1GB.");
        var logPushAddrOption = new Option<string?>("-log.push.addr", () => null,
            "log push url, use jsonline format");
        var logGlobalTagsOption = new Option<string?>("-log.global.tags", () => null,
            "global log tags: format is 'a=b&c=d' ");
        var http1PortOption = new Option<int>("-http1.port", "HTTP/1.1 port (must set)");
        var http2PortOption = new Option<int?>("-http2.port", () => null, "HTTP/2 port (optional)");
        var grpcPortOption = new Option<int?>("-grpc.port", () => null, "gRPC port (not support now)");
        var coresOption = new Option<int?>("-cores", () => null, "Thread pool's maximum thread count.");
        var withCpuProfilingOption = new Option<bool>("-with.cpu.profiling", () => false,
            "Enable CPU profiling endpoints (/traceme, /profile, /speedscope).");
        var metricIntervalMsOption = new Option<int>("-metric.interval.ms", () => 15000,
            "Metrics export interval (ms).");
        var metricPushAddrOption = new Option<string?>("-metric.push.addr", () => null,
            "Metrics push address.");
        http1PortOption.IsRequired = true;

        var root = new RootCommand("DemoServer");
        root.AddOption(logLevelOption);
        root.AddOption(logFlushIntervalOption);
        root.AddOption(logBufferSizeOption);
        root.AddOption(logPushAddrOption);
        root.AddOption(logGlobalTagsOption);
        root.AddOption(http1PortOption);
        root.AddOption(http2PortOption);
        root.AddOption(grpcPortOption);
        root.AddOption(coresOption);
        root.AddOption(withCpuProfilingOption);
        root.AddOption(metricIntervalMsOption);
        root.AddOption(metricPushAddrOption);

        root.SetHandler(async context =>
        {
            var options = new ServerCommandLineOptions(
                logLevel: context.ParseResult.GetValueForOption(logLevelOption)!,
                logFlushIntervalMs: context.ParseResult.GetValueForOption(logFlushIntervalOption),
                logBufferSize: context.ParseResult.GetValueForOption(logBufferSizeOption)!,
                logPushAddr: context.ParseResult.GetValueForOption(logPushAddrOption),
                logGlobalTags: context.ParseResult.GetValueForOption(logGlobalTagsOption),
                http1Port: context.ParseResult.GetValueForOption(http1PortOption),
                http2Port: context.ParseResult.GetValueForOption(http2PortOption),
                grpcPort: context.ParseResult.GetValueForOption(grpcPortOption),
                cores: context.ParseResult.GetValueForOption(coresOption),
                withCpuProfiling: context.ParseResult.GetValueForOption(withCpuProfilingOption),
                metricIntervalMs: context.ParseResult.GetValueForOption(metricIntervalMsOption),
                metricPushAddr: context.ParseResult.GetValueForOption(metricPushAddrOption));
            await handler(options);
        });
        return root;
    }
}
