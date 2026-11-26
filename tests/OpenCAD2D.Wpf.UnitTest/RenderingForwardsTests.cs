using System;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The drawing of a display list and the PDF writer live in OpenCAD2D.Rendering, still in the namespace OpenCAD2D.Wpf: a
    /// plugin built against an earlier OpenCAD2D.Wpf.dll names them there and must still bind, through the forwards.
    /// </summary>
    public class RenderingForwardsTests
    {
        [Theory]
        [InlineData("OpenCAD2D.Wpf.CanvasOptions")]
        [InlineData("OpenCAD2D.Wpf.SkiaCanvasRenderer")]
        [InlineData("OpenCAD2D.Wpf.CadPdfWriter")]
        [InlineData("OpenCAD2D.Wpf.CadPdfBookmark")]
        [InlineData("OpenCAD2D.Wpf.CadRasterWriter")]
        public void ATypeNamedInOpenCAD2DWpf_IsFoundInOpenCAD2DRendering(string name)
        {
            Type type = Type.GetType(name + ", OpenCAD2D.Wpf", throwOnError: true);

            Assert.Equal("OpenCAD2D.Rendering", type.Assembly.GetName().Name);
            Assert.Equal(name, type.FullName);
        }

        [Fact]
        public void TheForwardedTypes_AreTheOnesTheWpfAssemblyUses()
        {
            Assert.Same(typeof(SkiaCanvasRenderer), Type.GetType("OpenCAD2D.Wpf.SkiaCanvasRenderer, OpenCAD2D.Wpf"));
            Assert.NotSame(typeof(CadCanvas).Assembly, typeof(SkiaCanvasRenderer).Assembly);
        }
    }
}
