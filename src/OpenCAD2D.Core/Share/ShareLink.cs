using System;

namespace OpenCAD2D.Share
{
    /// <summary>
    /// The address of a tool on Share, as its page gives it: <c>https://opencad2d.com/share/stairs</c>, where <c>stairs</c> is
    /// the tool's slug. A slug is one to 64 lower-case letters, digits and hyphens.
    /// </summary>
    internal static class ShareLink
    {
        /// <summary>Gets the address Share is read at.</summary>
        public static Uri Site { get; } = new Uri("https://opencad2d.com/");

        /// <summary>
        /// Reads the slug out of a pasted link: <c>https://opencad2d.com/share/stairs</c> gives <c>stairs</c>; a trailing slash, a
        /// <c>www.</c> in front of the host, a query and a fragment are let go. A link to another host, to another page of
        /// Share (<c>/share/by/someone</c>) or with no slug is no link to a tool.
        /// </summary>
        /// <param name="link">The text the user pasted; surrounding spaces are let go.</param>
        /// <param name="site">The address Share is read at; <see cref="Site"/> for the real one.</param>
        /// <param name="slug">The slug, or null where the text is no tool's link.</param>
        /// <returns>True where the text is a link to a tool.</returns>
        public static bool TryGetSlug(string link, Uri site, out string slug)
        {
            slug = null;

            if (site == null)
            {
                throw new ArgumentNullException(nameof(site));
            }

            if (
                string.IsNullOrWhiteSpace(link)
                || !Uri.TryCreate(link.Trim(), UriKind.Absolute, out Uri address)
                || address.Scheme != site.Scheme
                || !(
                    string.Equals(address.Host, site.Host, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(address.Host, "www." + site.Host, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                return false;
            }

            string[] parts = address.AbsolutePath.Trim('/').Split('/');

            if (parts.Length != 2 || !string.Equals(parts[0], "share", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string name = parts[1].ToLowerInvariant();

            if (!IsSlug(name))
            {
                return false;
            }

            slug = name;
            return true;
        }

        /// <summary>Gets whether a text is a slug: one to 64 lower-case letters, digits and hyphens.</summary>
        /// <param name="text">The text.</param>
        /// <returns>True where it is.</returns>
        public static bool IsSlug(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 64)
            {
                return false;
            }

            foreach (char c in text)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
