using System.Runtime.CompilerServices;
using OpenCAD2D.Wpf;

// The drawing of a display list into pixels and the PDF writer now live in OpenCAD2D.Rendering, still in the namespace
// OpenCAD2D.Wpf. A plugin built against an earlier OpenCAD2D.Wpf.dll names these types there, and finds them through
// these forwards.
[assembly: TypeForwardedTo(typeof(CanvasOptions))]
[assembly: TypeForwardedTo(typeof(SkiaCanvasRenderer))]
[assembly: TypeForwardedTo(typeof(CadPdfWriter))]
[assembly: TypeForwardedTo(typeof(CadPdfBookmark))]
[assembly: TypeForwardedTo(typeof(CadRasterWriter))]
