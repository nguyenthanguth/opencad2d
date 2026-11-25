using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D
{
    /// <summary>
    /// OPENSHEETSET and NEWSHEETSET, AutoCAD 2021's commands to open a sheet set and to make one, and the variables of the Sheet
    /// Set Manager: SSLOCATE and SSMAUTOOPEN (0 or 1, 1 by default), SSMPOLLTIME (20 to 600 seconds, 60), SSMSHEETSTATUS (0, 1 or
    /// 2, 2), and the two that are read only, SSMSTATE (1 while the palette is shown) and SSFOUND (the file of the set found for
    /// the drawing). SHEETSET and SHEETSETHIDE, which show and close the palette, are in <c>CadCommands.SheetSets.cs</c>.
    /// </summary>
    internal static partial class CadCommands
    {
        private const string NoSheetSetWindow =
            "The Sheet Set Manager is a palette of the window; there is none to open the sheet set in.";

        private static void AddSheetSetFilesTo(CadCommandRegistry registry)
        {
            registry.Add(new CadCommand("OPENSHEETSET", OpenSheetSet));
            registry.Add(new CadCommand("-OPENSHEETSET", OpenSheetSetTyped));
            registry.Add(new CadCommand("NEWSHEETSET", NewSheetSet));

            registry.AddVariable(
                CadSystemVariable.Whole(
                    "SSLOCATE",
                    false,
                    0,
                    1,
                    s => s.SheetSetOptions.Locate ? 1 : 0,
                    (c, v) => c.Session.SheetSetOptions.Locate = v != 0
                )
            );
            registry.AddVariable(
                CadSystemVariable.Whole(
                    "SSMAUTOOPEN",
                    false,
                    0,
                    1,
                    s => s.SheetSetOptions.AutoOpen ? 1 : 0,
                    (c, v) => c.Session.SheetSetOptions.AutoOpen = v != 0
                )
            );
            registry.AddVariable(
                CadSystemVariable.Whole(
                    "SSMPOLLTIME",
                    false,
                    CadSheetSetOptions.LeastPollSeconds,
                    CadSheetSetOptions.MostPollSeconds,
                    s => s.SheetSetOptions.PollSeconds,
                    (c, v) => c.Session.SheetSetOptions.PollSeconds = v
                )
            );
            registry.AddVariable(
                CadSystemVariable.Whole(
                    "SSMSHEETSTATUS",
                    false,
                    0,
                    2,
                    s => s.SheetSetOptions.SheetStatus,
                    (c, v) => c.Session.SheetSetOptions.SheetStatus = v
                )
            );
            registry.AddVariable(
                CadSystemVariable.ReadOnly(
                    "SSMSTATE",
                    CadVariableKind.Integer,
                    s => IsSheetSetPaletteShown(s) ? "1" : "0",
                    s => new double[] { IsSheetSetPaletteShown(s) ? 1.0 : 0.0 }
                )
            );
            registry.AddVariable(
                CadSystemVariable.ReadOnly("SSFOUND", CadVariableKind.Text, s => "\"" + s.SheetSetFound + "\"")
            );
        }

        /// <summary>Whether the window shows the Sheet Set Manager: it answers the request SSMSTATE with "1" in the answer; with no window it is not shown.</summary>
        private static bool IsSheetSetPaletteShown(CadCommandSession session)
        {
            CadUiRequestEventArgs asked = session.Ask("SSMSTATE", session.Document);
            return asked.Handled && asked.Answer == "1";
        }

        /// <summary>
        /// OPENSHEETSET: the window's Open Sheet Set dialog (the request OPENSHEETSET with no path); with FILEDIA 0 or no window,
        /// "Enter name of sheet set to open:" as -OPENSHEETSET asks it. The file chosen is opened in the Sheet Set Manager, which
        /// makes it the current sheet set.
        /// </summary>
        private static IEnumerable<CadPrompt> OpenSheetSet(CadCommandContext c)
        {
            if (
                c.Session.FileDialogs
                && c.Session.Request("OPENSHEETSET", c.Document, new CadSheetSetRequest(null, false))
            )
            {
                yield break;
            }

            foreach (CadPrompt prompt in OpenSheetSetTyped(c))
            {
                yield return prompt;
            }
        }

        /// <summary>
        /// -OPENSHEETSET: "Enter name of sheet set to open:", a .dst typed with its path or, with none, one in the folder of the
        /// drawing; the extension is added where there is none. A file that is not there is answered "File can not be opened."
        /// and one that is no sheet set data file "Sheet Set file is not valid.", AutoCAD 2021's own words for them.
        /// </summary>
        private static IEnumerable<CadPrompt> OpenSheetSetTyped(CadCommandContext c)
        {
            yield return CadPrompt.ForString("Enter name of sheet set to open").AllowingSpaces();
            string typed = c.Input.Kind == CadInputKind.Text ? c.Input.Text.Trim().Trim('"') : string.Empty;

            if (typed.Length == 0)
            {
                yield break;
            }

            string file = SheetSetFileOf(c, typed);

            if (!File.Exists(file))
            {
                c.Write("File can not be opened.");
                yield break;
            }

            if (!IsSheetSetFile(file))
            {
                c.Write("Sheet Set file is not valid.");
                yield break;
            }

            OpenInManager(c, file);
        }

        /// <summary>The full path of a .dst typed: the extension given where it has none, a relative one taken from the drawing's folder.</summary>
        private static string SheetSetFileOf(CadCommandContext c, string typed)
        {
            try
            {
                if (!Path.HasExtension(typed))
                {
                    typed += ".dst";
                }

                string folder = c.Document.DrawingFolder;
                return Path.IsPathRooted(typed) || folder == null
                    ? Path.GetFullPath(typed)
                    : Path.GetFullPath(Path.Combine(folder, typed));
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                return typed;
            }
        }

        private static bool IsSheetSetFile(string file)
        {
            try
            {
                CadSheetSetFile.Open(file);
                return true;
            }
            catch (Exception e)
                when (e is FormatException
                    || e is IOException
                    || e is UnauthorizedAccessException
                    || e is System.Xml.XmlException
                    || e is ArgumentException
                )
            {
                return false;
            }
        }

        private static void OpenInManager(CadCommandContext c, string file)
        {
            if (!c.Session.Request("OPENSHEETSET", c.Document, new CadSheetSetRequest(file, false)))
            {
                c.Write(NoSheetSetWindow);
            }
        }

        // ---- NEWSHEETSET ----

        /// <summary>
        /// NEWSHEETSET: the window's Create Sheet Set wizard (the request NEWSHEETSET); with FILEDIA 0 or no window its pages on the
        /// command line, in the wizard's words. AutoCAD 2021 has no typed form of it, so these questions are ours:
        /// "Create a sheet set using [Example/Drawings]" (an example sheet set, or existing drawings), then for an example "Select a
        /// sheet set to use as an example" (a number of the list shown, a name in it or the path of a .dst) and for drawings the
        /// drawings whose layouts become the sheets; "Name of new sheet set", "Description (optional)", "Store sheet set data file
        /// (.dst) here" and, for an example, "Create a folder hierarchy based on subsets [Yes/No]". The set is made and opened in
        /// the Sheet Set Manager.
        /// </summary>
        private static IEnumerable<CadPrompt> NewSheetSet(CadCommandContext c)
        {
            if (
                c.Session.FileDialogs
                && c.Session.Request("NEWSHEETSET", c.Document, new CadSheetSetRequest(null, false))
            )
            {
                yield break;
            }

            yield return CadPrompt.ForKeyword("Create a sheet set using", new[] { "Example", "Drawings" }, "Example");

            if (!c.Input.IsNone && !c.Input.Is("Example") && !c.Input.Is("Drawings"))
            {
                yield break;
            }

            bool example = c.Input.IsNone || c.Input.Is("Example");
            string examplePath = null;
            var drawings = new List<CadSheetImportSource>();

            if (example)
            {
                IReadOnlyList<string> found = CadSheetSetExamples.Shipped();

                for (int i = 0; i < found.Count; i++)
                {
                    c.Write("  " + (i + 1) + "  " + Path.GetFileNameWithoutExtension(found[i]));
                }

                yield return found.Count == 0
                    ? CadPrompt
                        .ForString("Select a sheet set to use as an example (the path of a .dst)")
                        .AllowingSpaces()
                    : CadPrompt.ForString("Select a sheet set to use as an example", "1").AllowingSpaces();
                string chosen = c.Input.Kind == CadInputKind.Text ? c.Input.Text.Trim().Trim('"') : string.Empty;
                examplePath = ExampleOf(c, found, chosen.Length == 0 && found.Count > 0 ? "1" : chosen);

                if (examplePath == null)
                {
                    c.Write("Cannot find the specified sheet set data (DST) file.");
                    yield break;
                }
            }
            else
            {
                yield return CadPrompt
                    .ForString("Enter the drawings whose layouts become the sheets, separated by commas")
                    .AllowingSpaces();
                string typed = c.Input.Kind == CadInputKind.Text ? c.Input.Text : string.Empty;

                foreach (string each in typed.Split(',').Select(t => t.Trim().Trim('"')).Where(t => t.Length > 0))
                {
                    string drawing = DrawingOf(c, each);
                    CadSheetImportLayout[] ok;

                    try
                    {
                        IReadOnlyList<CadSheetImportLayout> layouts = CadSheetSetFile.CheckLayouts(drawing);
                        ok = layouts.Where(l => !l.IsRefused).ToArray();

                        foreach (CadSheetImportLayout refused in layouts.Where(l => l.IsRefused))
                        {
                            c.Write(refused.Refusal);
                        }
                    }
                    catch (Exception e)
                        when (e is IOException
                            || e is UnauthorizedAccessException
                            || e is ArgumentException
                            || e is NotSupportedException
                            || e is FormatException
                            || e is InvalidOperationException
                        )
                    {
                        c.Write("Cannot read the drawing " + drawing + ": " + e.Message);
                        yield break;
                    }

                    if (ok.Length > 0)
                    {
                        drawings.Add(new CadSheetImportSource(null, drawing, ok.Select(l => l.Name)));
                    }
                }
            }

            string path = null;
            string name = null;

            while (path == null)
            {
                yield return CadPrompt.ForString("Name of new sheet set").AllowingSpaces();
                name = c.Input.Kind == CadInputKind.Text ? c.Input.Text.Trim() : string.Empty;

                if (name.Length == 0)
                {
                    yield break;
                }

                yield return CadPrompt.ForString("Description (optional)").AllowingSpaces();
                string description = c.Input.Kind == CadInputKind.Text ? c.Input.Text.Trim() : null;

                string folder = c.Session.SheetSetOptions.StoreFolder;
                yield return CadPrompt.ForString("Store sheet set data file (.dst) here", folder).AllowingSpaces();
                string typedFolder = c.Input.Kind == CadInputKind.Text ? c.Input.Text.Trim().Trim('"') : string.Empty;
                folder = typedFolder.Length == 0 ? folder : typedFolder;
                string made = Path.Combine(folder, FileNameOf(name) + ".dst");

                if (File.Exists(made))
                {
                    c.Write("This file already exists. Please enter a different filename.");
                    continue;
                }

                if (!Directory.Exists(folder))
                {
                    yield return CadPrompt.ForKeyword(
                        "The location entered is not an existing folder. Create the folder",
                        new[] { "Yes", "No" },
                        "Yes"
                    );

                    if (c.Input.Is("No"))
                    {
                        continue;
                    }
                }

                bool hierarchy = false;

                if (example)
                {
                    yield return CadPrompt.ForKeyword(
                        "Create a folder hierarchy based on subsets",
                        new[] { "Yes", "No" },
                        "No"
                    );
                    hierarchy = c.Input.Is("Yes");
                }

                CadSheetSetFile file = MakeSheetSet(c, made, name, description, examplePath, hierarchy, drawings);

                if (file == null)
                {
                    yield break;
                }

                path = file.Path;
            }

            c.Write("The sheet set \"" + name + "\" is made: " + path + ".");
            OpenInManager(c, path);
        }

        /// <summary>The file of the example a wizard's answer names: a number of the list, a name in it (letter case ignored, the extension left out or not), or the path of a .dst; null where it names none.</summary>
        private static string ExampleOf(CadCommandContext c, IReadOnlyList<string> found, string answer)
        {
            if (
                int.TryParse(
                    answer,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int number
                )
                && number >= 1
                && number <= found.Count
            )
            {
                return found[number - 1];
            }

            string named = found.FirstOrDefault(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), answer, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(f), answer, StringComparison.OrdinalIgnoreCase)
            );

            if (named != null)
            {
                return named;
            }

            string file = SheetSetFileOf(c, answer);
            return File.Exists(file) ? file : null;
        }

        private static string DrawingOf(CadCommandContext c, string typed)
        {
            try
            {
                string folder = c.Document.DrawingFolder;
                return Path.IsPathRooted(typed) || folder == null
                    ? Path.GetFullPath(typed)
                    : Path.GetFullPath(Path.Combine(folder, typed));
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                return typed;
            }
        }

        private static string FileNameOf(string name)
        {
            char[] letters = name.ToCharArray();

            for (int i = 0; i < letters.Length; i++)
            {
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), letters[i]) >= 0 || char.IsControl(letters[i]))
                {
                    letters[i] = '_';
                }
            }

            return new string(letters).Trim(' ', '.');
        }

        private static CadSheetSetFile MakeSheetSet(
            CadCommandContext c,
            string path,
            string name,
            string description,
            string examplePath,
            bool hierarchy,
            List<CadSheetImportSource> drawings
        )
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                return examplePath != null
                    ? CadSheetSetFile.CreateFromExample(path, name, description, examplePath, hierarchy)
                    : CadSheetSetFile.CreateFromDrawings(path, name, description, drawings, null);
            }
            catch (Exception e)
                when (e is IOException
                    || e is UnauthorizedAccessException
                    || e is ArgumentException
                    || e is NotSupportedException
                    || e is FormatException
                    || e is InvalidOperationException
                )
            {
                c.Write("Cannot create the sheet set file " + path + ": " + e.Message);
                return null;
            }
        }
    }
}
