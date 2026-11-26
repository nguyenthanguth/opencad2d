using System;
using System.Collections.Generic;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// AutoCAD 2021's View Category dialog, from New View Category… (or Rename and Properties… of a category) in the Sheet Views
    /// tab: the category's name, which must not be empty and must not be a name another category of the set has (letter case
    /// ignored), and the list of callout blocks, which is shown greyed with Add Blocks…: callout blocks are left out of this
    /// version (the set's own list is kept as it is). A name that is refused is said in red under the box and greys OK.
    /// </summary>
    public sealed class ViewCategoryDialogViewModel : ObservableObject
    {
        private readonly Func<string, string> _whyNot;
        private string _name;

        /// <summary>Makes the dialog.</summary>
        /// <param name="name">The name it starts with; empty for a new category.</param>
        /// <param name="whyNot">Says why a name is refused, or null where it is good.</param>
        /// <exception cref="ArgumentNullException">Thrown when the check is null.</exception>
        public ViewCategoryDialogViewModel(string name, Func<string, string> whyNot)
        {
            _whyNot = whyNot ?? throw new ArgumentNullException(nameof(whyNot));
            _name = name ?? string.Empty;
        }

        /// <summary>Gets the page of the guide the Help button opens.</summary>
        public string HelpUrl => SheetSetManagerViewModel.GuideSheetViews;

        /// <summary>Gets or sets the category's name, as typed.</summary>
        public string Name
        {
            get => _name;
            set
            {
                if (Set(ref _name, value ?? string.Empty))
                {
                    Raise(nameof(Error));
                    Raise(nameof(CanOk));
                }
            }
        }

        /// <summary>Gets the name as it is saved: without the spaces at its ends.</summary>
        public string Result => _name.Trim();

        /// <summary>Gets why the name is refused, "A view category is named." or "The sheet set has a view category named Plans."; null where it is good.</summary>
        public string Error => _whyNot(Result);

        /// <summary>Gets whether OK is on: the name is good.</summary>
        public bool CanOk => Error == null;

        /// <summary>Gets the words in the greyed list of callout blocks.</summary>
        public string CalloutHint => "The set names no callout blocks.";

        /// <summary>Gets whether Add Blocks… can be pressed: never, callout blocks being left out.</summary>
        public bool CanAddBlocks => false;

        /// <summary>Gets whether OK was accepted by <see cref="Accept"/>.</summary>
        public bool Accepted { get; private set; }

        /// <summary>Takes the dialog's answer, as OK does.</summary>
        /// <returns>False, and nothing taken, where the name is refused.</returns>
        public bool Accept()
        {
            Accepted = CanOk;
            return Accepted;
        }
    }

    /// <summary>One sheet view as Rename &amp; Renumber View walks them with Previous and Next: its key, number and title, and the sheet it is on.</summary>
    public sealed class SheetViewEntry
    {
        /// <summary>Makes an entry.</summary>
        /// <param name="id">The ID of the sheet view in the sheet set's file.</param>
        /// <param name="number">The number.</param>
        /// <param name="title">The title.</param>
        /// <param name="sheet">The sheet it is on, as "A-501 - Wall Sections".</param>
        public SheetViewEntry(string id, string number, string title, string sheet)
        {
            Id = id;
            Number = number;
            Title = title;
            Sheet = sheet;
        }

        /// <summary>Gets the ID of the sheet view.</summary>
        public string Id { get; }

        /// <summary>Gets the number the view has.</summary>
        public string Number { get; internal set; }

        /// <summary>Gets the title the view has.</summary>
        public string Title { get; internal set; }

        /// <summary>Gets the sheet it is on, as "A-501 - Wall Sections".</summary>
        public string Sheet { get; }
    }

    /// <summary>
    /// AutoCAD 2021's Rename &amp; Renumber View, from the Sheet Views tab: the Number and the View title of one sheet view. Previous
    /// and Next make the changes first, as the sheet's own dialog does, and load the view before or after it in the order of the
    /// tree as shown, going grey at the ends. OK makes the change of the view shown; a title that is empty is refused.
    /// </summary>
    public sealed class RenameRenumberViewDialogViewModel : ObservableObject
    {
        private readonly IReadOnlyList<SheetViewEntry> _views;
        private readonly Func<string, string, string, bool> _apply;
        private int _index;
        private string _number;
        private string _title;

        /// <summary>Makes the dialog over the views and the view it opens on.</summary>
        /// <param name="views">The sheet views in the order of the tree as shown.</param>
        /// <param name="index">The place of the view in <paramref name="views"/>.</param>
        /// <param name="apply">Makes a change given the view's ID, its new number and its new title; false where the set refused it.</param>
        /// <exception cref="ArgumentNullException">Thrown when the views or the change are null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the place is not one of the views.</exception>
        public RenameRenumberViewDialogViewModel(
            IReadOnlyList<SheetViewEntry> views,
            int index,
            Func<string, string, string, bool> apply
        )
        {
            _views = views ?? throw new ArgumentNullException(nameof(views));
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));

            if (index < 0 || index >= views.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "There is no view at that place.");
            }

            Load(index);
        }

        /// <summary>Gets the page of the guide the Help button opens.</summary>
        public string HelpUrl => SheetSetManagerViewModel.GuideSheetViews;

        /// <summary>Gets or sets the view's number, free text.</summary>
        public string Number
        {
            get => _number;
            set => Set(ref _number, value ?? string.Empty);
        }

        /// <summary>Gets or sets the view's title.</summary>
        public string Title
        {
            get => _title;
            set
            {
                if (Set(ref _title, value ?? string.Empty))
                {
                    Raise(nameof(Error));
                    Raise(nameof(CanOk));
                }
            }
        }

        /// <summary>Gets the line under the boxes, "Sheet: A-501 - Wall Sections.".</summary>
        public string Info => "Sheet: " + _views[_index].Sheet + ".";

        /// <summary>Gets why the entries are refused: "A view has a title."; null where they are good.</summary>
        public string Error => _title.Trim().Length == 0 ? "A view has a title." : null;

        /// <summary>Gets whether OK, Previous and Next are on: the title is not empty.</summary>
        public bool CanOk => Error == null;

        /// <summary>Gets whether there is a view before the one shown, and entries to change first.</summary>
        public bool CanPrevious => _index > 0 && CanOk;

        /// <summary>Gets whether there is a view after the one shown, and entries to change first.</summary>
        public bool CanNext => _index < _views.Count - 1 && CanOk;

        /// <summary>Gets the ID of the view shown.</summary>
        public string CurrentId => _views[_index].Id;

        /// <summary>Makes the change of the view shown, as OK does.</summary>
        /// <returns>Whether the set took it.</returns>
        public bool Accept() => CanOk && Apply();

        /// <summary>Makes the change of the view shown and shows the one before it.</summary>
        /// <returns>Whether it moved; false at the first view or where the set refused the change.</returns>
        public bool Previous() => CanPrevious && Apply() && Move(-1);

        /// <summary>Makes the change of the view shown and shows the one after it.</summary>
        /// <returns>Whether it moved; false at the last view or where the set refused the change.</returns>
        public bool Next() => CanNext && Apply() && Move(1);

        private bool Apply()
        {
            SheetViewEntry view = _views[_index];

            if (!_apply(view.Id, _number.Trim(), _title.Trim()))
            {
                return false;
            }

            view.Number = _number.Trim();
            view.Title = _title.Trim();
            return true;
        }

        private bool Move(int by)
        {
            Load(_index + by);
            return true;
        }

        private void Load(int index)
        {
            _index = index;
            _number = _views[index].Number;
            _title = _views[index].Title;
            Raise(nameof(Number));
            Raise(nameof(Title));
            Raise(nameof(Info));
            Raise(nameof(Error));
            Raise(nameof(CanOk));
            Raise(nameof(CanPrevious));
            Raise(nameof(CanNext));
            Raise(nameof(CurrentId));
        }
    }
}
