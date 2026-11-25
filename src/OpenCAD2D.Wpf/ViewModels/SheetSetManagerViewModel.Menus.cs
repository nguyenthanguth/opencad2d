using System;
using System.Collections.Generic;
using System.Linq;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    public sealed partial class SheetSetManagerViewModel
    {
        /// <summary>
        /// Makes the menu of the sheet set control: the sets open (the current one ticked), Recent (the last ten sets opened,
        /// newest first, shortened in the middle with the whole path in the tooltip, a file that is gone greyed), New Sheet Set…
        /// (NEWSHEETSET) and Open….
        /// </summary>
        /// <returns>The items, as they are now.</returns>
        public IReadOnlyList<CadMenuItemViewModel> ControlMenu()
        {
            var items = new List<CadMenuItemViewModel>();

            foreach (CadSheetSetFile file in _sets)
            {
                CadSheetSetFile set = file;
                items.Add(Item(set.SheetSet.Name, () => MakeCurrent(set), isChecked: set == _current));
            }

            if (items.Count > 0)
            {
                items.Add(CadMenuItemViewModel.Separator());
            }

            var recent = _main
                .Settings.RecentSheetSets.Select(path =>
                {
                    string full = path;
                    return new CadMenuItemViewModel(
                        ShortPath(full),
                        () => OpenSet(full),
                        isEnabled: System.IO.File.Exists(full),
                        toolTip: full
                    );
                })
                .ToList();
            items.Add(
                new CadMenuItemViewModel(
                    "Recent",
                    recent.Count > 0,
                    recent.Count > 0 ? recent : new List<CadMenuItemViewModel> { Item("None", () => { }, false) }
                )
            );
            items.Add(CadMenuItemViewModel.Separator());
            items.Add(Item("New Sheet Set…", () => _main.StartCommand("NEWSHEETSET")));
            items.Add(Item("Open…", OpenFromDialog));
            return items;
        }

        /// <summary>Publish to PDF of the toolbar: not built yet (A12 package 5a).</summary>
        public void PublishToPdf() => NotBuilt("Publish to PDF", "5a");

        /// <summary>Publish to Plotter of the toolbar: not built yet (A12 package 5a).</summary>
        public void PublishToPlotter() => NotBuilt("Publish to Plotter", "5a");

        /// <summary>
        /// Makes the list a drop-down of the toolbar opens: the page setups of the set's overrides file for Publish Using Page Setup
        /// Override, or the items of the set menu's Publish for Publish.
        /// </summary>
        /// <param name="pageSetupOverride">Whether it is the page setup override list.</param>
        /// <returns>The items; empty where no set is open.</returns>
        public IReadOnlyList<CadMenuItemViewModel> ToolbarMenu(bool pageSetupOverride)
        {
            if (_current == null)
            {
                return Array.Empty<CadMenuItemViewModel>();
            }

            CadMenuItemViewModel publish = SetMenu(_root, !IsLocked, false).Single(i => i.Header == "Publish");
            return pageSetupOverride
                ? publish.Items.Single(i => i.Header == "Publish Using Page Setup Override").Items
                : publish.Items;
        }

        /// <summary>Open… of the control: the file dialog the window gave, else OPENSHEETSET.</summary>
        private void OpenFromDialog()
        {
            if (AskOpenFile == null)
            {
                _main.StartCommand("OPENSHEETSET");
                return;
            }

            string path = AskOpenFile();

            if (path != null)
            {
                OpenSet(path);
            }
        }

        /// <summary>
        /// Makes the right-click menu of a row, with the items of AutoCAD 2021's Sheet List menus (minus the left-out ones) and
        /// which of them are on: changes are off in a locked set, Remove Subset where the subset holds something, Open where
        /// a sheet is missing, Insert Sheet List Table… unless the layout current in the app is a sheet of the set. A row inside the
        /// selection acts on every row selected of its kind; a row outside it acts alone.
        /// </summary>
        /// <param name="node">The row the menu is opened on.</param>
        /// <returns>The items; empty for a null row or where no set is open.</returns>
        public IReadOnlyList<CadMenuItemViewModel> MenuFor(SheetSetNodeViewModel node)
        {
            if (node == null || _current == null)
            {
                return Array.Empty<CadMenuItemViewModel>();
            }

            List<SheetSetNodeViewModel> chosen = SelectedNodes.Where(n => n.Kind == node.Kind).ToList();

            if (!chosen.Contains(node))
            {
                chosen = new List<SheetSetNodeViewModel> { node };
            }

            bool change = !IsLocked;
            bool table = change && CurrentLayoutIsASheet();

            switch (node.Kind)
            {
                case SheetSetNodeKind.SheetSet:
                    return SetMenu(node, change, table);
                case SheetSetNodeKind.Subset:
                    return SubsetMenu(node, change);
                default:
                    return SheetMenu(node, chosen, change, table);
            }
        }

        private IReadOnlyList<CadMenuItemViewModel> SetMenu(SheetSetNodeViewModel node, bool change, bool table)
        {
            bool overrides = _current.SheetSet.PageSetupOverrideNames().Count > 0;
            return new List<CadMenuItemViewModel>
            {
                Item("Close Sheet Set", CloseSet),
                CadMenuItemViewModel.Separator(),
                Item("New Sheet…", () => Ask(SheetSetDialog.NewSheet, node, "New Sheet"), change),
                Item("New Subset…", () => Ask(SheetSetDialog.NewSubset, node, "New Subset"), change),
                Item(
                    "Import Layout as Sheet…",
                    () => Ask(SheetSetDialog.ImportLayouts, node, "Import Layouts as Sheets"),
                    change
                ),
                CadMenuItemViewModel.Separator(),
                Item("Resave All Sheets", () => ResaveAllSheets(), change),
                CadMenuItemViewModel.Separator(),
                new CadMenuItemViewModel(
                    "Publish",
                    true,
                    new List<CadMenuItemViewModel>
                    {
                        Item("Publish to PDF", () => NotBuilt("Publish to PDF", "5a")),
                        Item("Publish to Plotter", () => NotBuilt("Publish to Plotter", "5a")),
                        OverrideItem(overrides),
                        CadMenuItemViewModel.Separator(),
                        Item(
                            "Publish in Reverse Order",
                            () => NotBuilt("Publish in Reverse Order", "5a"),
                            isChecked: false
                        ),
                        CadMenuItemViewModel.Separator(),
                        Item("Sheet Set PDF Publish Options…", () => NotBuilt("Sheet Set PDF Publish Options", "5a")),
                        Item("Publish Dialog Box…", () => _main.StartCommand("PUBLISH")),
                    }
                ),
                Item("eTransmit…", () => NotBuilt("eTransmit", "5b")),
                CadMenuItemViewModel.Separator(),
                Item("Insert Sheet List Table…", () => NotBuilt("Insert Sheet List Table", "4e"), table),
                Item("Manage Page Setups…", () => _main.StartCommand("PAGESETUP")),
                Item("Plot Stamp Settings…", () => _main.StartCommand("PLOTSTAMP")),
                CadMenuItemViewModel.Separator(),
                Item("Properties…", () => Ask(SheetSetDialog.Properties, node, "Sheet Set Properties")),
            };
        }

        private IReadOnlyList<CadMenuItemViewModel> SubsetMenu(SheetSetNodeViewModel node, bool change)
        {
            bool overrides = _current.SheetSet.PageSetupOverrideNames().Count > 0;
            return new List<CadMenuItemViewModel>
            {
                Item("New Sheet…", () => Ask(SheetSetDialog.NewSheet, node, "New Sheet"), change),
                Item("New Subset…", () => Ask(SheetSetDialog.NewSubset, node, "New Subset"), change),
                Item(
                    "Import Layout as Sheet…",
                    () => Ask(SheetSetDialog.ImportLayouts, node, "Import Layouts as Sheets"),
                    change
                ),
                Item("Remove Subset", () => Remove(new[] { node }), change && node.Children.Count == 0),
                Item("Rename Subset…", () => Ask(SheetSetDialog.RenameSubset, node, "Subset Properties"), change),
                CadMenuItemViewModel.Separator(),
                new CadMenuItemViewModel(
                    "Publish",
                    true,
                    new List<CadMenuItemViewModel>
                    {
                        Item("Publish to PDF", () => NotBuilt("Publish to PDF", "5a")),
                        Item("Publish to Plotter", () => NotBuilt("Publish to Plotter", "5a")),
                        OverrideItem(overrides),
                        CadMenuItemViewModel.Separator(),
                        Item(
                            "Publish Sheets in Subset",
                            () => NotBuilt("Publish Sheets in Subset", "5a"),
                            isChecked: true
                        ),
                        Item(
                            "Publish in Reverse Order",
                            () => NotBuilt("Publish in Reverse Order", "5a"),
                            isChecked: false
                        ),
                        CadMenuItemViewModel.Separator(),
                        Item("Publish Dialog Box…", () => _main.StartCommand("PUBLISH")),
                    }
                ),
                Item("eTransmit…", () => NotBuilt("eTransmit", "5b")),
                CadMenuItemViewModel.Separator(),
                Item("Properties…", () => Ask(SheetSetDialog.Properties, node, "Subset Properties")),
            };
        }

        private IReadOnlyList<CadMenuItemViewModel> SheetMenu(
            SheetSetNodeViewModel node,
            List<SheetSetNodeViewModel> chosen,
            bool change,
            bool table
        )
        {
            bool overrides = _current.SheetSet.PageSetupOverrideNames().Count > 0;
            bool openable = chosen.All(s => !s.IsMissing);
            bool included = !chosen[0].IsExcluded;
            return new List<CadMenuItemViewModel>
            {
                Item("Open", () => OpenSheets(chosen, false), openable, isDefault: true),
                Item("Open Read-Only", () => OpenSheets(chosen, true), openable),
                CadMenuItemViewModel.Separator(),
                Item("New Sheet…", () => Ask(SheetSetDialog.NewSheet, node, "New Sheet"), change),
                Item(
                    "Rename & Renumber…",
                    () => Ask(SheetSetDialog.RenameAndRenumber, chosen[0], "Rename & Renumber Sheet"),
                    change
                ),
                Item("Remove Sheet", () => Remove(chosen), change),
                CadMenuItemViewModel.Separator(),
                new CadMenuItemViewModel(
                    "Publish",
                    true,
                    new List<CadMenuItemViewModel>
                    {
                        Item("Publish to PDF", () => NotBuilt("Publish to PDF", "5a")),
                        Item("Publish to Plotter", () => NotBuilt("Publish to Plotter", "5a")),
                        OverrideItem(overrides),
                        CadMenuItemViewModel.Separator(),
                        Item(
                            "Include for Publish",
                            () => IncludeForPublish(chosen, !included),
                            change,
                            isChecked: included
                        ),
                        CadMenuItemViewModel.Separator(),
                        Item("Publish Dialog Box…", () => _main.StartCommand("PUBLISH")),
                    }
                ),
                Item("eTransmit…", () => NotBuilt("eTransmit", "5b")),
                CadMenuItemViewModel.Separator(),
                Item("Insert Sheet List Table…", () => NotBuilt("Insert Sheet List Table", "4e"), table),
                CadMenuItemViewModel.Separator(),
                Item("Properties…", () => Ask(SheetSetDialog.Properties, chosen[0], "Sheet Properties")),
            };
        }

        /// <summary>Publish Using Page Setup Override: the page setups of the set's overrides file, greyed where the set names none.</summary>
        private CadMenuItemViewModel OverrideItem(bool overrides)
        {
            const string header = "Publish Using Page Setup Override";

            if (!overrides)
            {
                return new CadMenuItemViewModel(
                    header,
                    false,
                    new List<CadMenuItemViewModel> { Item("None", () => { }, false) }
                );
            }

            return new CadMenuItemViewModel(
                header,
                true,
                _current
                    .SheetSet.PageSetupOverrideNames()
                    .Select(name => Item(name, () => NotBuilt("Publish Using Page Setup Override", "5a")))
                    .ToList()
            );
        }

        private static CadMenuItemViewModel Item(
            string header,
            Action run,
            bool isEnabled = true,
            bool? isChecked = null,
            bool isDefault = false
        ) => new CadMenuItemViewModel(header, run, isEnabled, isChecked: isChecked) { IsDefault = isDefault };

        /// <summary>Whether the layout current in the app is a sheet of the current set: Insert Sheet List Table is not offered on Model or on a layout that is not one.</summary>
        private bool CurrentLayoutIsASheet()
        {
            CadDocument document = _main.Document;
            CadSpace space = document?.CurrentSpace;

            if (space == null || space.IsModel || document.FileName == null)
            {
                return false;
            }

            return AllSheets()
                .Any(s =>
                    !s.IsMissing
                    && SamePath(s.Found.DrawingFile, document.FileName)
                    && string.Equals(s.Found.LayoutName, space.Name, StringComparison.Ordinal)
                );
        }

        /// <summary>Asks the window for a dialog of the next package; says on the command line that there is none where no handler shows it.</summary>
        private void Ask(SheetSetDialog dialog, SheetSetNodeViewModel node, string name)
        {
            var args = new SheetSetDialogEventArgs(dialog, node);
            DialogRequested?.Invoke(this, args);

            if (!args.Handled)
            {
                _main.Say("The " + name + " dialog is not built yet (A12 package 4b).");
            }
        }

        /// <summary>Says on the command line that an item of the menus belongs to a package not built yet.</summary>
        private void NotBuilt(string item, string package) =>
            _main.Say(item + " is not built yet (A12 package " + package + ").");
    }
}
