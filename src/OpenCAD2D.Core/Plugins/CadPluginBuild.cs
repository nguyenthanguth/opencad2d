using System;
using System.Collections.Generic;

namespace OpenCAD2D.Plugins
{
    /// <summary>
    /// What compiling a plugin given as C# source gave (<see cref="CadPluginLoader.Compile"/>): the assembly and its PDB, or
    /// the errors that kept it from being built. A plugin Stairs.cs that compiles gives <c>Stairs.dll</c> and <c>Stairs.pdb</c>
    /// in the cache; one with an unknown name on line 12 gives one error,
    /// <c>Stairs.cs(12,9): error CS0103: The name 'x' does not exist in the current context</c>.
    /// </summary>
    public sealed class CadPluginBuild
    {
        /// <summary>Initializes a build that succeeded.</summary>
        /// <param name="assemblyPath">The full path of the assembly built.</param>
        /// <param name="pdbPath">The full path of its PDB.</param>
        /// <param name="fromCache">Whether the assembly was taken from the cache with no compile.</param>
        public CadPluginBuild(string assemblyPath, string pdbPath, bool fromCache)
        {
            AssemblyPath = assemblyPath ?? throw new ArgumentNullException(nameof(assemblyPath));
            PdbPath = pdbPath ?? throw new ArgumentNullException(nameof(pdbPath));
            FromCache = fromCache;
            Errors = Array.Empty<string>();
        }

        /// <summary>Initializes a build that failed.</summary>
        /// <param name="errorCount">The number of errors, which may be more than the lines given.</param>
        /// <param name="errors">The error lines, the first ones only, as <c>Stairs.cs(12,9): error CS0103: ...</c>.</param>
        public CadPluginBuild(int errorCount, IReadOnlyList<string> errors)
        {
            if (errorCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(errorCount), "A build that failed has an error at least.");
            }

            ErrorCount = errorCount;
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        /// <summary>Gets whether the assembly was built (or found in the cache).</summary>
        public bool Succeeded => AssemblyPath != null;

        /// <summary>Gets the full path of the assembly; null where the build failed.</summary>
        public string AssemblyPath { get; }

        /// <summary>Gets the full path of the assembly's PDB; null where the build failed.</summary>
        public string PdbPath { get; }

        /// <summary>Gets whether the assembly was taken from the cache, the sources unchanged since they were last compiled.</summary>
        public bool FromCache { get; }

        /// <summary>Gets the number of errors; 0 where the build succeeded.</summary>
        public int ErrorCount { get; }

        /// <summary>Gets the error lines, at most the first 20, sorted by file, line, column and id; empty where the build succeeded.</summary>
        public IReadOnlyList<string> Errors { get; }
    }
}
