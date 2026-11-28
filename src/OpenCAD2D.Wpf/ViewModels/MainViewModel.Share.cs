using System;
using System.Net.Http;
using OpenCAD2D.Share;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// SHAREDOWNLOAD, the window that downloads a tool from Share (opencad2d.com/share) into the folder the app loads its kind
    /// from. The window is shown by <see cref="ShowShareDownload"/>; what it asks of Share it asks through
    /// <see cref="ShareClient"/>, and only when its Find or Download is pressed.
    /// </summary>
    public sealed partial class MainViewModel
    {
        private static readonly Lazy<HttpClient> ShareHttp = new Lazy<HttpClient>(() =>
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        );
        private ShareClient _shareClient;
        private Func<ShareDownloadViewModel, bool> _showShareDownload;

        /// <summary>
        /// Gets or sets how the window shows Download from Share, over the view model it is given, until the dialog is closed; the
        /// window is modal. Null where no window does, when SHAREDOWNLOAD says so.
        /// </summary>
        public Func<ShareDownloadViewModel, bool> ShowShareDownload
        {
            get => _showShareDownload;
            set => Set(ref _showShareDownload, value);
        }

        /// <summary>
        /// Gets or sets what Share is asked through: <c>https://opencad2d.com</c> over one shared <see cref="HttpClient"/>, made on
        /// first use (making it asks nothing), which follows no redirect. A test sets a client over a fake handler.
        /// </summary>
        internal ShareClient ShareClient
        {
            get => _shareClient ?? (_shareClient = new ShareClient(ShareHttp.Value));
            set => _shareClient = value;
        }

        /// <summary>SHAREDOWNLOAD: shows Download from Share with <see cref="ShowShareDownload"/>; a window with no such dialog says that it cannot.</summary>
        private void DownloadFromShare()
        {
            if (ShowShareDownload == null)
            {
                Say("Download from Share cannot be shown here.");
                return;
            }

            ShowShareDownload(new ShareDownloadViewModel(this));
        }
    }
}
