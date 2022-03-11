# Addressables - Multi-Catalog

The Addressables package by Unity provides a novel way of managing and packing assets for your build. It builds on top of the Asset Bundle system, and in certain ways, also seeks to dispose of the Resources folder.

This variant forked from the original Addressables-project adds support for building your assets across several catalogs in one go and provides several other benefits, e.g. reduced build times and build size, as well as keeping the buildcache intact.

Multiple catalogs can be used for ODR (on demand resources), DLC or for iOS app slicing.

**Note**: this repository does not track every available version of the _vanilla_ Addressables package. It's only kept up-to-date sporadically.

## The problem

A frequently recurring question is:

> Can Addressables be used for DLC bundles/assets?

The answer to that question largely depends on how you define what DLC means for your project, but for clarity's sake, let's define it this way:

> A package that contains assets and features to expand the base game. Such a package holds unique assets that _may_ rely on assets already present in the base game.

So, does Addressables support DLC packages as defined above? Yes, it's supported, but in a very crude and inefficient way.

In the vanilla implementation of Addressables, only one content catalog file is built at a time, and only the assets as defined to be included in the current build will be packed and saved. Any implicit dependency of an asset will get included in that build too, however. This creates several major problems:

* Each build (one for the base game, and one for each DLC package) will include each and every asset it relies on. This is expected for the base game, but not for the DLC package. This essentially creates unnecessary large DLC packages, and in the end, your running game will include the same assets multiple times on the player's system, and perhaps even in memory at runtime.
* No build caching can be used, since each build done for a DLC package is considered a whole new build for the Addressables system, and will invalidate any prior caching done. This significantly increases build times.
* Build caching and upload systems as those used by Steam for example, can't fully differentiate between the changes properly of subsequent builds. This results in longer and larger uploads, and consequently, in bigger updates for players to download.

## The solution

The solution comes in the form of performing the build process of the base game and all DLC packages in one step. In essence, what this implementation does is, have the Addressables build-pipeline perform one large build of all assets tracked by the base game and each of its DLC packages, and afterwards, extract the contents per defined DLC package and create a separate content catalog for each of them. Finally, Addressables creates the final content catalog file for the left-over asset groups, those that remain for the base game.

## Installation

This package is best installed using Unity's Package Manager. Fill in the URL found below in the package manager's input field for git-tracked packages:

> https://github.com/juniordiscart/com.unity.addressables.git

### Updating a vanilla installation

When you've already set up Addressables in your project and adjusted the settings to fit your project's needs, it might be cumbersome to set everything back. In that case, it might be better to update your existing settings with the new objects rather than starting with a clean slate:

