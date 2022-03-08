using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.Initialization;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceProviders;
#if UNITY_IOS || UNITY_MACOS
using Resource = UnityEditor.iOS.Resource;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
#endif
namespace UnityEditor.AddressableAssets.Build.DataBuilders
{
	/// <summary>
	/// Build script used for player builds and running with bundles in the editor, allowing building of multiple catalogs.
	/// </summary>
	[CreateAssetMenu(fileName = "BuildScriptPackedMultiCatalog.asset", menuName = "Addressables/Content Builders/Multi-Catalog Build Script")]
	public class BuildScriptPackedMultiCatalogMode : BuildScriptPackedMode, IMultipleCatalogsBuilder
	{
		/// <summary>
		/// Move a file, deleting it first if it exists.
		/// </summary>
		/// <param name="src">the file to move</param>
		/// <param name="dst">the destination</param>
		private static void FileMoveOverwrite(string src, string dst)
		{
			if (File.Exists(dst))
			{
				File.Delete(dst);
			}
			File.Move(src, dst);
		}

		[SerializeField]
		private List<ExternalCatalogSetup> externalCatalogs = new List<ExternalCatalogSetup>();

		private readonly List<CatalogSetup> catalogSetups = new List<CatalogSetup>();

		public override string Name
		{
			get { return base.Name + " - Multi-Catalog"; }
		}

		public List<ExternalCatalogSetup> ExternalCatalogs
		{
			get { return externalCatalogs; }
			set { externalCatalogs = value; }
		}

		protected override List<ContentCatalogBuildInfo> GetContentCatalogs(AddressablesDataBuilderInput builderInput, AddressableAssetsBuildContext aaContext)
		{
			// cleanup
			catalogSetups.Clear();

			// Prepare catalogs
			var defaultCatalog = new ContentCatalogBuildInfo(ResourceManagerRuntimeData.kCatalogAddress, builderInput.RuntimeCatalogFilename);
			foreach (ExternalCatalogSetup catalogContentGroup in externalCatalogs)
			{
				if (catalogContentGroup != null)
				{
					catalogSetups.Add(new CatalogSetup(catalogContentGroup));
				}
			}

			// Assign assets to new catalogs based on included groups
			var profileSettings = aaContext.Settings.profileSettings;
			var profileId = aaContext.Settings.activeProfileId;
			foreach (var loc in aaContext.locations)
			{
				CatalogSetup preferredCatalog = catalogSetups.FirstOrDefault(cs => cs.CatalogContentGroup.IsPartOfCatalog(loc, aaContext));
				if (preferredCatalog != null)
				{
					if (loc.ResourceType == typeof(IAssetBundleResource))
					{
						string fileName;
						if (loc.InternalId.StartsWith("res://"))
						{							
							// The fileName is needed to generate the runtime path. No files are added to preferredCatalog, since
							// a res:// reference is stored in an iOS AssetCatalog, and is not managed by the catalog.
							fileName = Path.GetFileName(loc.InternalId);
						}
						else
						{
							string filePath = Path.GetFullPath(loc.InternalId.Replace("{UnityEngine.AddressableAssets.Addressables.RuntimePath}", Addressables.BuildPath));
							fileName = Path.GetFileName(filePath);

							preferredCatalog.Files.Add(filePath);
						}
						string runtimeLoadPath = Path.Combine(preferredCatalog.CatalogContentGroup.RuntimeLoadPath, fileName);
						runtimeLoadPath = profileSettings.EvaluateString(profileId, runtimeLoadPath);

						preferredCatalog.BuildInfo.Locations.Add(new ContentCatalogDataEntry(typeof(IAssetBundleResource), runtimeLoadPath, loc.Provider, loc.Keys, loc.Dependencies, loc.Data));
					}
					else
					{
						preferredCatalog.BuildInfo.Locations.Add(loc);
					}
				}
				else
				{
					defaultCatalog.Locations.Add(loc);
				}
			}


			// Process dependencies
			foreach (CatalogSetup additionalCatalog in catalogSetups)
			{
				var dataEntries = new Queue<ContentCatalogDataEntry>(additionalCatalog.BuildInfo.Locations);
				var processedEntries = new HashSet<ContentCatalogDataEntry>();
				while (dataEntries.Count > 0)
				{
					ContentCatalogDataEntry dataEntry = dataEntries.Dequeue();
					if (!processedEntries.Add(dataEntry) || (dataEntry.Dependencies == null) || (dataEntry.Dependencies.Count == 0))
					{
						continue;
					}

					foreach (var entryDependency in dataEntry.Dependencies)
					{
						// Search for the dependencies in the default catalog only.
						var depLocation = defaultCatalog.Locations.Find(loc => loc.Keys[0] == entryDependency);
						if (depLocation != null)
						{
							dataEntries.Enqueue(depLocation);

							// If the dependency wasn't part of the catalog yet, add it.
							if (!additionalCatalog.BuildInfo.Locations.Contains(depLocation))
							{
								additionalCatalog.BuildInfo.Locations.Add(depLocation);
							}
						}
						else if (!additionalCatalog.BuildInfo.Locations.Exists(loc => loc.Keys[0] == entryDependency))
						{
							Debug.LogErrorFormat("Could not find location for dependency ID {0} in the default catalog.", entryDependency);
						}
					}
				}
			}

			// Gather catalogs
			var catalogs = new List<ContentCatalogBuildInfo>(catalogSetups.Count + 1);
			catalogs.Add(defaultCatalog);
			foreach (var setup in catalogSetups)
			{
				if (!setup.Empty)
				{
					catalogs.Add(setup.BuildInfo);
				}
			}
			return catalogs;
		}

