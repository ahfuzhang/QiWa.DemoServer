
# 目标

实现一个基于 dotnet asp.net 框架实现的 api 服务器。

# 详细要求

## 命令行参数

* `-http1.port=8091`
  - http 1.1 协议的监听端口
* `-http2.port=8092`
  - http 2 协议的监听端口
* `-cores=1`
  - 设置线程池的最大数量

## 请求路径

* `/api/v1/login`
* 只接受 post 请求
* 使用 attirbute 来处理路径映射

## 请求格式

```protobuf
message LoginRequest {
  string user_name = 1;
  string user_password_sha256 = 2;
}
```

使用 attirbute 来对类的成员进行注解，然后使用标准库的 json 编解码方法。

## 响应格式

```protobuf
message LoginResponse {
  uint32 code = 1;
  string message = 2;
  uint64 user_id = 3;
  string user_session = 4;
}
```
使用 attirbute 来对类的成员进行注解，然后使用标准库的 json 编解码方法。

## 主要业务逻辑

* 打印请求流水日志
  - json 格式
  - 包含以下字段：
    - "request": 把请求格式化为 json 字符串
    - "path": http 请求路径
    - "method": post 或者 get
    - "protocol": http/1.1 或者 http/2
    - 如果 client ip 是 ipv6 格式：输出 "client_ipv6":"${client_ip_v6}"; 如果是 ipv4，则输出 "client_ipv4":"${client_ip_v4}"
    - "_file": 当前的源码的文件名
    - "_line": 当前源码的行号
    - "_func": 当前源码的函数名
    - "_time": UTC 格式的当前时间的字符串
* 返回:

```csharp
        rsp.Code = 0;
        rsp.Message = $"success. req={request_json_string}";
```

## metrics 上报

* 上报请求量，错误量等 counter
* 上报接口延迟

# 输出

* AspNetServer.csproj: 工程文件
* Program.cs: 主要代码逻辑，使用传统的 Main() 函数格式
* Makefile: 提供 make build 进行编译

