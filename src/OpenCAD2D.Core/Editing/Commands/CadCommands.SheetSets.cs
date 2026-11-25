namespace OpenCAD2D
{
    /// <summary>
    /// SHEETSET and SHEETSETHIDE, AutoCAD 2021's commands for the Sheet Set Manager: the window's palette is shown, or closed.
    /// The palette is a window of the presenter that stays open beside the drawing; with no window the commands say so.
    /// </summary>
    internal static partial class CadCommands
    {
        private static void AddSheetSetsTo(CadCommandRegistry registry)
        {
            registry.Add(new CadCommand("SHEETSET", c => SheetSetPalette(c, "SHEETSET")));
            registry.Add(new CadCommand("SHEETSETHIDE", c => SheetSetPalette(c, "SHEETSETHIDE")));
        }

        private static System.Collections.Generic.IEnumerable<CadPrompt> SheetSetPalette(
            CadCommandContext c,
            string request
        )
        {
            if (!c.Session.Request(request, c.Document))
            {
                c.Write(
                    "The Sheet Set Manager is a palette of the window; there is none to "
                        + (request == "SHEETSET" ? "show." : "close.")
                );
            }

            yield break;
        }
    }
}
