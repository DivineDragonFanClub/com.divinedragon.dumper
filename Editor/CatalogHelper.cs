// using System.Collections.Generic;
// using UnityEditor;
// using UnityEngine;
// using UnityEngine.AddressableAssets;
// using UnityEngine.ResourceManagement.AsyncOperations;
// using UnityEngine.ResourceManagement.ResourceLocations;
//
// namespace DivineDragon
// {
//     public class CatalogHelper
//     {
//         [InitializeOnLoadMethod]
//         static void LoadCatalogOnProjectLoad()
//         {
//             Dragonstone.EngageAddressableSettings.Initialize();
//             
//             Debug.Log("Starting catalog loading ");
//             
//             var handle = Addressables.LoadContentCatalogAsync(Dragonstone.EngageAddressableSettings.GameCatalogLocation, true);
//             handle.WaitForCompletion();
//             
//             if (handle.Status == AsyncOperationStatus.Succeeded)
//             {
//                 Debug.Log("Successfully loaded Engage's catalog.json ");
//
//                 // var cock = Addressables.LoadResourceLocationsAsync("Unit/Model/uBody/Drg0AF/c052/Prefabs/uBody_Drg0AF_c052");
//                 // cock.WaitForCompletion();
//                 //
//                 // if (cock.Status == AsyncOperationStatus.Succeeded)
//                 // {
//                 //     IList<IResourceLocation> locations = cock.Result;
//                 //
//                 //     Debug.Log($"Dependencies for address:");
//                 //
//                 //     foreach (var location in locations)
//                 //     {
//                 //         Debug.Log($"- Main location: {location}");
//                 //
//                 //         foreach (var dep in location.Dependencies)
//                 //         {
//                 //             Debug.Log($"    ↳ Dependency: {dep.InternalId.Replace(Addressables.BuildPath + "/Switch/", "")}");
//                 //         }
//                 //     }
//                 // }
//                 // else
//                 // {
//                 //     Debug.LogError("Failed to get resource locations for address");
//                 // }
//             }
//             else
//             {
//                 Debug.LogError("Could not load catalog.json file from Fire Emblem Engage");
//             }
//         }
//     }
// }