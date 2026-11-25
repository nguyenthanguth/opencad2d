using System;
using System.IO;
using System.Text;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The .DBL lock file AutoCAD 2021 keeps beside a sheet set while it is open for editing (58 bytes of text with
    /// <c>\r\r\n</c> line ends for the user InuYasha on INUYASHA-MEV, as measured), and a set that opens read-only where its
    /// file is read-only or another user holds the lock (A12-D21).
    /// </summary>
    public sealed class SheetSetLockTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetsetlock-" + Guid.NewGuid().ToString("N")
        );

        public SheetSetLockTests()
        {
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            try
            {
                foreach (string file in Directory.GetFiles(_folder))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_folder, true);
            }
            catch (IOException)
            {
                // A file a watcher still holds is left for the system to clear.
            }
        }

        private string Dst => Path.Combine(_folder, "Tower.dst");

        private string Dbl => Path.Combine(_folder, "Tower.DBL");

        private void MakeSet() => CadSheetSetFile.Create(Dst, "Tower", null);

        private void AnotherUserHoldsTheLock() =>
            File.WriteAllBytes(
                Dbl,
                Encoding.ASCII.GetBytes("[UserInfo]\r\r\nUserName=SOMEONE\r\r\nMachine=OTHER-PC\r\r\n\r\n")
            );

        [Fact]
        public void TheLockFileOfASet_IsTheDstWithDBLInPlaceOfDst()
        {
            Assert.Equal(Path.Combine(_folder, "Tower.DBL"), CadSheetSetLock.PathOf(Dst));
            Assert.Throws<ArgumentNullException>(() => CadSheetSetLock.PathOf(null));
        }

        [Fact]
        public void TakingTheLock_WritesAutoCadsLayout_InTextWithTripleLineEnds()
        {
            MakeSet();

            CadSheetSetLock.Take(Dst);

            byte[] bytes = File.ReadAllBytes(Dbl);
            Assert.Equal(
                "[UserInfo]\r\r\nUserName="
                    + Environment.UserName
                    + "\r\r\nMachine="
                    + Environment.MachineName
                    + "\r\r\n\r\n",
                Encoding.ASCII.GetString(bytes)
            );
            Assert.Equal(38 + Environment.UserName.Length + Environment.MachineName.Length, bytes.Length);
            CadSheetSetLock held = CadSheetSetLock.Read(Dst);
            Assert.Equal(Environment.UserName, held.UserName);
            Assert.Equal(Environment.MachineName, held.Machine);
            Assert.True(held.IsMine);
        }

        [Fact]
        public void ALockOfAnotherUser_IsNotMine_AndSaysWhoHoldsIt()
        {
            MakeSet();
            AnotherUserHoldsTheLock();

            CadSheetSetLock held = CadSheetSetLock.Read(Dst);

            Assert.False(held.IsMine);
            Assert.Equal("SOMEONE", held.UserName);
            Assert.Equal("OTHER-PC", held.Machine);
            Assert.Equal("Locked for Edit by SOMEONE on OTHER-PC", held.Description);
            Assert.False(CadSheetSetLock.Release(Dst));
            Assert.True(File.Exists(Dbl));
        }

        [Fact]
        public void ThereIsNoLock_WhereThereIsNoLockFile_AndAFileOfAnotherShapeIsALockOfAnUnknownUser()
        {
            MakeSet();
            Assert.Null(CadSheetSetLock.Read(Dst));

            File.WriteAllText(Dbl, "something else");

            CadSheetSetLock held = CadSheetSetLock.Read(Dst);
            Assert.False(held.IsMine);
            Assert.Equal("an unknown user", held.UserName);
        }

        [Fact]
        public void OpeningForEditing_TakesTheLock_AndLettingGoDeletesIt()
        {
            MakeSet();

            CadSheetSetFile file = CadSheetSetFile.OpenForEditing(Dst);

            Assert.True(file.HoldsLock);
            Assert.False(file.IsReadOnly);
            Assert.Null(file.ReadOnlyReason);
            Assert.True(File.Exists(Dbl));
            file.Change(set => set.AddSubset("Plans"));
            Assert.Single(CadSheetSetFile.Open(Dst).SheetSet.Subsets);

            file.ReleaseLock();

            Assert.False(file.HoldsLock);
            Assert.False(File.Exists(Dbl));
            file.ReleaseLock();
        }

        [Fact]
        public void APlainOpen_WritesNoLockFile()
        {
            MakeSet();

            CadSheetSetFile file = CadSheetSetFile.Open(Dst);

            Assert.False(file.HoldsLock);
            Assert.False(File.Exists(Dbl));
        }

        [Fact]
        public void ASetWhoseLockIsAnotherUsers_OpensReadOnly_RefusesEveryChange_AndLeavesTheLockAlone()
        {
            MakeSet();
            AnotherUserHoldsTheLock();

            CadSheetSetFile file = CadSheetSetFile.OpenForEditing(Dst);

            Assert.True(file.IsReadOnly);
            Assert.False(file.HoldsLock);
            Assert.Equal("SOMEONE", file.LockedBy.UserName);
            Assert.Equal("Locked for Edit by SOMEONE on OTHER-PC.", file.ReadOnlyReason);
            var refused = Assert.Throws<InvalidOperationException>(() => file.Change(set => set.AddSubset("Plans")));
            Assert.Contains("read-only", refused.Message);
            Assert.Empty(CadSheetSetFile.Open(Dst).SheetSet.Subsets);
            file.ReleaseLock();
            Assert.True(File.Exists(Dbl));
        }

        [Fact]
        public void ASetWhoseFileIsReadOnly_OpensReadOnly_WritesNoLock_AndRefusesEveryChange()
        {
            MakeSet();
            File.SetAttributes(Dst, FileAttributes.ReadOnly);

            CadSheetSetFile file = CadSheetSetFile.OpenForEditing(Dst);

            Assert.True(file.IsReadOnly);
            Assert.Equal("The sheet set data file is read-only.", file.ReadOnlyReason);
            Assert.False(File.Exists(Dbl));
            Assert.Throws<InvalidOperationException>(() => file.Change(set => set.AddSubset("Plans")));
            Assert.Throws<InvalidOperationException>(() =>
                CadSheetSetFile.Open(Dst).Change(set => set.AddSubset("Plans"))
            );
            File.SetAttributes(Dst, FileAttributes.Normal);
            Assert.False(file.IsReadOnly);
        }

        [Fact]
        public void ALockOfThisUserOnThisMachine_LeftByAProgramThatWasKilled_IsTakenOver()
        {
            MakeSet();
            CadSheetSetLock.Take(Dst);

            CadSheetSetFile file = CadSheetSetFile.OpenForEditing(Dst);

            Assert.True(file.HoldsLock);
            Assert.False(file.IsReadOnly);
            Assert.Null(file.LockedBy);
        }
    }
}
