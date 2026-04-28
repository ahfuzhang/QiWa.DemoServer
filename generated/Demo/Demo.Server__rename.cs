#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Generated.Demo;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.ObjectPool;
using QiWa.Common;
using QiWa.ConsoleLogger;
using QiWa.KestrelWrap;
using QiWa.Metrics;

public class DemoCounters : QiWa.Metrics.MetricsBase, QiWa.Common.IResettable
{

    [PrometheusMetric("http_request_total", "service=\"Demo\",method=\"Login\"")]
    public UInt64 LoginRequestTotal;
    [PrometheusMetric("errors_total", "service=\"Demo\",method=\"Login\",error_type=\"decode\"")]
    public UInt64 LoginDecodeErrorsTotal;
    [PrometheusMetric("errors_total", "service=\"Demo\",method=\"Login\",error_type=\"exception\"")]
    public UInt64 LoginExceptionsTotal;
    [PrometheusMetric("errors_total", "service=\"Demo\",method=\"Login\",error_type=\"logic error\"")]
    public UInt64 LoginLogicErrorsTotal;
    [PrometheusMetric("http_request_total", "service=\"Demo\",method=\"GetUserInfo\"")]
    public UInt64 GetUserInfoRequestTotal;
    [PrometheusMetric("errors_total", "service=\"Demo\",method=\"GetUserInfo\",error_type=\"decode\"")]
    public UInt64 GetUserInfoDecodeErrorsTotal;
    [PrometheusMetric("errors_total", "service=\"Demo\",method=\"GetUserInfo\",error_type=\"exception\"")]
    public UInt64 GetUserInfoExceptionsTotal;
    [PrometheusMetric("errors_total", "service=\"Demo\",method=\"GetUserInfo\",error_type=\"logic error\"")]
    public UInt64 GetUserInfoLogicErrorsTotal;
    [PrometheusMetric("http_request_total", "service=\"Demo\",method=\"SetUserTags\"")]
    public UInt64 SetUserTagsRequestTotal;
    [PrometheusMetric("errors_total", "service=\"Demo\",method=\"SetUserTags\",error_type=\"decode\"")]
    public UInt64 SetUserTagsDecodeErrorsTotal;
    [PrometheusMetric("errors_total", "service=\"Demo\",method=\"SetUserTags\",error_type=\"exception\"")]
    public UInt64 SetUserTagsExceptionsTotal;
    [PrometheusMetric("errors_total", "service=\"Demo\",method=\"SetUserTags\",error_type=\"logic error\"")]
    public UInt64 SetUserTagsLogicErrorsTotal;

    public void Reset()
    {
        LoginRequestTotal = 0;
        LoginDecodeErrorsTotal = 0;
        LoginExceptionsTotal = 0;
        LoginLogicErrorsTotal = 0;
        GetUserInfoRequestTotal = 0;
        GetUserInfoDecodeErrorsTotal = 0;
        GetUserInfoExceptionsTotal = 0;
        GetUserInfoLogicErrorsTotal = 0;
        SetUserTagsRequestTotal = 0;
        SetUserTagsDecodeErrorsTotal = 0;
        SetUserTagsExceptionsTotal = 0;
        SetUserTagsLogicErrorsTotal = 0;
    }
}

public class Demo  // 这里是 service 的名字
{
    internal static readonly DefaultObjectPool<LoginContext> LoginContextPool = new DefaultObjectPool<LoginContext>(
        new ContextObjectPolicy<LoginContext>(),
        maximumRetained: ServerConfig.MaxCocurrentCount
    );
    internal static readonly DefaultObjectPool<GetUserInfoContext> GetUserInfoContextPool = new DefaultObjectPool<GetUserInfoContext>(
        new ContextObjectPolicy<GetUserInfoContext>(),
        maximumRetained: ServerConfig.MaxCocurrentCount
    );
    internal static readonly DefaultObjectPool<SetUserTagsContext> SetUserTagsContextPool = new DefaultObjectPool<SetUserTagsContext>(
        new ContextObjectPolicy<SetUserTagsContext>(),
        maximumRetained: ServerConfig.MaxCocurrentCount
    );

    // ThreadLocal
    internal static readonly ThreadLocal<DemoCounters> _threadLocal =
        new ThreadLocal<DemoCounters>(() => new DemoCounters(), trackAllValues: true);
    public static DemoCounters Counters => _threadLocal.Value!;

