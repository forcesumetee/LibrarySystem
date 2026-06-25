using LibraryShared;

namespace LibraryKiosk.Services;

/// <summary>Outcome of a server call, distinguishing "unlicensed" from "unreachable".</summary>
public enum ConnectionState
{
    /// <summary>Request in flight (UI placeholder).</summary>
    Loading,
    /// <summary>200 OK — data is valid.</summary>
    Connected,
    /// <summary>403 — server reachable but product key not activated.</summary>
    Unlicensed,
    /// <summary>Timeout / connection refused / DNS — server not reachable.</summary>
    Unreachable
}

public sealed class MetaResult
{
    public ConnectionState State { get; init; }
    public KioskMetaDto? Meta { get; init; }
    public string? Message { get; init; }

    public static MetaResult Ok(KioskMetaDto meta) =>
        new() { State = ConnectionState.Connected, Meta = meta };

    public static MetaResult Unlicensed(string? message = null) =>
        new() { State = ConnectionState.Unlicensed, Message = message };

    public static MetaResult Unreachable(string? message = null) =>
        new() { State = ConnectionState.Unreachable, Message = message };
}
