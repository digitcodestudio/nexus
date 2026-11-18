using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexus.Core.Safety;

namespace Nexus.IPC;

public sealed class NexusIpcServer : IAsyncDisposable
{
    private const int MaxFrameBytes = 1024 * 1024; // 1 MB DoS cap
    private readonly string _pipeName;
    private readonly Func<IpcMessageEnvelope, CancellationToken, Task<IpcMessageEnvelope>> _handler;
    private readonly ILogger<NexusIpcServer> _logger;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public NexusIpcServer(
        Func<IpcMessageEnvelope, CancellationToken, Task<IpcMessageEnvelope>> handler,
        ILogger<NexusIpcServer> logger,
        string? pipeName = null)
    {
        _handler = handler;
        _logger = logger;
        _pipeName = pipeName ?? GuardrailDefaults.IpcPipeName;
    }

    public void Start(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listenTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                var security = BuildRestrictedPipeSecurity();
                server = NamedPipeServerStreamAcl.Create(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    4096,
                    4096,
                    security);

                await server.WaitForConnectionAsync(ct);
                var req = await ReadFrameAsync(server, ct);
                if (req is not null)
                {
                    var resp = await _handler(req, ct);
                    await WriteFrameAsync(server, resp, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kesalahan sementara pada Named Pipe IPC server.");
                await Task.Delay(300, ct);
            }
            finally
            {
                if (server is not null) await server.DisposeAsync();
            }
        }
    }

    public static PipeSecurity BuildRestrictedPipeSecurity()
    {
        var security = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser is not null)
        {
            security.AddAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.FullControl, AccessControlType.Allow));
        }
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    internal static async Task<IpcMessageEnvelope?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var lenBuf = new byte[4];
        if (!await ReadExactAsync(stream, lenBuf, ct)) return null;
        int len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
        if (len <= 0 || len > MaxFrameBytes) return null;

        var payload = new byte[len];
        if (!await ReadExactAsync(stream, payload, ct)) return null;
        return JsonSerializer.Deserialize<IpcMessageEnvelope>(payload);
    }

    internal static async Task WriteFrameAsync(Stream stream, IpcMessageEnvelope message, CancellationToken ct)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        if (jsonBytes.Length > MaxFrameBytes)
            throw new InvalidOperationException("Ukuran pesan IPC melebihi batas 1 MB.");

        var lenBuf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBuf, jsonBytes.Length);
        await stream.WriteAsync(lenBuf, ct);
        await stream.WriteAsync(jsonBytes, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_listenTask is not null)
            {
                try { await _listenTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            }
            _cts.Dispose();
        }
    }
}

public sealed class NexusIpcClient
{
    private readonly string _pipeName;

    public NexusIpcClient(string? pipeName = null)
    {
        _pipeName = pipeName ?? GuardrailDefaults.IpcPipeName;
    }

    public async Task<IpcMessageEnvelope?> SendAsync(string command, string? payloadJson = null, int timeoutMs = 1500, CancellationToken ct = default)
    {
        await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(timeoutMs, ct);

        var request = new IpcMessageEnvelope(Guid.NewGuid(), 1, command, DateTimeOffset.UtcNow, payloadJson);
        await NexusIpcServer.WriteFrameAsync(client, request, ct);
        return await NexusIpcServer.ReadFrameAsync(client, ct);
    }
}

