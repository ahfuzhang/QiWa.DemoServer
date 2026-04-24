
# 目标

实现一个命令行 http client 用于压测。

# 细节 

## 命令行参数

* `-http1.addr=http://127.0.0.1:8091/api/v1/login`
  - 指定 http1 协议的压测地址
* `-http2.addr=http://127.0.0.1:8092/api/v1/login`
  - 指定 http2 协议的压测地址
* 对于 `/api/v1/login` 路径
  - 使用 ./generated/Demo/ 下的类型：
    - 使用 LoginRequest 类型来请求，使用随机值填充各个成员，然后调用 ToJSON() 或者 ToProtobuf() 来序列化
    - 如果有压缩选项，使用 QiWa.Compress 包下面的 gzip 或者 zstd 库来对数据进行压缩
    - 当服务器返回后，使用 ReadonlyLoginResponse 类型的 FromJSON() 或者 FromProtobuf() 来进行反序列化
    - 注意：类型 LoginRequest 和 ReadonlyLoginResponse 要使用 Reset() 来重用，避免新分配对象
* `-thread.count=1`
  - 配置物理线程的数量(其实还是线程池的数量)
    - 调用 ThreadPool.SetMaxThreads() 来配置线程池数量
  - 以 threadlocal 的方式来定义变量，便于对象的数量总是和物理线程相关
* `-connection.per.thread=3`
  - 配置每个物理线程上的 tcp 连接数
  - 每个 HttpClient 对象，只有一个 tcp 连接
  - 然后在每个 HttpClient 上，使用独立的 Task 来循环发包
* `-task.per.connection=5`
  - 对于每个 http2 的请求，在每个 tcp 连接上的并发数

* 也就是说：
  - http1: thread.count * connection.per.thread 决定了并发的总数
  - http2: thread.count * connection.per.thread * task.per.connection 决定了并发的总数

* `-compress=gzip/zstd`
  - 可以设置压缩的方式为 gzip 或者 zstd，不设置时不使用压缩
  - 发送请求时：
    - http header 加上 `Content-Encoding: gzip/zstd`
    - http header 加上 `Accept-Encoding: gzip/zstd`
* `-encode.type=json/protobuf`
  - 可以设置数据序列化的方式，支持 json 或者 protobuf，默认 json

* `-stress.test.seconds=30`
  - 压测的总时间，单位为秒。默认 10 秒
  - 到达这个时间后，停止发送数据，输出压测统计信息

## 连接数限制

* 定义一个全局的 SocketsHttpHandler 对象

```csharp
var handler = new SocketsHttpHandler
{
    // 强制限制对单个服务器的最大 TCP 连接数
    // 设置为 1 表示所有的请求都必须通过这一个连接进行多路复用
    MaxConnectionsPerServer = 1, 

    // 如果当前连接的流达到上限，是否允许开启额外的 TCP 连接？
    // 如果你想严格限制连接数，这里建议设为 false
    EnableMultipleHttp2Connections = false,

     // 2. 优化连接池管理
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
    
    // 3. 开启心跳，确保连接常驻
    KeepAlivePingDelay = TimeSpan.FromSeconds(60),
    KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always
};
```

* 程序启动时：
  - 先配置线程池数量
  - 创建 `connection.per.thread` 指定的 task，每个 task 内创建一个 HttpClient
    - 如果配置了 `task.per.connection`，每个 task 内再创建对应数量的 task，在 task 内循环 post 和接收数据

## 数据统计

* 序列化后的总字节数，再输出平均到每秒的字节数
* 如果配置了压缩，统计压缩后的总字节数，再输出平均到每秒的压缩后字节数
* 记录服务器的延迟，从 100 微秒开始，以 1.5 倍为间隔统计时间分布
  - 某个间隔内为空，不要输出
  - 输出这个区间内的 qps, 输出这个区间内的请求占总请求的百分比，输出第一个区间到当前这个区间的累计百分比
* 记录服务器直接返回的字节数的总和，再输出平均每秒接收字节数
* 如果配置了压缩，记录解压缩后的总字节数，再输出平均到每秒的字节数
* 输出 qps 信息

# 输出

* `StressTest.csproj` 文件，包含了 QiWa.Common 和 QiWa.framework 库。包含了 ./generated/Demo/ 下的项目。
* `Program.cs` 文件，使用传统的 Main() 的格式
* `Makefile`: 提供 make build 命令
