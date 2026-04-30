namespace DemoServer.Metrics;

using System.Globalization;
using Microsoft.AspNetCore.Http;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using QiWa.Common;
using QiWa.Compress;

/// <summary>
/// 将 OpenTelemetry Metrics 快照缓存为 Prometheus 文本，供 /metrics 端点直接返回。
/// Export() 由 PeriodicExportingMetricReader 在后台定期调用；HandleMetricsAsync() 由 HTTP 线程读取并压缩输出。
/// 双 RentedBuffer：_current 用于后台线程拼装，_last 保留上次结果供 HTTP 线程压缩输出。
/// </summary>
internal sealed class SnapshotMetricExporter : BaseExporter<Metric>
{
    // used only in WriteDouble; 'G' format output is pure ASCII so char→byte cast is safe
    private readonly char[] _numBuf = new char[32];
    // double-buffer: _current is written by Export(), _last is read by HTTP thread
    private RentedBuffer _current;
    private RentedBuffer _last;
    RentedBuffer compressed;
    private readonly ReaderWriterLockSlim _swapLock = new();
    const int defaultBufferSize = 1024 * 16;


    private SnapshotMetricExporter()
    {
        _current = new RentedBuffer(defaultBufferSize);
        _last = new RentedBuffer(defaultBufferSize);
        compressed = new RentedBuffer(defaultBufferSize);
    }