		protected override TResult DoBuild<TResult>(AddressablesDataBuilderInput builderInput, AddressableAssetsBuildContext aaContext)
		{
			// execute build script
			var result = base.DoBuild<TResult>(builderInput, aaContext);

			// move extra catalogs to CatalogsBuildPath
			foreach (var setup in catalogSetups)
			{
				// Empty catalog setups are not added/built
				if (setup.Empty)
				{
					continue;
				}

				if (setup.CatalogContentGroup.BuildPath != null && setup.CatalogContentGroup.BuildPath != "")
				{
					var bundlePath = aaContext.Settings.profileSettings.EvaluateString(aaContext.Settings.activeProfileId, setup.CatalogContentGroup.BuildPath);
					Directory.CreateDirectory(bundlePath);

					var bundleFileName = setup.BuildInfo.JsonFilename;
					if (aaContext.Settings.BundleLocalCatalog) bundleFileName = bundleFileName.Replace(".json", ".bundle");

					FileMoveOverwrite(Path.Combine(Addressables.BuildPath, bundleFileName), Path.Combine(bundlePath, bundleFileName));
					foreach (var file in setup.Files)
					{
						FileMoveOverwrite(file, Path.Combine(bundlePath, Path.GetFileName(file)));
					}
				}
			}

			return result;
		}

