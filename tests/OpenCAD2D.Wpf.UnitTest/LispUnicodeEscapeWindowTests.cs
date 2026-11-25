using System.Linq;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// A routine's prompt that writes Vietnamese as <c>\U+XXXX</c> (<c>"\nCh\U+1ECDn \U+0111i\U+1EC3m"</c>) shows the letters in the
    /// window as AutoCAD 2021 does: on the command line, in the history and in the box of dynamic input. Driven with no window.
    /// </summary>
    public sealed class LispUnicodeEscapeWindowTests
    {
        [Fact]
        public void APromptWithUnicodeEscapes_ShowsTheLettersOnTheCommandLine_TheHistoryAndTheDynamicInputBox()
        {
            using (var folder = new TestFolder())
            {
                var model = new MainViewModel();
                Assert.True(model.Open(folder.Write("room.dxf", TestFiles.Room(out _, out _))));
                model.Execute(@"(setq p (getpoint ""\nCh\U+1ECDn \U+0111i\U+1EC3m \U+0111\U+1EA7u HCN :""))");

                const string asked = "Chọn điểm đầu HCN :";

                Assert.True(model.IsCommandRunning);
                Assert.Equal(asked, model.Prompt);
                Assert.True(model.IsDynamicInputShown);
                Assert.Equal(asked, model.DynamicInputPrompt);

                model.Execute("5,6");

                Assert.Contains(asked + "5,6", model.History.Select(l => l.TrimEnd()));
                Assert.DoesNotContain(model.History.Skip(1), l =>
                    l.Contains("\\U+") && !l.StartsWith("Command: ", System.StringComparison.Ordinal)
                );
            }
        }
    }
}