    public static DemoCounters SumCounters(DemoCounters? dst)
    {
        if (dst == null)
        {
            dst = new DemoCounters();
        }
        foreach (DemoCounters c in _threadLocal.Values)
        {
            dst.LoginRequestTotal = Interlocked.Read(ref c.LoginRequestTotal);
            dst.LoginDecodeErrorsTotal = Interlocked.Read(ref c.LoginDecodeErrorsTotal);
            dst.LoginExceptionsTotal = Interlocked.Read(ref c.LoginExceptionsTotal);
            dst.LoginLogicErrorsTotal = Interlocked.Read(ref c.LoginLogicErrorsTotal);
            dst.GetUserInfoRequestTotal = Interlocked.Read(ref c.GetUserInfoRequestTotal);
            dst.GetUserInfoDecodeErrorsTotal = Interlocked.Read(ref c.GetUserInfoDecodeErrorsTotal);
            dst.GetUserInfoExceptionsTotal = Interlocked.Read(ref c.GetUserInfoExceptionsTotal);
            dst.GetUserInfoLogicErrorsTotal = Interlocked.Read(ref c.GetUserInfoLogicErrorsTotal);
            dst.SetUserTagsRequestTotal = Interlocked.Read(ref c.SetUserTagsRequestTotal);
            dst.SetUserTagsDecodeErrorsTotal = Interlocked.Read(ref c.SetUserTagsDecodeErrorsTotal);
            dst.SetUserTagsExceptionsTotal = Interlocked.Read(ref c.SetUserTagsExceptionsTotal);
            dst.SetUserTagsLogicErrorsTotal = Interlocked.Read(ref c.SetUserTagsLogicErrorsTotal);            
        }
        return dst;
    }

