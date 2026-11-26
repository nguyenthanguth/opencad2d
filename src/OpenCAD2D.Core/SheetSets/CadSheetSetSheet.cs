using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using GeometryHelper;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// A sheet of a sheet set: a number and a title, and the layout of a drawing it is (<see cref="Layout"/>), with the
    /// revision fields, whether it is published, its custom property values and its sheet views. It is named
    /// <c>CadSheetSetSheet</c>, not <c>CadSheet</c>, because <see cref="CadSheet"/> is a sheet of a spreadsheet.
    /// </summary>
    public sealed partial class CadSheetSetSheet : CadSheetComponent
    {
        internal CadSheetSetSheet(CadSheetSetFile file, XElement element)
            : base(file, element) { }

        /// <summary>Gets or sets the sheet number, free text ("A-101"); empty for none.</summary>
        public string Number
        {
            get => CadDstTree.GetString(Element, "Number");
            set => SetText("Number", value);
        }

        /// <summary>Gets or sets the sheet title.</summary>
        public string Title
        {
            get => CadDstTree.GetString(Element, "Title");
            set => SetText("Title", value);
        }

        /// <summary>Gets or sets whether the sheet is published with its set; the file keeps the opposite, <c>DoNotPlot</c>, and leaves it out for a sheet that is.</summary>
        public bool IncludeForPublish
        {
            get => !CadDstTree.GetBool(Element, "DoNotPlot");
            set
            {
                Edit();
                CadDstTree.SetBool(Element, "DoNotPlot", !value);
            }
        }

        /// <summary>Gets or sets the revision number.</summary>
        public string RevisionNumber
        {
            get => CadDstTree.GetString(Element, "RevisionNumber");
            set => SetText("RevisionNumber", value);
        }

        /// <summary>Gets or sets the revision date, free text.</summary>
        public string RevisionDate
        {
            get => CadDstTree.GetString(Element, "RevisionDate");
            set => SetText("RevisionDate", value);
        }

        /// <summary>Gets or sets the issue purpose.</summary>
        public string IssuePurpose
        {
            get => CadDstTree.GetString(Element, "IssuePurpose");
            set => SetText("IssuePurpose", value);
        }

        /// <summary>Gets or sets the sheet's category, free text.</summary>
        public string Category
        {
            get => CadDstTree.GetString(Element, "Category");
            set => SetText("Category", value);
        }

        /// <summary>
        /// Gets or sets the drawing and layout the sheet is: the drawing's file, the layout's name, its handle and the handle
        /// of its owner. Null for a sheet with no layout yet. Finding the layout in the drawing is not done here.
        /// </summary>
        public CadDrawingReference Layout
        {
            get => CadDstTree.ReadDrawing(CadDstTree.FindObject(Element, "Layout"));
            set
            {
                Edit();
                XElement element = CadDstTree.FindObject(Element, "Layout");
                if (element == null)
                {
                    if (value == null)
                    {
                        return;
                    }

                    element = File.NewObject(CadDstTree.LayoutReferenceClass, "Layout");
                    CadDstTree.InsertNamed(Element, element);
                }

                CadDstTree.WriteDrawing(element, value);
            }
        }

        /// <summary>Gets the sheet views (the named model space views placed on the sheet), in the order of the file.</summary>
        public IReadOnlyList<CadSheetView> Views => ViewElements().Select(e => new CadSheetView(File, e)).ToList();

        /// <summary>Adds a sheet view, at the end.</summary>
        /// <param name="number">The view number.</param>
        /// <param name="title">The view title.</param>
        /// <param name="namedView">The named view of the drawing it shows, or null for none.</param>
        /// <param name="category">The view category, one of this sheet set's; null for none.</param>
        /// <returns>The new view.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the number or the title is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the category is not one of this sheet set as it is loaded.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public CadSheetView AddView(
            string number,
            string title,
            CadDrawingReference namedView,
            CadViewCategory category
        )
        {
            if (number == null)
            {
                throw new ArgumentNullException(nameof(number));
            }

            if (title == null)
            {
                throw new ArgumentNullException(nameof(title));
            }

            Edit();
            Guard.Argument(
                category == null || (category.File == File && category.Element.Document == Element.Document),
                nameof(category),
                "The category is not one of this sheet set as it is loaded."
            );
            XElement views = CadDstTree.FindObject(Element, "SheetViews");
            if (views == null)
            {
                views = File.NewObject(CadDstTree.SheetViewsClass, "SheetViews");
                CadDstTree.InsertNamed(Element, views);
            }

            XElement view = File.NewObject(CadDstTree.SheetViewClass, null);
            CadDstTree.SetString(view, "Number", number);
            CadDstTree.SetString(view, "Title", title);
            if (namedView != null)
            {
                XElement named = File.NewObject(CadDstTree.ViewReferenceClass, "NamedView");
                CadDstTree.WriteDrawing(named, namedView);
                CadDstTree.InsertNamed(view, named);
            }

            if (category != null)
            {
                XElement reference = File.NewObject(CadDstTree.ObjectReferenceClass, "Category");
                reference.Add(
                    new XElement(
                        CadDstTree.Prop,
                        new XAttribute("propname", "ReferencedObject"),
                        new XAttribute("vt", "-1"),
                        category.Id
                    )
                );
                CadDstTree.InsertNamed(view, reference);
            }

            views.Add(view);
            return new CadSheetView(File, view);
        }

        /// <summary>Takes a sheet view out of the sheet.</summary>
        /// <param name="view">The view.</param>
        /// <exception cref="ArgumentNullException">Thrown when the view is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the view is not one of this sheet's, as it is loaded.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public void RemoveView(CadSheetView view)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            Edit();
            Guard.Argument(
                view.File == File && ViewElements().Contains(view.Element),
                nameof(view),
                "The view is not one of this sheet's."
            );
            view.Element.Remove();
        }

        private List<XElement> ViewElements() =>
            (
                CadDstTree.FindObject(Element, "SheetViews")?.Elements(CadDstTree.SheetViewClass)
                ?? Enumerable.Empty<XElement>()
            ).ToList();

        private void SetText(string name, string value)
        {
            Edit();
            CadDstTree.SetString(Element, name, value);
        }
    }

    /// <summary>
    /// A sheet view: a named model space view of a drawing placed on a sheet (what Place on Sheet in the Model Views tab
    /// makes), with a number and a title and the view category it is listed under. Taken from
    /// <see cref="CadSheetSetSheet.Views"/>.
    /// </summary>
    public sealed class CadSheetView
    {
        internal CadSheetView(CadSheetSetFile file, XElement element)
        {
            File = file;
            Element = element;
        }

        /// <summary>Gets the <c>ID</c> of the view in the file.</summary>
        public string Id => (string)Element.Attribute("ID");

        /// <summary>Gets or sets the view number.</summary>
        public string Number
        {
            get => CadDstTree.GetString(Element, "Number");
            set => SetText("Number", value);
        }

        /// <summary>Gets or sets the view title.</summary>
        public string Title
        {
            get => CadDstTree.GetString(Element, "Title");
            set => SetText("Title", value);
        }

        /// <summary>Gets the named view of the drawing the sheet view shows; null for none.</summary>
        public CadDrawingReference NamedView => CadDstTree.ReadDrawing(CadDstTree.FindObject(Element, "NamedView"));

        /// <summary>
        /// Gets or sets the view category the view is listed under; null for none (or, read, one that is no longer in the set).
        /// Set from <see cref="CadSheetSet.ViewCategories"/> as it is loaded.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown, setting, when the category is not one of this sheet set as it is loaded.</exception>
        /// <exception cref="InvalidOperationException">Thrown, setting, outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public CadViewCategory Category
        {
            get
            {
                XElement reference = CadDstTree.FindObject(Element, "Category");
                string id = reference == null ? string.Empty : CadDstTree.GetString(reference, "ReferencedObject");
                if (id.Length == 0)
                {
                    return null;
                }

                XElement category = Element
                    .Document.Descendants(CadDstTree.ViewCategoryClass)
                    .FirstOrDefault(e => (string)e.Attribute("ID") == id);
                return category == null ? null : new CadViewCategory(File, category);
            }
            set
            {
                File.RequireEditing(Element);
                Guard.Argument(
                    value == null || (value.File == File && value.Element.Document == Element.Document),
                    nameof(value),
                    "The category is not one of this sheet set as it is loaded."
                );
                CadDstTree.FindObject(Element, "Category")?.Remove();
                if (value != null)
                {
                    XElement reference = File.NewObject(CadDstTree.ObjectReferenceClass, "Category");
                    reference.Add(
                        new XElement(
                            CadDstTree.Prop,
                            new XAttribute("propname", "ReferencedObject"),
                            new XAttribute("vt", "-1"),
                            value.Id
                        )
                    );
                    CadDstTree.InsertNamed(Element, reference);
                }
            }
        }

        internal CadSheetSetFile File { get; }

        internal XElement Element { get; }

        private void SetText(string name, string value)
        {
            File.RequireEditing(Element);
            CadDstTree.SetString(Element, name, value);
        }
    }

    /// <summary>
    /// A view category of a sheet set: a name that sheet views are listed under in the Sheet Views tab. A new set holds one
    /// with no name. Taken from <see cref="CadSheetSet.ViewCategories"/>.
    /// </summary>
    public sealed class CadViewCategory
    {
        internal CadViewCategory(CadSheetSetFile file, XElement element)
        {
            File = file;
            Element = element;
        }

        /// <summary>Gets the <c>ID</c> of the category in the file, which a sheet view names.</summary>
        public string Id => (string)Element.Attribute("ID");

        /// <summary>Gets or sets the name.</summary>
        public string Name
        {
            get => CadDstTree.GetString(Element, "Name");
            set => SetText("Name", value);
        }

        /// <summary>Gets or sets the description; empty for none.</summary>
        public string Description
        {
            get => CadDstTree.GetString(Element, "Desc");
            set => SetText("Desc", value);
        }

        internal CadSheetSetFile File { get; }

        internal XElement Element { get; }

        private void SetText(string name, string value)
        {
            File.RequireEditing(Element);
            CadDstTree.SetString(Element, name, value);
        }
    }
}
