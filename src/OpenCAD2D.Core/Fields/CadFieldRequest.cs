using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GeometryHelper.Dxf;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D
{
    /// <summary>How the Field dialog was closed.</summary>
    internal enum CadFieldAction
    {
        /// <summary>The window is not shown or the dialog was cancelled.</summary>
        Cancel,

        /// <summary>OK: the field is the one the dialog holds.</summary>
        Ok,

        /// <summary>The Select object button: the dialog is shown again once an object is picked.</summary>
        SelectObject,
    }

    /// <summary>What a field shows, which gives it the options its dialog offers.</summary>
    internal enum CadFieldType
    {
        /// <summary>A date and time: a format of letters.</summary>
        Date,

        /// <summary>Words of the drawing or its page setup: a case.</summary>
        Words,

        /// <summary>The name of the file: its parts and a case.</summary>
        FileName,

        /// <summary>A number: units, precision, a factor, a prefix and a suffix.</summary>
        Number,

        /// <summary>A property of an object picked.</summary>
        Object,

        /// <summary>A formula, 2*3+1.</summary>
        Formula,

        /// <summary>A DIESEL expression.</summary>
        Diesel,

        /// <summary>A system variable by its name.</summary>
        SystemVariable,

        /// <summary>A property of a sheet set, a subset, a sheet or a view, chosen in the sheet set's tree.</summary>
        SheetSet,

        /// <summary>A placeholder of a label or callout block that is bound to a view or a sheet when the block is placed.</summary>
        SheetSetPlaceholder,

        /// <summary>A property of the sheet of the layout the field is on, or of its set, or of its subset.</summary>
        CurrentSheet,

        /// <summary>A custom property of the sheet of the layout the field is on, or of its set, by the name chosen.</summary>
        CurrentSheetCustom,
    }

    /// <summary>A field by its name in a category of the Field dialog.</summary>
    internal sealed class CadFieldName
    {
        internal CadFieldName(
            string name,
            string category,
            CadFieldType type,
            string description,
            string sheetSetCode = null
        )
        {
            Name = name;
            Category = category;
            Type = type;
            Description = description;
            SheetSetCode = sheetSetCode;
        }

        /// <summary>
        /// Gets the component and property a CurrentSheet field reads, as its code writes them (<c>Sheet.Number</c>); for a
        /// custom property the component and a dot (<c>Sheet.</c>), the name chosen following; null for any other field.
        /// </summary>
        internal string SheetSetCode { get; }

        /// <summary>Gets the name as the dialog lists it: Date, Author, Object.</summary>
        internal string Name { get; }

        /// <summary>Gets the category it is listed in.</summary>
        internal string Category { get; }

        /// <summary>Gets what it shows, which gives it its options.</summary>
        internal CadFieldType Type { get; }

        /// <summary>Gets the line the dialog says of it.</summary>
        internal string Description { get; }

        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>A format the dialog offers a field: the label it is listed by and the code of it.</summary>
    internal sealed class CadFieldChoice
    {
        internal CadFieldChoice(string label, string code)
        {
            Label = label;
            Code = code;
        }

        /// <summary>Gets the label: an example of what it shows, or its name.</summary>
        internal string Label { get; }

        /// <summary>Gets the code after <c>\f</c>, without its quotes; empty for none.</summary>
        internal string Code { get; }

        /// <inheritdoc/>
        public override string ToString() => Label;
    }

    /// <summary>
    /// The Field dialog of AutoCAD 2021, without its window: the categories (All, Date &amp; Time, Document, Linked, Objects,
    /// Other, Plot) and the field names in each, the field chosen, the options it offers (the format of a date or of words, the
    /// object and its property, the units, precision, factor, prefix and suffix of a number), the code that comes of them, which is
    /// the field expression the dialog shows, and the words it shows now, which is its preview. The window sets what the user
    /// chose and reads what came of it; OK gives <see cref="Code"/>, the field in its markers, as <c>%&lt;\AcVar Date \f
    /// "M/d/yyyy"&gt;%</c>.
    /// </summary>
    internal sealed class CadFieldRequest
    {
        /// <summary>The categories in the order AutoCAD lists them.</summary>
        internal static readonly string[] CategoryNames =
        {
            "All",
            "Date & Time",
            "Document",
            "Linked",
            "Objects",
            "Other",
            "Plot",
            "SheetSet",
        };

        private static readonly CadFieldName[] Names =
        {
            new CadFieldName("CreateDate", "Date & Time", CadFieldType.Date, "Date the drawing was created"),
            new CadFieldName("Date", "Date & Time", CadFieldType.Date, "Current date and time"),
            new CadFieldName("PlotDate", "Date & Time", CadFieldType.Date, "Date the drawing was last plotted"),
            new CadFieldName("SaveDate", "Date & Time", CadFieldType.Date, "Date the drawing was last saved"),
            new CadFieldName("Author", "Document", CadFieldType.Words, "Author of the drawing"),
            new CadFieldName("Comments", "Document", CadFieldType.Words, "Comments of the drawing"),
            new CadFieldName("Filename", "Document", CadFieldType.FileName, "Name and path of the drawing"),
            new CadFieldName("Filesize", "Document", CadFieldType.Number, "Size of the drawing's file in bytes"),
            new CadFieldName("HyperlinkBase", "Document", CadFieldType.Words, "Base of the drawing's hyperlinks"),
            new CadFieldName("Keywords", "Document", CadFieldType.Words, "Keywords of the drawing"),
            new CadFieldName("LastSavedBy", "Document", CadFieldType.Words, "Who saved the drawing last"),
            new CadFieldName("Subject", "Document", CadFieldType.Words, "Subject of the drawing"),
            new CadFieldName("Title", "Document", CadFieldType.Words, "Title of the drawing"),
            new CadFieldName("Formula", "Objects", CadFieldType.Formula, "Result of a formula"),
            new CadFieldName("Object", "Objects", CadFieldType.Object, "A property of an object"),
            new CadFieldName("DieselExpression", "Other", CadFieldType.Diesel, "Result of a DIESEL expression"),
            new CadFieldName("SystemVariable", "Other", CadFieldType.SystemVariable, "Value of a system variable"),
            new CadFieldName("Login", "Plot", CadFieldType.Words, "Name of the user"),
            new CadFieldName("PageSetupName", "Plot", CadFieldType.Words, "Name of the page setup"),
            new CadFieldName("PaperSize", "Plot", CadFieldType.Words, "Paper size of the page setup"),
            new CadFieldName("PlotDeviceName", "Plot", CadFieldType.Words, "Plotter of the page setup"),
            new CadFieldName("PlotOrientation", "Plot", CadFieldType.Words, "Portrait or landscape"),
            new CadFieldName("PlotScale", "Plot", CadFieldType.Words, "Scale of the plot"),
            new CadFieldName("PlotStyleTable", "Plot", CadFieldType.Words, "Plot style table of the page setup"),
            new CadFieldName(
                "SheetSet",
                "SheetSet",
                CadFieldType.SheetSet,
                "A property of a sheet set, a subset, a sheet or a view"
            ),
            new CadFieldName(
                "SheetSetPlaceholder",
                "SheetSet",
                CadFieldType.SheetSetPlaceholder,
                "A placeholder for a label or callout block"
            ),
            new CadFieldName(
                "CurrentSheetCategory",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Category of the sheet of the layout",
                "Sheet.Category"
            ),
            new CadFieldName(
                "CurrentSheetCustom",
                "SheetSet",
                CadFieldType.CurrentSheetCustom,
                "A custom property of the sheet of the layout",
                "Sheet."
            ),
            new CadFieldName(
                "CurrentSheetDescription",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Description of the sheet of the layout",
                "Sheet.Description"
            ),
            new CadFieldName(
                "CurrentSheetIssuePurpose",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Issue purpose of the sheet of the layout",
                "Sheet.IssuePurpose"
            ),
            new CadFieldName(
                "CurrentSheetNumber",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Number of the sheet of the layout",
                "Sheet.Number"
            ),
            new CadFieldName(
                "CurrentSheetNumberAndTitle",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Number and title of the sheet of the layout",
                "Sheet.NumberAndTitle"
            ),
            new CadFieldName(
                "CurrentSheetRevisionDate",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Revision date of the sheet of the layout",
                "Sheet.RevisionDate"
            ),
            new CadFieldName(
                "CurrentSheetRevisionNumber",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Revision number of the sheet of the layout",
                "Sheet.RevisionNumber"
            ),
            new CadFieldName(
                "CurrentSheetSet",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Name of the sheet set",
                "SheetSet.Name"
            ),
            new CadFieldName(
                "CurrentSheetSetCustom",
                "SheetSet",
                CadFieldType.CurrentSheetCustom,
                "A custom property of the sheet set",
                "SheetSet."
            ),
            new CadFieldName(
                "CurrentSheetSetDescription",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Description of the sheet set",
                "SheetSet.Description"
            ),
            new CadFieldName(
                "CurrentSheetSetProjectMilestone",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Project milestone of the sheet set",
                "SheetSet.ProjectMilestone"
            ),
            new CadFieldName(
                "CurrentSheetSetProjectName",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Project name of the sheet set",
                "SheetSet.ProjectName"
            ),
            new CadFieldName(
                "CurrentSheetSetProjectNumber",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Project number of the sheet set",
                "SheetSet.ProjectNumber"
            ),
            new CadFieldName(
                "CurrentSheetSetProjectPhase",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Project phase of the sheet set",
                "SheetSet.ProjectPhase"
            ),
            new CadFieldName(
                "CurrentSheetSubSet",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Name of the subset the sheet is in",
                "Subset.Name"
            ),
            new CadFieldName(
                "CurrentSheetTitle",
                "SheetSet",
                CadFieldType.CurrentSheet,
                "Title of the sheet of the layout",
                "Sheet.Title"
            ),
        };

        private static readonly string[] PlaceholderNames =
        {
            "SheetNumberAndTitle",
            "SheetTitle",
            "SheetNumber",
            "SheetDescription",
            "SheetCustom",
            "ViewNumberAndTitle",
            "ViewTitle",
            "ViewNumber",
            "ViewportScale",
        };

        private static readonly CadFieldChoice[] CaseChoices =
        {
            new CadFieldChoice("(none)", string.Empty),
            new CadFieldChoice("Uppercase", "%tc1"),
            new CadFieldChoice("Lowercase", "%tc2"),
            new CadFieldChoice("First capital", "%tc3"),
            new CadFieldChoice("Title case", "%tc4"),
        };

        private static readonly CadFieldChoice[] NameChoices =
        {
            new CadFieldChoice("Path and file name", string.Empty),
            new CadFieldChoice("File name with extension", "%fn6"),
            new CadFieldChoice("File name only", "%fn2"),
            new CadFieldChoice("Path only", "%fn1"),
            new CadFieldChoice("Extension only", "%fn4"),
        };

        private static readonly string[] DateFormats =
        {
            "M/d/yyyy",
            "dddd, MMMM dd, yyyy",
            "MMMM d, yyyy",
            "MMM d, yyyy",
            "d MMMM yyyy",
            "dd/MM/yyyy",
            "yyyy-MM-dd",
            "M/d/yyyy h:mm tt",
            "M/d/yyyy h:mm:ss tt",
            "h:mm tt",
            "h:mm:ss tt",
            "HH:mm",
        };

        private static readonly CadFieldChoice[] LengthUnits =
        {
            new CadFieldChoice("Current units", string.Empty),
            new CadFieldChoice("Scientific", "%lu1"),
            new CadFieldChoice("Decimal", "%lu2"),
            new CadFieldChoice("Engineering", "%lu3"),
            new CadFieldChoice("Architectural", "%lu4"),
            new CadFieldChoice("Fractional", "%lu5"),
        };

        private static readonly CadFieldChoice[] AngleUnits =
        {
            new CadFieldChoice("Decimal degrees", "%au0"),
            new CadFieldChoice("Degrees/minutes/seconds", "%au1"),
            new CadFieldChoice("Grads", "%au2"),
            new CadFieldChoice("Radians", "%au3"),
            new CadFieldChoice("Surveyor's units", "%au4"),
        };

        private readonly CadFieldContext _context;
        private CadFieldName _name;
        private CadFieldProperty _property;
        private IReadOnlyList<CadFieldProperty> _properties = Array.Empty<CadFieldProperty>();
        private CadSheetSetFile _setFile;
        private CadFieldSheetNode _tree;
        private CadFieldSheetNode _node;
        private string _setProperty;
        private string _placeholder = PlaceholderNames[0];
        private string _customName = string.Empty;

        /// <summary>Makes the dialog for a drawing, with Date chosen.</summary>
        /// <param name="context">What the preview is evaluated against.</param>
        internal CadFieldRequest(CadFieldContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            Category = CategoryNames[0];
            Choose(Names.First(n => n.Name == "Date"));
            CadSheetSetSheet sheet = CadSheetSetFields.SheetOfField(context, out CadSheetSetFile file);
            SetSheetSet(file, sheet?.Id);
        }

        /// <summary>Gets or sets how the dialog was closed.</summary>
        internal CadFieldAction Action { get; set; }

        /// <summary>Gets the category listed now.</summary>
        internal string Category { get; private set; }

        /// <summary>Gets the field chosen.</summary>
        internal CadFieldName Name => _name;

        /// <summary>Gets the sheet set the SheetSet field reads, opened for reading; null where the drawing is no sheet of a set the dialog can reach and none was chosen.</summary>
        internal CadSheetSetFile SheetSetFile => _setFile;

        /// <summary>Gets the tree of the sheet set: the set, its subsets and sheets in set order, and the views of each sheet; null where there is no set.</summary>
        internal CadFieldSheetNode SheetSetTree => _tree;

        /// <summary>Gets the node of the tree chosen: a sheet, a subset, a view or the set; null where there is no set.</summary>
        internal CadFieldSheetNode SheetSetNode => _node;

        /// <summary>Gets the properties the node chosen has, for the property list.</summary>
        internal IReadOnlyList<string> SheetSetProperties => _node == null ? Array.Empty<string>() : _node.Properties;

        /// <summary>Gets the property chosen of the node; null for none.</summary>
        internal string SheetSetProperty => _setProperty;

        /// <summary>Gets the types of placeholder the dialog lists.</summary>
        internal IReadOnlyList<string> PlaceholderTypes => PlaceholderNames;

        /// <summary>Gets the type of placeholder chosen.</summary>
        internal string PlaceholderType => _placeholder;

        /// <summary>
        /// Gets the custom properties a field of a custom property may name: those of the set for CurrentSheetSetCustom, those
        /// every sheet holds for CurrentSheetCustom and the SheetCustom placeholder; none where there is no set.
        /// </summary>
        internal IReadOnlyList<string> CustomNames
        {
            get
            {
                if (_setFile == null)
                {
                    return Array.Empty<string>();
                }

                bool ofSet =
                    _name.Type == CadFieldType.CurrentSheetCustom
                    && _name.SheetSetCode.StartsWith("SheetSet", StringComparison.Ordinal);
                return _setFile
                    .SheetSet.CustomProperties.Where(p => ofSet ? p.IsSetProperty : p.IsSheetProperty)
                    .Select(p => p.Name)
                    .ToList();
            }
        }

        /// <summary>Gets the custom property chosen; empty for none.</summary>
        internal string CustomName => _customName;

        /// <summary>
        /// Gives the SheetSet field a sheet set to read, as the Open Sheet Set button does: the tree of the set is made and the
        /// node of a sheet (the set where there is none) chosen.
        /// </summary>
        /// <param name="file">The set; null for none.</param>
        /// <param name="nodeId">The ID of the node to choose; null for the set.</param>
        internal void SetSheetSet(CadSheetSetFile file, string nodeId = null)
        {
            _setFile = file;
            _tree = file == null ? null : CadFieldSheetNode.Of(file, file.SheetSet);
            _node = null;
            _setProperty = null;
            ChooseSheetSetNode(nodeId);
            _customName = CustomNames.FirstOrDefault() ?? string.Empty;
        }

        /// <summary>Chooses a node of the tree by its ID; the set where none has it. The property is kept where the node has it, else the first.</summary>
        /// <param name="id">The ID of a sheet, subset or view; null for the set.</param>
        internal void ChooseSheetSetNode(string id)
        {
            _node = _tree == null ? null : (id == null ? null : _tree.Find(id)) ?? _tree;
            ChooseSheetSetProperty(_setProperty);
        }

        /// <summary>Chooses a property of the node, letter case ignored; the first the node has where the name is none of them.</summary>
        /// <param name="name">The property's name.</param>
        internal void ChooseSheetSetProperty(string name) =>
            _setProperty =
                _node == null
                    ? null
                    : _node.Properties.FirstOrDefault(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase))
                        ?? _node.Properties.FirstOrDefault();

        /// <summary>Chooses a placeholder type; the first where the name is none of them.</summary>
        /// <param name="name">The type, SheetNumber or ViewTitle.</param>
        internal void ChoosePlaceholder(string name) =>
            _placeholder = PlaceholderNames.FirstOrDefault(p =>
                string.Equals(p, name, StringComparison.OrdinalIgnoreCase)
            ) ?? PlaceholderNames[0];

        /// <summary>Chooses the custom property a custom field names.</summary>
        /// <param name="name">The name; empty for none.</param>
        internal void ChooseCustomName(string name) => _customName = name ?? string.Empty;

        /// <summary>Gets the handle of the object an Object field reads; null for none.</summary>
        internal string ObjectHandle { get; private set; }

        /// <summary>Gets the class of the object picked, as AutoCAD names it (AcDbCircle); null where none is picked or it is not in the drawing.</summary>
        internal string ObjectKind { get; private set; }

        /// <summary>Gets the properties of the object picked, by name; none where no object is picked.</summary>
        internal IReadOnlyList<CadFieldProperty> Properties => _properties;

        /// <summary>Gets the property chosen of the object picked; null for none.</summary>
        internal CadFieldProperty Property => _property;

        /// <summary>Gets or sets the expression of a Formula or DieselExpression field, or the name of a SystemVariable field.</summary>
        internal string Expression { get; set; } = string.Empty;

        /// <summary>Gets or sets the format of a date, by its letters; the choice's where it is one of <see cref="DateChoices"/>.</summary>
        internal string DateFormat { get; set; } = DateFormats[0];

        /// <summary>Gets or sets the index of the case or file name format chosen among <see cref="Formats"/>.</summary>
        internal int FormatIndex { get; set; }

        /// <summary>Gets or sets the index of the units chosen among <see cref="Units"/>.</summary>
        internal int UnitsIndex { get; set; }

        /// <summary>Gets or sets the precision of a number, 0 to 8; -1 for the drawing's.</summary>
        internal int Precision { get; set; } = -1;

        /// <summary>Gets or sets the factor a number is multiplied by; 1 for none.</summary>
        internal double Factor { get; set; } = 1.0;

        /// <summary>Gets or sets the words before a number.</summary>
        internal string Prefix { get; set; } = string.Empty;

        /// <summary>Gets or sets the words after a number.</summary>
        internal string Suffix { get; set; } = string.Empty;

        /// <summary>Gets or sets the thousands separator of a number, as its character; nought for none.</summary>
        internal char Thousands { get; set; }

        /// <summary>Gets the date formats the dialog lists.</summary>
        internal IReadOnlyList<string> DateChoices => DateFormats;

        /// <summary>
        /// Gets the formats listed for the field chosen: the cases of words, the parts of a file name; the units of a number or of
        /// an angle for <see cref="Units"/>.
        /// </summary>
        internal IReadOnlyList<CadFieldChoice> Formats =>
            _name.Type == CadFieldType.FileName ? NameChoices
            : _name.Type == CadFieldType.Words || IsTextProperty || IsSheetSetField ? CaseChoices
            : Array.Empty<CadFieldChoice>();

        /// <summary>Gets whether the field chosen is one of the SheetSet category, which shows words of a sheet set (a case may be set).</summary>
        internal bool IsSheetSetField =>
            _name.Type == CadFieldType.SheetSet
            || _name.Type == CadFieldType.SheetSetPlaceholder
            || _name.Type == CadFieldType.CurrentSheet
            || _name.Type == CadFieldType.CurrentSheetCustom;

        /// <summary>Gets the units listed for a number: those of lengths, or of angles for an angle.</summary>
        internal IReadOnlyList<CadFieldChoice> Units =>
            IsNumberField
                ? (_property != null && _property.Kind == CadFieldKind.Angle ? AngleUnits : LengthUnits)
                : Array.Empty<CadFieldChoice>();

        /// <summary>Gets whether the field chosen is a number the units, precision, factor, prefix and suffix apply to.</summary>
        internal bool IsNumberField =>
            _name.Type == CadFieldType.Number
            || (
                _name.Type == CadFieldType.Object
                && _property != null
                && (
                    _property.Kind == CadFieldKind.Real
                    || _property.Kind == CadFieldKind.Angle
                    || _property.Kind == CadFieldKind.Whole
                    || _property.Kind == CadFieldKind.Point
                )
            )
            || _name.Type == CadFieldType.Formula;

        private bool IsTextProperty =>
            _name.Type == CadFieldType.Object && _property != null && _property.Kind == CadFieldKind.Text;

        /// <summary>Gets the field names of a category, in the order of their names; every field for All.</summary>
        /// <param name="category">The category.</param>
        /// <returns>The names.</returns>
        internal static IReadOnlyList<CadFieldName> NamesOf(string category) =>
            Names
                .Where(n =>
                    category == CategoryNames[0] || string.Equals(n.Category, category, StringComparison.Ordinal)
                )
                .OrderBy(n => category == "SheetSet" && n.SheetSetCode == null ? 0 : 1)
                .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Lists another category; the field chosen is kept where the category holds it, else its first is chosen.</summary>
        /// <param name="category">The category.</param>
        internal void SetCategory(string category)
        {
            Category = CategoryNames.Contains(category) ? category : CategoryNames[0];
            IReadOnlyList<CadFieldName> names = NamesOf(Category);

            if (names.Count > 0 && !names.Contains(_name))
            {
                Choose(names[0]);
            }
        }

        /// <summary>Chooses a field; its options start as they were left for another.</summary>
        /// <param name="name">The field.</param>
        internal void Choose(CadFieldName name)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            FormatIndex = 0;
            UnitsIndex = _name.Type == CadFieldType.Number || _name.Type == CadFieldType.Formula ? 0 : UnitsIndex;
            Expression = _name.Type == CadFieldType.Formula ? string.Empty : Expression;

            if (_name.Type != CadFieldType.Object)
            {
                _property = null;
            }
            else
            {
                ChooseProperty(_property?.Name);
            }
        }

        /// <summary>Names the object an Object field reads, by its handle.</summary>
        /// <param name="handle">The handle; null for none.</param>
        internal void SetObject(string handle)
        {
            ObjectHandle = handle;
            DxfEntity entity = handle == null ? null : _context.Document.FindEntity(handle);
            ObjectKind = entity == null ? null : CadFieldProperties.ObjectName(entity);
            _properties = entity == null ? Array.Empty<CadFieldProperty>() : CadFieldProperties.Of(entity);
            ChooseProperty(_property?.Name);
        }

        /// <summary>Chooses a property of the object picked; the first where the name is none of them.</summary>
        /// <param name="name">The property's name.</param>
        internal void ChooseProperty(string name)
        {
            _property = _properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? (_properties.Count > 0 ? _properties[0] : null);
            UnitsIndex = 0;
            FormatIndex = 0;
        }

        /// <summary>
        /// The code the dialog holds, the field expression it shows: <c>\AcVar Date \f "dd/MM/yyyy"</c>, in its markers
        /// <c>%&lt;\AcVar Date&gt;%</c>; empty where an Object field has no object picked.
        /// </summary>
        internal string Code
        {
            get
            {
                string body = Body();
                return body.Length == 0 ? string.Empty : "%<" + body + ">%";
            }
        }

        /// <summary>The field's code without its markers, empty where it has none to give.</summary>
        private string Body()
        {
            switch (_name.Type)
            {
                case CadFieldType.Date:
                    return Variable(_name.Name, DateFormat);
                case CadFieldType.Words:
                    return Variable(_name.Name, Formats.Count > FormatIndex ? Formats[FormatIndex].Code : string.Empty);
                case CadFieldType.FileName:
                    return Variable(
                        "Filename",
                        NameChoices[Math.Max(0, Math.Min(FormatIndex, NameChoices.Length - 1))].Code
                    );
                case CadFieldType.Number:
                    return Variable(_name.Name, NumberFormat());
                case CadFieldType.Formula:
                    return Expression.Trim().Length == 0
                        ? string.Empty
                        : "\\AcExpr " + Expression.Trim() + Switch(NumberFormat());
                case CadFieldType.Diesel:
                    return Expression.Trim().Length == 0 ? string.Empty : "\\AcDiesel " + Expression.Trim();
                case CadFieldType.SystemVariable:
                    return Expression.Trim().Length == 0 ? string.Empty : "\\AcVar " + Expression.Trim();
                case CadFieldType.SheetSet:
                    return SheetSetBody();
                case CadFieldType.SheetSetPlaceholder:
                    return PlaceholderBody();
                case CadFieldType.CurrentSheet:
                    return "\\AcSm " + _name.SheetSetCode + Switch(CaseFormat());
                case CadFieldType.CurrentSheetCustom:
                    return _customName.Length == 0
                        ? string.Empty
                        : "\\AcSm " + _name.SheetSetCode + _customName + Switch(CaseFormat());
                default:
                    if (ObjectHandle == null || _property == null)
                    {
                        return string.Empty;
                    }

                    string format =
                        _property.Kind == CadFieldKind.Text
                            ? (Formats.Count > FormatIndex ? Formats[FormatIndex].Code : string.Empty)
                            : NumberFormat();
                    return "\\AcObjProp Object(%<\\_ObjId "
                        + Decimal(ObjectHandle)
                        + ">%)."
                        + _property.Name
                        + Switch(format);
            }
        }

        private string CaseFormat() => Formats.Count > FormatIndex ? Formats[FormatIndex].Code : string.Empty;

        /// <summary>The bound field of the node and property chosen, as AutoCAD writes one; empty where there is no set to read.</summary>
        private string SheetSetBody()
        {
            if (_setFile == null || _node == null || _setProperty == null)
            {
                return string.Empty;
            }

            return "\\AcSm Database(\""
                + _setFile.Path
                + "\").SheetSet(\""
                + _setFile.SheetSet.Id
                + "\")"
                + (_node.Kind == CadFieldSheetNodeKind.Set ? string.Empty : ".Component(\"" + _node.Id + "\")")
                + "."
                + _setProperty
                + Switch(CaseFormat());
        }

        /// <summary>The placeholder of the type chosen, <c>?Sheet.Number</c> or <c>?View.Title</c>; a custom one of the name chosen, none where it has none.</summary>
        private string PlaceholderBody()
        {
            string type = _placeholder;

            if (type == "SheetCustom")
            {
                return _customName.Length == 0 ? string.Empty : "\\AcSm ?Sheet." + _customName + Switch(CaseFormat());
            }

            if (type == "ViewportScale")
            {
                return "\\AcSm ?View.ViewportScale" + Switch(CaseFormat());
            }

            int split = type.StartsWith("View", StringComparison.Ordinal) ? 4 : 5;
            return "\\AcSm ?" + type.Substring(0, split) + "." + type.Substring(split) + Switch(CaseFormat());
        }

        private static string Decimal(string handle) =>
            ulong.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value)
                ? value.ToString(CultureInfo.InvariantCulture)
                : handle;

        private static string Variable(string name, string format) => "\\AcVar " + name + Switch(format);

        private static string Switch(string format) => format.Length == 0 ? string.Empty : " \\f \"" + format + "\"";

        /// <summary>The format of a number as the dialog's units, precision, factor, prefix and suffix say: empty where none is set.</summary>
        private string NumberFormat()
        {
            bool angle = _property != null && _property.Kind == CadFieldKind.Angle;
            IReadOnlyList<CadFieldChoice> units = Units;
            string unit =
                units.Count == 0 ? string.Empty : units[Math.Max(0, Math.Min(UnitsIndex, units.Count - 1))].Code;
            bool set =
                unit.Length > 0
                || Precision >= 0
                || Math.Abs(Factor - 1.0) > 1E-12
                || Prefix.Length > 0
                || Suffix.Length > 0
                || Thousands != '\0';

            if (!set)
            {
                return string.Empty;
            }

            var text = new StringBuilder(angle || unit.Length > 0 ? unit : "%lu2");
            text.Append("%pr")
                .Append(Math.Max(0, Precision < 0 ? 2 : Precision).ToString(CultureInfo.InvariantCulture));

            if (Prefix.Length > 0 || Suffix.Length > 0)
            {
                text.Append("%ps[").Append(Prefix).Append(',').Append(Suffix).Append(']');
            }

            if (Thousands != '\0')
            {
                text.Append("%th").Append(((int)Thousands).ToString(CultureInfo.InvariantCulture));
            }

            if (Math.Abs(Factor - 1.0) > 1E-12)
            {
                text.Append("%ct8[").Append(Factor.ToString("R", CultureInfo.InvariantCulture)).Append(']');
            }

            return text.ToString();
        }

        /// <summary>The words the field chosen shows now, the dialog's preview; ---- or #### where it has none.</summary>
        internal string Preview
        {
            get
            {
                string code = Code;

                if (code.Length == 0)
                {
                    return string.Empty;
                }

                DxfFieldDraft draft = CadFields.Parse(code);
                return draft == null ? string.Empty : CadFields.Evaluate(draft, _context, false);
            }
        }

        /// <summary>
        /// Sets the dialog to a field it is to edit, as a double click on a field in the text editor opens it: the field named in
        /// the code chosen with the options its format gives, the object of an Object field picked.
        /// </summary>
        /// <param name="code">The field in its markers, <c>%&lt;\AcVar Date \f "dd/MM/yyyy"&gt;%</c>.</param>
        /// <returns>False where the code is no field the dialog knows; the dialog is left as it was.</returns>
        internal bool Load(string code)
        {
            DxfFieldDraft draft = CadFields.Parse(code ?? string.Empty);

            if (draft == null || draft.Children.Count != 1)
            {
                return false;
            }

            DxfFieldDraft field = draft.Children[0];
            string format = DxfField.FormatOf(field.Code);
            CadFieldFormat parsed = CadFieldFormat.Parse(format);
            string rest = field.Code.Substring(Math.Min(field.Code.Length, field.Evaluator.Length + 2)).Trim();
            int space = rest.IndexOfAny(new[] { ' ', '\\' });
            string first = space < 0 ? rest : rest.Substring(0, space);

            switch (field.Evaluator)
            {
                case "AcVar":
                    CadFieldName named = Names.FirstOrDefault(n =>
                        string.Equals(n.Name, first, StringComparison.OrdinalIgnoreCase)
                        && n.Type != CadFieldType.Object
                    );

                    if (named == null)
                    {
                        Choose(Names.First(n => n.Type == CadFieldType.SystemVariable));
                        Expression = first;
                        return true;
                    }

                    Choose(named);

                    if (named.Type == CadFieldType.Date)
                    {
                        DateFormat = format;
                    }
                    else if (named.Type == CadFieldType.FileName)
                    {
                        FormatIndex = Math.Max(0, Array.FindIndex(NameChoices, c => c.Code == FileCode(format)));
                    }
                    else if (named.Type == CadFieldType.Words)
                    {
                        FormatIndex = Math.Max(0, Array.FindIndex(CaseChoices, c => c.Code == CaseCode(format)));
                    }
                    else
                    {
                        TakeNumber(parsed, format);
                    }

                    return true;
                case "AcObjProp":
                    Choose(Names.First(n => n.Type == CadFieldType.Object));

                    if (field.Objects.Count > 0)
                    {
                        SetObject(field.Objects[0]);
                    }

                    int dot = field.Code.IndexOf(").", StringComparison.Ordinal);

                    if (dot >= 0)
                    {
                        string rest2 = field.Code.Substring(dot + 2);
                        int end = rest2.IndexOfAny(new[] { ' ', '\\' });
                        ChooseProperty(end < 0 ? rest2 : rest2.Substring(0, end));
                    }

                    if (_property != null && _property.Kind == CadFieldKind.Text)
                    {
                        FormatIndex = Math.Max(0, Array.FindIndex(CaseChoices, c => c.Code == CaseCode(format)));
                    }
                    else
                    {
                        TakeNumber(parsed, format);
                    }

                    return true;
                case "AcExpr":
                    Choose(Names.First(n => n.Type == CadFieldType.Formula));
                    Expression = StripFormat(field.Code, "\\AcExpr ");
                    TakeNumber(parsed, format);
                    return true;
                case "AcDiesel":
                    Choose(Names.First(n => n.Type == CadFieldType.Diesel));
                    Expression = StripFormat(field.Code, "\\AcDiesel ");
                    return true;
                case "AcSm":
                    return LoadSheetSet(field, format);
                default:
                    return false;
            }
        }

        /// <summary>Sets the dialog to an AcSm field: bound, a placeholder, or the unbound code of one of the CurrentSheet names.</summary>
        private bool LoadSheetSet(DxfFieldDraft field, string format)
        {
            CadSheetSetFields.Target target = CadSheetSetFields.TargetOf(field.Code);

            if (target == null)
            {
                return false;
            }

            int formatAt = Math.Max(0, Array.FindIndex(CaseChoices, c => c.Code == CaseCode(format)));

            if (target.Bound)
            {
                CadSheetSetFile file = CadSheetSetFields.OpenSet(target.Database, target.SetId, _context);

                if (file != null)
                {
                    SetSheetSet(file, target.ComponentId);
                }

                Choose(Names.First(n => n.Type == CadFieldType.SheetSet));
                ChooseSheetSetProperty(target.Property);
                FormatIndex = formatAt;
                return true;
            }

            if (target.Component.StartsWith("?", StringComparison.Ordinal))
            {
                Choose(Names.First(n => n.Type == CadFieldType.SheetSetPlaceholder));
                string kind = target.Component.Substring(1);
                string type = target.Property.Equals("ViewportScale", StringComparison.OrdinalIgnoreCase)
                    ? "ViewportScale"
                    : kind + target.Property;
                ChoosePlaceholder(type);

                if (!PlaceholderNames.Contains(_placeholder, StringComparer.OrdinalIgnoreCase) || _placeholder != type)
                {
                    ChoosePlaceholder("SheetCustom");
                    ChooseCustomName(target.Property);
                }

                FormatIndex = formatAt;
                return true;
            }

            string code = target.Component + "." + target.Property;
            CadFieldName named = Names.FirstOrDefault(n =>
                n.Type == CadFieldType.CurrentSheet
                && string.Equals(n.SheetSetCode, code, StringComparison.OrdinalIgnoreCase)
            )
                ?? Names.First(n =>
                    n.Type == CadFieldType.CurrentSheetCustom
                    && string.Equals(n.SheetSetCode, target.Component + ".", StringComparison.OrdinalIgnoreCase)
                );
            Choose(named);

            if (named.Type == CadFieldType.CurrentSheetCustom)
            {
                ChooseCustomName(target.Property);
            }

            FormatIndex = formatAt;
            return true;
        }

        private static string StripFormat(string code, string head)
        {
            string text = code.Length >= head.Length ? code.Substring(head.Length) : string.Empty;
            int at = text.IndexOf(" \\f \"", StringComparison.Ordinal);
            return at < 0 ? text : text.Substring(0, at);
        }

        private static string CaseCode(string format) =>
            CaseChoices.Select(c => c.Code).FirstOrDefault(code => code.Length > 0 && format.Contains(code))
            ?? string.Empty;

        private static string FileCode(string format) =>
            NameChoices.Select(c => c.Code).FirstOrDefault(code => code.Length > 0 && format.Contains(code))
            ?? string.Empty;

        /// <summary>Sets the units, precision, factor, prefix, suffix and separator to what a format says.</summary>
        private void TakeNumber(CadFieldFormat parsed, string format)
        {
            IReadOnlyList<CadFieldChoice> units = Units;
            UnitsIndex = 0;

            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].Code.Length > 0 && format.Contains(units[i].Code))
                {
                    UnitsIndex = i;
                }
            }

            Precision = format.Contains("%pr") ? parsed.Precision : -1;
            Factor = parsed.Factor;
            Prefix = parsed.Prefix;
            Suffix = parsed.Suffix;
            Thousands = parsed.Thousands;
        }
    }

    /// <summary>What a node of the Field dialog's sheet set tree is.</summary>
    internal enum CadFieldSheetNodeKind
    {
        /// <summary>The sheet set.</summary>
        Set,

        /// <summary>A subset.</summary>
        Subset,

        /// <summary>A sheet.</summary>
        Sheet,

        /// <summary>A view of a sheet.</summary>
        View,
    }

    /// <summary>
    /// A node of the sheet set tree of the Field dialog: the set, a subset, a sheet or a view, the words it is listed by, its ID
    /// in the set's file, the properties a field of it may read and the nodes under it, in set order.
    /// </summary>
    internal sealed class CadFieldSheetNode
    {
        private readonly List<CadFieldSheetNode> _children = new List<CadFieldSheetNode>();

        private CadFieldSheetNode(CadFieldSheetNodeKind kind, string id, string text, IReadOnlyList<string> properties)
        {
            Kind = kind;
            Id = id;
            Text = text;
            Properties = properties;
        }

        /// <summary>Gets what the node is.</summary>
        internal CadFieldSheetNodeKind Kind { get; }

        /// <summary>Gets the ID of the node in the set's file (a <c>g</c> and a GUID).</summary>
        internal string Id { get; }

        /// <summary>Gets the words the tree lists it by: the name of the set or subset, a sheet's or view's number and title.</summary>
        internal string Text { get; }

        /// <summary>Gets the properties a field of the node may read, in the order the property list shows them.</summary>
        internal IReadOnlyList<string> Properties { get; }

        /// <summary>Gets the nodes under it: the subsets and sheets of a set or subset, the views of a sheet.</summary>
        internal IReadOnlyList<CadFieldSheetNode> Children => _children;

        /// <summary>Finds a node of this one's tree by its ID.</summary>
        /// <param name="id">The ID.</param>
        /// <returns>The node; null where none has it.</returns>
        internal CadFieldSheetNode Find(string id)
        {
            if (Id == id)
            {
                return this;
            }

            foreach (CadFieldSheetNode child in _children)
            {
                CadFieldSheetNode found = child.Find(id);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <inheritdoc/>
        public override string ToString() => Text;

        /// <summary>Makes the node of a set or subset and the nodes under it.</summary>
        internal static CadFieldSheetNode Of(CadSheetSetFile file, CadSheetSubset subset)
        {
            bool isSet = subset is CadSheetSet;
            var properties = new List<string> { "Name", "Description" };

            if (isSet)
            {
                properties.AddRange(new[] { "ProjectNumber", "ProjectName", "ProjectPhase", "ProjectMilestone" });
                properties.AddRange(subset.CustomProperties.Where(p => p.IsSetProperty).Select(p => p.Name));
            }

            var node = new CadFieldSheetNode(
                isSet ? CadFieldSheetNodeKind.Set : CadFieldSheetNodeKind.Subset,
                subset.Id,
                subset.Name,
                properties
            );

            foreach (CadSheetComponent child in subset.Children)
            {
                node._children.Add(
                    child is CadSheetSubset inner ? Of(file, inner) : OfSheet(file, (CadSheetSetSheet)child)
                );
            }

            return node;
        }

        private static CadFieldSheetNode OfSheet(CadSheetSetFile file, CadSheetSetSheet sheet)
        {
            var properties = new List<string>
            {
                "Number",
                "Title",
                "NumberAndTitle",
                "Description",
                "Category",
                "RevisionNumber",
                "RevisionDate",
                "IssuePurpose",
            };
            properties.AddRange(file.SheetSet.CustomProperties.Where(p => p.IsSheetProperty).Select(p => p.Name));
            var node = new CadFieldSheetNode(
                CadFieldSheetNodeKind.Sheet,
                sheet.Id,
                CadSheetSetFields.NumberAndTitle(sheet.Number, sheet.Title),
                properties
            );

            foreach (CadSheetView view in sheet.Views)
            {
                node._children.Add(
                    new CadFieldSheetNode(
                        CadFieldSheetNodeKind.View,
                        view.Id,
                        CadSheetSetFields.NumberAndTitle(view.Number, view.Title),
                        new[] { "Number", "Title", "NumberAndTitle" }
                    )
                );
            }

            return node;
        }
    }
}
