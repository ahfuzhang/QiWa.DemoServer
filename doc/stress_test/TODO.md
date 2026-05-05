* 与传统的 asp.net 的写法，进行性能对比  ✅
* 生成 client 类型的代码
* 在 k8s 环境，部署 20 核的容器进行压测
* 还不支持 GET 请求访问
* proxy 能力的支持

* metrics
  * metrics push 能力，方便做压测时候的观测  ✅
  * metrics 上报，如何把多个 ulong 自动变成 metrics 文本?
    - 如何才能把 output 函数注册进去?
  * histogram 的支持
    - 全局的延迟分布，应该怎么去分桶？ 100 微秒开始，乘以 1.5 的系数
    - 字节数的分桶
  * metrics:
    - 请求量
    - 错误量
    - 总延迟
    - 总字节数  
* profiling
  * trace_me 的能力的接入  ✅
