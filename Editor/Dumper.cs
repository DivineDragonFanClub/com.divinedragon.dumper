using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dragonstone;
using UnityEngine;

namespace DivineDragon
{
    public static class Dumper
    {
        public static bool Initialized { get; private set; }
        
        public static bool Initialize()
        {
            // Check if we already have extracted a copy of the game's catalog as JSON.
            // Verify every time just in case the user deleted the file for some reason.
            
            // TODO: Turn this into a const variable somewhere relevant.
            if (!File.Exists("Assets/Share/AddressableAssetsData/TempCatalogFolder/catalog.json"))
            {
                Initialized = false;
                
                if (!ExtractAssetAtPath(EngageAddressableSettings.GameCatalogLocation))
                {
                    Debug.LogError("Couldn't extract catalog.json");
                    return false;
                }
            }

            if (!Initialized)
            {
                if (!CBT.LoadCatalogContent("Assets/Share/AddressableAssetsData/TempCatalogFolder/catalog.json"))
                {
                    return false;
                }

                Initialized = true;
            }

            return true;
        }

        /// <summary>
        /// Calls on DivineRipper to extract a bundle from the game
        /// </summary>
        /// <remarks>
        /// This method will automatically find dependencies for this specific key and get them extracted alongside the specified bundle.
        /// </remarks>
        /// <param name="key">The key for the asset, also known as address</param>
        /// <returns>Returns true if the operation is successful</returns>
        public static bool ExtractAsset(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("ExtractAsset: key is null or empty");
                return false;
            }

            if (!Initialize())
            {
                Debug.LogError("ExtractAsset: Dumper failed to initialize. See earlier errors.");
                return false;
            }

            // TODO: Gather dependencies for the key for queueing them in the extraction
            var dependencies = CBT.GetDependenciesForAsset(key);

            AssetRipperRequestBuilder builder = new AssetRipperRequestBuilder();

            foreach (string dependency in dependencies)
            {
                builder.AddFile(dependency);
            }

            bool success = builder.Export("dumper");
            if (!success)
                Debug.LogError("Export was unsuccessful");

            return success;
        }
        
        /// <summary>
        /// Calls on DivineRipper to extract multiple bundles from the game
        /// </summary>
        /// <param name="paths">Paths to the bundles relative to the game's BuildPath directory</param>
        /// <returns>Returns true if the operation is successful</returns>
        public static bool ExtractAssets(string[] paths)
        {
            if (paths.Length <= 0)
            {
                Debug.LogWarning("ExtractAssets: no bundle found in directory and subdirectories");
                return false;
            }

            if (!Initialize())
            {
                Debug.LogError("ExtractAssets: Dumper failed to initialize. See earlier errors.");
                return false;
            }

            IEnumerable<string> dependencies = new List<string>();

            foreach (string bundle in paths)
            {
                dependencies = dependencies.Concat(CBT.GetDependenciesForAsset(CBT.PathToInternalId(bundle.Replace("\\", "/"))));
            }

            AssetRipperRequestBuilder builder = new AssetRipperRequestBuilder();

            foreach (string dependency in dependencies.Distinct())
            {
                builder.AddFile(dependency);
            }

            bool success = builder.Export("dumper");
            if (!success)
                Debug.LogError("Export was unsuccessful");

            return success;
        }

        /// <summary>
        /// Calls on DivineRipper to extract an arbitrary bundle file
        /// </summary>
        /// <remarks>
        /// This method is to be used for bundles located in mods where we cannot resolve dependencies(to be experimented with?) to extract alongside them.
        /// </remarks>
        /// <param name="path">Absolute path to the bundle to extract</param>
        /// <returns>Returns true if the operation succeeded</returns>
        public static bool ExtractAssetAtPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("ExtractAssetAtPath: path is null or empty");
                return false;
            }

            // Maybe turn this into a helper? This seems like it could be useful in multiple places
            if (Path.GetExtension(path) == ".bundle" && File.Exists(path))
            {
                // Use the wrapper for now, maybe move over to the Builder when it's not stupid
                return Rip.ExtractAssets(path, InputMode.File);
            }
            
            Debug.LogError($"Asset path does not exist or has invalid extension: {path}");
            return false;
        }

        /// <summary>
        /// Calls on DivineRipper to extract multiple arbitrary bundle files, gathering catalog dependencies for any paths that resolve to a known internal id.
        /// </summary>
        /// <param name="paths">Collection of absolute bundle paths to extract</param>
        /// <returns>Returns true if the export succeeded</returns>
        public static bool ExtractMultipleAssets(IEnumerable<string> paths)
        {
            if (paths == null)
            {
                return false;
            }

            if (!Initialize())
            {
                Debug.LogError("ExtractMultipleAssets: Dumper failed to initialize. See earlier errors.");
                return false;
            }

            var builder = new AssetRipperRequestBuilder();
            var filesToExtract = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                string internalId = CBT.PathToInternalId(path);

                if (!string.IsNullOrEmpty(internalId))
                {
                    var dependencies = CBT.GetDependenciesForAsset(internalId)?.ToList();

                    if (dependencies != null && dependencies.Count > 0)
                    {
                        foreach (string dependency in dependencies)
                        {
                            if (filesToExtract.Add(dependency))
                            {
                                builder.AddFile(dependency);
                            }
                        }
                    }
                }

                if (filesToExtract.Add(path))
                {
                    builder.AddFile(path);
                }
            }

            if (filesToExtract.Count == 0)
            {
                return false;
            }

            if (!builder.Export("dumper"))
            {
                Debug.LogError("ExtractMultipleAssets: export failed");
                return false;
            }

            return true;
        }
    }
}