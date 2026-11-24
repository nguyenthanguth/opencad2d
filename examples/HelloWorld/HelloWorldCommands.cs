using System.Collections.Generic;
using OpenCAD2D;
using OpenCAD2D.Plugins;

namespace HelloWorld
{
    /// <summary>
    /// The smallest plugin that does anything: HELLOWORLD writes <c>Hello, World!</c> on the command line, asks nothing
    /// and changes nothing.
    /// </summary>
    public sealed class HelloWorldCommands
    {
        /// <summary>Writes <c>Hello, World!</c>; the command takes no prompt.</summary>
        /// <param name="c">The command's context.</param>
        /// <returns>No prompts: the command asks nothing.</returns>
        [CadCommand("HELLOWORLD", Description = "Write Hello, World! on the command line")]
        public IEnumerable<CadPrompt> Hello(CadCommandContext c)
        {
            c.Write("Hello, World!");
            yield break;
        }
    }
}
