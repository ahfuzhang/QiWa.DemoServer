
# QiWa.DemoServer

本项目展示，如何通过 QiWa (https://github.com/ahfuzhang/QiWa) 这个框架，并配合 BaoHuLu 命令行工具，如何构建一个简单 rpc 服务器。

## 目录结构

* ./proto/Demo.proto
  - 先通过 proto 文件，定义请求格式、响应格式、service+method
* `make QiWa.rpc`
  - 通过命令行工具 `BaoHulu` (https://github.com/ahfuzhang/BaoHuLu) 来生成 rpc 服务器的基本代码
* ./generated/Demo 目录下是代码生成工具生成的代码
  - `ReadonlyXX`: 每个 message 对应的只读对象，用于解码
  - `XX`: 每个 message 对应的写对象，用于编码
  - `${method}Context__rename.cs`: 为每个 method 生成一个 Context 对象。
    - `__rename` 后缀说明：应该在这个文件里填写具体的业务逻辑，之后对文件重新命名
  - `${service}__rename.cs`: 为整个 service 生成 kestrel 框架的回调
    - kestrel 框架中要使用这个类的静态成员来进行注册：

    ```csharp
    var app = builder.Build();
    app.MapFallback(Demo.HandleAsync);
    ```
* ./src/Program.cs
  - 服务的入口
* ./src/GlobalUsing.cs: 把每个地方都会用到的库，使用 `global using` 来引入。
* ./src/KestrelInit.cs: 对 kestrel 框架的参数进行初始化
* ./src/CmdlineArgs/ServerCommandLineOptions.cs: 命令行参数解析

## How to use

* `make QiWa.rpc`
  - 根据 proto 文件，生成 rpc 服务器的基础代码
  - 注意，需要先安装工具：`go install github.com/ahfuzhang/BaoHuLu/cmd/hulu@v0.3.0`
* `make build`
  - 编译项目
* `make run`
  - 运行项目
* `make send_login`
  - 构造 http 1.1 的请求
* `make send_login_h2c`
  - 构造 http 2 的请求  
