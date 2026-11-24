using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>The lines of a tool's <c>requirements.txt</c> (S29): a lower bound is a pin, an option is refused, the rest goes to pip as written.</summary>
    public sealed class PythonEnvRequirementsTests
    {
        private static IReadOnlyList<PythonRequirement> Read(string text, out string error) =>
            PythonRequirement.Read(text, out error);

        [Theory]
        [InlineData("numpy>=1.26", "numpy==1.26")]
        [InlineData("numpy >= 1.26   # for the arrays", "numpy==1.26")]
        [InlineData("Demo_Pkg>=2", "Demo_Pkg==2")]
        [InlineData("pkg[extra,more]>=1.0.3", "pkg[extra,more]==1.0.3")]
        [InlineData("pkg>=1.0 ; python_version >= \"3.9\"", "pkg==1.0 ; python_version >= \"3.9\"")]
        [InlineData("pkg>=1.0rc1", "pkg==1.0rc1")]
        public void ALowerBoundAndNothingMore_IsPinned(string line, string pinned)
        {
            IReadOnlyList<PythonRequirement> read = Read(line + "\n", out string error);

            Assert.Null(error);
            PythonRequirement requirement = Assert.Single(read);
            Assert.Equal(pinned, requirement.Pinned);
            Assert.Equal(pinned, requirement.Text);
            Assert.True(requirement.IsPinned);
        }

        [Theory]
        [InlineData("numpy")]
        [InlineData("numpy==1.26")]
        [InlineData("numpy>=1.26,<2")]
        [InlineData("numpy>=1.26,!=1.27")]
        [InlineData("numpy~=1.26")]
        [InlineData("numpy<2")]
        [InlineData("numpy>1.26")]
        [InlineData("numpy==1.*")]
        public void AnyOtherForm_GoesToPipAsWritten(string line)
        {
            IReadOnlyList<PythonRequirement> read = Read(line, out string error);

            Assert.Null(error);
            PythonRequirement requirement = Assert.Single(read);
            Assert.Null(requirement.Pinned);
            Assert.Equal(line, requirement.Text);
            Assert.False(requirement.IsPinned);
        }

        [Fact]
        public void APinThatIsTakenBack_IsGivenAsWritten()
        {
            PythonRequirement requirement = Read("numpy>=1.26 # arrays\r\n", out string error).Single();

            requirement.Unpin();

            Assert.Null(error);
            Assert.Equal("numpy>=1.26", requirement.Text);
            Assert.False(requirement.IsPinned);
            Assert.Equal("numpy==1.26", requirement.Pinned);
        }

        [Fact]
        public void BlankLinesAndComments_AreSkipped_AndTheOrderIsKept()
        {
            IReadOnlyList<PythonRequirement> read = Read(
                "# the tool's packages\r\n\r\nzeta>=1.0\r\n   \r\nalpha==2.0   # pinned already\r\n",
                out string error
            );

            Assert.Null(error);
            Assert.Equal(new[] { "zeta==1.0", "alpha==2.0" }, read.Select(r => r.Text));
        }

        [Theory]
        [InlineData("-r other.txt\n", 1)]
        [InlineData("numpy\n\n--index-url http://elsewhere/\n", 3)]
        [InlineData("numpy\n-e .\n", 2)]
        [InlineData("numpy\n  --extra-index-url http://x/\n", 2)]
        public void AnOptionLine_IsRefused_WithItsLineNumber(string text, int line)
        {
            IReadOnlyList<PythonRequirement> read = Read(text, out string error);

            Assert.Equal("requirements.txt line " + line + ": options are not taken", error);
            Assert.Empty(read);
        }
    }
}
