namespace DemoServer.Metrics;

using System.Globalization;
using System.Text;
using OpenTelemetry;
using OpenTelemetry.Metrics;

/// <summary>
/// 将 OpenTelemetry Metrics 快照缓存为 Prometheus 文本，供 /metrics 端点直接返回。
/// Export() 由 PeriodicExportingMetricReader 在后台定期调用；GetScrape() 由 HTTP 线程读取。
/// StringBuilder / char[] 作为成员复用，避免频繁分配。
/// </summary>
internal sealed class SnapshotMetricExporter : BaseExporter<Metric>
{
    // 预分配 64 KB；大多数情况下不会触发扩容
    private readonly StringBuilder _sb = new(capacity: 64 * 1024);
    // 用于 double/float 无分配格式化（TryFormat 写入 Span）
    private readonly char[] _numBuf = new char[32];
    // volatile 保证 HTTP 线程读到最新写入，无需加锁
    private volatile string _lastScrape = string.Empty;

    public string GetScrape() => _lastScrape;

    public override ExportResult Export(in Batch<Metric> batch)
    {
        _sb.Clear();
        foreach (var metric in batch)
            WriteMetric(metric);
        _lastScrape = _sb.ToString();
        return ExportResult.Success;
    }

    private void WriteMetric(Metric metric)
    {
        //if (!string.IsNullOrEmpty(metric.Description))
        //    _sb.Append("# HELP ").Append(metric.Name).Append(' ').AppendLine(metric.Description);
        //_sb.Append("# TYPE ").Append(metric.Name).Append(' ').AppendLine(PrometheusType(metric.MetricType));

        foreach (ref readonly var point in metric.GetMetricPoints())
            WritePoint(metric.Name, metric.MetricType, in point);
    }

    private void WritePoint(string name, MetricType type, in MetricPoint point)
    {
        long ts = point.EndTime.ToUnixTimeMilliseconds();
        switch (type)
        {
            case MetricType.LongGauge:
                _sb.Append(name); WriteTags(point.Tags);
                _sb.Append(' ').Append(point.GetGaugeLastValueLong()).Append(' ').Append(ts).AppendLine();
                break;
            case MetricType.DoubleGauge:
                _sb.Append(name); WriteTags(point.Tags);
                _sb.Append(' '); WriteDouble(point.GetGaugeLastValueDouble());
                _sb.Append(' ').Append(ts).AppendLine();
                break;
            case MetricType.LongSum:
                _sb.Append(name); WriteTags(point.Tags);
                _sb.Append(' ').Append(point.GetSumLong()).Append(' ').Append(ts).AppendLine();
                break;
            case MetricType.DoubleSum:
                _sb.Append(name); WriteTags(point.Tags);
                _sb.Append(' '); WriteDouble(point.GetSumDouble());
                _sb.Append(' ').Append(ts).AppendLine();
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
        _sb.Append(name).Append("_count"); WriteTags(tags);
        _sb.Append(' ').Append(point.GetHistogramCount()).Append(' ').Append(ts).AppendLine();

        _sb.Append(name).Append("_sum"); WriteTags(tags);
        _sb.Append(' '); WriteDouble(point.GetHistogramSum());
        _sb.Append(' ').Append(ts).AppendLine();

        long cumCount = 0;
        foreach (var bucket in point.GetHistogramBuckets())
        {
            cumCount += bucket.BucketCount;
            _sb.Append(name).Append("_bucket");
            WriteTagsWithLe(tags, bucket.ExplicitBound);
            _sb.Append(' ').Append(cumCount).Append(' ').Append(ts).AppendLine();
        }
    }

    private void WriteExponentialHistogram(string name, ReadOnlyTagCollection tags, in MetricPoint point, long ts)
    {
        _sb.Append(name).Append("_count"); WriteTags(tags);
        _sb.Append(' ').Append(point.GetHistogramCount()).Append(' ').Append(ts).AppendLine();

        _sb.Append(name).Append("_sum"); WriteTags(tags);
        _sb.Append(' '); WriteDouble(point.GetHistogramSum());
        _sb.Append(' ').Append(ts).AppendLine();
    }

    // 直接写入 _sb，避免构建中间 string
    private void WriteTags(ReadOnlyTagCollection tags)
    {
        if (tags.Count == 0) return;
        _sb.Append('{');
        bool first = true;
        foreach (var kv in tags)
        {
            if (!first) _sb.Append(',');
            _sb.Append(kv.Key).Append("=\"").Append(kv.Value).Append('"');
            first = false;
        }
        _sb.Append('}');
    }

    // 写带 le 标签的 tag set（用于 histogram bucket）
    private void WriteTagsWithLe(ReadOnlyTagCollection tags, double explicitBound)
    {
        _sb.Append('{');
        bool first = true;
        foreach (var kv in tags)
        {
            if (!first) _sb.Append(',');
            _sb.Append(kv.Key).Append("=\"").Append(kv.Value).Append('"');
            first = false;
        }
        if (!first) _sb.Append(',');
        _sb.Append("le=\"");
        if (double.IsPositiveInfinity(explicitBound))
            _sb.Append("+Inf");
        else
            WriteDouble(explicitBound);
        _sb.Append("\"}");
    }

    // TryFormat 写入栈上 char[]，避免 double.ToString() 的堆分配；
    // 使用 InvariantCulture 确保小数点为 '.'，符合 Prometheus 文本格式
    private void WriteDouble(double value)
    {
        if (value.TryFormat(_numBuf.AsSpan(), out int written, "G", CultureInfo.InvariantCulture))
            _sb.Append(_numBuf, 0, written);
        else
            _sb.Append(value.ToString("G", CultureInfo.InvariantCulture));
    }

    private static string PrometheusType(MetricType type) => type switch
    {
        MetricType.LongGauge or MetricType.DoubleGauge => "gauge",
        MetricType.LongSum or MetricType.DoubleSum => "counter",
        MetricType.Histogram or MetricType.ExponentialHistogram => "histogram",
        _ => "untyped",
    };
}
