using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// Reads and writes the parts of a sheet set data file's XML tree the model knows, and puts what it writes where AutoCAD
    /// puts it: scalar properties as <c>&lt;AcSmProp propname="Name" vt="8"&gt;value&lt;/AcSmProp&gt;</c>, a property at
    /// its default (an empty string, a false flag) left out, and the properties and objects an object holds in the
    /// alphabetical order of their <c>propname</c> (letter case ignored, as AutoCAD orders its custom properties), then the
    /// children that have no <c>propname</c> (the subsets and sheets of a set) in set order. Anything else in the tree is
    /// left where it is.
    /// </summary>
    internal static class CadDstTree
    {
        internal const string Prop = "AcSmProp";
        internal const string Database = "AcSmDatabase";
        internal const string SheetSetClass = "AcSmSheetSet";
        internal const string SubsetClass = "AcSmSubset";
        internal const string SheetClass = "AcSmSheet";
        internal const string SheetViewsClass = "AcSmSheetViews";
        internal const string SheetViewClass = "AcSmSheetView";
        internal const string CustomPropertyBagClass = "AcSmCustomPropertyBag";
        internal const string CustomPropertyValueClass = "AcSmCustomPropertyValue";
        internal const string FileReferenceClass = "AcSmFileReference";
        internal const string LayoutReferenceClass = "AcSmAcDbLayoutReference";
        internal const string BlockReferenceClass = "AcSmAcDbBlockRecordReference";
        internal const string ViewReferenceClass = "AcSmAcDbViewReference";
        internal const string ObjectReferenceClass = "AcSmObjectReference";
        internal const string ViewCategoriesClass = "AcSmViewCategories";
        internal const string ViewCategoryClass = "AcSmViewCategory";
        internal const string CalloutBlockReferencesClass = "AcSmCalloutBlockReferences";
        internal const string ResourcesClass = "AcSmResources";

        /// <summary>The fixed class identifier of an element name (the GUID a file must carry for AutoCAD to read the object).</summary>
        internal static string ClassId(string className)
        {
            switch (className)
            {
                case Database:
                    return "2162C6B6-0CE4-40E8-912B-46F59DFDF826";
                case SheetSetClass:
                    return "B20534F2-0978-418C-8D14-2E6928A077ED";
                case SubsetClass:
                    return "076D548F-B0F5-4FE1-B35D-7F7B73B8D322";
                case SheetClass:
                    return "16A07941-BC15-4D48-A880-9D5A211D5065";
                case LayoutReferenceClass:
                    return "94910E94-4FCA-427C-B6ED-2EC9E1C900C7";
                case FileReferenceClass:
                    return "6BF87AE7-1BEC-4BDB-98BB-5B91F7772793";
                case BlockReferenceClass:
                    return "11782523-474B-4C83-9646-57C052847FBB";
                case ViewReferenceClass:
                    return "9BEA33B1-05AD-419F-B680-BC7FF6A4F41D";
                case ObjectReferenceClass:
                    return "00DEB7FB-A073-4ECD-BCE0-121B45C6864D";
                case CustomPropertyBagClass:
                    return "4D103908-8C86-4D95-BBF4-68B9A7B00731";
                case CustomPropertyValueClass:
                    return "8D22A2A4-1777-4D78-84CC-69EF741FE954";
                case SheetViewsClass:
                    return "F40F931B-64BC-4B90-9FC8-A11A77D6815B";
                case SheetViewClass:
                    return "B6E09611-4659-4F0D-981D-D62B11FD8426";
                case ResourcesClass:
                    return "3F0FAF10-09DE-4EBA-AED1-C4E4D6FECF5D";
                case "AcSmPublishOptions":
                    return "F57F96E7-0F16-4DC9-8F09-52F7BB389AB6";
                case "AcSmSheetSelSets":
                    return "444780B8-6527-43A8-8DC4-FAB41B7E48BB";
                case ViewCategoriesClass:
                    return "021730DF-5BEA-48E9-BC7A-35087A674FD0";
                case ViewCategoryClass:
                    return "4AEA81ED-C24F-477B-A534-EA69220A276A";
                case "AcSmCalloutBlocks":
                    return "203EAB46-483B-4E6B-A10B-15E9A4B210FF";
                case CalloutBlockReferencesClass:
                    return "67C52FE4-0A6B-4C82-A4CC-5E68537747B0";
                case "AcSmProjectPointLocations":
                    return "E40EA246-BAB4-4907-81A5-511EA30C16FD";
                default:
                    throw new ArgumentException(
                        "Not a class of a sheet set data file: " + className,
                        nameof(className)
                    );
            }
        }

        /// <summary>Gets the <c>propname</c> of an element, or null where it has none.</summary>
        internal static string PropName(XElement element) => (string)element.Attribute("propname");

        /// <summary>Compares two <c>propname</c>s as AutoCAD orders them: letter case ignored, then the ordinal order as the tie-break.</summary>
        internal static int Compare(string a, string b)
        {
            int c = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }

        /// <summary>Finds the scalar property of a name among an object's children.</summary>
        internal static XElement FindProp(XElement owner, string name) =>
            owner.Elements(Prop).FirstOrDefault(e => PropName(e) == name);

        /// <summary>Finds the object a property name holds among an object's children (a layout reference, a file reference, a bag).</summary>
        internal static XElement FindObject(XElement owner, string name) =>
            owner.Elements().FirstOrDefault(e => e.Name.LocalName != Prop && PropName(e) == name);

        /// <summary>Gets a string property; "" where it is not there.</summary>
        internal static string GetString(XElement owner, string name) => FindProp(owner, name)?.Value ?? string.Empty;

        /// <summary>Sets a string property (<c>vt="8"</c>); an empty or null value takes the property out, as AutoCAD writes none.</summary>
        internal static void SetString(XElement owner, string name, string value)
        {
            XElement property = FindProp(owner, name);
            if (string.IsNullOrEmpty(value))
            {
                property?.Remove();
            }
            else if (property != null)
            {
                property.Value = value;
            }
            else
            {
                InsertNamed(
                    owner,
                    new XElement(Prop, new XAttribute("propname", name), new XAttribute("vt", "8"), value)
                );
            }
        }

        /// <summary>Gets a COM boolean property (<c>vt="2"</c>, -1 true); false where it is not there.</summary>
        internal static bool GetBool(XElement owner, string name)
        {
            string text = FindProp(owner, name)?.Value;
            return text != null
                && int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                && n != 0;
        }

        /// <summary>Sets a boolean property; false takes it out, as AutoCAD writes none.</summary>
        internal static void SetBool(XElement owner, string name, bool value)
        {
            XElement property = FindProp(owner, name);
            if (!value)
            {
                property?.Remove();
            }
            else if (property != null)
            {
                property.Value = "-1";
            }
            else
            {
                InsertNamed(
                    owner,
                    new XElement(Prop, new XAttribute("propname", name), new XAttribute("vt", "2"), "-1")
                );
            }
        }

        /// <summary>Gets an integer property (<c>vt="3"</c>); the fallback where it is not there or not a number.</summary>
        internal static int GetInt(XElement owner, string name, int fallback)
        {
            string text = FindProp(owner, name)?.Value;
            return
                text != null && int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                ? n
                : fallback;
        }

        /// <summary>Sets an integer property.</summary>
        internal static void SetInt(XElement owner, string name, int value)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            XElement property = FindProp(owner, name);
            if (property != null)
            {
                property.Value = text;
            }
            else
            {
                InsertNamed(
                    owner,
                    new XElement(Prop, new XAttribute("propname", name), new XAttribute("vt", "3"), text)
                );
            }
        }

        /// <summary>
        /// Puts an element that has a <c>propname</c> among its new parent's children: before the first child that has none,
        /// or whose <c>propname</c> comes later, else at the end.
        /// </summary>
        internal static void InsertNamed(XElement owner, XElement child)
        {
            string name = PropName(child);
            foreach (XElement sibling in owner.Elements())
            {
                string other = PropName(sibling);
                if (other == null || Compare(other, name) > 0)
                {
                    sibling.AddBeforeSelf(child);
                    return;
                }
            }

            owner.Add(child);
        }

        /// <summary>Puts a child that has no <c>propname</c> (a sheet, a subset, a view) among the children of its kind, at a place counted among them.</summary>
        /// <param name="owner">The parent.</param>
        /// <param name="child">The new child.</param>
        /// <param name="siblings">The children of the parent of that kind, in order.</param>
        /// <param name="index">Their place among them; -1 for the end.</param>
        internal static void InsertUnnamed(XElement owner, XElement child, IReadOnlyList<XElement> siblings, int index)
        {
            if (index < 0 || index >= siblings.Count)
            {
                if (siblings.Count > 0)
                {
                    siblings[siblings.Count - 1].AddAfterSelf(child);
                }
                else
                {
                    owner.Add(child);
                }
            }
            else
            {
                siblings[index].AddBeforeSelf(child);
            }
        }

        /// <summary>The text of a handle as the file keeps it: upper-case hexadecimal.</summary>
        internal static string Handle(string handle) => handle?.ToUpperInvariant();

        /// <summary>Reads a file reference from its element; null where the element has none of the four forms.</summary>
        internal static CadFileReference ReadFile(XElement element)
        {
            if (element == null)
            {
                return null;
            }

            var reference = new CadFileReference(
                GetString(element, "FileName"),
                GetString(element, "Relative_FileName"),
                GetString(element, "Environ_FileName"),
                GetString(element, "SpecialFolder_FileName")
            );
            return
                reference.FileName == null
                && reference.RelativeFileName == null
                && reference.EnvironFileName == null
                && reference.SpecialFolderFileName == null
                ? null
                : reference;
        }

        /// <summary>Writes the four forms of a file reference into an element (a form that is null takes its property out).</summary>
        internal static void WriteFile(XElement element, CadFileReference reference)
        {
            SetString(element, "Environ_FileName", reference?.EnvironFileName);
            SetString(element, "FileName", reference?.FileName);
            SetString(element, "Relative_FileName", reference?.RelativeFileName);
            SetString(element, "SpecialFolder_FileName", reference?.SpecialFolderFileName);
        }

        /// <summary>Reads an object of a drawing (a layout, a block, a view) from its element; null where it names no drawing.</summary>
        internal static CadDrawingReference ReadDrawing(XElement element)
        {
            CadFileReference file = ReadFile(element);
            return file == null
                ? null
                : new CadDrawingReference(
                    file,
                    GetString(element, "Name"),
                    GetString(element, "AcDbHandle"),
                    GetString(element, "OwnerAcDbHandle")
                );
        }

        /// <summary>Writes an object of a drawing into its element; null clears every property of it and leaves the element.</summary>
        internal static void WriteDrawing(XElement element, CadDrawingReference reference)
        {
            WriteFile(element, reference?.File);
            SetString(element, "AcDbHandle", Handle(reference?.Handle));
            SetString(element, "Name", reference?.Name);
            SetString(element, "OwnerAcDbHandle", Handle(reference?.OwnerHandle));
        }
    }
}
