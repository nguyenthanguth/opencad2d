using System;
using System.IO;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The first start of OpenCAD2D takes the user's files from the folders of GeometryHelper CAD, as the app was called
    /// before: once, into a folder that is not there yet, the old folders left as they were.
    /// </summary>
    public sealed class CadOldFoldersTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "OpenCAD2D.UnitTest",
            Guid.NewGuid().ToString("N")
        );

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private string Write(string relative, string text)
        {
            string path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }

        [Fact]
        public void TheFirstStart_CopiesTheSettingsFolderAndThePluginFolder_IntoTheNewOne()
        {
            string drawing = Path.Combine(_root, "GeometryHelper", "Drawing");
            string dxf = Path.Combine(_root, "GeometryHelper", "Dxf");
            Write(@"GeometryHelper\Drawing\settings.json", "{\"recent\":1}");
            Write(@"GeometryHelper\Drawing\PlotStyles\monochrome.ctb", "ctb");
            Write(@"GeometryHelper\Drawing\Template\a3.dwt", "dwt");
            Write(@"GeometryHelper\Dxf\plugins.json", "[]");
            Write(@"GeometryHelper\Dxf\settings.json", "the second copy");
            string target = Path.Combine(_root, "OpenCAD2D");

            int copied = CadOldFolders.CopyOnce(target, drawing, dxf);

            // A name in both old folders keeps the first folder's file.
            Assert.Equal(4, copied);
            Assert.Equal("{\"recent\":1}", File.ReadAllText(Path.Combine(target, "settings.json")));
            Assert.Equal("ctb", File.ReadAllText(Path.Combine(target, "PlotStyles", "monochrome.ctb")));
            Assert.Equal("dwt", File.ReadAllText(Path.Combine(target, "Template", "a3.dwt")));
            Assert.Equal("[]", File.ReadAllText(Path.Combine(target, "plugins.json")));

            // The old folders are left for an older build.
            Assert.True(File.Exists(Path.Combine(drawing, "settings.json")));
            Assert.True(File.Exists(Path.Combine(dxf, "plugins.json")));
        }

        [Fact]
        public void ALaterStart_CopiesNothing_ThoughTheOldFilesChanged()
        {
            string drawing = Path.Combine(_root, "GeometryHelper", "Drawing");
            Write(@"GeometryHelper\Drawing\settings.json", "old");
            string target = Path.Combine(_root, "OpenCAD2D");
            Write(@"OpenCAD2D\settings.json", "new");
            Write(@"GeometryHelper\Drawing\crash.log", "a crash");

            Assert.Equal(0, CadOldFolders.CopyOnce(target, drawing));
            Assert.Equal("new", File.ReadAllText(Path.Combine(target, "settings.json")));
            Assert.False(File.Exists(Path.Combine(target, "crash.log")));
        }

        [Fact]
        public void NoOldFolder_CopiesNothing_AndMakesNoFolder()
        {
            string target = Path.Combine(_root, "OpenCAD2D");

            Assert.Equal(0, CadOldFolders.CopyOnce(target, Path.Combine(_root, "GeometryHelper", "Drawing"), null));
            Assert.False(Directory.Exists(target));
            Assert.Throws<ArgumentNullException>(() => CadOldFolders.CopyOnce(null, _root));
        }
    }
}
