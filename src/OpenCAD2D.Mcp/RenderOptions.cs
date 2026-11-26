using System;

namespace OpenCAD2D.Mcp
{
    /// <summary>The <see cref="CadRenderOptions"/> a <see cref="RenderRequest"/> stands for.</summary>
    internal static class RenderOptions
    {
        /// <summary>The pixels along the width of a picture where the request gives no size: a size a model reads well.</summary>
        public const int DefaultWidth = 1024;

        /// <summary>Makes the options: white ground (so that colour 7 is drawn black, as on paper), the layout and the part asked for.</summary>
        public static CadRenderOptions Of(RenderRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var options = new CadRenderOptions
            {
                Layout = string.IsNullOrEmpty(request.Layout) ? "Model" : request.Layout,
                Width = request.Width,
                Height = request.Height,
                Dpi = request.Dpi > 0.0 ? request.Dpi : 96.0,
                Background = 0xFFFFFF,
            };

            if (request.Width == 0 && request.Height == 0 && request.Dpi <= 0.0)
            {
                options.Width = DefaultWidth;
            }

            if (request.Box != null)
            {
                options.Box = new DisplayBox(request.Box[0], request.Box[1], request.Box[2], request.Box[3]);
            }

            return options;
        }
    }
}
