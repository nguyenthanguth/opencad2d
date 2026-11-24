#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace OpenCAD2D.Wpf.Plugins
{
    /// <summary>
    /// A plugin's .csproj as the app reads it, with <c>System.Xml.Linq</c> and no MSBuild: what the compiler is told (the
    /// assembly's name and version, the language, the usings and the symbols), the files it compiles (every .cs below the
    /// folder but <c>bin</c> and <c>obj</c>, less what <c>Compile Remove</c> takes out) and the libraries it names
    /// (<c>Reference</c> with a <c>HintPath</c> inside the folder). What cannot be read is an error of its line, such as
    /// <c>Stairs.csproj(7,10): error OC0002: ...</c>; what is skipped on purpose is a warning line. Reading touches nothing of
    /// the compiler, so that a build taken from the cache never loads it.
    /// </summary>
    internal sealed class CadPluginProject
    {
        /// <summary>The assemblies of the app a plugin is built against: its own, never taken from a plugin's folder.</summary>
        private static readonly string[] Host =
        {
            "GeometryHelper",
            "OpenCAD2D.Core",
            "OpenCAD2D.Remote",
            "OpenCAD2D.Wpf",
        };

        /// <summary>The usings the SDK adds for <c>ImplicitUsings</c>.</summary>
        private static readonly string[] SdkUsings =
        {
            "System",
            "System.Collections.Generic",
            "System.IO",
            "System.Linq",
            "System.Net.Http",
            "System.Threading",
            "System.Threading.Tasks",
        };

        /// <summary>The symbols the SDK defines for a Release build of <c>net8.0-windows</c>, in the order it lists them.</summary>
        private static readonly string[] SdkSymbols = BuildSdkSymbols();

        private static readonly string[] DotNetFolders =
        {
            Path.GetDirectoryName(typeof(object).Assembly.Location),
            Path.GetDirectoryName(typeof(System.Windows.Window).Assembly.Location),
        };

        private CadPluginProject() { }

        /// <summary>Gets the full path of the .csproj.</summary>
        internal string FullPath { get; private set; }

        /// <summary>Gets the full path of its folder, which holds the sources.</summary>
        internal string Folder { get; private set; }

        /// <summary>Gets the file name of the .csproj, which the lines about it begin with.</summary>
        internal string FileName { get; private set; }

        /// <summary>Gets the .csproj as written, which the cache key takes whole.</summary>
        internal byte[] Bytes { get; private set; }

        /// <summary>Gets the assembly's name: <c>AssemblyName</c>, else the name of the .csproj without its extension.</summary>
        internal string AssemblyName { get; private set; }

        /// <summary>Gets <c>Version</c> as written; "1.0.0" where the project has none.</summary>
        internal string Version { get; private set; } = "1.0.0";

        /// <summary>Gets whether the SDK's assembly attributes (version, file version, informational version) are generated: <c>GenerateAssemblyInfo</c>, true unless the project says false.</summary>
        internal bool GenerateAssemblyInfo { get; private set; } = true;

        /// <summary>Gets the source of the assembly attributes the SDK generates from <c>Version</c>; null where <c>GenerateAssemblyInfo</c> is false.</summary>
        internal string AssemblyInfo { get; private set; }

        /// <summary>Gets <c>LangVersion</c> as written; null where the project has none, which is the latest.</summary>
        internal string LangVersion { get; private set; }

        /// <summary>Gets the line and column of <c>LangVersion</c> in the .csproj, where the compiler's error about it is placed.</summary>
        internal int LangVersionLine { get; private set; }

        /// <summary>Gets the column of <c>LangVersion</c> (see <see cref="LangVersionLine"/>).</summary>
        internal int LangVersionColumn { get; private set; }

        /// <summary>Gets <c>Nullable</c> as written, lower case: enable, disable, warnings or annotations; "disable" where the project has none.</summary>
        internal string Nullable { get; private set; } = "disable";

        /// <summary>Gets whether <c>AllowUnsafeBlocks</c> is true.</summary>
        internal bool AllowUnsafe { get; private set; }

        /// <summary>Gets the preprocessor symbols: the SDK's for <c>net8.0-windows</c> Release, then the project's own.</summary>
        internal IReadOnlyList<string> Symbols { get; private set; }

        /// <summary>Gets the global using lines: the implicit ones, then each <c>Using</c> item, as <c>global using System;</c>.</summary>
        internal IReadOnlyList<string> Usings { get; private set; }

        /// <summary>Gets the .cs files to compile, by their path from the folder in ordinal order, each read once.</summary>
        internal IReadOnlyList<SourceFile> Sources { get; private set; }

        /// <summary>Gets the full paths of the .dll files its references name, sorted ignoring case, each once.</summary>
        internal IReadOnlyList<string> Libraries { get; private set; }

        /// <summary>
        /// Reads a .csproj. Errors and warnings are added to the lists given; a project with errors holds nothing that can be
        /// compiled.
        /// </summary>
        /// <param name="path">The full path of the .csproj.</param>
        /// <param name="errors">Receives the errors, as <c>Stairs.csproj(7,10): error OC0002: ...</c> are made of.</param>
        /// <param name="warnings">Receives the warning lines.</param>
        internal static CadPluginProject Read(string path, List<CadPluginError> errors, List<string> warnings)
        {
            var project = new CadPluginProject
            {
                FullPath = path,
                Folder = Path.GetDirectoryName(path),
                FileName = Path.GetFileName(path),
                Bytes = File.ReadAllBytes(path),
            };
            project.AssemblyName = Path.GetFileNameWithoutExtension(path);
            int before = errors.Count;
            XElement root = project.Parse(errors);

            if (root != null)
            {
                project.ReadProperties(root, errors, warnings);
                project.ReadItems(root, errors, warnings);
                project.ReadVersion(root, errors);

                if (project._noDefaultItems != null)
                {
                    errors.Add(
                        project.Error(
                            project._noDefaultItems,
                            "OC0002",
                            "EnableDefaultCompileItems false is not read: the sources are every .cs below the folder."
                        )
                    );
                }
            }

            foreach (
                string name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" }
            )
            {
                if (File.Exists(Path.Combine(project.Folder, name)))
                {
                    warnings.Add(project.FileName + ": " + name + " is not read.");
                }
            }

            if (errors.Count == before)
            {
                project.Usings = project.UsingLines;
                project.Symbols = SdkSymbols.Concat(project._defines).ToArray();
                project.ReadSources(errors);
            }

            project.Libraries = project._libraries.OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToArray();
            return project;
        }

        private readonly List<string> _defines = new List<string>();
        private readonly List<string> _libraries = new List<string>();
        private readonly List<string> _usings = new List<string>();
        private readonly List<string> _removes = new List<string>();
        private readonly List<string> _includes = new List<string>();
        private bool _implicitUsings;
        private bool _wpf;
        private bool _forms;
        private XElement _noDefaultItems;
        private XElement _versionAt;

        private IReadOnlyList<string> UsingLines
        {
            get
            {
                var names = new List<string>();

                if (_implicitUsings)
                {
                    names.AddRange(SdkUsings.Where(u => !(_wpf && (u == "System.IO" || u == "System.Net.Http"))));

                    if (_forms)
                    {
                        names.Add("System.Drawing");
                        names.Add("System.Windows.Forms");
                    }
                }

                return names.Select(u => "global using global::" + u + ";").Concat(_usings).ToArray();
            }
        }

        private XElement Parse(List<CadPluginError> errors)
        {
            XDocument document;

            try
            {
                using (var stream = new MemoryStream(Bytes))
                {
                    document = XDocument.Load(stream, LoadOptions.SetLineInfo);
                }
            }
            catch (XmlException exception)
            {
                errors.Add(
                    new CadPluginError(
                        FileName,
                        exception.LineNumber,
                        exception.LinePosition,
                        "OC0002",
                        exception.Message
                    )
                );
                return null;
            }

            XElement root = document.Root;

            if (root.Name.LocalName != "Project" || root.Attribute("Sdk") == null)
            {
                errors.Add(
                    Error(root, "OC0002", "Only an SDK-style project is read: <Project Sdk=\"Microsoft.NET.Sdk\">.")
                );
                return null;
            }

            return root;
        }

        private static string Key(XElement element) => element.Name.LocalName;

        /// <summary>
        /// Works out the assembly attributes the SDK writes for <c>Version</c>: <c>1.2.3-beta.1</c> gives the assembly version and
        /// file version <c>1.2.3.0</c> and the informational version as written; a version that is not numbers is an error.
        /// </summary>
        private void ReadVersion(XElement root, List<CadPluginError> errors)
        {
            if (!GenerateAssemblyInfo)
            {
                return;
            }

            string[] parts = Version.Split('-', '+')[0].Split('.');
            var numbers = new List<string>();

            foreach (string part in parts)
            {
                if (
                    !ushort.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out ushort number)
                    || number == ushort.MaxValue
                )
                {
                    numbers = null;
                    break;
                }

                numbers.Add(number.ToString(CultureInfo.InvariantCulture));
            }

            if (numbers == null || numbers.Count > 4)
            {
                errors.Add(
                    Error(
                        (XObject)_versionAt ?? root,
                        "OC0002",
                        "Version '" + Version + "' is not a version number such as 1.2.3 or 1.2.3-beta.1."
                    )
                );
                return;
            }

            while (numbers.Count < 4)
            {
                numbers.Add("0");
            }

            string assembly = string.Join(".", numbers);
            string informational = Version.Replace("\\", "\\\\").Replace("\"", "\\\"");
            AssemblyInfo =
                "[assembly: System.Reflection.AssemblyFileVersion(\""
                + assembly
                + "\")]\n"
                + "[assembly: System.Reflection.AssemblyInformationalVersion(\""
                + informational
                + "\")]\n"
                + "[assembly: System.Reflection.AssemblyVersion(\""
                + assembly
                + "\")]\n";
        }

        private CadPluginError Error(XObject at, string id, string message)
        {
            var line = (IXmlLineInfo)at;
            return new CadPluginError(FileName, line.LineNumber, line.LinePosition, id, message);
        }

        /// <summary>
        /// Whether an element is skipped for its <c>Condition</c>; one that holds something the app reads says so, once, in a
        /// warning line.
        /// </summary>
        private bool Skipped(XElement element, Func<XElement, bool> reads, List<string> warnings)
        {
            XAttribute condition = element.Attribute("Condition");

            if (condition == null)
            {
                return false;
            }

            if (reads(element) || element.Elements().Any(reads))
            {
                warnings.Add(
                    FileName
                        + ": <"
                        + Key(element)
                        + " Condition=\""
                        + condition.Value
                        + "\"> was not read (conditions are not evaluated)."
                );
            }

            return true;
        }

        private static readonly string[] HonouredProperties =
        {
            "AssemblyName",
            "Version",
            "LangVersion",
            "Nullable",
            "ImplicitUsings",
            "UseWPF",
            "UseWindowsForms",
            "DefineConstants",
            "AllowUnsafeBlocks",
            "GenerateAssemblyInfo",
            "EnableDefaultCompileItems",
            "TargetFramework",
        };

        private static readonly string[] HonouredItems =
        {
            "Using",
            "PackageReference",
            "ProjectReference",
            "Reference",
            "Compile",
        };

        private static bool ReadsProperty(XElement property) => Array.IndexOf(HonouredProperties, Key(property)) >= 0;

        private static bool ReadsItem(XElement item) => Array.IndexOf(HonouredItems, Key(item)) >= 0;

        private void ReadProperties(XElement root, List<CadPluginError> errors, List<string> warnings)
        {
            foreach (XElement group in root.Elements().Where(e => Key(e) == "PropertyGroup"))
            {
                if (Skipped(group, ReadsProperty, warnings))
                {
                    continue;
                }

                foreach (XElement property in group.Elements())
                {
                    if (ReadsProperty(property) && !Skipped(property, ReadsProperty, warnings))
                    {
                        Apply(property, errors, warnings);
                    }
                }
            }
        }

        private void Apply(XElement property, List<CadPluginError> errors, List<string> warnings)
        {
            string value = property.Value.Trim();
            string name = Key(property);

            if (name != "DefineConstants")
            {
                AsWritten(name, value, warnings);
            }

            switch (name)
            {
                case "AssemblyName":
                    AssemblyName = value;
                    break;
                case "Version":
                    Version = value;
                    _versionAt = property;
                    break;
                case "LangVersion":
                    LangVersion = value;
                    LangVersionLine = ((IXmlLineInfo)property).LineNumber;
                    LangVersionColumn = ((IXmlLineInfo)property).LinePosition;
                    break;
                case "Nullable":
                    value = value.ToLowerInvariant();

                    if (value != "enable" && value != "disable" && value != "warnings" && value != "annotations")
                    {
                        errors.Add(
                            Error(
                                property,
                                "OC0002",
                                "Nullable '"
                                    + property.Value.Trim()
                                    + "' is none of enable, disable, warnings and annotations."
                            )
                        );
                    }
                    else
                    {
                        Nullable = value;
                    }

                    break;
                case "ImplicitUsings":
                    _implicitUsings =
                        IsTrue(value) || string.Equals(value, "enable", StringComparison.OrdinalIgnoreCase);
                    break;
                case "UseWPF":
                    _wpf = IsTrue(value);
                    break;
                case "UseWindowsForms":
                    _forms = IsTrue(value);
                    break;
                case "AllowUnsafeBlocks":
                    AllowUnsafe = IsTrue(value);
                    break;
                case "GenerateAssemblyInfo":
                    GenerateAssemblyInfo = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
                    break;
                case "EnableDefaultCompileItems":
                    _noDefaultItems = string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                        ? property
                        : null;
                    break;
                case "DefineConstants":
                    _defines.Clear();
                    _defines.AddRange(
                        value
                            .Replace("$(DefineConstants)", string.Empty)
                            .Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    );
                    break;
                case "TargetFramework":
                    if (
                        value != "net8.0"
                        && !value.StartsWith("net8.0-windows", StringComparison.Ordinal)
                        && !Regex.IsMatch(value, @"^netstandard2\.\d+$")
                    )
                    {
                        warnings.Add(
                            FileName
                                + ": TargetFramework "
                                + value
                                + " is not net8.0 or netstandard2.x; the app compiles for its own .NET 8."
                        );
                    }

                    break;
            }
        }

        private static bool IsTrue(string value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

        private void ReadItems(XElement root, List<CadPluginError> errors, List<string> warnings)
        {
            foreach (XElement group in root.Elements().Where(e => Key(e) == "ItemGroup"))
            {
                if (Skipped(group, ReadsItem, warnings))
                {
                    continue;
                }

                foreach (XElement item in group.Elements())
                {
                    if (!ReadsItem(item) || Skipped(item, ReadsItem, warnings))
                    {
                        continue;
                    }

                    switch (Key(item))
                    {
                        case "Using":
                            ApplyUsing(item, warnings);
                            break;
                        case "PackageReference":
                            ApplyPackage(item, errors);
                            break;
                        case "ProjectReference":
                            errors.Add(
                                Error(
                                    item,
                                    "OC0002",
                                    "<ProjectReference> is not read: a plugin is built from its own folder alone."
                                )
                            );
                            break;
                        case "Reference":
                            ApplyReference(item, errors, warnings);
                            break;
                        case "Compile":
                            ApplyCompile(item, errors, warnings);
                            break;
                    }
                }
            }
        }

        /// <summary>A value of an item: the attribute of the name, else the child element of it.</summary>
        /// <summary>A value that holds an MSBuild property, which the app does not expand: it is taken as written, with one warning line.</summary>
        private void AsWritten(string element, string value, List<string> warnings)
        {
            if (value != null && value.Contains("$("))
            {
                warnings.Add(
                    FileName
                        + ": <"
                        + element
                        + "> holds $(...), which is not expanded; '"
                        + value
                        + "' is taken as written."
                );
            }
        }

        private static string Meta(XElement item, string name) =>
            (item.Attribute(name)?.Value ?? item.Elements().FirstOrDefault(e => Key(e) == name)?.Value)?.Trim();

        private void ApplyUsing(XElement item, List<string> warnings)
        {
            string include = item.Attribute("Include")?.Value.Trim();

            if (string.IsNullOrEmpty(include))
            {
                return;
            }

            AsWritten("Using", include, warnings);
            string alias = Meta(item, "Alias");
            bool isStatic = IsTrue(Meta(item, "Static") ?? string.Empty);
            _usings.Add(
                "global using "
                    + (
                        alias != null ? alias + " = "
                        : isStatic ? "static "
                        : string.Empty
                    )
                    + "global::"
                    + include
                    + ";"
            );
        }

        private void ApplyPackage(XElement item, List<CadPluginError> errors)
        {
            string id = item.Attribute("Include")?.Value.Trim() ?? string.Empty;
            string version = Meta(item, "Version");

            if (string.IsNullOrEmpty(version))
            {
                errors.Add(
                    Error(
                        item,
                        "OC0002",
                        "<PackageReference Include=\""
                            + id
                            + "\"> has no Version: central package management is not read."
                    )
                );
            }
            else if (!Array.Exists(Host, h => string.Equals(h, id, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(
                    Error(item, "OC0002", "<PackageReference Include=\"" + id + "\"> is refused: NuGet comes with TF3.")
                );
            }
        }

        private void ApplyReference(XElement item, List<CadPluginError> errors, List<string> warnings)
        {
            string include = item.Attribute("Include")?.Value.Trim() ?? string.Empty;
            string name = include.Split(',')[0].Trim();

            if (
                Array.Exists(Host, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase)) || InFramework(name)
            )
            {
                return;
            }

            string hint = Meta(item, "HintPath");

            if (string.IsNullOrEmpty(hint))
            {
                errors.Add(Error(item, "OC0002", "<Reference Include=\"" + include + "\"> has no HintPath."));
                return;
            }

            AsWritten("Reference", hint, warnings);
            string full = Inside(hint);

            if (full == null || !string.Equals(Path.GetExtension(hint), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(Error(item, "OC0001", "<Reference> takes a .dll inside the tool's folder: '" + hint + "'"));
            }
            else if (!File.Exists(full))
            {
                errors.Add(Error(item, "CS0006", "Metadata file '" + hint + "' could not be found"));
            }
            else if (!_libraries.Exists(l => string.Equals(l, full, StringComparison.OrdinalIgnoreCase)))
            {
                _libraries.Add(full);
            }
        }

        private void ApplyCompile(XElement item, List<CadPluginError> errors, List<string> warnings)
        {
            string remove = item.Attribute("Remove")?.Value;
            string include = item.Attribute("Include")?.Value;
            AsWritten("Compile", remove, warnings);
            AsWritten("Compile", include, warnings);

            if (remove != null)
            {
                _removes.AddRange(
                    remove.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim())
                );
            }

            foreach (
                string each in (include ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            )
            {
                string pattern = each.Trim();

                if (Inside(pattern) == null)
                {
                    errors.Add(
                        Error(
                            item,
                            "OC0002",
                            "<Compile Include> takes a file inside the plugin's folder: '" + pattern + "'."
                        )
                    );
                }
                else
                {
                    _includes.Add(pattern);
                }
            }
        }

        /// <summary>The full path of a path given relative to the folder (or rooted) where it lies inside the folder; null where it leaves it or is not a path.</summary>
        private string Inside(string path)
        {
            if (path.Length == 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return null;
            }

            string full = Path.GetFullPath(Path.Combine(Folder, path));
            return full.StartsWith(Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? full
                : null;
        }

        /// <summary>Whether the name is an assembly of the two frameworks the app runs on (.NET and the Windows desktop).</summary>
        private static bool InFramework(string name) =>
            name.Length > 0
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && DotNetFolders.Any(f => File.Exists(Path.Combine(f, name + ".dll")));

        /// <summary>Takes every .cs below the folder but bin and obj, adds <c>Compile Include</c> and takes <c>Compile Remove</c> out.</summary>
        private void ReadSources(List<CadPluginError> errors)
        {
            var found = new SortedDictionary<string, string>(StringComparer.Ordinal);

            foreach (string folder in CadSupportWalk.Folders(Folder))
            {
                string relative = Path.GetRelativePath(Folder, folder).Replace('\\', '/');

                if (
                    relative == "bin"
                    || relative == "obj"
                    || relative.StartsWith("bin/")
                    || relative.StartsWith("obj/")
                )
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(folder))
                {
                    if (string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        found[Path.GetRelativePath(Folder, file).Replace('\\', '/')] = file;
                    }
                }
            }

            foreach (string include in _includes)
            {
                string full = Inside(include);

                if (!include.Contains("*") && !include.Contains("?"))
                {
                    string relative = Path.GetRelativePath(Folder, full).Replace('\\', '/');

                    if (File.Exists(full))
                    {
                        found[relative] = full;
                    }
                    else
                    {
                        errors.Add(
                            new CadPluginError(
                                FileName,
                                0,
                                0,
                                "CS2001",
                                "Source file '" + include + "' could not be found."
                            )
                        );
                    }
                }
            }

            foreach (string remove in _removes)
            {
                Regex pattern = Glob(remove);

                foreach (string relative in found.Keys.Where(k => pattern.IsMatch(k)).ToList())
                {
                    found.Remove(relative);
                }
            }

            var sources = new List<SourceFile>();

            foreach (KeyValuePair<string, string> pair in found)
            {
                sources.Add(new SourceFile(pair.Value, pair.Key, File.ReadAllBytes(pair.Value)));
            }

            Sources = sources;
        }

        /// <summary>A glob of <c>Compile Remove</c> as a pattern over a path from the folder with / for separators: <c>*</c> within a name, <c>**</c> across folders, <c>?</c> one character.</summary>
        private static Regex Glob(string glob)
        {
            string text = glob.Replace('\\', '/');
            while (text.StartsWith("./", StringComparison.Ordinal))
            {
                text = text.Substring(2);
            }

            var pattern = new StringBuilder("^");

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    bool slash = i + 2 < text.Length && text[i + 2] == '/';
                    pattern.Append(slash ? "(?:.*/)?" : ".*");
                    i += slash ? 2 : 1;
                }
                else if (c == '*')
                {
                    pattern.Append("[^/]*");
                }
                else if (c == '?')
                {
                    pattern.Append("[^/]");
                }
                else
                {
                    pattern.Append(Regex.Escape(c.ToString()));
                }
            }

            return new Regex(pattern.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string[] BuildSdkSymbols()
        {
            var symbols = new List<string>
            {
                "TRACE",
                "RELEASE",
                "NET",
                "NET8_0",
                "NETCOREAPP",
                "WINDOWS",
                "WINDOWS7_0",
            };

            for (int major = 5; major <= 8; major++)
            {
                symbols.Add("NET" + major.ToString(CultureInfo.InvariantCulture) + "_0_OR_GREATER");
            }

            foreach (string version in new[] { "1_0", "1_1", "2_0", "2_1", "2_2", "3_0", "3_1" })
            {
                symbols.Add("NETCOREAPP" + version + "_OR_GREATER");
            }

            symbols.Add("WINDOWS7_0_OR_GREATER");
            return symbols.ToArray();
        }
    }
}
#endif
