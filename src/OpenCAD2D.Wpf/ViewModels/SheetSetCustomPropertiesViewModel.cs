using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>One row of the list of Custom Properties: a name, its default value and who holds it.</summary>
    public sealed class SheetSetCustomPropertyItem
    {
        internal SheetSetCustomPropertyItem(
            string name,
            string defaultValue,
            CadSheetSetPropertyFlags owner,
            bool isNew
        )
        {
            Name = name;
            DefaultValue = defaultValue ?? string.Empty;
            Owner = owner;
            IsNew = isNew;
        }

        /// <summary>Gets the name.</summary>
        public string Name { get; }

        /// <summary>Gets the value of a property of the set, or the default value of a property of every sheet.</summary>
        public string DefaultValue { get; }

        /// <summary>Gets who holds the property: <see cref="CadSheetSetPropertyFlags.Set"/> or <see cref="CadSheetSetPropertyFlags.Sheet"/>.</summary>
        public CadSheetSetPropertyFlags Owner { get; }

        /// <summary>Gets the owner as the list says it: "Sheet Set" or "Sheet".</summary>
        public string OwnerText => Owner == CadSheetSetPropertyFlags.Sheet ? "Sheet" : "Sheet Set";

        /// <summary>Gets whether the property is added in this dialog and not written yet.</summary>
        public bool IsNew { get; }
    }

    /// <summary>
    /// Custom Properties, as the board A12CustomProperties draws it (AutoCAD 2021's Sheet Set Custom Properties): the list of the
    /// set's custom properties with Name, Default Value and Owner, and Add… and Delete. A property is never edited, only deleted
    /// and added again. What is added and deleted is kept in the dialog until OK writes the set once: the deletes first, which
    /// take a property out of the set and out of every sheet, then the adds, a property of every sheet being given at its
    /// default to every sheet.
    /// </summary>
    public sealed class SheetSetCustomPropertiesViewModel : SheetSetDialogViewModel
    {
        private readonly ObservableCollection<SheetSetCustomPropertyItem> _items =
            new ObservableCollection<SheetSetCustomPropertyItem>();
        private readonly List<string> _deleted = new List<string>();
        private SheetSetCustomPropertyItem _selected;

        /// <summary>Makes the dialog over a set, the list read from the set as the file has it.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        public SheetSetCustomPropertiesViewModel(CadSheetSetFile file, SheetSetDialogOptions options = null)
            : base(file, options)
        {
            foreach (CadSheetSetProperty property in File.SheetSet.CustomProperties)
            {
                _items.Add(
                    new SheetSetCustomPropertyItem(
                        property.Name,
                        property.Value,
                        property.IsSheetProperty ? CadSheetSetPropertyFlags.Sheet : CadSheetSetPropertyFlags.Set,
                        false
                    )
                );
            }
        }

        /// <summary>Gets the dialog's title: "Custom Properties".</summary>
        public override string Title => Titled("Custom Properties");

        /// <summary>Gets the properties in the list, those added here at the end.</summary>
        public IReadOnlyList<SheetSetCustomPropertyItem> Items => _items;

        /// <summary>Gets or sets the property clicked in the list.</summary>
        public SheetSetCustomPropertyItem SelectedItem
        {
            get => _selected;
            set
            {
                if (Set(ref _selected, value))
                {
                    Raise(nameof(CanDelete));
                }
            }
        }

        /// <summary>Gets or sets what Delete asks before it deletes: given the name, it returns whether to go on. The window sets it to a question; null goes on without asking.</summary>
        public Func<string, bool> ConfirmDelete { get; set; }

        /// <summary>Gets whether Add… is live: not on a locked set.</summary>
        public bool CanAdd => !IsReadOnly;

        /// <summary>Gets whether Delete is live: a property is clicked, and the set is not locked.</summary>
        public bool CanDelete => !IsReadOnly && _selected != null;

        /// <summary>Gets why a name cannot be a new property's, in the dialog's words.</summary>
        /// <param name="name">The name typed.</param>
        /// <returns>The reason (empty, a dot or a double quote that a field cannot carry, or used already, letter case ignored); null where the name fits.</returns>
        public string WhyNotName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "A custom property needs a name.";
            }

            if (name.IndexOf('.') >= 0 || name.IndexOf('"') >= 0)
            {
                return "The name \"" + name + "\" holds a dot or a double quote, which a field cannot carry.";
            }

            SheetSetCustomPropertyItem used = _items.FirstOrDefault(i =>
                string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)
            );
            return used == null ? null : "A custom property named " + used.Name + " already exists in this sheet set.";
        }

        /// <summary>Adds a property to the list; it is written at OK.</summary>
        /// <param name="name">The name.</param>
        /// <param name="defaultValue">The value, or the default value of a property of every sheet; empty for none.</param>
        /// <param name="owner">Who holds it: <see cref="CadSheetSetPropertyFlags.Set"/> or <see cref="CadSheetSetPropertyFlags.Sheet"/>.</param>
        /// <exception cref="ArgumentException">Thrown when <see cref="WhyNotName"/> refuses the name, or the owner is neither.</exception>
        public void Add(string name, string defaultValue, CadSheetSetPropertyFlags owner)
        {
            string why = WhyNotName(name);
            if (why != null)
            {
                throw new ArgumentException(why, nameof(name));
            }

            if (owner != CadSheetSetPropertyFlags.Set && owner != CadSheetSetPropertyFlags.Sheet)
            {
                throw new ArgumentException(
                    "A custom property is held by the sheet set or by every sheet.",
                    nameof(owner)
                );
            }

            _items.Add(new SheetSetCustomPropertyItem(name.Trim(), defaultValue, owner, true));
        }

        /// <summary>Takes the property clicked out of the list, after asking; it is deleted from the set and every sheet at OK.</summary>
        /// <returns>True when it was taken out; false where nothing is clicked, the set is locked or the question was answered no.</returns>
        public bool DeleteSelected()
        {
            SheetSetCustomPropertyItem item = _selected;
            if (item == null || IsReadOnly || (ConfirmDelete != null && !ConfirmDelete(item.Name)))
            {
                return false;
            }

            _items.Remove(item);
            if (!item.IsNew)
            {
                _deleted.Add(item.Name);
            }

            SelectedItem = null;
            return true;
        }

        /// <inheritdoc />
        protected override string Validate() => null;

        /// <inheritdoc />
        protected override void Save()
        {
            List<SheetSetCustomPropertyItem> added = _items.Where(i => i.IsNew).ToList();
            if (_deleted.Count == 0 && added.Count == 0)
            {
                return;
            }

            string[] deleted = _deleted.ToArray();
            Commit(set =>
            {
                foreach (string name in deleted)
                {
                    set.DeleteCustomProperty(name);
                }

                foreach (SheetSetCustomPropertyItem item in added)
                {
                    set.AddCustomProperty(item.Name, item.DefaultValue, item.Owner);
                }
            });
        }
    }

    /// <summary>
    /// Add Custom Property, as the board A12CustomProperties draws it: a name, a default value and an owner (the sheet set, or
    /// every sheet). A name is refused in red where it is empty, holds a dot or a double quote, or a property has it already,
    /// letter case ignored, and OK is greyed while the line shows. OK adds the property to the list of Custom Properties, which
    /// writes the set at its own OK.
    /// </summary>
    public sealed class SheetSetAddPropertyViewModel : SheetSetDialogViewModel
    {
        private readonly SheetSetCustomPropertiesViewModel _list;
        private string _name = string.Empty;
        private string _defaultValue = string.Empty;
        private CadSheetSetPropertyFlags _owner = CadSheetSetPropertyFlags.Set;

        /// <summary>Makes the dialog for a list.</summary>
        /// <param name="list">The Custom Properties dialog the property is added to.</param>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        public SheetSetAddPropertyViewModel(SheetSetCustomPropertiesViewModel list)
            : base((list ?? throw new ArgumentNullException(nameof(list))).File, null)
        {
            _list = list;
        }

        /// <summary>Gets the dialog's title: "Add Custom Property".</summary>
        public override string Title => "Add Custom Property";

        /// <summary>Gets or sets the name.</summary>
        public string Name
        {
            get => _name;
            set
            {
                if (Set(ref _name, value ?? string.Empty))
                {
                    Revalidate();
                }
            }
        }

        /// <summary>Gets or sets the default value (the value, for a property of the sheet set).</summary>
        public string DefaultValue
        {
            get => _defaultValue;
            set => Set(ref _defaultValue, value ?? string.Empty);
        }

        /// <summary>Gets or sets who holds the property: <see cref="CadSheetSetPropertyFlags.Set"/> or <see cref="CadSheetSetPropertyFlags.Sheet"/>.</summary>
        public CadSheetSetPropertyFlags Owner
        {
            get => _owner;
            set
            {
                if (Set(ref _owner, value))
                {
                    Raise(nameof(OwnerIsSheet));
                }
            }
        }

        /// <summary>Gets or sets whether the owner is Sheet (a radio button's view of <see cref="Owner"/>).</summary>
        public bool OwnerIsSheet
        {
            get => _owner == CadSheetSetPropertyFlags.Sheet;
            set => Owner = value ? CadSheetSetPropertyFlags.Sheet : CadSheetSetPropertyFlags.Set;
        }

        /// <inheritdoc />
        protected override bool IsComplete => _name.Trim().Length > 0;

        /// <inheritdoc />
        protected override string Validate() => _name.Trim().Length == 0 ? null : _list.WhyNotName(_name);

        /// <inheritdoc />
        protected override void Save() => _list.Add(_name, _defaultValue, _owner);
    }
}
