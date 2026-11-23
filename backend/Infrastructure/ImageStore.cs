using System.Text.RegularExpressions;

namespace Ecommerce.Api.Infrastructure
{
    /// Uploaded images live on disk (UPLOADS_DIR, a bind-mounted host folder in
    /// production), so Redis is only a cache. Names are content hashes, so files
    /// are immutable.
    public static class ImageStore
    {
        private static readonly Regex SafeName = new(@"^[a-f0-9]{16}\.[a-z0-9]{2,5}$", RegexOptions.Compiled);
        public static readonly string[] Folders = { "product", "logo", "other" };

        public static string Root =>
            Environment.GetEnvironmentVariable("UPLOADS_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "uploads-data");

        /// Returns null for anything that isn't a whitelisted folder + hash-style name (blocks path traversal).
        public static string? PathFor(string folder, string fileName)
        {
            if (!Folders.Contains(folder) || !SafeName.IsMatch(fileName)) return null;
            return Path.Combine(Root, folder, fileName);
        }

        public static async Task SaveAsync(string folder, string fileName, byte[] bytes)
        {
            var path = PathFor(folder, fileName) ?? throw new ArgumentException("bad image name");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path)) return;
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }
    }
}
