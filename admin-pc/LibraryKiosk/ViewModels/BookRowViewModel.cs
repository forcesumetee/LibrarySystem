using System.Collections.Generic;

namespace LibraryKiosk.ViewModels;

/// <summary>
/// One row of up to 3 book cards. The browse grid binds to a list of these rows
/// inside a VirtualizingStackPanel, so only the visible rows are realised — the grid
/// stays smooth with thousands of books (a plain UniformGrid over every book would
/// build every card up front).
/// </summary>
public sealed class BookRowViewModel
{
    public IReadOnlyList<BookCardViewModel> Cards { get; }

    public BookRowViewModel(IReadOnlyList<BookCardViewModel> cards) => Cards = cards;
}
