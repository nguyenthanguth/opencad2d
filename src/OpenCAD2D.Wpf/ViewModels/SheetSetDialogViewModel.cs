using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// How a sheet set dialog changes the set: the edit it makes, the drawings on disk that edit rewrites (the ones the caller
    /// must save and close first if it has them open, and open again after), and the ID of the sheet whose drawing, the first
    /// of those, may be given another name by it (so that the caller can open it again under the new name). The default is the
    /// file's own <see cref="CadSheetSetFile.Change"/>; a palette gives its own to do the saving and reopening and to refresh
    /// its tree. A refusal is thrown, and the dialog says it.
    /// </summary>
    /// <param name="edit">The edit, given the sheet set as the file has it.</param>
    /// <param name="drawings">The paths of the drawings the edit rewrites; empty for none.</param>
    /// <param name="followSheetId">The ID of the sheet whose drawing may be renamed; null for none.</param>
    public delegate void SheetSetChanger(
        Action<CadSheetSet> edit,
        IReadOnlyList<string> drawings,
        string followSheetId
    );

    /// <summary>
    /// What the caller gives a sheet set dialog besides the set: why the set is read-only where it is locked, how a change is
    /// made, and which drawings are open in the session. Every part is optional.
    /// </summary>
    public sealed class SheetSetDialogOptions
    {
        /// <summary>
        /// Gets or sets why the dialog is read-only ("The sheet set is locked by h.le on WS-07."); null for a set that can be
        /// changed. The dialog opens with every value grey, no OK and no Cancel but one Close, and says
        /// "Nothing here can be changed." after it.
        /// </summary>
        public string ReadOnlyReason { get; set; }

        /// <summary>Gets or sets whether the Name row of Sheet Set Properties is read-only: the Create Sheet Set wizard, which names the set on its own page, opens the dialog so.</summary>
        public bool NameReadOnly { get; set; }

        /// <summary>Gets or sets how a change is made; null for <see cref="CadSheetSetFile.Change"/> of the dialog's own file.</summary>
        public SheetSetChanger Changer { get; set; }

        /// <summary>Gets or sets whether a drawing is open in this session, given its full path; null for none is.</summary>
        public Func<string, bool> IsOpenInSession { get; set; }
    }

    /// <summary>
    /// The base of the sheet set dialogs of A12 4b (Sheet Set, Subset and Sheet Properties, Custom Properties, New Sheet,
    /// Rename and Renumber, Import Layouts): the set it works on, whether it is locked, the red line that says why a value is
    /// refused (the dialog's own words, with OK greyed while it shows), the yellow line, and OK. A value is checked as it is
    /// edited, by <see cref="Validate"/>; OK runs <see cref="Save"/> through the caller's <see cref="SheetSetChanger"/>, and a
    /// refusal of the core ends in the red line and leaves the dialog open.
    /// </summary>
    public abstract class SheetSetDialogViewModel : ObservableObject
    {
        private const string HelpPage =
            "https://nguyenthanguth.github.io/opencad2d/guides/presenter/sheet-sets-dialogs.html";

        private readonly SheetSetDialogOptions _options;
        private string _error;
        private string _notice;

        /// <summary>Makes the view model over a set.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        protected SheetSetDialogViewModel(CadSheetSetFile file, SheetSetDialogOptions options)
        {
            File = file ?? throw new ArgumentNullException(nameof(file));
            _options = options ?? new SheetSetDialogOptions();
            file.ReloadIfChanged();
        }

        /// <summary>Gets the sheet set's file.</summary>
        public CadSheetSetFile File { get; }

        /// <summary>Gets the dialog's title: "Sheet Set Properties - Tower".</summary>
        public abstract string Title { get; }

        /// <summary>Gets whether the set is locked: every value is grey and nothing can be applied.</summary>
        public bool IsReadOnly => _options.ReadOnlyReason != null;

        /// <summary>Gets the line a locked set is announced with: "The sheet set is locked by h.le on WS-07. Nothing here can be changed."; null where it is not locked.</summary>
        public string ReadOnlyNotice => IsReadOnly ? _options.ReadOnlyReason + " Nothing here can be changed." : null;

        /// <summary>Gets the red line, in the dialog's words, that says why a value is refused; null where none is. OK is greyed while it shows.</summary>
        public string Error
        {
            get => _error;
            protected set
            {
                if (Set(ref _error, value))
                {
                    Raise(nameof(HasError));
                    Raise(nameof(CanApply));
                }
            }
        }

        /// <summary>Gets whether the red line shows.</summary>
        public bool HasError => _error != null;

        /// <summary>Gets the yellow line, a notice that does not stop OK; null where there is none.</summary>
        public string Notice
        {
            get => _notice;
            protected set
            {
                if (Set(ref _notice, value))
                {
                    Raise(nameof(HasNotice));
                }
            }
        }

        /// <summary>Gets whether the yellow line shows.</summary>
        public bool HasNotice => _notice != null;

        /// <summary>Gets whether OK is live: the set is not locked, no value is refused and the dialog has what it needs.</summary>
        public virtual bool CanApply => !IsReadOnly && !HasError && IsComplete;

        /// <summary>Gets the page of the guide the Help button opens.</summary>
        public virtual string HelpUrl => HelpPage;

        /// <summary>Gets or sets what a button that picks a file asks: given a title, a filter ("Drawing templates (*.dwt)|*.dwt") and the file now named, it returns the path chosen or null. The window sets it to its file dialog.</summary>
        public Func<string, string, string, string> BrowseFile { get; set; }

        /// <summary>Gets or sets what a button that picks a folder asks: given the folder now named, it returns the folder chosen or null. The window sets it to its folder dialog.</summary>
        public Func<string, string> BrowseFolder { get; set; }

        /// <summary>Gets whether the dialog has what OK needs besides a value that is right (a title typed, a template chosen).</summary>
        protected virtual bool IsComplete => true;

        /// <summary>Gets what the caller gave the dialog, for the dialogs it opens in turn.</summary>
        internal SheetSetDialogOptions Options => _options;

        /// <summary>Gets the changer of the caller's; null for the file's own.</summary>
        protected SheetSetChanger Changer => _options.Changer;

        /// <summary>Gets whether a drawing is open in this session.</summary>
        /// <param name="path">The full path of the drawing.</param>
        /// <returns>True when it is.</returns>
        protected bool IsOpenInSession(string path) => path != null && _options.IsOpenInSession?.Invoke(path) == true;

        /// <summary>
        /// Applies the dialog's values to the set, as OK does. Where a value is refused or the core refuses the change, the
        /// red line says why and nothing is saved.
        /// </summary>
        /// <returns>True when the values were applied (or there was nothing to apply); false when the dialog must stay open.</returns>
        public bool Apply()
        {
            if (!CanApply)
            {
                return false;
            }

            try
            {
                Save();
                return true;
            }
            catch (Exception e) when (IsRefusal(e))
            {
                Error = Words(e);
                return false;
            }
        }

        /// <summary>Writes the dialog's values to the set, through <see cref="Commit"/>.</summary>
        protected abstract void Save();

        /// <summary>Checks the values as they stand, in the dialog's own words.</summary>
        /// <returns>Why a value is refused; null where all are right.</returns>
        protected abstract string Validate();

        /// <summary>Makes the change to the set: through the caller's changer where it gave one, else the file's own <see cref="CadSheetSetFile.Change"/>.</summary>
        /// <param name="edit">The edit.</param>
        /// <param name="drawings">The paths of the drawings it rewrites; null for none.</param>
        /// <param name="followSheetId">The ID of the sheet whose drawing may be renamed; null for none.</param>
        protected void Commit(
            Action<CadSheetSet> edit,
            IReadOnlyList<string> drawings = null,
            string followSheetId = null
        )
        {
            if (_options.Changer != null)
            {
                _options.Changer(edit, drawings ?? new string[0], followSheetId);
            }
            else
            {
                File.Change(edit);
            }
        }

        /// <summary>Checks the values again after one was edited, and shows the red line or takes it away.</summary>
        protected void Revalidate()
        {
            Error = IsReadOnly ? null : Validate();
            Raise(nameof(CanApply));
        }

        /// <summary>Gets whether an exception is a refusal the dialog says in its red line, and not a fault of the program.</summary>
        private static bool IsRefusal(Exception e) =>
            e is ArgumentException
            || e is InvalidOperationException
            || e is IOException
            || e is NotSupportedException
            || e is FormatException
            || e is UnauthorizedAccessException;

        /// <summary>The words of a refusal: the message, without the name of the argument .NET puts after an argument's.</summary>
        private static string Words(Exception e)
        {
            string text = e.Message ?? string.Empty;
            int line = text.IndexOfAny(new[] { '\r', '\n' });
            if (line >= 0)
            {
                text = text.Substring(0, line);
            }

            return Regex.Replace(text, @" \(Parameter '[^']*'\)$", string.Empty);
        }

        /// <summary>The title with " (read-only)" after it where the set is locked.</summary>
        /// <param name="title">The title.</param>
        /// <returns>The title to show.</returns>
        protected string Titled(string title) => IsReadOnly ? title + " (read-only)" : title;
    }
}
