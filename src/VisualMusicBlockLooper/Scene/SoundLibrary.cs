using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VisualMusicBlockLooper.Scene
{
    internal sealed class SoundLibrary
    {
        private readonly Dictionary<string, string> files;

        private SoundLibrary(string directory)
        {
            files = Directory.EnumerateFiles(directory)
                .Where(path => string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        }

        public IEnumerable<string> Names => files.Keys.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase);

        public static SoundLibrary Load(string directory)
        {
            var resolved = Path.IsPathRooted(directory) ? directory :
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, directory);
            return new SoundLibrary(Path.GetFullPath(resolved));
        }

        public string Resolve(string name) =>
            name != null && files.TryGetValue(name, out var path) && File.Exists(path) ? path : null;
    }
}
