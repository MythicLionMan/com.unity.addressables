using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace UnityEditor.AddressableAssets.Build.DataBuilders
{
	/// <summary>
	/// Separate catalog for the assigned asset groups.
	/// </summary>
	[CreateAssetMenu(menuName = "Addressables/new External Catalog", fileName = "newExternalCatalogSetup")]
	public class ExternalCatalogSetup : ScriptableObject
	{
        /// <summary>
        /// The catalog variant identifier. Multiple catalogs that provide the same assets should share
        /// a variant identifier.
        /// </summary>
		[SerializeField, Tooltip("If multiple catalogs provide assets that are variants of one another they should all share a variant group.")]
		private string VariantGroup;
        public string VariantName => (VariantGroup == null) || (VariantGroup == "") ? "AssetCatalog" : VariantGroup;

		[SerializeField, Tooltip("Assets groups that belong to this catalog. Entries found in these will get extracted from the default catalog.")]
		private List<AddressableAssetGroup> assetGroups = new List<AddressableAssetGroup>();
#if UNITY_IOS || UNITY_MACOS
		[SerializeField, Tooltip("A set of key/value pairs for configuring an iOSDeviceRequirement.")]
		private List<RequirementKeyValuePair> deviceProperties = new List<RequirementKeyValuePair>();

		[System.Serializable]
		public struct RequirementKeyValuePair {
			[SerializeField]
			private string Key;
			[SerializeField]
			private string Value;

			public void AddToRequirement(iOSDeviceRequirement requirement) {
				requirement.values.Add(Key, Value);
			}
		}

		public iOSDeviceRequirement DeviceRequirement
		{
			get
			{
				var requirement = new iOSDeviceRequirement();
				foreach (var pair in deviceProperties) pair.AddToRequirement(requirement);
				return requirement;
			}
		}
#endif
		[SerializeField, Tooltip("Build path for the produced files associated with this catalog.")]
		private string buildPath = string.Empty;
		[SerializeField, Tooltip("Runtime load path for assets associated with this catalog.")]
		private string runtimeLoadPath = string.Empty;
		[SerializeField, Tooltip("Catalog name. This will also be the name of the exported catalog file.")]
		private string catalogName = string.Empty;

		public string CatalogName
		{
			get { return catalogName; }
			set { catalogName = value; }
		}

		public string BuildPath
		{
			get { return buildPath; }
			set { buildPath = value; }
		}

		public string RuntimeLoadPath
		{
			get { return runtimeLoadPath; }
			set { runtimeLoadPath = value; }
		}

		public IReadOnlyList<AddressableAssetGroup> AssetGroups
		{
			get { return assetGroups; }
			set { new List<AddressableAssetGroup>(value); }
		}

		public bool IsPartOfCatalog(ContentCatalogDataEntry loc, AddressableAssetsBuildContext aaContext)
		{
			if ((assetGroups != null) && (assetGroups.Count > 0))
			{
				if ((loc.ResourceType == typeof(IAssetBundleResource)))
				{
					AddressableAssetEntry entry = aaContext.assetEntries.Find(ae => string.Equals(ae.BundleFileId, loc.InternalId));
					if (entry == null)
					{
						return false;
					}

					return assetGroups.Exists(ag => ag.entries.Contains(entry));
				}
				else
				{
					return assetGroups.Exists(ag => ag.entries.Any(e => loc.Keys.Contains(e.guid)));
				}
			}
			else
			{
				return false;
			}
		}
	}
}