    public static async Task HandleAsync(HttpContext context)
    {
        Interlocked.Increment(ref ContextBase.Counters.HttpRequestTotal);
        Error err = ContextBase.Validate(context);
        if (err.Err())
        {
            // 打日志
            ThreadLocalLogger.Current.Warn(
                Field.String("path"u8, context.Request.Path.Value ?? ""),
                Field.String("method"u8, context.Request.Method),
                Field.String("protocol"u8, context.Request.Protocol),
                Field.String(
                    (context.Request.HttpContext.Connection.RemoteIpAddress?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        ? "client_ipv6"u8 : "client_ipv4"u8,
                    context.Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""),
                Field.Int64("error_code"u8, err.Code),
                Field.String("message"u8, err.Message)
            );
            // metrics 上报
            Interlocked.Increment(ref ContextBase.Counters.HttpBadRequestTotal);
            return;
        }
        // 判断请求路径
        byte[]? responseBytes;
        switch (context.Request.Path)
        {
            case "/Demo/Login":
            case "/api/v1/login":
                {
                    Interlocked.Increment(ref Counters.LoginRequestTotal);
                    LoginContext ctx = LoginContextPool.Get();
                    using var _ = new QiWa.Helper.ScopeGuard(() =>
                    {
                        LoginContextPool.Return(ctx);
                        //todo: 上报处理时间
                    });
                    err = ctx.InitFromHttp(context);
                    if (err.Err())
                    {
                        // 打日志
                        ThreadLocalLogger.Current.Warn(
                            Field.String("path"u8, context.Request.Path.Value ?? ""),
                            Field.String("method"u8, context.Request.Method),
                            Field.String("protocol"u8, context.Request.Protocol),
                            Field.String(
                                (context.Request.HttpContext.Connection.RemoteIpAddress?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                                    ? "client_ipv6"u8 : "client_ipv4"u8,
                                context.Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""),
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref ContextBase.Counters.InitErrorsTotal);
                        return;
                    }
                    // read http post body
                    err = await ctx.ReadRequest().ConfigureAwait(true);
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref ContextBase.Counters.InitErrorsTotal);
                        return;
                    }
                    // 解压缩
                    byte[]? reqRequest;
                    (reqRequest, err) = ctx.Decompress();
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref ContextBase.Counters.InitErrorsTotal);
                        return;
                    }                    
                    // 解码
                    err = ctx.Decode<ReadonlyLoginRequest>(reqRequest!, ref ctx.Request);
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref Counters.LoginDecodeErrorsTotal);
                        return;
                    }
                    // 调用业务
                    try
                    {
                        // 加上计时
                        err = await ctx.Run().ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, 65535),
                            Field.String("message"u8, ex.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref Counters.LoginExceptionsTotal);
                        context.Response.StatusCode = 500;
                        return;
                    }
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref Counters.LoginLogicErrorsTotal);
                        return;
                    }
                    // 响应
                    (responseBytes, err) = ctx.Encode<LoginResponse>(ref ctx.Response);
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref ContextBase.Counters.EncodeErrorsTotal);
                        return;
                    }
                }
                break;
            case "/Demo/GetUserInfo":
            case "/api/v1/get_user_info":
                {
                    Interlocked.Increment(ref Counters.GetUserInfoRequestTotal);
                    GetUserInfoContext ctx = GetUserInfoContextPool.Get();
                    using var _ = new QiWa.Helper.ScopeGuard(() =>
                    {
                        GetUserInfoContextPool.Return(ctx);
                        //todo: 上报处理时间
                    });
                    err = ctx.InitFromHttp(context);
                    if (err.Err())
                    {
                        // 打日志
                        ThreadLocalLogger.Current.Warn(
                            Field.String("path"u8, context.Request.Path.Value ?? ""),
                            Field.String("method"u8, context.Request.Method),
                            Field.String("protocol"u8, context.Request.Protocol),
                            Field.String(
                                (context.Request.HttpContext.Connection.RemoteIpAddress?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                                    ? "client_ipv6"u8 : "client_ipv4"u8,
                                context.Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""),
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref ContextBase.Counters.InitErrorsTotal);
                        return;
                    }
                    // read http post body
                    err = await ctx.ReadRequest().ConfigureAwait(true);
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref ContextBase.Counters.InitErrorsTotal);
                        return;
                    }
                    // 解压缩
                    byte[]? reqRequest;
                    (reqRequest, err) = ctx.Decompress();
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref ContextBase.Counters.InitErrorsTotal);
                        return;
                    }                    
                    // 解码
                    err = ctx.Decode<ReadonlyGetUserInfoRequest>(reqRequest!, ref ctx.Request);
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref Counters.GetUserInfoDecodeErrorsTotal);
                        return;
                    }
                    // 调用业务
                    try
                    {
                        // 加上计时
                        err = await ctx.Run().ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, 65535),
                            Field.String("message"u8, ex.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref Counters.GetUserInfoExceptionsTotal);
                        context.Response.StatusCode = 500;
                        return;
                    }
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref Counters.GetUserInfoLogicErrorsTotal);
                        return;
                    }
                    // 响应
                    (responseBytes, err) = ctx.Encode<GetUserInfoResponse>(ref ctx.Response);
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref ContextBase.Counters.EncodeErrorsTotal);
                        return;
                    }
                }
                break;
            case "/Demo/SetUserTags":
            case "/api/v1/set_user_tags":
                {
                    Interlocked.Increment(ref Counters.SetUserTagsRequestTotal);
                    SetUserTagsContext ctx = SetUserTagsContextPool.Get();
                    using var _ = new QiWa.Helper.ScopeGuard(() =>
                    {
                        SetUserTagsContextPool.Return(ctx);
                        //todo: 上报处理时间
                    });
                    err = ctx.InitFromHttp(context);
                    if (err.Err())
                    {
                        // 打日志
                        ThreadLocalLogger.Current.Warn(
                            Field.String("path"u8, context.Request.Path.Value ?? ""),
                            Field.String("method"u8, context.Request.Method),
                            Field.String("protocol"u8, context.Request.Protocol),
                            Field.String(
                                (context.Request.HttpContext.Connection.RemoteIpAddress?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                                    ? "client_ipv6"u8 : "client_ipv4"u8,
                                context.Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""),
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref ContextBase.Counters.InitErrorsTotal);
                        return;
                    }
                    // read http post body
                    err = await ctx.ReadRequest().ConfigureAwait(true);
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref ContextBase.Counters.InitErrorsTotal);
                        return;
                    }
                    // 解压缩
                    byte[]? reqRequest;
                    (reqRequest, err) = ctx.Decompress();
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref ContextBase.Counters.InitErrorsTotal);
                        return;
                    }                    
                    // 解码
                    err = ctx.Decode<ReadonlySetUserTagsRequest>(reqRequest!, ref ctx.Request);
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref Counters.SetUserTagsDecodeErrorsTotal);
                        return;
                    }
                    // 调用业务
                    try
                    {
                        // 加上计时
                        err = await ctx.Run().ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, 65535),
                            Field.String("message"u8, ex.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref Counters.SetUserTagsExceptionsTotal);
                        context.Response.StatusCode = 500;
                        return;
                    }
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref Counters.SetUserTagsLogicErrorsTotal);
                        return;
                    }
                    // 响应
                    (responseBytes, err) = ctx.Encode<SetUserTagsResponse>(ref ctx.Response);
                    if (err.Err())
                    {
                        // 打日志
                        ctx.L!.Warn(
                            Field.Int64("error_code"u8, err.Code),
                            Field.String("message"u8, err.Message)
                        );
                        // 数据上报
                        Interlocked.Increment(ref ContextBase.Counters.EncodeErrorsTotal);
                        return;
                    }
                }
                break;
            default:
                // 多个 service 如何处理?
                context.Response.StatusCode = 404;
                // 打日志  => 避免因为扫描路径而产生大量日志。此处避免输出太多日志
                // 数据上报  => 可以考虑使用 thread local
                Interlocked.Increment(ref ContextBase.Counters.HttpNotFoundErrorsTotal);
                return;
        }
        // 输出
        context.Response.StatusCode = 200;
        try
        {
            // ??? 网络发送的时间，是否需要记录
            await context.Response.Body.WriteAsync(responseBytes, context.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            ThreadLocalLogger.Current.Warn(
                Field.String("path"u8, context.Request.Path.Value ?? ""),
                Field.String("method"u8, context.Request.Method),
                Field.String("protocol"u8, context.Request.Protocol),
                Field.String(
                    (context.Request.HttpContext.Connection.RemoteIpAddress?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        ? "client_ipv6"u8 : "client_ipv4"u8,
                    context.Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""),
                Field.Int64("error_code"u8, 65535),
                Field.String("message"u8, ex.Message),
                Field.String("exception"u8, "OperationCanceledException")
            );
            Interlocked.Increment(ref ContextBase.Counters.SendErrorsTotal);
            return;
        }
        // todo: 拦截器调用
    }
}
