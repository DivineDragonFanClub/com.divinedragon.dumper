using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dragonstone;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace DivineDragon
{
    public static class CBT
    {
        public static bool Initialized { get; private set; }

        private static Dictionary<string, string> InternalIdCache { get; set; }

        // Address (lowercased) to the asset type the catalog says lives at it, e.g. "GameObject",
        // "Texture2D", "Sprite". Lets the picker filter bundles by what they contain.
        private static Dictionary<string, string> TypesByAddress { get; set; }

        // [InitializeOnLoadMethod]
        // private static void InitializeCatalogOnLoad()
        // {
        //     if (File.Exists("Assets/Share/AddressableAssetsData/TempCatalogFolder/catalog.json"))
        //     {
        //         if (!LoadCatalogContent("Assets/Share/AddressableAssetsData/TempCatalogFolder/catalog.json"))
        //         {
        //             Debug.LogError("CBT failed to load catalog.json on load even though it exists. Corrupted/Edited?");
        //         }
        //     }
        //     else
        //     {
        //         Debug.LogWarning("Divine Dragon Core settings are not configured yet. Consider configuring them to use the entire suite of tools.");
        //     }
        // }
        
        /// <summary>
        /// Loads the catalog file from Fire Emblem Engage to fetch file information and dependencies
        /// </summary>
        /// <param name="catalogPath">Path to the game's catalog file, extracted as a JSON</param>
        /// <returns>Returns true if the catalog loaded successfully</returns>
        public static bool LoadCatalogContent(string catalogPath)
        {
            if (Initialized)
                return true;
            
            if (string.IsNullOrEmpty(catalogPath))
            {
                Debug.LogError("catalogPath is not set");
                return false;
            }
            
            Debug.Log("Starting catalog loading");
            var handle = Addressables.LoadContentCatalogAsync(catalogPath, false);
            // Editor support for async is lacking so we just work sync.
            handle.WaitForCompletion();
            
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                 Debug.Log("Successfully loaded Engage's catalog.json ");

                 var loadedLocator = handle.Result;
                 if (loadedLocator == null)
                 {
                     Addressables.Release(handle);
                     Debug.LogError("LoadContentCatalogAsync succeeded but returned a null locator");
                     return false;
                 }

                 InternalIdCache = loadedLocator.Keys.OfType<string>()
                     .Where(s => !s.StartsWith("fe_assets_") && !(Guid.TryParseExact(s, "D", out _) && !Path.HasExtension(s)))
                     .ToDictionary(x => x.ToLower(), x => x);

                 BuildTypesByAddress(loadedLocator);

                 Addressables.Release(handle);
                 Initialized = true;
                 return true;
            }
            
            Addressables.Release(handle);
            
            Debug.LogError("Could not load catalog.json file from Fire Emblem Engage");
            return false;
        }

        /// <summary>
        /// Transforms a physical path in the dump into a InternalId as best as possible
        /// </summary>
        /// <param name="path">Physical path of a bundle in the dump</param>
        /// <returns>Returns the path as a InternalId or a empty string if it failed</returns>
        public static string PathToInternalId(string path)
        {
            if (InternalIdCache == null)
            {
                Debug.LogError("CBT.PathToInternalId called before catalog was loaded. Call CBT.LoadCatalogContent first.");
                return string.Empty;
            }

            // Turn the path into a lowercase InternalId
            string processed_path = Path.ChangeExtension(path.Replace("\\", "/").Replace(EngageAddressableSettings.GameBuildPath + "/fe_assets_", "").Replace(EngageAddressableSettings.GameBuildPath + "/fe_scenes_", ""), null);
            Debug.Log(processed_path);

            if (InternalIdCache.TryGetValue(processed_path, out string internalId))
            {
                Debug.Log($"Found matching InternalId: {internalId}");
                return internalId;
            }
            
            Debug.LogError($"Could not find matching InternalId: {processed_path}");
            return string.Empty;
        }

        // Each FE Engage bundle holds one addressable asset, so we can tag a bundle with the
        // type the catalog records for its address (the same address PathToInternalId resolves).
        private static void BuildTypesByAddress(IResourceLocator locator)
        {
            TypesByAddress = new Dictionary<string, string>();

            if (!(locator is ResourceLocationMap map))
                return;

            foreach (var entry in map.Locations)
            {
                if (!(entry.Key is string key))
                    continue;

                foreach (IResourceLocation location in entry.Value)
                {
                    // Skip the bundle wrapper entries, we only want the real asset type.
                    if (location.ResourceType == null || location.ResourceType == typeof(IAssetBundleResource))
                        continue;

                    TypesByAddress[key.ToLower()] = location.ResourceType.Name;
                    break;
                }
            }
        }

        /// <summary>
        /// Looks up the asset type the game catalog records for a bundle on disk.
        /// </summary>
        /// <param name="path">Physical path of a bundle in the dump</param>
        /// <returns>The type name (e.g. "GameObject"), or null if unknown or the catalog is not loaded</returns>
        public static string GetTypeForBundlePath(string path)
        {
            if (TypesByAddress == null || string.IsNullOrEmpty(path))
                return null;

            string processed = Path.ChangeExtension(path.Replace("\\", "/")
                .Replace(EngageAddressableSettings.GameBuildPath + "/fe_assets_", "")
                .Replace(EngageAddressableSettings.GameBuildPath + "/fe_scenes_", ""), null).ToLower();

            return TypesByAddress.TryGetValue(processed, out string typeName) ? typeName : null;
        }

        /// <summary>
        /// Provides the dependencies for a specific key if found in the game's catalog
        /// </summary>
        /// <param name="key">The address to the asset to extract dependencies for</param>
        /// <returns>Returns a IEnumerable of absolute paths to the dependencies</returns>
        public static IEnumerable<string> GetDependenciesForAsset(string key)
        {
            if (!Initialized)
            {
                Debug.LogError("CBT library is not initialized");
                return new List<string>();
            }
            
            AsyncOperationHandle<IList<IResourceLocation>> handle = Addressables.LoadResourceLocationsAsync(key);
            handle.WaitForCompletion();

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                // Addressables can return multiple locations for the same key (e.g. the prefab
                // entry plus a per-bundle entry). The first one only carries a subset of the
                // direct bundle deps, the second carries the rest. Union them so AssetRipper
                // gets every bundle the prefab actually pulls in.
                var seen = new HashSet<string>();
                var paths = new List<string>();
                foreach (var loc in handle.Result)
                {
                    if (loc.Dependencies == null)
                        continue;
                    foreach (var dep in loc.Dependencies)
                    {
                        string resolved = RemapToGameBuildPath(dep.InternalId);
                        if (seen.Add(resolved))
                            paths.Add(resolved);
                    }
                }
                return paths;
            }
            else
            {
                Debug.Log("Fail");
            }
                
            return new List<string>();
        }
        
        // Addressables in editor expands {RuntimePath} to <Addressables.BuildPath> and rewrites the
        // catalog's platform folder to match the editor's active build target. With a Switch-built
        // catalog and an Android editor target, an InternalId like
        //   {RuntimePath}/Switch/fe_assets_ui/.../foo.bundle
        // comes out as
        //   Library/com.unity.addressables/aa/Android/Android/fe_assets_ui/.../foo.bundle
        // We strip <BuildPath>/<activePlatform>/ and prepend the game's own build path so the
        // resolved path actually points at the dumped Switch bundles on disk.
        private static string RemapToGameBuildPath(string internalId)
        {
            string activePlatform = System.IO.Path.GetFileName(Addressables.BuildPath);
            string prefix = Addressables.BuildPath + "/" + activePlatform;
            if (internalId.StartsWith(prefix))
                return Dragonstone.EngageAddressableSettings.GameBuildPath + internalId.Substring(prefix.Length);
            return internalId.Replace(Addressables.BuildPath, Dragonstone.EngageAddressableSettings.GameRuntimePath);
        }

        private static void TraverseDependencies(IResourceLocation location, HashSet<IResourceLocation> visited, HashSet<string> paths)
        {
            if (location == null || visited.Contains(location))
                return;

            visited.Add(location);

            string resolvedPath = RemapToGameBuildPath(location.InternalId);

            paths.Add(resolvedPath);

            if (location.HasDependencies)
            {
                foreach (var dep in location.Dependencies)
                {
                    TraverseDependencies(dep, visited, paths);
                }
            }
        }
    }
}