    public static SnapshotMetricExporter Register(WebApplicationBuilder builder, int exportIntervalMilliseconds)
    {
        var exporter = new SnapshotMetricExporter();
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddReader(new PeriodicExportingMetricReader(exporter, exportIntervalMilliseconds: exportIntervalMilliseconds));
                metrics.AddProcessInstrumentation();
                metrics.AddRuntimeInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddEventCountersInstrumentation(o =>
                {
                    o.AddEventSources("System.Net.Sockets");
                });
                metrics.AddMeter(
                    "System.Runtime",
                    "OpenTelemetry.Instrumentation.Runtime",
                    "System.Net.Http",
                    "System.Net.Sockets",
                    "System.Net.NameResolution",
                    "Microsoft.AspNetCore.Hosting",
                    "Microsoft.AspNetCore.Server.Kestrel",
                    "OpenTelemetry.Instrumentation.Process",
                    "OpenTelemetry.Instrumentation.EventCounters",
                    RuntimeExtraMetrics.MeterName);
            });
        return exporter;
    }

    public async Task HandleMetricsAsync(HttpContext context)
    {
        var acceptEncoding = context.Request.Headers.AcceptEncoding.ToString();
        bool useZstd = acceptEncoding.Contains("zstd", StringComparison.OrdinalIgnoreCase);

        context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";

        if (useZstd)
        {
            compressed.Length = 0;
            _swapLock.EnterReadLock();
            try
            {
                ZstdCompressor.Compress(ref compressed, _last.AsSpan());
            }
            finally
            {
                _swapLock.ExitReadLock();
            }
            context.Response.Headers.ContentEncoding = "zstd";
            context.Response.ContentLength = compressed.Length;
            await context.Response.Body.WriteAsync(compressed.Data.AsMemory(0, compressed.Length));
            return;
        }
        // use gzip
        RentedBuffer gzipData = default;
        Error err = default;
        _swapLock.EnterReadLock();
        try
        {
            (gzipData, err) = GzipCompressor.Compress(_last.AsSpan());
        }
        finally
        {
            _swapLock.ExitReadLock();
        }
        if (err.Err())
        {
            throw new Exception("impossible error");
        }
        context.Response.Headers.ContentEncoding = "gzip";
        context.Response.ContentLength = gzipData.Length;
        await context.Response.Body.WriteAsync(gzipData.Data.AsMemory(0, gzipData.Length));
        gzipData.Dispose();
    }

    public override ExportResult Export(in Batch<Metric> batch)
    {
        _current.Length = 0;
        foreach (var metric in batch)
            WriteMetric(metric);
        _swapLock.EnterWriteLock();
        try
        {
            (_current, _last) = (_last, _current);
        }
        finally
        {
            _swapLock.ExitWriteLock();
        }
        return ExportResult.Success;
    }

    private void WriteMetric(Metric metric)
    {
        var name = ToPrometheusName(metric.Name, metric.Unit, metric.MetricType);
        foreach (ref readonly var point in metric.GetMetricPoints())
            WritePoint(name, metric.MetricType, in point);
    }

    // Normalizes an OTel metric name to Prometheus text-format rules:
    //   - "ec." prefix from EventCounters bridge is stripped (ec.System.Net.Sockets.x → System.Net.Sockets.x)
    //   - name lowercased (Prometheus convention)
    //   - dots/dashes → underscores (Prometheus only allows [a-zA-Z0-9_:])
    //   - well-known OTel units appended as a suffix before _total
    //   - monotonic Sums (Counters) get _total; non-monotonic Sums (UpDownCounters) do not
    private static string ToPrometheusName(string otelName, string? unit, MetricType type)
    {
        var rawName = otelName.StartsWith("ec.", StringComparison.Ordinal)
            ? otelName.Substring(3)
            : otelName;
        var name = rawName.ToLowerInvariant().Replace('.', '_').Replace('-', '_');
        var unitSuffix = unit switch
        {
            "s" => "_seconds",
            "ms" => "_milliseconds",
            "By" => "_bytes",
            "KiBy" => "_kibibytes",
            "MiBy" => "_mebibytes",
            _ => string.Empty,
        };
        if (unitSuffix.Length > 0 && !name.EndsWith(unitSuffix, StringComparison.Ordinal))
            name += unitSuffix;
        if ((type == MetricType.LongSum || type == MetricType.DoubleSum)
            && !name.EndsWith("_total", StringComparison.Ordinal))
            name += "_total";
        return name;
    }

    private void WritePoint(string name, MetricType type, in MetricPoint point)
    {
        long ts = point.EndTime.ToUnixTimeMilliseconds();
        switch (type)
        {
            case MetricType.LongGauge:
                _current.Append(name); WriteTags(point.Tags);
                _current.Append(" "); _current.Append(point.GetGaugeLastValueLong()); _current.Append(" "); _current.Append(ts); _current.Append("\n");
                break;
            case MetricType.DoubleGauge:
                _current.Append(name); WriteTags(point.Tags);
                _current.Append(" "); WriteDouble(point.GetGaugeLastValueDouble());
                _current.Append(" "); _current.Append(ts); _current.Append("\n");
                break;
            case MetricType.LongSum:
            case MetricType.LongSumNonMonotonic:
                _current.Append(name); WriteTags(point.Tags);
                _current.Append(" "); _current.Append(point.GetSumLong()); _current.Append(" "); _current.Append(ts); _current.Append("\n");
                break;
            case MetricType.DoubleSum:
            case MetricType.DoubleSumNonMonotonic:
                _current.Append(name); WriteTags(point.Tags);
                _current.Append(" "); WriteDouble(point.GetSumDouble());
                _current.Append(" "); _current.Append(ts); _current.Append("\n");
                break;
            case MetricType.Histogram:
                WriteHistogram(name, point.Tags, in point, ts);
                break;
            case MetricType.ExponentialHistogram:
                // ExponentialHistogram 无法直接转换为 Prometheus 桶格式，仅输出 count/sum
                WriteExponentialHistogram(name, point.Tags, in point, ts);
                break;
        }
    }

    private void WriteHistogram(string name, ReadOnlyTagCollection tags, in MetricPoint point, long ts)
    {
        _current.Append(name); _current.Append("_count"); WriteTags(tags);
        _current.Append(" "); _current.Append(point.GetHistogramCount()); _current.Append(" "); _current.Append(ts); _current.Append("\n");

        _current.Append(name); _current.Append("_sum"); WriteTags(tags);
        _current.Append(" "); WriteDouble(point.GetHistogramSum());
        _current.Append(" "); _current.Append(ts); _current.Append("\n");

        long cumCount = 0;
        foreach (var bucket in point.GetHistogramBuckets())
        {
            cumCount += bucket.BucketCount;
            _current.Append(name); _current.Append("_bucket");
            WriteTagsWithLe(tags, bucket.ExplicitBound);
            _current.Append(" "); _current.Append(cumCount); _current.Append(" "); _current.Append(ts); _current.Append("\n");
        }
    }

    private void WriteExponentialHistogram(string name, ReadOnlyTagCollection tags, in MetricPoint point, long ts)
    {
        _current.Append(name); _current.Append("_count"); WriteTags(tags);
        _current.Append(" "); _current.Append(point.GetHistogramCount()); _current.Append(" "); _current.Append(ts); _current.Append("\n");

        _current.Append(name); _current.Append("_sum"); WriteTags(tags);
        _current.Append(" "); WriteDouble(point.GetHistogramSum());
        _current.Append(" "); _current.Append(ts); _current.Append("\n");
    }

    // 直接写入 _current，避免构建中间 string；label key 中的 '.' 替换为 '_' 以符合 Prometheus 格式
    private void WriteTags(ReadOnlyTagCollection tags)
    {
        if (tags.Count == 0) return;
        _current.Append("{");
        bool first = true;
        foreach (var kv in tags)
        {
            if (!first) _current.Append(",");
            _current.Append(kv.Key.Replace('.', '_').Replace('-', '_'));
            _current.Append("=\"");
            _current.Append(kv.Value?.ToString() ?? "");
            _current.Append("\"");
            first = false;
        }
        _current.Append("}");
    }

    // 写带 le 标签的 tag set（用于 histogram bucket）
    private void WriteTagsWithLe(ReadOnlyTagCollection tags, double explicitBound)
    {
        _current.Append("{");
        bool first = true;
        foreach (var kv in tags)
        {
            if (!first) _current.Append(",");
            _current.Append(kv.Key.Replace('.', '_').Replace('-', '_'));
            _current.Append("=\"");
            _current.Append(kv.Value?.ToString() ?? "");
            _current.Append("\"");
            first = false;
        }
        if (!first) _current.Append(",");
        _current.Append("le=\"");
        if (double.IsPositiveInfinity(explicitBound))
            _current.Append("+Inf");
        else
            WriteDouble(explicitBound);
        _current.Append("\"}");
    }

    // TryFormat 写入栈上 char[]，使用 InvariantCulture 确保小数点为 '.'，符合 Prometheus 文本格式；
    // 'G' 格式输出仅含 ASCII 字符，直接截断为 byte 写入 _current
    private void WriteDouble(double value)
    {
        if (value >= long.MinValue && value <= long.MaxValue && value == Math.Truncate(value))
        {
            _current.Append((long)value);
            return;
        }
        if (value.TryFormat(_numBuf.AsSpan(), out int written, "G", CultureInfo.InvariantCulture))
        {
            Span<byte> utf8 = stackalloc byte[written];
            for (int i = 0; i < written; i++)
                utf8[i] = (byte)_numBuf[i];
            _current.Append(utf8);
        }
        else
            _current.Append(value.ToString("G", CultureInfo.InvariantCulture));
    }

    private static string PrometheusType(MetricType type) => type switch
    {
        MetricType.LongGauge or MetricType.DoubleGauge => "gauge",
        MetricType.LongSum or MetricType.DoubleSum => "counter",
        MetricType.LongSumNonMonotonic or MetricType.DoubleSumNonMonotonic => "gauge",
        MetricType.Histogram or MetricType.ExponentialHistogram => "histogram",
        _ => "untyped",
    };
}
