using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Серверний interceptor gRPC (Лаб. 06) — «middleware для RPC»: для кожного вхідного виклику логує
/// метод, адресу клієнта (peer), статус і тривалість структурованими властивостями.
/// RpcException (NotFound, InvalidArgument…) — Warning зі статусом; інші винятки — Error зі stack trace
/// і перетворення на Internal (деталі — лише в лозі, не клієнту).
/// Підключення в сервісі: builder.Services.AddGrpc(o => o.Interceptors.Add&lt;GrpcLoggingInterceptor&gt;()).
/// </summary>
public sealed class GrpcLoggingInterceptor(ILogger<GrpcLoggingInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var sw = Stopwatch.StartNew();
        var correlationId = context.RequestHeaders.GetValue("x-correlation-id");   // CorrelationId у метаданих gRPC
        Activity.Current?.SetTag("rpc.peer", context.Peer);

        try
        {
            var response = await continuation(request, context);
            logger.LogInformation(
                "gRPC {GrpcMethod} from {Peer}: {GrpcStatus} in {ElapsedMs} ms (CorrelationId {GrpcCorrelationId})",
                context.Method, context.Peer, StatusCode.OK, sw.ElapsedMilliseconds, correlationId);
            return response;
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "gRPC {GrpcMethod} from {Peer}: {GrpcStatus} in {ElapsedMs} ms — {GrpcDetail}",
                context.Method, context.Peer, ex.StatusCode, sw.ElapsedMilliseconds, ex.Status.Detail);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "gRPC {GrpcMethod} from {Peer}: {GrpcStatus} in {ElapsedMs} ms",
                context.Method, context.Peer, StatusCode.Internal, sw.ElapsedMilliseconds);
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error"));
        }
    }
}
