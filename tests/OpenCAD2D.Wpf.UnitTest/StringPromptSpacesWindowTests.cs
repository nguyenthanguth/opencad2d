using System;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The window types a space only where AutoCAD 2021 takes it as part of the answer (<see cref="MainViewModel.PromptTakesSpaces"/>)
    /// and takes Space as Enter elsewhere: the text of TEXT and a text variable such as POLARADDANG take it, the list of object
    /// snap modes of -OSNAP and a layer name do not. Driven with no window.
    /// </summary>
    public sealed class StringPromptSpacesWindowTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private MainViewModel Opened()
        {
            string room = _folder.Write("room-" + Guid.NewGuid().ToString("N") + ".dxf", TestFiles.Room(out _, out _));
            var model = new MainViewModel();
            Assert.True(model.Open(room));
            return model;
        }

        [Fact]
        public void TheListOfObjectSnapModes_AndALayerName_EndAtASpace()
        {
            MainViewModel model = Opened();

            model.Execute("-OSNAP");
            Assert.Contains("Enter list of object snap modes", model.Session.Prompt.Text, StringComparison.Ordinal);
            Assert.False(model.PromptTakesSpaces);
            model.Session.Escape();

            model.Execute("-LAYER");
            model.Execute("N");
            Assert.Contains("Enter name list for new layer", model.Session.Prompt.Text, StringComparison.Ordinal);
            Assert.False(model.PromptTakesSpaces);
            model.Session.Escape();
        }

        [Fact]
        public void TheTextOfTEXT_AndAFreeTextVariable_TakeSpaces()
        {
            MainViewModel model = Opened();

            model.Execute("TEXT");
            model.Execute("0,0");
            model.Execute("2");
            model.Execute("0");
            Assert.Contains("Enter text", model.Session.Prompt.Text, StringComparison.Ordinal);
            Assert.True(model.PromptTakesSpaces);
            model.Session.Escape();

            model.Execute("SETVAR");
            model.Execute("POLARADDANG");
            Assert.Contains("Enter new value for POLARADDANG", model.Session.Prompt.Text, StringComparison.Ordinal);
            Assert.True(model.PromptTakesSpaces);
            model.Session.Escape();
        }
    }
}
