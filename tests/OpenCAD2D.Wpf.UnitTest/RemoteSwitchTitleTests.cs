using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using OpenCAD2D.Remote;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>The title the app announces to other programs follows its window, and the time it was last activated does not move with it.</summary>
    public sealed class RemoteSwitchTitleTests
    {
        private sealed class InlineDispatcher : ICadRemoteDispatcher
        {
            public void Invoke(Action action) => action();
        }

        [Fact]
        public void TheAnnouncedTitle_IsWrittenAgain_WhenTheWindowsTitleChanges_AndTheLastActivationStays()
        {
            using (var folder = new TestFolder())
            {
                string files = Path.Combine(folder.FullPath, "remote");
                string title = "OpenCAD2D  ";
                var model = new MainViewModel();
                var host = new CadAppRemoteHost(model, () => title, Dispatcher.CurrentDispatcher);

                using (var remote = new CadRemoteSwitch(model, host, new InlineDispatcher(), files, "ti"))
                {
                    remote.Apply();
                    CadRunningApp before = Assert.Single(CadApplication.GetRunning(files));
                    Assert.Equal("OpenCAD2D  ", before.Title);
                    Thread.Sleep(30);

                    title = "OpenCAD2D  plan.dxf";
                    remote.NoteTitleChanged();

                    CadRunningApp after = Assert.Single(CadApplication.GetRunning(files));
                    Assert.Equal("OpenCAD2D  plan.dxf", after.Title);
                    Assert.Equal(before.Instance.LastActivated, after.Instance.LastActivated);
                }
            }
        }
    }
}
