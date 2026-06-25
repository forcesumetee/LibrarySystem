using System.Collections.Generic;
using System.Windows.Media.Imaging;
using LibraryShared;

namespace LibraryKiosk.Services;

/// <summary>Immutable result of one <see cref="SyncService.SyncNowAsync"/> pass.</summary>
public sealed class SyncSnapshot
{
    public ConnectionState State { get; init; } = ConnectionState.Loading;
    public KioskMetaDto? Meta { get; init; }
    public IReadOnlyList<BookDto> Books { get; init; } = new List<BookDto>();

    /// <summary>Distinct categories for chips, with "ทั้งหมด" prepended.</summary>
    public IReadOnlyList<string> Categories { get; init; } = new List<string>();

    public BitmapSource? Logo { get; init; }
    public BitmapSource? Background { get; init; }
    public string? Message { get; init; }
}
