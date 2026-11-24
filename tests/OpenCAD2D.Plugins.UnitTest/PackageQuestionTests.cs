#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCAD2D.Wpf;
using OpenCAD2D.Wpf.Plugins;
using Xunit;

namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// What the window asks before a plugin's NuGet packages are downloaded (TF6): the texts of the PackageQuestion board made
    /// from the packages the restore chose, and the .csproj the question names, which the loader knows while it compiles.
    /// </summary>
    public sealed class PackageQuestionTests : IDisposable
    {
        private readonly PluginFiles _files = new PluginFiles();

        public void Dispose() => _files.Dispose();

        [Fact]
        public void TheQuestion_ListsTheDirectPackagesFirstInBold_ThenTheRest_EachInOrdinalOrder_AndMarksTheCachedOnes()
        {
            var packages = new List<CadPackage>
            {
                new CadPackage("System.Memory", "4.5.5", false, true),
                new CadPackage("Newtonsoft.Json", "13.0.3", true, false),
                new CadPackage("Aaa.Helper", "1.0.0", false, false),
                new CadPackage("NetTopologySuite", "2.5.0", true, false),
            };

            PackageQuestionViewModel question = CadPackageQuestion.Question(
                "Stairs",
                "D:\\Tools\\Stairs\\Stairs.csproj",
                packages
            );

            Assert.Equal(
                new[] { "NetTopologySuite 2.5.0", "Newtonsoft.Json 13.0.3", "Aaa.Helper 1.0.0", "System.Memory 4.5.5" },
                question.Rows.Select(r => r.Name + " " + r.Version)
            );
            Assert.Equal(new[] { true, true, false, false }, question.Rows.Select(r => r.IsDirect));
            Assert.Equal(
                new[] { "download", "download", "download", "in the cache" },
                question.Rows.Select(r => r.Note)
            );
            Assert.Equal(new[] { true, true, true, false }, question.Rows.Select(r => r.IsNoteHighlighted));
        }

        [Fact]
        public void TheQuestion_SaysTheTitleTheLeadTheFootAndTheButtonsOfTheBoard()
        {
            var packages = new List<CadPackage>
            {
                new CadPackage("Newtonsoft.Json", "13.0.3", true, false),
                new CadPackage("NetTopologySuite", "2.5.0", true, false),
                new CadPackage("System.Memory", "4.5.5", false, true),
            };

            PackageQuestionViewModel question = CadPackageQuestion.Question(
                "Stairs",
                "D:\\Tools\\Stairs\\Stairs.csproj",
                packages
            );

            Assert.Equal("Packages for Stairs", question.Title);
            Assert.Equal(
                "The plugin Stairs (D:\\Tools\\Stairs\\Stairs.csproj) uses 2 packages that are not on this computer yet.",
                question.Lead
            );
            Assert.Equal("State", question.ThirdHead);
            Assert.Equal(
                "Loading a plugin runs its code, and its packages\u2019 code, with your rights. The packages are kept in %LocalAppData%\\OpenCAD2D\\NuGet and shared by every plugin.",
                question.Foot
            );
            Assert.Equal("From nuget.org", question.Source);
            Assert.Equal("Don\u2019t load", question.NoLabel);
            Assert.Equal("Download and load", question.YesLabel);
        }

        [Fact]
        public void TheLead_CountsOnlyThePackagesToDownload_InTheSingular_AndLeavesOutAProjectItDoesNotKnow()
        {
            var one = new List<CadPackage>
            {
                new CadPackage("Foo", "1.0.0", true, false),
                new CadPackage("Bar", "1.0.0", false, true),
            };

            PackageQuestionViewModel question = CadPackageQuestion.Question("Stairs", null, one);

            Assert.Equal("The plugin Stairs uses 1 package that is not on this computer yet.", question.Lead);
        }

        [Fact]
        public void TheLoader_KnowsTheProjectItCompiles_WhileItAsks_AndForgetsItAfter_AnsweredYesOrNo()
        {
            var feed = new SourcePluginFeed(Path.Combine(_files.Root, "feed"));
            feed.AddLibrary("Foo", "1.0.0", "f");
            string folder = Path.Combine(_files.Root, "dev", "Stairs");
            SourceProject.Write(folder, "Commands.cs", "namespace P { public sealed class C { } }\n");
            string project = SourceProject.Write(
                folder,
                "Stairs.csproj",
                SourceProject.Csproj(items: new[] { "<PackageReference Include=\"Foo\" Version=\"1.0.0\" />" })
            );
            var loader = new CadPluginAssemblyLoader(
                _files.CacheFolder,
                new CadPackageRestorer(feed.Folder, Path.Combine(_files.Root, "packages"))
            );
            var named = new List<string>();
            bool answer = false;
            loader.ConfirmDownload = (tool, packages) =>
            {
                named.Add(loader.CompilingProject);
                return answer;
            };

            Assert.Throws<CadPluginException>(() => loader.Compile("Stairs", project));
            Assert.Null(loader.CompilingProject);

            answer = true;
            CadPluginBuild build = loader.Compile("Stairs", project);

            Assert.True(build.Succeeded, string.Join("\n", build.Errors));
            Assert.Null(loader.CompilingProject);
            Assert.Equal(new[] { project, project }, named);
        }
    }
}
#endif