1. Remove the currently tracked Addressables package from the Unity Package manager and track this version instead as defined by the [Installation section](#installation). However, __don't delete__ the `Assets/AddressableAssetsData` folder from your project!

2. In your project's `Assets/AddressableAssetsData/DataBuilders` folder, create a new 'multi-catalog' data builder:

   > Create → Addressables → Content Builders → Multi-Catalog Build Script

   ![Create multi-catalog build script](Documentation~/images/multi_catalogs/CreateDataBuilders.png)

3. Select your existing Addressable asset settings object, navigate to the `Build and Play Mode Scripts` property and add your newly created multi-catalog data builder to the list.

   ![Assign data builder to Addressable asset settings](Documentation~/images/multi_catalogs/AssignDataBuilders.png)

4. Optionally, if you have the Addressables build set to be triggered by the player build, or have a custom build-pipeline, you will have to set the `ActivePlayerDataBuilderIndex` property. This value must either be set through the debug-inspector view (it's not exposed by the custom inspector), or set it through script.

   ![Set data builder index](Documentation~/images/multi_catalogs/SetDataBuilderIndex.png)

### Setting up multiple catalogs

With the multi-catalog system installed, additional catalogs can now be created and included in build:

1. Create a new `ExternalCatalogSetup` object, one for each DLC package:

   > Create → Addressables → new External Catalog

2. In this object, fill in the following properties:
   * Catalog name: the name of the catalog file produced during build.
   * Build path: where this catalog and it's assets will be exported to after the build is done. This supports the same variable syntax as the build path in the Addressable Asset Settings.
   * Runtime load path: when the game is running, where should these assets be loaded from. This should depend on how you will deploy your DLC assets on the systems of your players. It also supports the same variable syntax.

   ![Set external catalog properties](Documentation~/images/multi_catalogs/SetCatalogSettings.png)

3. Assign the Addressable asset groups that belong to this package.

4. Now, select the `BuildScriptPackedMultiCatalogMode` data builder object and assign your external catalog object(s).

   ![Assign external catalogs to data builder](Documentation~/images/multi_catalogs/AssignCatalogsToDataBuilder.png)

## Building

With everything set up and configured, it's time to build the project's contents!

In your Addressable Groups window, tick all 'Include in build' boxes of those groups that should be built. From the build tab, there's a new `Default build script - Multi-Catalog` option. Select this one to start a content build with the multi-catalog setup.

## Loading the external catalogs

When you need to load in the assets put aside in these external packages, you can do so using:

> `Addressables.LoadContentCatalogAsync("path/to/dlc/catalogName.json");`

## iOS App Slicing

iOS app slicing is a feature that allows multiple versions of an asset to be combined in a build, but only versions that are compatible with the users device are downloaded from the app store. In order to configure App thining assets are placed into an Asset Catalog, a bundle with an extensions of xcasset. The meta daa of the catalog defines which variants should be selected on different platforms.

Unity AssetBundles have features to support slicing, including loading bundles from a catalog and storing resources in a catalog during a build. Unfortunately the release version of the Addressables package doesn't take advantage of these features. This fork has a few fixes and new features to allow addressable assets to be sliced.

Normally Asset Catalogs have multiple variants for each resource. Client code requests resources by name and get the variant supported by the local hardware without having to know which version is being returned. In an ideal workflow Addressable asset bundles would be stored in iOS resources and the Addressables system would request resources to load and get the bundle for the local platform. Unfortunately, the Addressable index is not tolerant to receiving a different bundle than was requested, so a different approach is used.

Each variant bundle is stored in the catalog as a separate resource. There are 'empty' variants for these resources. In order for this scheme to work the Addressable index needs to have sub indexes that select different assets based on the current platform. To achieve this end the additional addressable indexes are stored in the asset catalog, so that the same slicing rules are applied to the index as to the bundles. When the application starts it loads any sub indexes from the catalog.

In a traditional unity Addressable build the bundles are stored in the Library folder. In order to put the resources in the iOS Asset Catalog they need to be built into the Asset database instead so that the build script can access them. It is likely that you want to disable version control for this folder so that the bundles aren't stored in your repository. NOTE I'm not 100% sure if this step is necessary, but I've had issues getting it to work otherwise.
### Configuring iOS App Slicing

1. In order to store the Addresable catalogs (indexes) in the iOS Asset Catalog they need to be in an asset bundle. This is enabled in the Addressable Asset Settings. The API property is 'm_BundleLocalCatalog', but perversely the option to select is "Compress Local Catalog". Select this option.
2. A new Addressables profile is needed to configure where bundles are built to and loaded from. Go to 'Window > Asset Management > Addressables > Profiles' and select 'Create > Profile'. Most of the settings are the same as the 'Default' profile, but LocalBuildPath should be set to the folder in the AssetDatabase where the bundles will be located. For instance, "Assets/AddressableAssetsData/GeneratedBundles". LocalLoadPath should be set to "res://" which directs the addressable system to load bundles from the asset catalog.
3. Create catalogs for your variant assets. Each catalog in a variant group should have the same "variant group" identifier. Each catalog  should contain different versions of assets with the same address. For example, one catalog might have low resolution textures and another might have high resolution, but the atlases in both groups will have the same addresses.
4. SpriteAtlases that are included in an Addressable Group that is included in the build should not be included in the build themselves. Check all sprite atlases and ensure that "include in build" is set to false. Not only can including the atlases in the build lead to double inclusion, it can create dependency issues with variants. Either sprites won’t show up, or there will be build errors.
5. Each variant that you create will require a new AssetCatalog. 
   1. First, setup two or more asset catalogs as described in [setting up multiple catalogs](#Setting up multiple catalogs).
   2. Select variant asset groups for each catalog.
   3. Set 'Build Path' for the group to the same path as the profile created above (eg: "Assets/AddressableAssetsData/GeneratedBundles").
   4. Set 'Runtime Load Path' to "res://"
   5. Configure the device properties list with Key Value pairs for the Asset Catalog device requirements. It is possible to have one group which has no device requirements (a default group) and then another group that will override in some situations. For example, if one catalog has no requirements and the other contains "memory" => "2GB", the first catalog will be used unless the target device has >= 2GB of memory in which case the second catalog is selected.
6. The BuildScriptPacketMultiCatalogResources build script is required in order to create the asset bundle catalog. In addition to using this script, the resources must be collected. Call 

   > UnityEditor.iOS.BuildPipeline.collectResources += packedMulti.CollectResources;
   > BuildPipeline.BuildPlayer(buildPlayerOptions);

   to setup the build script as the resource collector during the build. The resources will be used to create an iOS asset catalog in the resulting XCode project.
7. Before your applciation accesses any of the variant resources load the variant catalog with the following call:

   > Addressables.LoadContentCatalogAsync("res://AssetCatalog", true);

