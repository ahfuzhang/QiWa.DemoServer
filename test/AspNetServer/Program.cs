using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace AspNetServer;

public class LoginRequest
{
    [JsonPropertyName("user_name")]
    public string UserName { get; set; } = "";

    [JsonPropertyName("user_password_sha256")]
    public string UserPasswordSha256 { get; set; } = "";
}

public class LoginResponse
{
    [JsonPropertyName("code")]
    public uint Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("user_id")]
    public ulong UserId { get; set; }

    [JsonPropertyName("user_session")]
    public string UserSession { get; set; } = "";
}

[ApiController]
public class LoginController : ControllerBase
{
    private static readonly Meter s_meter = new("AspNetServer.Metrics", "1.0.0");
    private static readonly Counter<long> s_requestTotal =
        s_meter.CreateCounter<long>("login_requests_total", description: "Total login requests");
    private static readonly Counter<long> s_errorTotal =
        s_meter.CreateCounter<long>("login_errors_total", description: "Total login errors");
    private static readonly Histogram<double> s_latencyMs =
        s_meter.CreateHistogram<double>("login_latency_ms", "ms", "Login handler latency in milliseconds");

    [HttpPost("/api/v1/login")]
    public IActionResult Login([FromBody] LoginRequest req)
    {
        var sw = Stopwatch.StartNew();
        s_requestTotal.Add(1);
        try
        {
            string reqJson = JsonSerializer.Serialize(req);
            WriteAccessLog(reqJson);

            var rsp = new LoginResponse
            {
                Code = 0,
                Message = $"success. req={reqJson}",
            };
            return Ok(rsp);
        }
        catch
        {
            s_errorTotal.Add(1);
            throw;
        }
        finally
        {
            sw.Stop();
            s_latencyMs.Record(sw.Elapsed.TotalMilliseconds);
        }
    }

    private void WriteAccessLog(
        string requestJson,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string func = "")
    {
        var ctx = HttpContext;
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

class Program
{
    static void Main(string[] args)
    {
        int http1Port = 8091;
        int http2Port = 8092;
        int cores = 1;

        foreach (var arg in args)
        {
            if (arg.StartsWith("-http1.port="))
                http1Port = int.Parse(arg["-http1.port=".Length..]);
            else if (arg.StartsWith("-http2.port="))
                http2Port = int.Parse(arg["-http2.port=".Length..]);
            else if (arg.StartsWith("-cores="))
                cores = int.Parse(arg["-cores=".Length..]);
        }
        ThreadPool.SetMinThreads(cores, cores);
        ThreadPool.SetMaxThreads(cores, cores);

        var builder = WebApplication.CreateBuilder([]);

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.Listen(IPAddress.Any, http1Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1;
            });
            serverOptions.Listen(IPAddress.Any, http2Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        builder.Services.AddControllers();
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole();
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);

        var app = builder.Build();
        app.MapControllers();
        app.Run();
    }
}
