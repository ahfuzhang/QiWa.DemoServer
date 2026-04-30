namespace DemoServer.Metrics;

using System.Diagnostics.Metrics;

// .NET 9+ 将运行时指标迁移到内置 System.Runtime Meter，指标名从 process.runtime.dotnet.*
// 改为 dotnet.*，导致 system_runtime_alloc_total 在标准 OTel 栈中不再存在。
// 此类补充该指标，数据来自 GC.GetTotalAllocatedBytes()（堆累计分配字节数）。
internal static class RuntimeExtraMetrics
{
    internal const string MeterName = "DemoServer.Runtime";

    private static readonly Meter s_meter = new(MeterName, "1.0");

    // 注册后由 PeriodicExportingMetricReader 定期观察，输出为 system_runtime_alloc_total
    private static readonly ObservableCounter<long> _ = s_meter.CreateObservableCounter<long>(
        "system.runtime.alloc",
        () => GC.GetTotalAllocatedBytes());
}
