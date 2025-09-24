using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using Dragonstone;
using UnityEditor;
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
                if (CBT.LoadCatalogContent("Assets/Share/AddressableAssetsData/TempCatalogFolder/catalog.json")) {
                    Initialized = true;
                }
            }
            
            return true;
        }

        /// <summary>
        /// Calls on DivineRipper to extract an asset from the game
        /// </summary>
        /// <remarks>
        /// This method will automatically find dependencies for this specific key and get them extracted alongside the specified bundle.
        /// 
        /// </remarks>
        /// <param name="key">Also known as address</param>
        public static bool ExtractAsset(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("ExtractAsset: key is null or empty");
                return false;
            }
            
            Initialize();

            // TODO: Gather dependencies for the key for queueing them in the extraction
            var dependencies = CBT.GetDependenciesForAsset(key);
            
            AssetRipperRequestBuilder builder = new AssetRipperRequestBuilder();
            
            foreach (string dependency in dependencies)
            {
                builder.AddFile(dependency);
            }

            if (!builder.Export("dumper"))
                Debug.LogError($"Export was unsuccessful");
            
            return true;
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
    }
}