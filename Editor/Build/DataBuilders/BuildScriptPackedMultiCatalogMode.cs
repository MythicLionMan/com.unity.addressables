using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.Initialization;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceProviders;

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

					FileMoveOverwrite(Path.Combine(Addressables.BuildPath, setup.BuildInfo.JsonFilename), Path.Combine(bundlePath, setup.BuildInfo.JsonFilename));
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
	}
}
