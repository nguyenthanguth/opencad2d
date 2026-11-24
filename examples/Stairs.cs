using System.Collections.Generic;
using OpenCAD2D;
using OpenCAD2D.Plugins;

namespace Stairs
{
    public sealed class StairsCommands
    {
        [CadCommand("STAIRS")]
        public IEnumerable<CadPrompt> Draw(CadCommandContext c)
        {
            c.Write("Steps: " + (int)System.Math.Ceiling(3000.0 / 175.0)); // Steps: 18
            yield break;
        }
    }
}
