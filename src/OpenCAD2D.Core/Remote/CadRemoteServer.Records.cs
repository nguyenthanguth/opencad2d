using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>The table records a commit adds, the linetypes of a drawing and its current dimension style, from outside.</summary>
    public sealed partial class CadRemoteServer
    {
        private static JsonObject Linetypes(CadDocument document)
        {
            var names = new JsonArray();

            foreach (DxfLinetypeTableRecord linetype in document.Database.LinetypeTable)
            {
                names.Add(linetype.Name);
            }

            return new JsonObject().Set("names", names);
        }

        private static JsonObject CurrentDimStyle(CadDocument document, string name)
        {
            if (name != null)
            {
                CadEditResult result = CadDimStyleEditor.SetCurrent(document, name);

                if (!result.Succeeded)
                {
                    throw new Refusal(result.Message);
                }
            }

            return new JsonObject().Set("name", CadDimStyleEditor.Current(document.Database));
        }

        // ---- Records in a commit -------------------------------------------------------------------------------------

        /// <summary>The names of the linetypes the records of a commit add, which a layer of the commit may have.</summary>
        private static HashSet<string> LinetypesAdded(JsonObject request)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (object item in request.GetArray("operations"))
            {
                if (!(item is JsonObject operation))
                {
                    throw new Refusal("Each change in the member operations has to be an object.");
                }

                if (operation.GetString("op") == "records")
                {
                    JsonObject tables = operation.GetObject("tables")
                        ?? throw new Refusal("The change records needs the member tables.");

                    foreach (object name in tables.GetArray("LTYPE"))
                    {
                        names.Add(name as string ?? throw new Refusal("The member LTYPE has to be a list of text."));
                    }
                }
            }

            return names;
        }

        /// <summary>
        /// Checks the records an <c>AddRecords</c> sent, in words, before anything is applied: each is in the commit's small
        /// drawing.
        /// </summary>
        private static Step PlanRecords(DxfDatabase fragment, JsonObject tables)
        {
            Plan("STYLE", "text style", fragment?.TextStyleTable);
            Plan("LTYPE", "linetype", fragment?.LinetypeTable);
            Plan("BLOCK", "block", fragment?.BlockTable);
            Plan("DIMSTYLE", "dimension style", fragment?.DimStyleTable);
            return new Step { Kind = StepKind.Records, Tables = tables };

            void Plan(string kind, string word, DxfSymbolTable sent)
            {
                foreach (object item in tables.GetArray(kind))
                {
                    string name = item as string;

                    if (string.IsNullOrEmpty(name) || sent == null || !sent.Has(name))
                    {
                        throw new Refusal("The commit does not hold the " + word + " " + name + " it adds.");
                    }
                }
            }
        }

        /// <summary>
        /// Writes the records of a commit into the drawing, as <see cref="CadTableRecords.Add"/> does for a drawing of a file, and
        /// tells the edit of each.
        /// </summary>
        private static void WriteRecords(
            CadDocument document,
            CadEditScope scope,
            DxfDatabase fragment,
            JsonObject tables
        )
        {
            // A record the drawing has is the drawing's own: the small drawing's is left out, as a merge leaves it.
            List<string> Names(string kind, DxfSymbolTable have) =>
                tables.GetArray(kind).Select(n => (string)n).Where(n => !have.Has(n)).ToList();
            DxfDatabase target = document.Database;
            List<string> styles = Names("STYLE", target.TextStyleTable);
            List<string> linetypes = Names("LTYPE", target.LinetypeTable);
            List<string> blocks = Names("BLOCK", target.BlockTable);
            List<string> dimensionStyles = Names("DIMSTYLE", target.DimStyleTable);
            CadTableRecords.Add(target, fragment, styles, linetypes, blocks, dimensionStyles);

            foreach (string name in styles)
            {
                scope.Record(new CadEditRecord(CadEditRecordKind.TableChanged, null, "New text style", null, name));
            }

            foreach (string name in linetypes)
            {
                scope.Record(new CadEditRecord(CadEditRecordKind.TableChanged, null, "New linetype", null, name));
            }

            foreach (string name in blocks)
            {
                scope.Record(new CadEditRecord(CadEditRecordKind.TableChanged, null, "New block", null, name));
            }

            foreach (string name in dimensionStyles)
            {
                scope.Record(
                    new CadEditRecord(CadEditRecordKind.TableChanged, null, "New dimension style", null, name)
                );
            }
        }
    }
}
