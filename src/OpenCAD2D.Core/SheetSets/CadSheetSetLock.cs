using System;
using System.IO;
using System.Text;
using IoPath = System.IO.Path;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// The lock file AutoCAD keeps beside a sheet set data file while the set is open for editing: <c>Tower.DBL</c> for
    /// <c>Tower.dst</c>, 58 bytes of text for the user "InuYasha" on the machine "INUYASHA-MEV", the lines ended
    /// <c>\r\r\n</c> as AutoCAD's own writer ends them (measured on AutoCAD 2021): <c>[UserInfo]</c>, <c>UserName=InuYasha</c>,
    /// <c>Machine=INUYASHA-MEV</c> and an empty line. A set whose lock is another user's opens read-only, with the lock's
    /// words as the tooltip of the palette's lock icon ("Locked for Edit by InuYasha"). A lock of this user on this machine is
    /// taken as one's own, so that a lock left by a program that was killed does not shut its user out.
    /// </summary>
    public sealed class CadSheetSetLock
    {
        private const string Head = "[UserInfo]";

        private CadSheetSetLock(string userName, string machine)
        {
            UserName = userName;
            Machine = machine;
        }

        /// <summary>Gets the user who holds the lock (<c>UserName</c>).</summary>
        public string UserName { get; }

        /// <summary>Gets the machine the lock was taken on (<c>Machine</c>).</summary>
        public string Machine { get; }

        /// <summary>Gets whether the lock is this user's on this machine, the letter case of the names ignored.</summary>
        public bool IsMine =>
            string.Equals(UserName, Environment.UserName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Gets the words that say who holds the lock, as the lock icon's tooltip shows them: "Locked for Edit by InuYasha on
        /// INUYASHA-MEV".
        /// </summary>
        public string Description => "Locked for Edit by " + UserName + " on " + Machine;

        /// <summary>Gets the path of the lock file of a sheet set data file: the .dst's own path with .DBL in place of .dst.</summary>
        /// <param name="dst">The path of the .dst.</param>
        /// <returns>The path of the lock file.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        public static string PathOf(string dst) =>
            IoPath.ChangeExtension(dst ?? throw new ArgumentNullException(nameof(dst)), ".DBL");

        /// <summary>Reads the lock of a sheet set data file.</summary>
        /// <param name="dst">The path of the .dst.</param>
        /// <returns>The lock; null where there is no lock file or it cannot be read. A file of another shape than AutoCAD's is a lock of nobody known ("an unknown user").</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        public static CadSheetSetLock Read(string dst)
        {
            string file = PathOf(dst);

            try
            {
                if (!File.Exists(file))
                {
                    return null;
                }

                string user = string.Empty;
                string machine = string.Empty;

                foreach (string line in File.ReadAllText(file).Split('\r', '\n'))
                {
                    if (line.StartsWith("UserName=", StringComparison.OrdinalIgnoreCase))
                    {
                        user = line.Substring(9).Trim();
                    }
                    else if (line.StartsWith("Machine=", StringComparison.OrdinalIgnoreCase))
                    {
                        machine = line.Substring(8).Trim();
                    }
                }

                return new CadSheetSetLock(
                    user.Length == 0 ? "an unknown user" : user,
                    machine.Length == 0 ? "an unknown machine" : machine
                );
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Takes the lock of a sheet set data file for this user on this machine: writes the lock file, in place of one that is
        /// there.
        /// </summary>
        /// <param name="dst">The path of the .dst.</param>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        /// <exception cref="IOException">Thrown when the lock file cannot be written.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the folder may not be written to.</exception>
        public static void Take(string dst) =>
            File.WriteAllBytes(
                PathOf(dst),
                Encoding.ASCII.GetBytes(
                    Head
                        + "\r\r\nUserName="
                        + Environment.UserName
                        + "\r\r\nMachine="
                        + Environment.MachineName
                        + "\r\r\n\r\n"
                )
            );

        /// <summary>Lets go of the lock of a sheet set data file: deletes the lock file where it is this user's on this machine.</summary>
        /// <param name="dst">The path of the .dst.</param>
        /// <returns>True when the lock file was deleted; false where there was none, or it is another user's, which is left.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        public static bool Release(string dst)
        {
            CadSheetSetLock held = Read(dst);

            if (held == null || !held.IsMine)
            {
                return false;
            }

            try
            {
                File.Delete(PathOf(dst));
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
