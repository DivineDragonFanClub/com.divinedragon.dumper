using System;
using System.Collections.Generic;
using System.IO;

namespace DivineDragon.Editor
{
    /// <summary>
    /// Maps CRC32 hashes of transform paths back to the original paths.
    /// Loads the shipped bone_hash_map.txt on first access.
    /// </summary>
    public static class BoneHashMap
    {
        private const string ShippedMapPath =
            "Packages/com.divinedragon.dumper/Editor/AssetPostprocessor/AnimBoneFixer/bone_hash_map.txt";

        private static Dictionary<uint, string> _map;
        private static readonly object Lock = new object();

        public static int Count
        {
            get
            {
                EnsureLoaded();
                return _map.Count;
            }
        }

        public static bool TryGetPath(uint hash, out string path)
        {
            EnsureLoaded();
            return _map.TryGetValue(hash, out path);
        }

        private static void EnsureLoaded()
        {
            lock (Lock)
            {
                if (_map != null) return;
                _map = new Dictionary<uint, string>();

                using var reader = new StreamReader(ShippedMapPath);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    int tab = line.IndexOf('\t');
                    uint hash = Convert.ToUInt32(line.Substring(0, tab), 16);
                    _map[hash] = line.Substring(tab + 1);
                }
            }
        }
    }
}