		public override void ClearCachedData()
		{
			base.ClearCachedData();

			if ((externalCatalogs == null) || (externalCatalogs.Count == 0))
			{
				return;
			}

			// Cleanup the additional catalogs
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			foreach (ExternalCatalogSetup additionalCatalog in externalCatalogs)
			{
				string buildPath = settings.profileSettings.EvaluateString(settings.activeProfileId, additionalCatalog.BuildPath);
				if (!Directory.Exists(buildPath))
				{
					continue;
				}

				foreach (string catalogFile in Directory.GetFiles(buildPath))
				{
					File.Delete(catalogFile);
				}

				Directory.Delete(buildPath, true);
			}
		}

// TODO I'd like this to be in a different file, but I need access to certain private members
#if UNITY_IOS || UNITY_MACOS
		public Resource[] CollectResources()
		{
			// In typical use an AssetCatalog has one or more variant for each resource, and slicing rules determine
			// which one is used. Because the addressables index is 
			// The variant index references each resource variant using a unique name. So the highres and lowres
			// variants of the same texture will have two separate entries in the asset catalog. This means that
			// each resource returned by the resource collector will be returning Resource instances with one valid
			// variant, and one 'empty' varaint. If the 'empty' variant is not initialized then XCode will detect
			// this when it generates the catalog and always include the other variant, despite the fact that the
			// bundle at that address will never be requested when the 'empty' variant is returned by slicing.
			// To circumvent this an empty file is used as the variant for these 'unavailable' resources. If the
			// empty file does not exist this will create it.
			string emptyPath = Path.Combine(Application.temporaryCachePath, "EmptyFile");
            LocalExtensions.EnsureFileExists(emptyPath);
			
			// The catalog for each group is a Resource. Note that this assumes that the catalog is in a bundle.
			// If it is not, then the catalog can't be loaded.
			var catalogs = catalogSetups.Select(setup => (setup, "AssetCatalog", setup.BuildInfo.JsonFilename.Replace(".json", ".bundle"))).ToArray();
			
			// Asset bundles that are referenced with res:// in each group are variant resources
			var variants = catalogSetups.SelectMany(setup => 
				setup.BuildInfo
						.Locations
						.Select(location => (setup, location.InternalId, location.InternalId))
						.Where(t => t.Item2.StartsWith("res://"))
			).ToArray();

			// Combine the catalogs and the variant assets and normalize any paths
			var allVariants = catalogs.Concat(variants)
									  .Select(t => {
										var name = t.Item2;
										if (name.StartsWith("res://")) name = name.Remove(0, 6);
											var fileName = t.Item3;
											if (fileName.StartsWith("res://")) fileName = fileName.Remove(0, 6);
											return (
												t.Item1, 
												name,
												Path.Combine(t.Item1.CatalogContentGroup.BuildPath, fileName)
											);
									  }).ToArray();

			// The paths of all bundles for which there is a variant. These will all be generated as variant resources,
			// so the normal resource path should not create any instances of these.
			var variantBundles = allVariants.Select(t => t.Item3);

			// Pivot allVariants to get a list of the catalogs associated with each address
			var catalogsByAddress = allVariants
				.GroupBy(t => t.Item2)
				.Select(group => (group.Key, group.ToDictionary(g => g.Item1, g => g.Item3)))
				.ToArray();
			
			// Map each group to a DeviceRequirements
			var requirements = catalogSetups.Select(setup => setup.CatalogContentGroup)
			                                .ToDictionary(group => group.CatalogName, group => group.DeviceRequirement);

			// Map the definitions to the variant Resources
			var variantResources = catalogsByAddress.Select(t => {
				Resource resource = new Resource(t.Item1);
				var resourceVariants = t.Item2;
			   #if true
				foreach (var variant in catalogSetups) {
					var variantName = variant.BuildInfo.Identifier;
					if (resourceVariants.ContainsKey(variant)) {
						string path = resourceVariants[variant];
						resource = resource.BindVariant(path, requirements[variantName]);
					} else {
						// There is no variant for this catalog. Generate an empty variant
						// so that the AssetCatalog doesn't contain the alternate.
						resource = resource.BindVariant(emptyPath, requirements[variantName]);
					}
				}
			   #else
				foreach (var variantInfo in resourceVariants) {
					CatalogSetup variant = variantInfo.Key;
					string path = variantInfo.Value;
					var variantName = variant.BuildInfo.Identifier;
					resource = resource.BindVariant(path, requirements[variantName]);
				}
			   #endif
				return resource;
			}).ToArray();

			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;

			// Find all unique bundle build paths
			var allBuildPaths = catalogSetups.Select(setup => settings.profileSettings.EvaluateString(settings.activeProfileId, setup.CatalogContentGroup.BuildPath))
			             					 .Append(settings.profileSettings.EvaluateString(settings.activeProfileId, Addressables.BuildPath))
											 .Distinct()
											 .ToArray();

			// Search all build paths for all bundles
			var bundles = AssetDatabase.FindAssets("", allBuildPaths);

			// Create resource definitions for the non variant resources, map them to resource objects
			// and then combine them with the variant resources
			return bundles.Select(guid => AssetDatabase.GUIDToAssetPath(guid))
						  .Except(variantBundles)           // filter out any bundles that have a variant
						  .Select(bundlePath => {
						  	  var name = Path.GetFileName(bundlePath);
							  //Debug.Log($"Non variant resource = {name}:{bundlePath}");
							  return new Resource(name, bundlePath);
						  })
						  .Concat(variantResources)			// Append the variant resources
						  .ToArray();		
		}
#endif
		private class CatalogSetup
		{
			public readonly ExternalCatalogSetup CatalogContentGroup = null;

			/// <summary>
			/// The catalog build info.
			/// </summary>
			public readonly ContentCatalogBuildInfo BuildInfo;

			/// <summary>
			/// The files associated to the catalog.
			/// </summary>
			public readonly List<string> Files = new List<string>(1);

			/// <summary>
			/// Tells whether the catalog is empty.
			/// </summary>
			public bool Empty
			{
				get { return BuildInfo.Locations.Count == 0; }
			}

			public CatalogSetup(ExternalCatalogSetup buildCatalog)
			{
				this.CatalogContentGroup = buildCatalog;
				BuildInfo = new ContentCatalogBuildInfo(buildCatalog.CatalogName, buildCatalog.CatalogName + ".json");
				BuildInfo.Register = false;
			}
		}

		private static class LocalExtensions {
			public static void EnsureFileExists(string path) {
                // If the file exists return
                if (File.Exists(path)) return;

				StreamWriter writer = new StreamWriter(path, true);
				writer.Close();				
			}
		}
	}
}
