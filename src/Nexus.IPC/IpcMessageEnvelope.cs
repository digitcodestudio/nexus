namespace Nexus.IPC;

public sealed record IpcMessageEnvelope(
    Guid Id,
    int Version,
    string Command,
    DateTimeOffset TimestampUtc,
    string? PayloadJson);

