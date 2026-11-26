using System;
using System.Collections.Generic;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// One bookmark of a PDF's outline, the tree a reader shows beside the pages: a title, the page it goes to and the
    /// bookmarks under it. A PDF that has <c>Sheets and Views</c> with <c>A-101 Ground floor plan</c> and
    /// <c>A-102 First floor plan</c> under it, as AutoCAD writes a published sheet set, is one bookmark with no page
    /// (<see cref="PageIndex"/> -1) holding two that go to pages 0 and 1.
    /// </summary>
    public sealed class CadPdfBookmark
    {
        private readonly List<CadPdfBookmark> _children = new List<CadPdfBookmark>();

        /// <summary>Makes a bookmark.</summary>
        /// <param name="title">The title the reader shows.</param>
        /// <param name="pageIndex">The page it goes to, counted from 0; -1 for a bookmark that goes nowhere and only holds others.</param>
        /// <exception cref="ArgumentNullException">Thrown when the title is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the page is below -1.</exception>
        public CadPdfBookmark(string title, int pageIndex)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));

            if (pageIndex < -1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pageIndex),
                    pageIndex,
                    "A page is counted from 0, or -1 for none."
                );
            }

            PageIndex = pageIndex;
        }

        /// <summary>Gets the title.</summary>
        public string Title { get; }

        /// <summary>Gets the page the bookmark goes to, counted from 0; -1 for none.</summary>
        public int PageIndex { get; }

        /// <summary>Gets the bookmarks under this one, in order; add to it to nest.</summary>
        public IList<CadPdfBookmark> Children => _children;
    }
}
