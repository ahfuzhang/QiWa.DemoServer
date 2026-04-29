using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using QiWa.Common;
using QiWa.Compress;
using Generated.Demo;

class TlsState
{
    public LoginRequest Request;
    public ReadonlyLoginResponse Response;
}

class Program
{
    static string s_http1Addr = "";
    static string s_http2Addr = "";
    static int s_threadCount = 1;
    static int s_connectionPerThread = 3;
    static int s_taskPerConnection = 5;
    static string s_compress = "";
    static string s_encodeType = "json";
    static int s_stressTestSeconds = 10;
    static string s_queryString = "";

    // bucket[0]=[0,100μs)  bucket[i]=[100*1.5^(i-1), 100*1.5^i) for i>=1
    const int NumBuckets = 40;
    static readonly long[] s_latencyBuckets = new long[NumBuckets];
    static readonly double[] s_bucketLo = new double[NumBuckets];

    static long s_totalRequests;
    static long s_totalBytesSent;
    static long s_totalCompressedBytesSent;
    static long s_totalBytesReceived;
    static long s_totalDecompressedBytesReceived;
    static long s_totalErrors;

    static readonly char[] s_chars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

    static readonly System.Net.Http.Headers.MediaTypeHeaderValue s_contentTypeProtobuf = new("application/protobuf");
    static readonly System.Net.Http.Headers.MediaTypeHeaderValue s_contentTypeJson = new("application/json");

    static Program()
    {
        s_bucketLo[0] = 0;
        s_bucketLo[1] = 100;
        for (int i = 2; i < NumBuckets; i++)
            s_bucketLo[i] = s_bucketLo[i - 1] * 1.5;
    }

    static int GetBucket(long latencyUs)
    {
        for (int i = NumBuckets - 1; i >= 0; i--)
        {
            if (latencyUs >= (long)s_bucketLo[i])
                return i;
        }
        return 0;
    }

