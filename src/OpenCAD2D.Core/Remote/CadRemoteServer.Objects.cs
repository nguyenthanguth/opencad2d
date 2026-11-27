using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>Reading objects: by handle, by filter, and the packing of them into the small drawing they travel in.</summary>
    public sealed partial class CadRemoteServer
    {
        private JsonObject Get(CadDocument document, JsonArray handles)
        {
            var entities = new List<DxfEntity>();

            foreach (object item in handles)
            {
                DxfEntity entity = item is string handle ? document.FindEntity(handle) : null;

                if (entity != null && !entity.IsErased)
                {
                    entities.Add(entity);
                }
            }

            return Pack(document, entities);
        }

        private JsonObject Query(CadDocument document, JsonObject request)
        {
            Type kind = null;
            string kindName = request.GetString("kind");

            if (kindName != null)
            {
                kind = typeof(DxfEntity).Assembly.GetType(kindName);

                if (kind == null || !typeof(DxfEntity).IsAssignableFrom(kind))
                {
                    throw new Refusal(kindName + " is not a kind of object the drawing holds.");
                }
            }

            string layer = request.GetString("layer");
            string layout = request.GetString("layout");
            IEnumerable<CadSpace> spaces = document.Spaces.Where(s => !s.IsBlockEditor);

            if (layout != null)
            {
                spaces = spaces.Where(s => string.Equals(s.Name, layout, StringComparison.OrdinalIgnoreCase));

                if (!spaces.Any())
                {
                    throw new Refusal("The drawing " + document.Title + " has no layout named " + layout + ".");
                }
            }

            var found = new List<DxfEntity>();

            foreach (CadSpace space in spaces)
            {
                foreach (DxfEntity entity in space.Block)
                {
                    if (
                        !entity.IsErased
                        && (kind == null || kind.IsInstanceOfType(entity))
                        && (layer == null || string.Equals(entity.Layer, layer, StringComparison.OrdinalIgnoreCase))
                    )
                    {
                        found.Add(entity);
                    }
                }
            }

            return Pack(document, found);
        }

        /// <summary>
        /// Puts the objects into a small drawing, each with the layers, styles and blocks it needs: the DXF text, and for each
        /// object that could be put in, its handle in the drawing and its handle in the small drawing, which the client reads
        /// the object by. An object that cannot be copied (a record that follows another, as an ATTRIB) is left out.
        /// </summary>
        private static JsonObject Pack(CadDocument document, IReadOnlyList<DxfEntity> entities)
        {
            var handles = new JsonArray();
            var references = new JsonArray();

            if (entities.Count == 0)
            {
                return new JsonObject().Set("handles", handles).Set("refs", references);
            }

            var fragment = new DxfDatabase(RemoteFragment.Version);
            IReadOnlyList<DxfEntity> copies = null;

            try
            {
                copies = document.Database.WblockCloneObjects(entities, fragment.ModelSpace);
            }
            catch (ArgumentException ex)
            {
                GeometryHelperLog.Debug("CadRemoteServer: the objects were copied one by one: " + ex.Message);
            }

            if (copies != null)
            {
                for (int i = 0; i < entities.Count; i++)
                {
                    NameStyle(document.Database, entities[i], copies[i], fragment);
                    Note(entities[i], copies[i], handles, references);
                }
            }
            else
            {
                foreach (DxfEntity entity in entities)
                {
                    try
                    {
                        DxfEntity single = document.Database.WblockCloneObjects(new[] { entity }, fragment.ModelSpace)[
                            0
                        ];
                        NameStyle(document.Database, entity, single, fragment);
                        Note(entity, single, handles, references);
                    }
                    catch (ArgumentException ex)
                    {
                        GeometryHelperLog.Debug("CadRemoteServer: " + entity + " was left out: " + ex.Message);
                    }
                }
            }

            return new JsonObject().Set("fragment", fragment.ToText()).Set("handles", handles).Set("refs", references);
        }

        /// <summary>
        /// Gives the copy of a multileader or a table the name of its style, as the original has it: the copy has no style table of
        /// its drawing to name one from, so the small drawing holds a copy of the style itself, with the settings it has (its arrow
        /// block, linetype and text style among the records the object brings), which the copy is set to. The copy keeps the arrow,
        /// linetype and text style it had, and a program reading it takes the name and the settings from the object, with no copy of
        /// the whole drawing to find them in.
        /// </summary>
        private static void NameStyle(DxfDatabase source, DxfEntity original, DxfEntity copy, DxfDatabase fragment)
        {
            try
            {
                if (original is DxfMLeader leader && copy is DxfMLeader leaderCopy && leader.MLeaderStyle != null)
                {
                    string name = leader.MLeaderStyle;
                    DxfMLeaderStyle real = CadMLeaderStyleEditor.Find(source, name);

                    if (real != null && CadMLeaderStyleEditor.Find(fragment, name) == null)
                    {
                        CarryMLeaderStyle(fragment, real, name);
                    }

                    leaderCopy.MLeaderStyle = name;
                }
                else if (original is DxfTable table && copy is DxfTable tableCopy && table.TableStyleName != null)
                {
                    string name = table.TableStyleName;
                    DxfTableStyle real = CadTableStyleEditor.Find(source, name);

                    if (real != null && CadTableStyleEditor.Find(fragment, name) == null)
                    {
                        CadTableStyleEditor.WriteNew(fragment, name, CadTableStyleSettings.Of(real));
                    }

                    tableCopy.TableStyle = CadTableStyleEditor.Find(fragment, name);
                }
            }
            catch (Exception ex)
                when (ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                GeometryHelperLog.Debug("CadRemoteServer: the style of " + original + " was not named: " + ex.Message);
            }
        }

        /// <summary>
        /// Writes a style of the drawing into the small drawing under its name, with what the object it comes with brought: the
        /// text style, linetype, arrow block and block of the style are the small drawing's own of those names, and left out where
        /// the object did not bring them, so that the arrow block of a leader that takes its arrow from the style stays its own.
        /// </summary>
        private static void CarryMLeaderStyle(DxfDatabase fragment, DxfMLeaderStyle real, string name)
        {
            CadMLeaderStyleSettings settings = CadMLeaderStyleSettings.Of(real);
            DxfBlockTableRecord arrow = real.ArrowSymbol;
            settings.Arrowhead = CadArrowheads.ClosedFilled;

            if (!fragment.TextStyleTable.Has(settings.TextStyle ?? string.Empty))
            {
                settings.TextStyle = "Standard";
            }

            if (!string.IsNullOrEmpty(settings.BlockName) && !fragment.BlockTable.Has(settings.BlockName))
            {
                settings.BlockName = string.Empty;
            }

            DxfMLeaderStyle style = CadMLeaderStyleEditor.WriteNew(fragment, name, settings);

            if (arrow != null && fragment.BlockTable.Has(arrow.Name))
            {
                style.ArrowSymbol = fragment.BlockTable[arrow.Name];
            }
        }

        private static void Note(DxfEntity original, DxfEntity copy, JsonArray handles, JsonArray references)
        {
            if (!string.IsNullOrEmpty(copy.Handle) && !copy.IsErased && !string.IsNullOrEmpty(original.Handle))
            {
                handles.Add(original.Handle);
                references.Add(copy.Handle);
            }
        }
    }
}
