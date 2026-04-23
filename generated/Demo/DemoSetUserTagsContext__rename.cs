#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Generated.Demo;

using System.Text;
using QiWa.Common;
using QiWa.ConsoleLogger;
using QiWa.KestrelWrap;

class SetUserTagsContext : ContextBase, QiWa.Common.IResettable
{
    public ReadonlySetUserTagsRequest Request;
    public SetUserTagsResponse Response;
    //todo: 在这里定义业务处理中的局部变量，从而最终做到 0 alloc
    SetUserTagsRequest writableReq;

    public new void Reset()
    {
        base.Reset();
        Request.Reset();
        Response.Reset();
        //todo: 局部变量的 reset 写在这里
        writableReq.Reset();
    }

    public async ValueTask<Error> Run()
    {
        ref readonly var req = ref Request;
        ref var rsp = ref Response;
        // todo: write your bussiness logic code here
        var buf = new RentedBuffer(1024);  // badcase. should move to class member
        string j;
        try
        {
            req.Clone(ref writableReq);
            writableReq.ToJSON(ref buf);
            //
            L!.Info(
                // 这里把请求流水写到日志
                Field.RawJson("request"u8, buf.AsSpan())
            );
            //
            j = Encoding.UTF8.GetString(buf.AsSpan());
        }
        finally
        {
            buf.Dispose();
        }
        rsp.Code = 0;
        rsp.Message = $"success. req={j}";
        return default;
    }
}