    static string FormatBytes(double bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / (1024 * 1024):F2} MB";
        if (bytes >= 1024) return $"{bytes / 1024:F2} KB";
        return $"{bytes:F2} B";
    }

    static string FormatRange(int i)
    {
        if (i == 0) return "[0, 100) μs";
        double lo = s_bucketLo[i];
        if (i == NumBuckets - 1) return $"[{lo:F0}, +∞) μs";
        double hi = lo * 1.5;
        if (hi >= 1_000_000) return $"[{lo / 1000:F0}, {hi / 1000:F0}) ms";
        if (lo >= 1000) return $"[{lo / 1000:F2}, {hi / 1000:F2}) ms";
        return $"[{lo:F0}, {hi:F0}) μs";
    }

    static async Task Main(string[] args)
    {
        ParseArgs(args);
        ThreadPool.SetMinThreads(s_threadCount, s_threadCount);
        ThreadPool.SetMaxThreads(s_threadCount, s_threadCount);

        using var cts = new CancellationTokenSource();
        var allTasks = new List<Task>();
        int totalConnections = s_threadCount * s_connectionPerThread;

        if (!string.IsNullOrEmpty(s_http1Addr))
        {
            for (int i = 0; i < totalConnections; i++)
            {
                var client = CreateClient(false);
                allTasks.Add(RunLoop(client, s_http1Addr, cts.Token));
            }
            Console.WriteLine($"HTTP/1.1 tasks: {totalConnections}");
        }

        if (!string.IsNullOrEmpty(s_http2Addr))
        {
            for (int i = 0; i < totalConnections; i++)
            {
                var client = CreateClient(true);
                for (int j = 0; j < s_taskPerConnection; j++)
                    allTasks.Add(RunLoop(client, s_http2Addr, cts.Token));
            }
            Console.WriteLine($"HTTP/2 tasks: {totalConnections * s_taskPerConnection}");
        }

        if (allTasks.Count == 0)
        {
            Console.Error.WriteLine("No address specified. Use -http1.addr or -http2.addr.");
            return;
        }

        Console.WriteLine($"Duration: {s_stressTestSeconds}s  Encode: {s_encodeType}  Compress: {(string.IsNullOrEmpty(s_compress) ? "none" : s_compress)}");
        var sw = Stopwatch.StartNew();

        await Task.Delay(TimeSpan.FromSeconds(s_stressTestSeconds));
        cts.Cancel();
        try { await Task.WhenAll(allTasks); } catch { }

        PrintStats(sw.Elapsed);
    }

    static HttpClient CreateClient(bool http2)
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = s_taskPerConnection > 1 ? s_taskPerConnection : 1,
            EnableMultipleHttp2Connections = false,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            AutomaticDecompression = DecompressionMethods.None,
        };
        var client = new HttpClient(handler);
        if (http2)
        {
            client.DefaultRequestVersion = HttpVersion.Version20;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        }
        return client;
    }

    static async Task RunLoop(HttpClient client, string addr, CancellationToken ct)
    {
        var state = new TlsState();
        var buf = new RentedBuffer(1024);
        RentedBuffer gzipBuf = default;
        RentedBuffer zstdBuf = new(1024 * 4);
        var respBuf = new RentedBuffer(1024);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var url = string.IsNullOrEmpty(s_queryString) ? addr : addr + "?" + s_queryString;
                var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Version = client.DefaultRequestVersion,
                    VersionPolicy = client.DefaultVersionPolicy,
                };
                if (!string.IsNullOrEmpty(s_compress))
                    req.Headers.TryAddWithoutValidation("Accept-Encoding", s_compress);
                // Serialize request
                state.Request.Reset();
                state.Request.UserName = RandomString(8);
                state.Request.UserPasswordSha256 = RandomString(64);
                buf.Length = 0;
                byte[] rawBody;
                if (s_encodeType == "protobuf")
                {
                    var protoReqErr = state.Request.ToProtobuf(ref buf);
                    if (protoReqErr.Err())
                    {
                        Console.WriteLine($"[ERROR] Request.ToProtobuf failed: {protoReqErr}");
                        Interlocked.Increment(ref s_totalErrors);
                        continue;
                    }
                    rawBody = buf.AsSpan().ToArray();
                }
                else
                {
                    state.Request.ToJSON(ref buf);
                    rawBody = buf.AsSpan().ToArray();
                }
                Interlocked.Add(ref s_totalBytesSent, rawBody.Length);

                // Compress if requested
                byte[] sendBody = rawBody;
                if (!string.IsNullOrEmpty(s_compress))
                {
                    if (s_compress == "gzip")
                    {
                        var (cbuf, gzipCompressErr) = GzipCompressor.Compress(rawBody);
                        if (gzipCompressErr.Err())
                        {
                            Console.WriteLine($"[ERROR] GzipCompressor.Compress failed: {gzipCompressErr}");
                            Interlocked.Increment(ref s_totalErrors);
                            continue;
                        }
                        gzipBuf.Dispose();
                        gzipBuf = cbuf;
                        sendBody = gzipBuf.AsSpan().ToArray();
                    }
                    else
                    {
                        zstdBuf.Length = 0;
                        var zstdCompressErr = ZstdCompressor.Compress(ref zstdBuf, rawBody);
                        if (zstdCompressErr.Err())
                        {
                            Console.WriteLine($"[ERROR] ZstdCompressor.Compress failed: {zstdCompressErr}");
                            Interlocked.Increment(ref s_totalErrors);
                            continue;
                        }
                        sendBody = zstdBuf.AsSpan().ToArray();
                    }
                    Interlocked.Add(ref s_totalCompressedBytesSent, sendBody.Length);
                }

                // Build request
                using var content = new ByteArrayContent(sendBody);
                content.Headers.ContentType = s_encodeType == "protobuf" ? s_contentTypeProtobuf : s_contentTypeJson;
                if (!string.IsNullOrEmpty(s_compress))
                    content.Headers.ContentEncoding.Add(s_compress);

                req.Content = content;

                // Send and measure latency
                long t0 = Stopwatch.GetTimestamp();
                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!resp.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[ERROR] HTTP {(int)resp.StatusCode} {resp.StatusCode} from {addr}");
                    Interlocked.Increment(ref s_totalErrors);
                    continue;
                }

                // Read response body into reusable buffer (zero allocation per iteration)
                respBuf.Length = 0;
                using (var respStream = await resp.Content.ReadAsStreamAsync(ct))
                {
                    long? cl = resp.Content.Headers.ContentLength;
                    if (cl.HasValue)
                    {
                        int len = (int)cl.Value;
                        if (respBuf.Data.Length < len)
                            respBuf.Extend(len - respBuf.Data.Length);
                        int offset = 0, remaining = len;
                        while (remaining > 0)
                        {
                            int n = await respStream.ReadAsync(respBuf.Data.AsMemory(offset, remaining), ct);
                            if (n == 0) break;
                            offset += n;
                            remaining -= n;
                        }
                        respBuf.Length = offset;
                    }
                    else
                    {
                        // chunked transfer: stream until EOF, grow buffer as needed
                        int offset = 0;
                        while (true)
                        {
                            int free = respBuf.Data.Length - offset;
                            if (free == 0)
                            {
                                respBuf.Extend(respBuf.Data.Length);
                                free = respBuf.Data.Length - offset;
                            }
                            int n = await respStream.ReadAsync(respBuf.Data.AsMemory(offset, free), ct);
                            if (n == 0) break;
                            offset += n;
                        }
                        respBuf.Length = offset;
                    }
                }

                long latencyUs = (Stopwatch.GetTimestamp() - t0) * 1_000_000L / Stopwatch.Frequency;

                Interlocked.Increment(ref s_latencyBuckets[GetBucket(latencyUs)]);
                Interlocked.Add(ref s_totalBytesReceived, respBuf.Length);

                // Decompress response if needed
                ReadOnlySpan<byte> decodeSpan = respBuf.Data.AsSpan(0, respBuf.Length);
                if (!string.IsNullOrEmpty(s_compress) && resp.Content.Headers.ContentEncoding.Count > 0)
                {
                    string enc = resp.Content.Headers.ContentEncoding.First();
                    if (enc == "gzip")
                    {
                        var (dbuf, gzipDecompressErr) = GzipCompressor.Uncompress(respBuf.Data.AsSpan(0, respBuf.Length));
                        if (gzipDecompressErr.Err())
                        {
                            Console.WriteLine($"[ERROR] GzipCompressor.Uncompress failed: {gzipDecompressErr}");
                            Interlocked.Increment(ref s_totalErrors);
                            continue;
                        }
                        gzipBuf.Dispose();
                        gzipBuf = dbuf;
                        decodeSpan = gzipBuf.AsSpan();
                    }
                    else if (enc == "zstd")
                    {
                        zstdBuf.Length = 0;
                        var zstdDecompressErr = ZstdCompressor.Uncompress(ref zstdBuf, respBuf.Data.AsSpan(0, respBuf.Length));
                        if (zstdDecompressErr.Err())
                        {
                            Console.WriteLine($"[ERROR] ZstdCompressor.Uncompress failed: {zstdDecompressErr}");
                            Interlocked.Increment(ref s_totalErrors);
                            continue;
                        }
                        decodeSpan = zstdBuf.AsSpan();
                    }
                    Interlocked.Add(ref s_totalDecompressedBytesReceived, decodeSpan.Length);
                }

                // Deserialize response (reuse state.Response via Reset)
                state.Response.Reset();
                if (s_encodeType == "protobuf")
                {
                    var protoRespErr = state.Response.FromProtobuf(decodeSpan);
                    if (protoRespErr.Err())
                    {
                        Console.WriteLine($"[ERROR] Response.FromProtobuf failed: {protoRespErr}");
                        Interlocked.Increment(ref s_totalErrors);
                        continue;
                    }
                }
                else
                {
                    var jsonRespErr = state.Response.FromJSON(decodeSpan);
                    if (jsonRespErr.Err())
                    {
                        Console.WriteLine($"[ERROR] Response.FromJSON failed: {jsonRespErr}");
                        Interlocked.Increment(ref s_totalErrors);
                        continue;
                    }
                }

                Interlocked.Increment(ref s_totalRequests);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.WriteLine($"[ERROR] Unexpected exception: {ex}"); Interlocked.Increment(ref s_totalErrors); }
        }
    }

    static string RandomString(int len)
    {
        var chars = new char[len];
        for (int i = 0; i < len; i++)
            chars[i] = s_chars[Random.Shared.Next(s_chars.Length)];
        return new string(chars);
    }

    static void ParseArgs(string[] args)
    {
        foreach (var arg in args)
        {
            var kv = arg.TrimStart('-').Split('=', 2);
            if (kv.Length != 2) continue;
            switch (kv[0])
            {
                case "http1.addr": s_http1Addr = kv[1]; break;
                case "http2.addr": s_http2Addr = kv[1]; break;
                case "thread.count": int.TryParse(kv[1], out s_threadCount); break;
                case "connection.per.thread": int.TryParse(kv[1], out s_connectionPerThread); break;
                case "task.per.connection": int.TryParse(kv[1], out s_taskPerConnection); break;
                case "compress": s_compress = kv[1]; break;
                case "encode.type": s_encodeType = kv[1]; break;
                case "stress.test.seconds": int.TryParse(kv[1], out s_stressTestSeconds); break;
                case "query.string": s_queryString = kv[1]; break;
            }
        }
    }

    static void PrintStats(TimeSpan elapsed)
    {
        double sec = elapsed.TotalSeconds;
        long total = s_totalRequests;
        long errors = s_totalErrors;

        Console.WriteLine();
        Console.WriteLine("========== Stress Test Results ==========");
        Console.WriteLine($"Duration        : {sec:F2}s");
        Console.WriteLine($"Total requests  : {total}");
        Console.WriteLine($"Errors          : {errors}");
        Console.WriteLine($"QPS             : {total / sec:F1}");
        Console.WriteLine();
        Console.WriteLine($"Sent (raw)      : {FormatBytes(s_totalBytesSent)}  ({FormatBytes(s_totalBytesSent / sec)}/s)");
        if (!string.IsNullOrEmpty(s_compress))
            Console.WriteLine($"Sent (compress) : {FormatBytes(s_totalCompressedBytesSent)}  ({FormatBytes(s_totalCompressedBytesSent / sec)}/s)");
        Console.WriteLine($"Received (raw)  : {FormatBytes(s_totalBytesReceived)}  ({FormatBytes(s_totalBytesReceived / sec)}/s)");
        if (!string.IsNullOrEmpty(s_compress))
            Console.WriteLine($"Recv (decomp)   : {FormatBytes(s_totalDecompressedBytesReceived)}  ({FormatBytes(s_totalDecompressedBytesReceived / sec)}/s)");
        Console.WriteLine($"Avg bytes/req   : {FormatBytes(total > 0 ? s_totalBytesSent / total : 0)}");
        Console.WriteLine($"Avg bytes/resp  : {FormatBytes(total > 0 ? s_totalBytesReceived / total : 0)}");

        Console.WriteLine();
        Console.WriteLine("Latency distribution:");
        Console.WriteLine($"  {"Range",-26} {"Count",10} {"QPS",10} {"Pct%",8} {"CumPct%",10}");
        Console.WriteLine($"  {new string('-', 70)}");

        long cumCount = 0;
        for (int i = 0; i < NumBuckets; i++)
        {
            long cnt = s_latencyBuckets[i];
            if (cnt == 0) continue;
            cumCount += cnt;
            double pct = total > 0 ? cnt * 100.0 / total : 0;
            double cumPct = total > 0 ? cumCount * 100.0 / total : 0;
            Console.WriteLine($"  {FormatRange(i),-26} {cnt,10} {cnt / sec,10:F1} {pct,8:F2} {cumPct,10:F2}");
        }
    }
}
