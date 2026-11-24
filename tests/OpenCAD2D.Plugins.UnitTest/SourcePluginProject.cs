using System.IO;
using System.Linq;
using System.Text;

namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>The files of a plugin given as C# source, a folder with a .csproj, that the tests of such plugins write.</summary>
    internal static class SourceProject
    {
        /// <summary>
        /// A .csproj of the smallest shape a Visual Studio user writes, <c>TargetFramework</c> on line 3, then
        /// <paramref name="properties"/> from line 4, one to a line, and an ItemGroup of <paramref name="items"/> after them.
        /// </summary>
        public static string Csproj(string[] properties = null, string[] items = null) =>
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            + "    <PropertyGroup>\n"
            + "        <TargetFramework>net8.0-windows</TargetFramework>\n"
            + Lines(properties)
            + "    </PropertyGroup>\n"
            + (
                items == null || items.Length == 0
                    ? string.Empty
                    : "    <ItemGroup>\n" + Lines(items) + "    </ItemGroup>\n"
            )
            + "</Project>\n";

        private static string Lines(string[] xml) =>
            xml == null ? string.Empty : string.Concat(xml.Select(line => "        " + line + "\n"));

        /// <summary>A plugin with one command STAIRS that writes how many steps a flight of 3 m has; <paramref name="steps"/> is the class it takes the count from.</summary>
        public static string Command(string steps = "Steps") =>
            "using System.Collections.Generic;\n"
            + "using OpenCAD2D;\n"
            + "using OpenCAD2D.Plugins;\n"
            + "\n"
            + "namespace Stairs\n"
            + "{\n"
            + "    public sealed class StairsCommands\n"
            + "    {\n"
            + "        [CadCommand(\"STAIRS\")]\n"
            + "        public IEnumerable<CadPrompt> Draw(CadCommandContext c)\n"
            + "        {\n"
            + "            c.Write(\"Steps: \" + "
            + steps
            + ".Count(3000.0));\n"
            + "            yield break;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        /// <summary>The class <see cref="Command"/> takes the count from by default: 3000 mm of rise in steps of at most 175 mm.</summary>
        public const string StepsSource =
            "namespace Stairs\n{\n    public static class Steps\n    {\n        public static int Count(double rise) => (int)System.Math.Ceiling(rise / 175.0);\n    }\n}\n";

        /// <summary>A plugin with one command STAIRS that writes how many steps of at most 175 mm a rise of 3000 mm takes, the count worked out in the command.</summary>
        public static string Stairs(string message = "Steps: ") =>
            Command()
                .Replace(
                    "\"Steps: \" + Steps.Count(3000.0)",
                    "\"" + message + "\" + (int)System.Math.Ceiling(3000.0 / 175.0)"
                );

        /// <summary>Writes a file below a folder, the folders in its path made; the full path.</summary>
        public static string Write(string folder, string file, string text)
        {
            string path = Path.Combine(folder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }

        /// <summary>
        /// Writes a plugin folder: <c>Stairs.csproj</c> with the project given, and each source of <paramref name="sources"/>
        /// (name, text), the name relative to the folder.
        /// </summary>
        /// <returns>The full path of the .csproj.</returns>
        public static string Folder(string folder, string csproj, params string[] sources)
        {
            for (int i = 0; i + 1 < sources.Length; i += 2)
            {
                Write(folder, sources[i], sources[i + 1]);
            }

            return Write(folder, "Stairs.csproj", csproj);
        }
    }
}
