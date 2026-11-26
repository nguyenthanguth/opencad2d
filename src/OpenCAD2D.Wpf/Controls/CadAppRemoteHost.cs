using System;
using System.Collections.Generic;
using System.Windows.Threading;
using SkiaSharp;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The app's drawings for the remote channel: a <see cref="CadRemoteServer"/> reads the window's view model through this,
    /// and runs its work on the window's thread through <see cref="Dispatcher"/>. Given the window's canvas it also tells a program
    /// that debugs the app what the canvas shows, and where the crash log is.
    /// </summary>
    internal sealed class CadAppRemoteHost
        : ICadRemoteHost,
            ICadRemoteDispatcher,
            ICadRemoteOwnerThread,
            ICadRemoteDiagnostics
    {
        private readonly MainViewModel _model;
        private readonly Func<string> _title;
        private readonly Dispatcher _dispatcher;
        private readonly Func<CadCanvas> _canvas;
        private readonly string _crashLog;

        public CadAppRemoteHost(
            MainViewModel model,
            Func<string> title,
            Dispatcher dispatcher,
            Func<CadCanvas> canvas = null,
            string crashLog = null
        )
        {
            _model = model;
            _title = title;
            _dispatcher = dispatcher;
            _canvas = canvas;
            _crashLog = crashLog ?? CadCrashLog.DefaultPath;
        }

        public string Title => _title();

        public IReadOnlyList<CadDocument> Documents => _model.RemoteDocuments();

        public CadDocument ActiveDocument => _model.Document;

        public CadDocument Open(string path) => _model.OpenForRemote(path);

        public CadDocument New(string template) => _model.NewForRemote(template);

        public void Activate(CadDocument document) => _model.ShowDocument(document);

        public CadCommandSession SessionOf(CadDocument document) => _model.SessionOfDocument(document);

        public void Type(CadDocument document, string line)
        {
            _model.ShowDocument(document);
            _model.TypeForRemote(line);
        }

        public void Save(CadDocument document) => _model.SaveForRemote(document);

        public void SaveAs(CadDocument document, string path, string version) =>
            _model.SaveAsForRemote(document, path, version);

        public void Close(CadDocument document) => _model.CloseForRemote(document);

        public void Invoke(Action action) => _dispatcher.Invoke(action, DispatcherPriority.Normal);

        public bool IsOwnerThread() => _dispatcher.CheckAccess();

        public string CrashLogPath => _crashLog;

        /// <summary>
        /// The canvas's own bitmap as last drawn (what the user sees under the overlays), made smaller to a side of
        /// <paramref name="maxSide"/> pixels where it is larger. The canvas is not drawn again: drawing it commits a viewport's view, a
        /// change that a request to look must not make.
        /// </summary>
        public CadRemotePicture CanvasPicture(int maxSide)
        {
            byte[] png = _canvas?.Invoke()?.BitmapPng();

            if (png == null)
            {
                return null;
            }

            using (SKBitmap picture = SKBitmap.Decode(png))
            {
                int longer = Math.Max(picture.Width, picture.Height);

                if (maxSide <= 0 || longer <= maxSide)
                {
                    return new CadRemotePicture(png, picture.Width, picture.Height);
                }

                double factor = (double)maxSide / longer;
                var size = new SKImageInfo(
                    Math.Max(1, (int)Math.Round(picture.Width * factor)),
                    Math.Max(1, (int)Math.Round(picture.Height * factor)),
                    SKColorType.Bgra8888,
                    SKAlphaType.Premul
                );

                using (
                    SKBitmap small = picture.Resize(
                        size,
                        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
                    )
                )
                {
                    return new CadRemotePicture(
                        CadRasterWriter.Encode(small, CadImageFormat.Png),
                        small.Width,
                        small.Height
                    );
                }
            }
        }
    }
}
