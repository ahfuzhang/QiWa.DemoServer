#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Generated.Demo;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Sockets;
using QiWa.Common;
using QiWa.KestrelWrap;
using QiWa.ConsoleLogger;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Compact;

class MsLogger
{
    ILoggerFactory loggerFactory;
    Microsoft.Extensions.Logging.ILogger logger;

    public MsLogger()
    {
        loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information)
                .AddJsonConsole(options =>
                {
                    options.IncludeScopes = true;
                    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";
                    options.UseUtcTimestamp = true;
                });
        });
        logger = loggerFactory.CreateLogger("ms_logger");
    }

    public void Log(HttpContext ctx,
        string requestJson,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string func = "")
    {
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["request"] = requestJson,
            ["path"] = ctx!.Request!.Path.Value ?? "",
            ["method"] = ctx!.Request!.Method,
            ["protocol"] = ctx!.Request!.Protocol,
            ["client_ip"] = ctx!.Connection.RemoteIpAddress?.ToString() ?? "",
            ["_file"] = file,
            ["_line"] = line,
            ["_func"] = func,
            ["_time"] = DateTime.UtcNow.ToString("o"),
        }))
        {
            logger.LogInformation("");
        }
    }
}

class Serlogger
{
    Serilog.ILogger _logger;

    public Serlogger()
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(new CompactJsonFormatter())
            .CreateLogger();
    }

    public void Log(HttpContext ctx,
        string requestJson,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string func = "")
    {
        _logger
            .ForContext("request", requestJson)
            .ForContext("path", ctx!.Request!.Path.Value)
            .ForContext("method", ctx!.Request!.Method)
            .ForContext("protocol", ctx!.Request!.Protocol)
            .ForContext("client_ip", ctx!.Connection.RemoteIpAddress?.ToString())
            .ForContext("_file", file)
            .ForContext("_line", line)
            .ForContext("_func", func)
            .ForContext("_time", DateTime.UtcNow.ToString("o"))
            .Information("");
    }
}

struct LoginRequestLog
{
    [JsonPropertyName("request")]
    public string request { get; set; }
    [JsonPropertyName("path")]
    public string path { get; set; }
    [JsonPropertyName("method")]
    public string method { get; set; }
    [JsonPropertyName("protocol")]
    public string protocol { get; set; }
    [JsonPropertyName("client_ip")]
    public string client_ip { get; set; }
    [JsonPropertyName("_file")]
    public string _file { get; set; }
    [JsonPropertyName("_line")]
    public int _line { get; set; }
    [JsonPropertyName("_func")]
    public string _func { get; set; }
    [JsonPropertyName("_time")]
    public string _time { get; set; }
}

[JsonSerializable(typeof(LoginRequestLog))]
partial class LoginRequestLogContext : JsonSerializerContext { }

class LoginContext : ContextBase, QiWa.Common.IResettable
{
    public ReadonlyLoginRequest Request;
    public LoginResponse Response;
    //todo: 在这里定义业务处理中的局部变量，从而最终做到 0 alloc
    Lazy<MsLogger> _service = new Lazy<MsLogger>(() => new MsLogger());
    Lazy<Serlogger> _serlogger = new Lazy<Serlogger>(() => new Serlogger());
    RentedBuffer buf = new RentedBuffer(1024);

    public new void Reset()
    {
        base.Reset();
        Request.Reset();
        Response.Reset();
        //todo: 局部变量的 reset 写在这里
        buf.Length = 0;
    }

    public async ValueTask<Error> Run()
    {
        ref readonly var req = ref Request;
        ref var rsp = ref Response;
        // todo: write your bussiness logic code here
        rsp.Code = 0;
        string j;

        LoginRequest temp = default;
        req.Clone(ref temp);
        temp.ToJSON(ref buf);
        j = Encoding.UTF8.GetString(buf.AsSpan());

        // request log
        var r = base.HttpContext!.Request;
        if (r.Query.TryGetValue("log_output_mode", out var logOutputMode))
        {
            switch (logOutputMode)
            {
                case "console_writeline":
                    WriteAccessLog(base.HttpContext!, j);
                    break;
                case "json_encode":
                    JsonEncode(base.HttpContext!, j);
                    break;
                case "struct_encode":
                    StructEncode(base.HttpContext!, j);
                    break; 
                case "ms_logger":
                    _service.Value.Log(base.HttpContext!, j);
                    break;
                case "serilog":
                    _serlogger.Value.Log(base.HttpContext!, j);
                    break;
                case "qiwa_logger":
                    base.L!.Info(
                        // Field.String("path"u8, base.HttpContext!.Request.Path.Value ?? ""),
                        // Field.String("method"u8, base.HttpContext!.Request.Method),
                        // Field.String("protocol"u8, base.HttpContext!.Request.Protocol),
                        // Field.String(
                        //     (base.HttpContext!.Request.HttpContext.Connection.RemoteIpAddress?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        //         ? "client_ipv6"u8 : "client_ipv4"u8,
                        //     base.HttpContext!.Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""),
                        Field.Utf8String("request"u8, buf.AsSpan())    
                    );
                    break;
            }
        }
        //
        rsp.Message = $"success. req={j}";
        return default;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:Intentional reflection-based JSON serialization for demo")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050:Intentional reflection-based JSON serialization for demo")]
    private void JsonEncode(HttpContext ctx,
        string requestJson,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string func = "")
    {
        var log = new
        {
            request = requestJson,
            path = ctx.Request.Path.Value,
            method = ctx.Request.Method,
            protocol = ctx.Request.Protocol,
            client_ip = ctx.Connection.RemoteIpAddress,
            _file = file,
            _line = line,
            _func = func,
            _time = DateTime.UtcNow.ToString("o")
        };
        Console.WriteLine(JsonSerializer.Serialize(log));
    }

    private void StructEncode(HttpContext ctx,
        string requestJson,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string func = "")
    {
        var st = new LoginRequestLog
        {
            request = requestJson,
            path = ctx.Request.Path.Value ?? "",
            method = ctx.Request.Method,
            protocol = ctx.Request.Protocol,
            client_ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "",
            _file = file,
            _line = line,
            _func = func,
            _time = DateTime.UtcNow.ToString("o")
        };
        Console.WriteLine(JsonSerializer.Serialize(st, LoginRequestLogContext.Default.LoginRequestLog));
    }

    private void WriteAccessLog(
        HttpContext ctx,
        string requestJson,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string func = "")
    {
        var remoteIp = ctx.Connection.RemoteIpAddress;
        bool isIpv6 = remoteIp?.AddressFamily == AddressFamily.InterNetworkV6;
        string protocol = ctx.Request.Protocol;

        using var ms = new MemoryStream(512);
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("request", requestJson);
            writer.WriteString("path", ctx.Request.Path.Value);
            writer.WriteString("method", ctx.Request.Method);
            writer.WriteString("protocol", protocol);
            if (isIpv6)
                writer.WriteString("client_ipv6", remoteIp?.ToString() ?? "");
            else
                writer.WriteString("client_ipv4", remoteIp?.ToString() ?? "");
            writer.WriteString("_file", Path.GetFileName(file));
            writer.WriteNumber("_line", line);
            writer.WriteString("_func", func);
            writer.WriteString("_time", DateTime.UtcNow.ToString("o"));
            writer.WriteEndObject();
        }
        Console.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
    }
}
