using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using GeometryHelper.Dxf;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// The <c>\AcSm</c> fields of a drawing: a title block's <c>%&lt;\AcSm Sheet.Number&gt;%</c>, a set's own
    /// <c>%&lt;\AcSm SheetSet.Client&gt;%</c> and the bound form AutoCAD writes into a drawing once the drawing is a sheet,
    /// <c>\AcSm Database("D:\Tower\Tower.dst").SheetSet("g…").Component("g…").Number</c>. They are evaluated as AutoCAD 2021
    /// evaluates them (each result below was read from AutoCAD's own FIELD objects for a sheet A-101 "Ground floor" in the subset
    /// Plans); the field dialog's <c>CurrentSheetNumber</c> and its kin write the unbound form and no <c>\AcVar</c>, which
    /// AutoCAD shows as <c>####</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A field is of one component of a set: <c>Sheet</c>, <c>Subset</c> (the subset the sheet is in) or <c>SheetSet</c>; the
    /// names of the component and of its property are read in any letter case. <c>Sheet</c> has Number, Title, NumberAndTitle
    /// (<c>A-101 - Ground floor</c>; the title alone with no number), Description, Category, RevisionNumber, RevisionDate,
    /// IssuePurpose and Name, then the sheet's custom properties (the sheet's own value, else the default of the set's
    /// definition). <c>Subset</c> has Name and Description. <c>SheetSet</c> has Name, Description, ProjectNumber, ProjectName,
    /// ProjectPhase, ProjectMilestone, then the set's custom properties. A property with no value shows <c>----</c>; a name
    /// the component does not have shows <c>####</c>. A placeholder, <c>?Sheet.Number</c> or <c>?View.Title</c>, shows its own
    /// name (<c>SheetNumber</c>, <c>ViewTitle</c>) until a block that holds it is bound.
    /// </para>
    /// <para>
    /// The sheet of an unbound field is the one whose drawing is the field's own and whose layout is the layout the field is
    /// on (found by its handle, else its name), in the set the drawing's link names (<c>AcSheetSetData</c>). Where the set's
    /// file is no longer where the link or the code says it is, the file is looked for the same distance from the drawing as
    /// it was when the link was written, which finds a set moved with its sheets. A field whose set or sheet cannot be reached
    /// keeps the words it showed; one never evaluated shows <c>####</c>.
    /// </para>
    /// </remarks>
    public static class CadSheetSetFields
    {
        private const string NoValue = "----";
        private const string Error = "####";
        private static readonly string[] SheetPlaceholders = { "Number", "Title", "NumberAndTitle", "Description" };
        private static readonly string[] ViewPlaceholders = { "Number", "Title", "NumberAndTitle" };

        /// <summary>
        /// Binds the unbound fields of one layout of a sheet's drawing to its sheet, as AutoCAD does when a sheet is made or a
        /// layout imported: <c>\AcSm Sheet.Number</c> becomes <c>\AcSm Database("…\Tower.dst").SheetSet("g…").Component("g…").Number</c>
        /// with the sheet's ID, <c>Subset.Name</c> the same with the subset's ID, and <c>SheetSet.Client</c> the same with no
        /// component, so that AutoCAD shows the right words in a sheet made here. The fields are evaluated again; a placeholder,
        /// a field already bound and a field of another evaluator are left as they are, and so is every field of another layout or
        /// of model space. A sheet in the set itself has no subset, so its <c>Subset.Name</c> stays unbound and shows <c>####</c>.
        /// </summary>
        /// <param name="document">The sheet's drawing.</param>
        /// <param name="layoutName">The name of the layout that is the sheet.</param>
        /// <param name="file">The sheet set the sheet is in.</param>
        /// <param name="sheet">The sheet, of <paramref name="file"/>.</param>
        /// <returns>How many fields were bound.</returns>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public static int Bind(CadDocument document, string layoutName, CadSheetSetFile file, CadSheetSetSheet sheet)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (layoutName == null)
            {
                throw new ArgumentNullException(nameof(layoutName));
            }

            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            DxfLayout layout = null;

            foreach (DxfLayout one in document.Database.Layouts)
            {
                if (
                    one.BlockTableRecord != null
                    && !string.Equals(one.LayoutName, "Model", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(one.LayoutName, layoutName, StringComparison.OrdinalIgnoreCase)
                )
                {
                    layout = one;
                    break;
                }
            }

            if (layout == null)
            {
                return 0;
            }

            var context = new CadFieldContext(document);
            int bound = 0;

            foreach (DxfEntity entity in layout.BlockTableRecord)
            {
                if (entity is DxfBlockReference insert)
                {
                    foreach (DxfAttributeReference attribute in insert.AttributeCollection)
                    {
                        bound += BindText(attribute, context, file, sheet);
                    }
                }
                else
                {
                    bound += BindText(entity, context, file, sheet);
                }
            }

            return bound;
        }

        private static int BindText(
            DxfEntity entity,
            CadFieldContext context,
            CadSheetSetFile file,
            CadSheetSetSheet sheet
        ) =>
            CadFields.IsText(entity) && CadFields.Has(entity)
                ? CadFields.Rewrite(entity, draft => BindDraft(draft, file, sheet), context)
                : 0;

        private static int BindDraft(DxfFieldDraft draft, CadSheetSetFile file, CadSheetSetSheet sheet)
        {
            int bound = 0;

            foreach (
                DxfFieldDraft one in draft.Evaluator == "_text" ? draft.Children : new List<DxfFieldDraft> { draft }
            )
            {
                Target target = one.Evaluator == "AcSm" ? Target.Parse(one.Code) : null;

                if (target == null || target.Bound || target.Component.StartsWith("?", StringComparison.Ordinal))
                {
                    continue;
                }

                string component;

                if (target.Component.Equals("Sheet", StringComparison.OrdinalIgnoreCase))
                {
                    component = sheet.Id;
                }
                else if (target.Component.Equals("SheetSet", StringComparison.OrdinalIgnoreCase))
                {
                    component = null;
                }
                else if (
                    target.Component.Equals("Subset", StringComparison.OrdinalIgnoreCase)
                    && sheet.Parent != null
                    && !(sheet.Parent is CadSheetSet)
                )
                {
                    component = sheet.Parent.Id;
                }
                else
                {
                    continue;
                }

                one.Code =
                    "\\AcSm Database(\""
                    + file.Path
                    + "\").SheetSet(\""
                    + file.SheetSet.Id
                    + "\")"
                    + (component == null ? string.Empty : ".Component(\"" + component + "\")")
                    + "."
                    + target.Property
                    + target.Suffix;
                bound++;
            }

            return bound;
        }

        /// <summary>Gives each AcSm field of a draft the words its FIELD showed, for the evaluation to keep where the set cannot be reached.</summary>
        internal static void Remember(DxfField field, DxfFieldDraft draft)
        {
            if (draft.Evaluator == "AcSm")
            {
                draft.Shown = field.ShownText();
                return;
            }

            DxfField[] children = field.GetChildren();

            for (int i = 0; i < draft.Children.Count && i < children.Length; i++)
            {
                if (draft.Children[i].Evaluator == "AcSm" && children[i] != null)
                {
                    draft.Children[i].Shown = children[i].ShownText();
                }
            }
        }

        /// <summary>
        /// Evaluates a field of the evaluator AcSm: its words, its value, the data AutoCAD keeps by name (the component and the
        /// property, and for a bound field the set's file and ID and the component's ID), and its state, status and error. A field
        /// whose set cannot be reached keeps the words it came with in <see cref="DxfFieldDraft.Shown"/>.
        /// </summary>
        internal static void Evaluate(DxfFieldDraft field, CadFieldContext context)
        {
            string cached = field.Shown ?? string.Empty;
            string format = DxfField.FormatOf(field.Code);
            field.Status = 2;
            field.Error = 0;
            field.Message = string.Empty;
            field.Format = format;
            field.Data.Clear();
            Target target = Target.Parse(field.Code);

            if (target == null)
            {
                Fail(field, format, cached, 16, 0, 41);
                return;
            }

            if (IsPlaceholder(target))
            {
                Name(field, "SheetSetCompName", target.Component);
                Name(field, "SheetSetPropertyName", target.Property);
                Succeed(field, format, Placeholder(target));
                return;
            }

            if (target.Bound)
            {
                if (target.ComponentId != null)
                {
                    Name(field, "SheetSetCompId", target.ComponentId);
                    Name(field, "SheetSetCompName", "Component");
                }

                Name(field, "SheetSetFile", target.Database);
                Name(field, "SheetSetId", target.SetId);
            }
            else
            {
                Name(field, "SheetSetCompName", target.Component);
            }

            Name(field, "SheetSetPropertyName", target.Property);
            CadSheetSetFile file = null;
            CadSheetComponent component = null;
            CadSheetView view = null;
            CadSheetSetSheet sheet = null;
            int status = 32;
            int error = 22;

            if (target.Bound)
            {
                file = Open(CandidatesOf(target.Database, context), target.SetId);

                if (file != null)
                {
                    component = target.ComponentId == null ? file.SheetSet : file.SheetSet.Find(target.ComponentId);
                    view = component == null ? ViewOf(file, target.ComponentId) : null;

                    if (component == null && view == null)
                    {
                        status = 64;
                        error = 80;
                    }
                    else
                    {
                        sheet = component as CadSheetSetSheet;
                    }
                }
            }
            else
            {
                sheet = SheetOfField(context, out file);

                if (sheet != null)
                {
                    if (target.Component.Equals("Sheet", StringComparison.OrdinalIgnoreCase))
                    {
                        component = sheet;
                    }
                    else if (target.Component.Equals("SheetSet", StringComparison.OrdinalIgnoreCase))
                    {
                        component = file.SheetSet;
                    }
                    else if (
                        target.Component.Equals("Subset", StringComparison.OrdinalIgnoreCase)
                        && !(sheet.Parent is CadSheetSet)
                    )
                    {
                        component = sheet.Parent;
                    }
                }
            }

            string text = null;

            if (
                !(component == null && view == null)
                && (
                    view != null
                        ? ReadView(view, target.Property, out text)
                        : Read(file, component, target.Property, out text)
                )
            )
            {
                string drawing = sheet == null ? null : DrawingOf(file, sheet);

                if (drawing != null && target.Bound)
                {
                    Name(field, "SheetSetDwgFile", drawing);
                    field.Data.Add(
                        new KeyValuePair<string, DxfFieldValue>(
                            "SheetSetDwgFileDate",
                            new DxfFieldValue
                            {
                                Flags = 2,
                                Type = 8,
                                Date = File.GetLastWriteTime(drawing),
                            }
                        )
                    );
                }

                field.Data.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                Succeed(field, format, text);
                return;
            }

            field.Data.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            Fail(field, format, cached, status, error, 43);
        }

        private static void Name(DxfFieldDraft field, string key, string value) =>
            field.Data.Add(new KeyValuePair<string, DxfFieldValue>(key, DxfFieldValue.Named(value)));

        /// <summary>The words of a field that came to a value; a value with no words shows as hyphens.</summary>
        private static void Succeed(DxfFieldDraft field, string format, string text)
        {
            string shown = text.Length == 0 ? NoValue : CadFieldFormat.Parse(format).Words(text);
            field.State = 59;
            field.Value = new DxfFieldValue
            {
                Flags = 4,
                Type = 4,
                Text = text,
                Format = format,
                Shown = shown,
            };
            field.Shown = shown;
        }

        /// <summary>A field that came to nothing: its words as they were where it showed any, else ####.</summary>
        private static void Fail(DxfFieldDraft field, string format, string cached, int status, int error, int state)
        {
            field.Status = status;
            field.Error = error;
            field.State = state;

            if (cached.Length > 0 && cached != Error)
            {
                field.Value = new DxfFieldValue
                {
                    Flags = 4,
                    Type = 4,
                    Text = cached,
                    Format = format,
                    Shown = cached,
                };
                field.Shown = cached;
                return;
            }

            field.Value = DxfFieldValue.None(format);
            field.Shown = Error;
        }

        /// <summary>
        /// What a placeholder shows, as AutoCAD shows it: the name of its type for the properties a placeholder is made for
        /// (SheetNumber, SheetTitle, SheetNumberAndTitle, SheetDescription, ViewNumber, ViewTitle, ViewNumberAndTitle,
        /// ViewportScale), the property's name as written for any other property of a sheet (a custom one: <c>Drawn By</c>),
        /// and hyphens for any other of a view.
        /// </summary>
        private static string Placeholder(Target target)
        {
            bool view = target.Component.Equals("?View", StringComparison.OrdinalIgnoreCase);

            foreach (string type in view ? ViewPlaceholders : SheetPlaceholders)
            {
                if (type.Equals(target.Property, StringComparison.OrdinalIgnoreCase))
                {
                    return (view ? "View" : "Sheet") + type;
                }
            }

            if (view)
            {
                return target.Property.Equals("ViewportScale", StringComparison.OrdinalIgnoreCase)
                    ? "ViewportScale"
                    : NoValue;
            }

            return target.Property;
        }

        private static bool IsPlaceholder(Target target) =>
            !target.Bound
            && (
                target.Component.Equals("?Sheet", StringComparison.OrdinalIgnoreCase)
                || target.Component.Equals("?View", StringComparison.OrdinalIgnoreCase)
            );

        // ---- Finding the set and the sheet ----

        /// <summary>The code of an AcSm field taken apart; null for a code that is none of AutoCAD's forms.</summary>
        internal static Target TargetOf(string code) => Target.Parse(code);

        /// <summary>Opens the set of a bound field's code, found as the evaluation finds it; null where it cannot be reached.</summary>
        internal static CadSheetSetFile OpenSet(string path, string setId, CadFieldContext context) =>
            Open(CandidatesOf(path, context), setId);

        /// <summary>The sheet of the layout a field is on, and its set; null where the drawing is no sheet of a set it can reach, or the field is not on a layout.</summary>
        internal static CadSheetSetSheet SheetOfField(CadFieldContext context, out CadSheetSetFile file)
        {
            file = null;
            CadDocument document = context.Document;
            DxfLayout layout = LayoutOf(context);
            CadSheetSetHint link = CadSheetSetHint.Read(document.Database);

            if (layout == null || link == null)
            {
                return null;
            }

            file = Open(CandidatesOf(null, context), null);

            if (file == null)
            {
                return null;
            }

            string path = DrawingPath(document, link);
            CadSheetSetSheet byName = null;

            foreach (CadSheetSetSheet sheet in file.SheetSet.AllSheets())
            {
                CadDrawingReference reference = sheet.Layout;

                if (reference == null || !IsFile(reference.File, file.Folder, path))
                {
                    continue;
                }

                if (
                    reference.Handle != null
                    && layout.Handle != null
                    && string.Equals(reference.Handle, layout.Handle, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return sheet;
                }

                if (
                    byName == null
                    && string.Equals(reference.Name, layout.LayoutName, StringComparison.OrdinalIgnoreCase)
                )
                {
                    byName = sheet;
                }
            }

            return byName;
        }

        /// <summary>The layout a field is on: that of the text it is in, else (a text not yet in the drawing, a dialog's preview) the layout shown; null for model space and for a block.</summary>
        private static DxfLayout LayoutOf(CadFieldContext context)
        {
            CadDocument document = context.Document;
            string block = context.Entity?.BlockName;

            if (block == null)
            {
                CadSpace space = document.CurrentSpace;
                block = space == null || space.IsModel ? null : space.Block?.Name;
            }

            if (block == null)
            {
                return null;
            }

            foreach (DxfLayout layout in document.Database.Layouts)
            {
                if (
                    layout.BlockTableRecord != null
                    && !string.Equals(layout.LayoutName, "Model", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(layout.BlockTableRecord.Name, block, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return layout;
                }
            }

            return null;
        }

        /// <summary>The path of the drawing: the file it was opened from, else the one its link says.</summary>
        private static string DrawingPath(CadDocument document, CadSheetSetHint link) =>
            !string.IsNullOrEmpty(document.FileName) && Path.IsPathRooted(document.FileName)
                ? document.FileName
                : link?.SheetDrawingName;

        /// <summary>Where a set's file may be: the path the code gives, the path the drawing's link gives, and the link's path moved with the drawing.</summary>
        private static IEnumerable<string> CandidatesOf(string given, CadFieldContext context)
        {
            if (!string.IsNullOrEmpty(given))
            {
                yield return given;
            }

            CadSheetSetHint link = CadSheetSetHint.Read(context.Document.Database);

            if (link == null)
            {
                yield break;
            }

            yield return link.SheetSetFileName;
            string moved = Moved(link, DrawingPath(context.Document, link));

            if (moved != null)
            {
                yield return moved;
            }
        }

        /// <summary>The set's file where the drawing now is, as far from it as the link says it was.</summary>
        private static string Moved(CadSheetSetHint link, string here)
        {
            if (
                string.IsNullOrEmpty(here)
                || !Path.IsPathRooted(here)
                || string.IsNullOrEmpty(link.SheetDrawingName)
                || !Path.IsPathRooted(link.SheetDrawingName)
                || !Path.IsPathRooted(link.SheetSetFileName)
            )
            {
                return null;
            }

            try
            {
                var from = new Uri(Path.GetDirectoryName(link.SheetDrawingName).TrimEnd('\\') + "\\");
                string relative = Uri.UnescapeDataString(
                        from.MakeRelativeUri(new Uri(link.SheetSetFileName)).ToString()
                    )
                    .Replace('/', '\\');
                return relative.IndexOf(':') >= 0
                    ? null
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), relative));
            }
            catch (Exception e)
                when (e is UriFormatException
                    || e is ArgumentException
                    || e is NotSupportedException
                    || e is PathTooLongException
                )
            {
                return null;
            }
        }

        /// <summary>Opens the first of the files that is a sheet set (the one of the ID, where one is asked for); null where none is.</summary>
        private static CadSheetSetFile Open(IEnumerable<string> candidates, string setId)
        {
            foreach (string path in candidates)
            {
                try
                {
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    {
                        continue;
                    }

                    CadSheetSetFile file = CadSheetSetFile.Open(path);

                    if (setId == null || file.SheetSet.Id == setId)
                    {
                        return file;
                    }
                }
                catch (Exception e)
                    when (e is IOException
                        || e is UnauthorizedAccessException
                        || e is FormatException
                        || e is XmlException
                        || e is ArgumentException
                        || e is NotSupportedException
                        || e is InvalidOperationException
                    )
                {
                    // A file that cannot be read as a sheet set is no set to find.
                }
            }

            return null;
        }

        /// <summary>Whether a file reference names a file, by its relative form against the set's folder or its absolute form; the file need not exist.</summary>
        private static bool IsFile(CadFileReference reference, string setFolder, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                string want = Path.GetFullPath(path);

                foreach (
                    string form in new[]
                    {
                        reference.RelativeFileName == null ? null : Path.Combine(setFolder, reference.RelativeFileName),
                        reference.FileName,
                    }
                )
                {
                    if (
                        !string.IsNullOrEmpty(form)
                        && string.Equals(Path.GetFullPath(form), want, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return true;
                    }
                }
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                // A form that is no path names no file.
            }

            return false;
        }

        /// <summary>The file of a sheet's drawing, where it exists.</summary>
        private static string DrawingOf(CadSheetSetFile file, CadSheetSetSheet sheet)
        {
            string path = sheet.Layout?.File.Resolve(file.Folder);
            return path != null && File.Exists(path) ? path : null;
        }

        private static CadSheetView ViewOf(CadSheetSetFile file, string id)
        {
            if (id == null)
            {
                return null;
            }

            foreach (CadSheetSetSheet sheet in file.SheetSet.AllSheets())
            {
                foreach (CadSheetView view in sheet.Views)
                {
                    if (view.Id == id)
                    {
                        return view;
                    }
                }
            }

            return null;
        }

        // ---- Reading a property ----

        /// <summary>Reads a property of a sheet, a subset or the set; false where the component has no such property.</summary>
        private static bool Read(CadSheetSetFile file, CadSheetComponent component, string property, out string text)
        {
            text = null;
            string name = property.ToUpperInvariant();

            if (component is CadSheetSetSheet sheet)
            {
                switch (name)
                {
                    case "NUMBER":
                        text = sheet.Number;
                        return true;
                    case "TITLE":
                        text = sheet.Title;
                        return true;
                    case "NUMBERANDTITLE":
                        text = NumberAndTitle(sheet.Number, sheet.Title);
                        return true;
                    case "NAME":
                        text = (sheet.Number + " " + sheet.Title).Trim();
                        return true;
                    case "DESCRIPTION":
                        text = sheet.Description;
                        return true;
                    case "CATEGORY":
                        text = sheet.Category;
                        return true;
                    case "REVISIONNUMBER":
                        text = sheet.RevisionNumber;
                        return true;
                    case "REVISIONDATE":
                        text = sheet.RevisionDate;
                        return true;
                    case "ISSUEPURPOSE":
                        text = sheet.IssuePurpose;
                        return true;
                }

                CadSheetSetProperty own = sheet.FindCustomProperty(property);

                if (own != null)
                {
                    text = own.Value;
                    return true;
                }

                CadSheetSetProperty definition = file.SheetSet.FindCustomProperty(property);

                if (definition != null && definition.IsSheetProperty)
                {
                    text = definition.Value;
                    return true;
                }

                return false;
            }

            if (component is CadSheetSet set)
            {
                switch (name)
                {
                    case "NAME":
                        text = set.Name;
                        return true;
                    case "DESCRIPTION":
                        text = set.Description;
                        return true;
                    case "PROJECTNUMBER":
                        text = set.ProjectNumber;
                        return true;
                    case "PROJECTNAME":
                        text = set.ProjectName;
                        return true;
                    case "PROJECTPHASE":
                        text = set.ProjectPhase;
                        return true;
                    case "PROJECTMILESTONE":
                        text = set.ProjectMilestone;
                        return true;
                }

                CadSheetSetProperty custom = set.FindCustomProperty(property);

                if (custom != null && custom.IsSetProperty)
                {
                    text = custom.Value;
                    return true;
                }

                return false;
            }

            if (component is CadSheetSubset subset)
            {
                switch (name)
                {
                    case "NAME":
                        text = subset.Name;
                        return true;
                    case "DESCRIPTION":
                        text = subset.Description;
                        return true;
                }

                CadSheetSetProperty custom = subset.FindCustomProperty(property);
                text = custom?.Value;
                return custom != null;
            }

            return false;
        }

        private static bool ReadView(CadSheetView view, string property, out string text)
        {
            text = null;

            switch (property.ToUpperInvariant())
            {
                case "NUMBER":
                    text = view.Number;
                    return true;
                case "TITLE":
                    text = view.Title;
                    return true;
                case "NUMBERANDTITLE":
                    text = NumberAndTitle(view.Number, view.Title);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The number and the title as AutoCAD joins them: "A-101 - Ground floor", the title alone where there is no number.</summary>
        internal static string NumberAndTitle(string number, string title) =>
            number.Length == 0 ? title : number + " - " + title;

        // ---- The code of a field ----

        /// <summary>A field of the evaluator AcSm taken apart: the component and property of an unbound one, the file, set and component IDs of a bound one.</summary>
        internal sealed class Target
        {
            internal bool Bound;
            internal string Database;
            internal string SetId;
            internal string ComponentId;

            /// <summary>Gets the component of an unbound field: Sheet, Subset, SheetSet, or a placeholder's ?Sheet and ?View.</summary>
            internal string Component;

            internal string Property;

            /// <summary>The switch after the property, <c> \f "%tc1"</c>; empty for none.</summary>
            internal string Suffix = string.Empty;

            /// <summary>Takes a code apart; null for a code that is none of AutoCAD's forms.</summary>
            internal static Target Parse(string code)
            {
                const string head = "\\AcSm";

                if (code == null || !code.StartsWith(head, StringComparison.Ordinal))
                {
                    return null;
                }

                string body = code.Substring(head.Length).TrimStart();
                var target = new Target();
                // The switches after the property, the format (\\f) and the hyperlink (\\href), begin at a blank and a
                // backslash; a path before them may hold a backslash but none after a blank.
                int switches = SwitchesAt(body);

                if (switches >= 0)
                {
                    target.Suffix = body.Substring(switches);
                    body = body.Substring(0, switches);
                }

                body = body.TrimEnd();

                if (body.StartsWith("Database(\"", StringComparison.Ordinal))
                {
                    int at = 10;
                    int close = body.IndexOf("\")", at, StringComparison.Ordinal);

                    if (close < 0)
                    {
                        return null;
                    }

                    target.Database = body.Substring(at, close - at);
                    at = close + 2;

                    if (!Take(body, ref at, ".SheetSet(\"", out target.SetId))
                    {
                        return null;
                    }

                    Take(body, ref at, ".Component(\"", out target.ComponentId);

                    if (at >= body.Length - 1 || body[at] != '.')
                    {
                        return null;
                    }

                    target.Property = body.Substring(at + 1);
                    target.Bound = true;
                    return target;
                }

                int dot = body.IndexOf('.');

                if (dot <= 0 || dot >= body.Length - 1)
                {
                    return null;
                }

                target.Component = body.Substring(0, dot);
                target.Property = body.Substring(dot + 1);
                return target;
            }

            private static int SwitchesAt(string body)
            {
                int from = 0;

                if (body.StartsWith("Database(\"", StringComparison.Ordinal))
                {
                    int close = body.IndexOf("\")", StringComparison.Ordinal);
                    from = close < 0 ? 0 : close;
                }

                for (int i = from; i + 2 < body.Length; i++)
                {
                    if (body[i] == ' ' && body[i + 1] == '\\' && char.IsLetter(body[i + 2]))
                    {
                        return i;
                    }
                }

                return -1;
            }

            private static bool Take(string body, ref int at, string prefix, out string value)
            {
                value = null;

                if (string.CompareOrdinal(body, at, prefix, 0, prefix.Length) != 0)
                {
                    return false;
                }

                int close = body.IndexOf("\")", at + prefix.Length, StringComparison.Ordinal);

                if (close < 0)
                {
                    return false;
                }

                value = body.Substring(at + prefix.Length, close - at - prefix.Length);
                at = close + 2;
                return true;
            }
        }
    }
}
