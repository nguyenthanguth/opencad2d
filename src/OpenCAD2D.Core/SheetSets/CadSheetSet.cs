using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using GeometryHelper;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// A part of a sheet set that is a sheet or a subset (or the set itself, which is a subset of everything): it has an
    /// <see cref="Id"/>, a place in the set, a description and a list of custom properties. The objects are read from, and
    /// written to, the XML tree of their <see cref="CadSheetSetFile"/>; they can be changed only inside
    /// <see cref="CadSheetSetFile.Change"/>.
    /// </summary>
    public abstract class CadSheetComponent
    {
        private readonly CadSheetSetFile _file;
        private readonly XElement _element;

        internal CadSheetComponent(CadSheetSetFile file, XElement element)
        {
            _file = file;
            _element = element;
        }

        /// <summary>Gets the <c>ID</c> of the part in the file (a <c>g</c> and an upper-case GUID), which stays as long as the part is in the set.</summary>
        public string Id => (string)_element.Attribute("ID");

        /// <summary>Gets the subset the part is in; null for the sheet set itself.</summary>
        public CadSheetSubset Parent
        {
            get
            {
                XElement parent = _element.Parent;
                return parent != null && IsContainer(parent) ? (CadSheetSubset)Wrap(_file, parent) : null;
            }
        }

        /// <summary>Gets or sets the description; empty for none, which the file leaves out.</summary>
        public string Description
        {
            get => CadDstTree.GetString(_element, "Desc");
            set
            {
                Edit();
                CadDstTree.SetString(_element, "Desc", value);
            }
        }

        /// <summary>
        /// Gets the custom properties in the order of the file (AutoCAD's: by name, letter case ignored). On the sheet set the
        /// list holds the definitions of both owners, the sheet-owned ones carrying the default value; on a sheet it holds
        /// that sheet's values.
        /// </summary>
        public IReadOnlyList<CadSheetSetProperty> CustomProperties => PropertyElements().Select(Read).ToList();

        internal CadSheetSetFile File => _file;

        internal XElement Element => _element;

        /// <summary>Finds a custom property by name, letter case ignored.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The property; null where there is none.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        public CadSheetSetProperty FindCustomProperty(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            XElement element = FindPropertyElement(name);
            return element == null ? null : Read(element);
        }

        /// <summary>
        /// Sets a custom property: the value and the owner bits of the property of that name (letter case ignored) where
        /// there is one, else a new property placed in the name order. The property is not copied to the sheets here.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="value">The value; null or empty for none, which the file leaves out.</param>
        /// <param name="flags">The owner bits.</param>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the name is empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public void SetCustomProperty(string name, string value, CadSheetSetPropertyFlags flags)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Guard.Argument(name.Length > 0, nameof(name), "A custom property needs a name.");
            Edit();
            XElement property = FindPropertyElement(name);
            if (property == null)
            {
                XElement bag = CadDstTree.FindObject(_element, "CustomPropertyBag");
                if (bag == null)
                {
                    bag = _file.NewObject(CadDstTree.CustomPropertyBagClass, "CustomPropertyBag");
                    CadDstTree.InsertNamed(_element, bag);
                }

                property = _file.NewObject(CadDstTree.CustomPropertyValueClass, name);
                CadDstTree.InsertNamed(bag, property);
            }

            CadDstTree.SetInt(property, "Flags", (int)flags);
            CadDstTree.SetString(property, "Value", value);
        }

        /// <summary>Takes a custom property out, by name, letter case ignored.</summary>
        /// <param name="name">The name.</param>
        /// <returns>True when there was such a property.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public bool RemoveCustomProperty(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Edit();
            XElement property = FindPropertyElement(name);
            property?.Remove();
            return property != null;
        }

        internal static CadSheetComponent Wrap(CadSheetSetFile file, XElement element)
        {
            switch (element.Name.LocalName)
            {
                case CadDstTree.SheetSetClass:
                    return new CadSheetSet(file, element);
                case CadDstTree.SubsetClass:
                    return new CadSheetSubset(file, element);
                default:
                    return new CadSheetSetSheet(file, element);
            }
        }

        internal static bool IsContainer(XElement element) =>
            element.Name.LocalName == CadDstTree.SheetSetClass || element.Name.LocalName == CadDstTree.SubsetClass;

        internal static bool IsComponent(XElement element) =>
            element.Name.LocalName == CadDstTree.SubsetClass || element.Name.LocalName == CadDstTree.SheetClass;

        internal void Edit() => _file.RequireEditing(_element);

        private XElement FindPropertyElement(string name) =>
            PropertyElements()
                .FirstOrDefault(e => string.Equals(CadDstTree.PropName(e), name, StringComparison.OrdinalIgnoreCase));

        private IEnumerable<XElement> PropertyElements()
        {
            XElement bag = CadDstTree.FindObject(_element, "CustomPropertyBag");
            if (bag == null)
            {
                return Enumerable.Empty<XElement>();
            }

            // An entry that holds an object instead of text (AutoCAD's transmittal setups) is not a property of this model.
            return bag.Elements(CadDstTree.CustomPropertyValueClass)
                .Where(e =>
                    CadDstTree.PropName(e) != null && e.Elements().All(c => c.Name.LocalName == CadDstTree.Prop)
                );
        }

        private static CadSheetSetProperty Read(XElement element) =>
            new CadSheetSetProperty(
                CadDstTree.PropName(element),
                CadDstTree.GetString(element, "Value"),
                (CadSheetSetPropertyFlags)CadDstTree.GetInt(element, "Flags", 0)
            );
    }

    /// <summary>
    /// A subset of a sheet set, a named group of sheets and of subsets inside it, as the Sheet List tab of the palette shows
    /// it, and the base of <see cref="CadSheetSet"/>, which is the group at the top. The <see cref="Children"/> are in set
    /// order, which is the order of the XML and nothing else.
    /// </summary>
    public class CadSheetSubset : CadSheetComponent
    {
        internal CadSheetSubset(CadSheetSetFile file, XElement element)
            : base(file, element) { }

        /// <summary>Gets or sets the name.</summary>
        public string Name
        {
            get => CadDstTree.GetString(Element, "Name");
            set
            {
                Edit();
                CadDstTree.SetString(Element, "Name", value);
            }
        }

        /// <summary>
        /// Gets or sets the sheet creation template: the layout of a drawing template (a .dwt) that a new sheet in this group
        /// is made from (<c>DefDwtLayout</c>). Null for none; the sheet set's own then applies to a subset that has none.
        /// </summary>
        public CadDrawingReference Template
        {
            get => CadDstTree.ReadDrawing(CadDstTree.FindObject(Element, "DefDwtLayout"));
            set
            {
                Edit();
                SetDrawing("DefDwtLayout", CadDstTree.LayoutReferenceClass, value);
            }
        }

        /// <summary>Gets or sets the folder a new sheet in this group is saved in (<c>NewSheetLocation</c>); null for none.</summary>
        public CadFileReference NewSheetLocation
        {
            get => CadDstTree.ReadFile(CadDstTree.FindObject(Element, "NewSheetLocation"));
            set
            {
                Edit();
                SetFile("NewSheetLocation", value);
            }
        }

        /// <summary>Gets or sets whether a new sheet asks which template to use (<c>PromptForDwt</c>).</summary>
        public bool PromptForTemplate
        {
            get => CadDstTree.GetBool(Element, "PromptForDwt");
            set
            {
                Edit();
                CadDstTree.SetBool(Element, "PromptForDwt", value);
            }
        }

        /// <summary>Gets the subsets and sheets directly in this group, in set order.</summary>
        public IReadOnlyList<CadSheetComponent> Children => Components().Select(e => Wrap(File, e)).ToList();

        /// <summary>Gets the subsets directly in this group, in set order.</summary>
        public IReadOnlyList<CadSheetSubset> Subsets =>
            Components()
                .Where(e => e.Name.LocalName == CadDstTree.SubsetClass)
                .Select(e => (CadSheetSubset)Wrap(File, e))
                .ToList();

        /// <summary>Gets the sheets directly in this group, in set order.</summary>
        public IReadOnlyList<CadSheetSetSheet> Sheets =>
            Components()
                .Where(e => e.Name.LocalName == CadDstTree.SheetClass)
                .Select(e => (CadSheetSetSheet)Wrap(File, e))
                .ToList();

        /// <summary>Walks every sheet in this group and the groups inside it, depth first in set order.</summary>
        /// <returns>The sheets.</returns>
        public IEnumerable<CadSheetSetSheet> AllSheets() =>
            Element.Descendants(CadDstTree.SheetClass).Select(e => (CadSheetSetSheet)Wrap(File, e)).ToList();

        /// <summary>Adds a subset to this group.</summary>
        /// <param name="name">The name.</param>
        /// <param name="description">The description, or null for none.</param>
        /// <param name="index">The place among the subsets and sheets of this group; -1 for the end.</param>
        /// <returns>The new subset.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the place is not -1 or from 0 to the count of children.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public CadSheetSubset AddSubset(string name, string description = null, int index = -1)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Edit();
            List<XElement> siblings = Components();
            RequireIndex(index, siblings.Count);
            XElement element = File.NewObject(CadDstTree.SubsetClass, null);
            CadDstTree.SetString(element, "Name", name);
            CadDstTree.SetString(element, "Desc", description);
            CadDstTree.InsertUnnamed(Element, element, siblings, index);
            return (CadSheetSubset)Wrap(File, element);
        }

        /// <summary>Adds a sheet to this group; it is given an empty list of sheet views, as AutoCAD gives one.</summary>
        /// <param name="number">The sheet number, free text ("A-101"); empty for none.</param>
        /// <param name="title">The sheet title.</param>
        /// <param name="layout">The drawing and layout the sheet is, or null where the sheet has none yet.</param>
        /// <param name="index">The place among the subsets and sheets of this group; -1 for the end.</param>
        /// <returns>The new sheet.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the number or the title is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the place is not -1 or from 0 to the count of children.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public CadSheetSetSheet AddSheet(string number, string title, CadDrawingReference layout, int index = -1)
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
            List<XElement> siblings = Components();
            RequireIndex(index, siblings.Count);
            XElement element = File.NewObject(CadDstTree.SheetClass, null);
            CadDstTree.SetString(element, "Number", number);
            CadDstTree.SetString(element, "Title", title);
            CadDstTree.InsertNamed(element, File.NewObject(CadDstTree.SheetViewsClass, "SheetViews"));
            if (layout != null)
            {
                XElement layoutElement = File.NewObject(CadDstTree.LayoutReferenceClass, "Layout");
                CadDstTree.WriteDrawing(layoutElement, layout);
                CadDstTree.InsertNamed(element, layoutElement);
            }

            CadDstTree.InsertUnnamed(Element, element, siblings, index);
            return (CadSheetSetSheet)Wrap(File, element);
        }

        /// <summary>Takes a sheet or a subset, with what is in it, out of this group or one inside it.</summary>
        /// <param name="component">The part to remove.</param>
        /// <exception cref="ArgumentNullException">Thrown when the part is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the part is not inside this group, of this file as it is loaded.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public void Remove(CadSheetComponent component)
        {
            Edit();
            RequireInside(component, nameof(component));
            component.Element.Remove();
        }

        /// <summary>Moves a sheet or a subset to another place in the set: another place in its own group, or another group.</summary>
        /// <param name="component">The part to move; it is inside this group or one inside it.</param>
        /// <param name="newParent">The group it goes to.</param>
        /// <param name="index">Its place among the subsets and sheets of the new group once it has left its old place; -1 for the end.</param>
        /// <exception cref="ArgumentNullException">Thrown when the part or the group is null.</exception>
        /// <exception cref="ArgumentException">Thrown when either is not of this file as it is loaded, or the group is the part itself or inside it.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the place is not -1 or from 0 to the count of the group's other children.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public void Move(CadSheetComponent component, CadSheetSubset newParent, int index)
        {
            Edit();
            RequireInside(component, nameof(component));
            if (newParent == null)
            {
                throw new ArgumentNullException(nameof(newParent));
            }

            Guard.Argument(
                newParent.File == File && newParent.Element.Document == Element.Document,
                nameof(newParent),
                "The group is not of this sheet set as it is loaded."
            );
            Guard.Argument(
                newParent.Element != component.Element && !newParent.Element.Ancestors().Contains(component.Element),
                nameof(newParent),
                "A part cannot be moved into itself."
            );
            List<XElement> siblings = newParent.Components().Where(e => e != component.Element).ToList();
            RequireIndex(index, siblings.Count);
            component.Element.Remove();
            CadDstTree.InsertUnnamed(newParent.Element, component.Element, siblings, index);
        }

        internal List<XElement> Components() => Element.Elements().Where(IsComponent).ToList();

        internal void SetFile(string propName, CadFileReference value)
        {
            XElement element = CadDstTree.FindObject(Element, propName);
            if (value == null)
            {
                element?.Remove();
                return;
            }

            if (element == null)
            {
                element = File.NewObject(CadDstTree.FileReferenceClass, propName);
                CadDstTree.InsertNamed(Element, element);
            }

            CadDstTree.WriteFile(element, value);
        }

        internal void SetDrawing(string propName, string className, CadDrawingReference value)
        {
            XElement element = CadDstTree.FindObject(Element, propName);
            if (element == null)
            {
                if (value == null)
                {
                    return;
                }

                element = File.NewObject(className, propName);
                CadDstTree.InsertNamed(Element, element);
            }

            CadDstTree.WriteDrawing(element, value);
        }

        private static void RequireIndex(int index, int count)
        {
            if (index < -1 || index > count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    "The place must be -1 for the end, or from 0 to " + count + "."
                );
            }
        }

        private void RequireInside(CadSheetComponent component, string name)
        {
            if (component == null)
            {
                throw new ArgumentNullException(name);
            }

            Guard.Argument(
                component.File == File
                    && component.Element.Document == Element.Document
                    && component.Element.Ancestors().Contains(Element),
                name,
                "The part is not inside this group of the sheet set as it is loaded."
            );
        }
    }

    /// <summary>
    /// The sheet set: the group at the top of the Sheet List, with its name and description, its project fields, the sheet
    /// creation template and the folder new sheets go to (inherited from <see cref="CadSheetSubset"/>), the page setup
    /// overrides file, the label block, the resource locations (the folders of the Model Views tab) and the view
    /// categories. Taken from <see cref="CadSheetSetFile.SheetSet"/>.
    /// </summary>
    public sealed class CadSheetSet : CadSheetSubset
    {
        internal CadSheetSet(CadSheetSetFile file, XElement element)
            : base(file, element) { }

        /// <summary>Gets or sets the project number (<c>ProjectNumber</c>).</summary>
        public string ProjectNumber
        {
            get => CadDstTree.GetString(Element, "ProjectNumber");
            set => SetText("ProjectNumber", value);
        }

        /// <summary>Gets or sets the project name (<c>ProjectName</c>).</summary>
        public string ProjectName
        {
            get => CadDstTree.GetString(Element, "ProjectName");
            set => SetText("ProjectName", value);
        }

        /// <summary>Gets or sets the project phase (<c>ProjectPhase</c>).</summary>
        public string ProjectPhase
        {
            get => CadDstTree.GetString(Element, "ProjectPhase");
            set => SetText("ProjectPhase", value);
        }

        /// <summary>Gets or sets the project milestone (<c>ProjectMilestone</c>).</summary>
        public string ProjectMilestone
        {
            get => CadDstTree.GetString(Element, "ProjectMilestone");
            set => SetText("ProjectMilestone", value);
        }

        /// <summary>Gets or sets the drawing template that holds the page setups a sheet can be published with in place of its own (<c>AltPageSetups</c>); null for none.</summary>
        public CadFileReference PageSetupOverrides
        {
            get => CadDstTree.ReadFile(CadDstTree.FindObject(Element, "AltPageSetups"));
            set
            {
                Edit();
                SetFile("AltPageSetups", value);
            }
        }

        /// <summary>Gets or sets the block that labels a view placed on a sheet (<c>DefLabelBlk</c>); null for none.</summary>
        public CadDrawingReference LabelBlock
        {
            get => CadDstTree.ReadDrawing(CadDstTree.FindObject(Element, "DefLabelBlk"));
            set
            {
                Edit();
                SetDrawing("DefLabelBlk", CadDstTree.BlockReferenceClass, value);
            }
        }

        /// <summary>Gets the folders the Model Views tab lists (<c>Resources</c>), in the order they were added.</summary>
        public IReadOnlyList<CadFileReference> ResourceLocations =>
            ResourceElements().Select(CadDstTree.ReadFile).ToList();

        /// <summary>Gets the view categories, in the order of the file; a new set holds one with no name.</summary>
        public IReadOnlyList<CadViewCategory> ViewCategories =>
            CategoryElements().Select(e => new CadViewCategory(File, e)).ToList();

        /// <summary>Adds a folder to the resource locations, at the end.</summary>
        /// <param name="location">The folder.</param>
        /// <exception cref="ArgumentNullException">Thrown when the folder is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public void AddResourceLocation(CadFileReference location)
        {
            if (location == null)
            {
                throw new ArgumentNullException(nameof(location));
            }

            Edit();
            XElement resources = CadDstTree.FindObject(Element, "Resources");
            if (resources == null)
            {
                resources = File.NewObject(CadDstTree.ResourcesClass, "Resources");
                CadDstTree.InsertNamed(Element, resources);
            }

            XElement item = File.NewObject(CadDstTree.FileReferenceClass, null);
            CadDstTree.WriteFile(item, location);
            resources.Add(item);
        }

        /// <summary>Takes a folder out of the resource locations.</summary>
        /// <param name="index">Its place in <see cref="ResourceLocations"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when there is no folder at that place.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public void RemoveResourceLocationAt(int index)
        {
            Edit();
            List<XElement> items = ResourceElements();
            if (index < 0 || index >= items.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    "There is no resource location at that place; there are " + items.Count + "."
                );
            }

            items[index].Remove();
        }

        /// <summary>Adds a view category, at the end.</summary>
        /// <param name="name">The name.</param>
        /// <param name="description">The description, or null for none.</param>
        /// <returns>The new category.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public CadViewCategory AddViewCategory(string name, string description)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Edit();
            XElement categories = CadDstTree.FindObject(Element, "ViewCategories");
            if (categories == null)
            {
                categories = File.NewObject(CadDstTree.ViewCategoriesClass, "ViewCategories");
                CadDstTree.InsertNamed(Element, categories);
            }

            XElement category = File.NewObject(CadDstTree.ViewCategoryClass, null);
            category.Add(File.NewObject(CadDstTree.CalloutBlockReferencesClass, "CalloutBlocks"));
            CadDstTree.SetString(category, "Name", name);
            CadDstTree.SetString(category, "Desc", description);
            categories.Add(category);
            return new CadViewCategory(File, category);
        }

        /// <summary>Takes a view category out; the sheet views that named it are left with no category.</summary>
        /// <param name="category">The category.</param>
        /// <exception cref="ArgumentNullException">Thrown when the category is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the category is not one of this sheet set as it is loaded.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public void RemoveViewCategory(CadViewCategory category)
        {
            if (category == null)
            {
                throw new ArgumentNullException(nameof(category));
            }

            Edit();
            Guard.Argument(
                category.File == File && CategoryElements().Contains(category.Element),
                nameof(category),
                "The category is not one of this sheet set as it is loaded."
            );
            string id = category.Id;
            foreach (XElement view in Element.Descendants(CadDstTree.SheetViewClass).ToList())
            {
                XElement reference = CadDstTree.FindObject(view, "Category");
                if (reference != null && CadDstTree.GetString(reference, "ReferencedObject") == id)
                {
                    reference.Remove();
                }
            }

            category.Element.Remove();
        }

        /// <summary>Finds a sheet or subset of the whole set by its <see cref="CadSheetComponent.Id"/>.</summary>
        /// <param name="id">The ID.</param>
        /// <returns>The part (this set too, where it is the one); null where none has that ID.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the ID is null.</exception>
        public CadSheetComponent Find(string id)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            if (Id == id)
            {
                return this;
            }

            XElement found = Element
                .Descendants()
                .FirstOrDefault(e => IsComponent(e) && (string)e.Attribute("ID") == id);
            return found == null ? null : Wrap(File, found);
        }

        private void SetText(string name, string value)
        {
            Edit();
            CadDstTree.SetString(Element, name, value);
        }

        private List<XElement> ResourceElements() =>
            (
                CadDstTree.FindObject(Element, "Resources")?.Elements(CadDstTree.FileReferenceClass)
                ?? Enumerable.Empty<XElement>()
            )
                .Where(e => CadDstTree.ReadFile(e) != null)
                .ToList();

        private List<XElement> CategoryElements() =>
            (
                CadDstTree.FindObject(Element, "ViewCategories")?.Elements(CadDstTree.ViewCategoryClass)
                ?? Enumerable.Empty<XElement>()
            ).ToList();
    }
}
