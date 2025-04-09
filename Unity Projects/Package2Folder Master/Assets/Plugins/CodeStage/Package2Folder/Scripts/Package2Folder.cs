// new argument was added in 19.1.4

#if UNITY_2019_3_OR_NEWER
#define CS_P2F_NEW_ARGUMENT_2
#elif (UNITY_2019_1_OR_NEWER && !UNITY_2019_1_0 && !UNITY_2019_1_1 && !UNITY_2019_1_2 && !UNITY_2019_1_3) || (UNITY_2018_4_OR_NEWER && !UNITY_2018_4_0 && !UNITY_2018_4_1 && !UNITY_2018_4_2)
#define CS_P2F_NEW_ARGUMENT
#endif

#if UNITY_2019_3_OR_NEWER
#define CS_P2F_NEW_NON_INTERACTIVE_LOGIC
#endif

using System;
using System.IO;
using System.Reflection;
using UnityEditor;

namespace CodeStage.PackageToFolder
{
	public static class Package2Folder
	{
		///////////////////////////////////////////////////////////////
		// Delegates and properties with caching for reflection stuff
		///////////////////////////////////////////////////////////////

		#region reflection stuff

#if CS_P2F_NEW_ARGUMENT_2
		private delegate object[] ExtractAndPrepareAssetListDelegate(string packagePath, out string packageIconPath, out string packageManagerDependenciesPath);
#elif CS_P2F_NEW_ARGUMENT
		private delegate object[] ExtractAndPrepareAssetListDelegate(string packagePath, out string packageIconPath, out bool allowReInstall, out string packageManagerDependenciesPath);
#else
		private delegate object[] ExtractAndPrepareAssetListDelegate(string packagePath, out string packageIconPath, out bool allowReInstall);
#endif

		private static Type packageUtilityType;
		private static Type PackageUtilityType
		{
			get
			{
				if (packageUtilityType == null)
					packageUtilityType = typeof(MenuItem).Assembly.GetType("UnityEditor.PackageUtility");
				return packageUtilityType;
			}
		}

		private static ExtractAndPrepareAssetListDelegate extractAndPrepareAssetList;
		private static ExtractAndPrepareAssetListDelegate ExtractAndPrepareAssetList
		{
			get
			{
				if (extractAndPrepareAssetList == null)
				{
					var method = PackageUtilityType.GetMethod("ExtractAndPrepareAssetList");
					if (method == null)
						throw new Exception("Couldn't extract method with ExtractAndPrepareAssetListDelegate delegate!");

					extractAndPrepareAssetList = (ExtractAndPrepareAssetListDelegate)Delegate.CreateDelegate(
					   typeof(ExtractAndPrepareAssetListDelegate),
					   null,
					   method);
				}

				return extractAndPrepareAssetList;
			}
		}
		
		private static FieldInfo destinationAssetPathFieldInfo;
		private static FieldInfo DestinationAssetPathFieldInfo
		{
			get
			{
				if (destinationAssetPathFieldInfo == null)
				{
					var importPackageItem = typeof(MenuItem).Assembly.GetType("UnityEditor.ImportPackageItem");
					destinationAssetPathFieldInfo = importPackageItem.GetField("destinationAssetPath");
				}
				return destinationAssetPathFieldInfo;
			}
		}

		private static MethodInfo importPackageAssetsMethodInfo;
		private static MethodInfo ImportPackageAssetsMethodInfo
		{
			get
			{
				if (importPackageAssetsMethodInfo == null)
					importPackageAssetsMethodInfo = PackageUtilityType.GetMethod("ImportPackageAssets");

				return importPackageAssetsMethodInfo;
			}
		}

		private static MethodInfo importPackageAssetsWithOriginMethodInfo;
		private static MethodInfo ImportPackageAssetsWithOriginMethodInfo
		{
			get
			{
				if (importPackageAssetsWithOriginMethodInfo == null)
					importPackageAssetsWithOriginMethodInfo = PackageUtilityType.GetMethod("ImportPackageAssetsWithOrigin");

				return importPackageAssetsWithOriginMethodInfo;
			}
		}

		private static MethodInfo showImportPackageMethodInfo;
		private static MethodInfo ShowImportPackageMethodInfo
		{
			get
			{
				if (showImportPackageMethodInfo == null)
				{
					var packageImport = typeof(MenuItem).Assembly.GetType("UnityEditor.PackageImport");
					showImportPackageMethodInfo = packageImport.GetMethod("ShowImportPackage");
				}

				return showImportPackageMethodInfo;
			}
		}

		#endregion reflection stuff

		///////////////////////////////////////////////////////////////
		// Unity Editor menus integration
		///////////////////////////////////////////////////////////////

		[MenuItem("Assets/Import Package/Here...", true, 10)]
		private static bool IsImportToFolderCheck()
		{
			var selectedFolderPath = GetSelectedFolderPath();
			return !string.IsNullOrEmpty(selectedFolderPath);
		}

		[MenuItem("Assets/Import Package/Here...", false, 10)]
		private static void Package2FolderCommand()
		{
			var packagePath = EditorUtility.OpenFilePanel("Import package ...", "",  "unitypackage");
			if (string.IsNullOrEmpty(packagePath)) return;
			if (!File.Exists(packagePath)) return;
			
			var selectedFolderPath = GetSelectedFolderPath();
			ImportPackageToFolder(packagePath, selectedFolderPath, true);
		}

		///////////////////////////////////////////////////////////////
		// Main logic
		///////////////////////////////////////////////////////////////

		/// <summary>
		/// Allows to import package to the specified folder either via standard import window or silently.
		/// </summary>
		/// <param name="packagePath">Native path to the package.</param>
		/// <param name="selectedFolderPath">Path to the target folder where you wish to import package into.
		/// Relative to the project folder (should start with 'Assets')</param>
		/// <param name="interactive">If true - imports using standard import window, otherwise does this silently.</param>
		/// <param name="assetOrigin">An optional UnityEditor.AssetOrigin object which Unity from version 2023+ uses internally to store the source of the imported asset inside the meta file.</param>
		public static void ImportPackageToFolder(string packagePath, string selectedFolderPath, bool interactive, object assetOrigin = null)
		{
			string packageIconPath;
#if CS_P2F_NEW_ARGUMENT_2
			string packageManagerDependenciesPath;
			var assetsItems = ExtractAndPrepareAssetList(packagePath, out packageIconPath, out packageManagerDependenciesPath);
#elif CS_P2F_NEW_ARGUMENT
			bool allowReInstall;
			string packageManagerDependenciesPath;
			var assetsItems = ExtractAndPrepareAssetList(packagePath, out packageIconPath, out allowReInstall, out packageManagerDependenciesPath);
#else
			bool allowReInstall;
			var assetsItems = ExtractAndPrepareAssetList(packagePath, out packageIconPath, out allowReInstall);
#endif

			if (assetsItems == null) return;

			foreach (object item in assetsItems)
			{
				ChangeAssetItemPath(item, selectedFolderPath);
			}

			if (interactive)
			{
#if CS_P2F_NEW_ARGUMENT_2
				ShowImportPackageWindow(packagePath, assetsItems, packageIconPath, assetOrigin);
#else	
				ShowImportPackageWindow(packagePath, assetsItems, packageIconPath, allowReInstall);
#endif

			}
			else
			{
				var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(packagePath);
				ImportPackageSilently(fileNameWithoutExtension, assetsItems, assetOrigin);
			}
		}

		private static void ChangeAssetItemPath(object assetItem, string selectedFolderPath)
		{
			string destinationPath = (string)DestinationAssetPathFieldInfo.GetValue(assetItem);
			if (destinationPath.StartsWith("Packages/")) return;
			
			destinationPath = selectedFolderPath + destinationPath.Remove(0, destinationPath.IndexOf('/'));
			DestinationAssetPathFieldInfo.SetValue(assetItem, destinationPath);
		}
		
#if CS_P2F_NEW_ARGUMENT_2
		public static void ShowImportPackageWindow(string path, object[] array, string packageIconPath, object assetOrigin = null)
		{
#if UNITY_2023_1_OR_NEWER
			int productId = 0;
			string packageName = null;
			string packageVersion = null;
			int uploadId = 0;
			if (assetOrigin != null) {
				Type assetOriginType = Type.GetType("UnityEditor.AssetOrigin, UnityEditor.CoreModule");
				if (assetOriginType != null)
				{
					FieldInfo productIdProp = assetOriginType.GetField("productId");
					FieldInfo packageVersionProp = assetOriginType.GetField("packageVersion");
					FieldInfo packageNameProp = assetOriginType.GetField("packageName");
					FieldInfo uploadIdProp = assetOriginType.GetField("uploadId");

					if (productIdProp != null) productId = productIdProp.GetValue(assetOrigin) as int? ?? 0;
					if (packageVersionProp != null) packageVersion = packageVersionProp.GetValue(assetOrigin) as string;
					if (packageNameProp != null) packageName = packageNameProp.GetValue(assetOrigin) as string;
					if (uploadIdProp != null) uploadId = uploadIdProp.GetValue(assetOrigin) as int? ?? 0;
				}
			}
			ShowImportPackageMethodInfo.Invoke(null, new object[]
			{
				path, array, packageIconPath, productId, packageName, packageVersion, uploadId
			});
#else
			ShowImportPackageMethodInfo.Invoke(null, new object[]
			{
				path, array, packageIconPath
			});
#endif
		}
#else
		public static void ShowImportPackageWindow(string path, object[] array, string packageIconPath, bool allowReInstall)
		{
			ShowImportPackageMethodInfo.Invoke(null, new object[] { path, array, packageIconPath, allowReInstall });
		}
#endif

		public static void ImportPackageSilently(string packageName, object[] assetsItems, object assetOrigin = null)
		{
#if CS_P2F_NEW_NON_INTERACTIVE_LOGIC
			if (assetOrigin != null)
			{
				ImportPackageAssetsWithOriginMethodInfo.Invoke(null, new[] {assetOrigin, assetsItems});
			}
			else
			{
				ImportPackageAssetsMethodInfo.Invoke(null, new object[] {packageName, assetsItems});
			}
#else
			ImportPackageAssetsMethodInfo.Invoke(null, new object[] { packageName, assetsItems, false });
#endif
		}

		///////////////////////////////////////////////////////////////
		// Utility methods
		///////////////////////////////////////////////////////////////

		private static string GetSelectedFolderPath()
		{
			if (Selection.assetGUIDs == null || Selection.assetGUIDs.Length == 0)
				return null;

			var assetGuid = Selection.assetGUIDs[0];
			var path = AssetDatabase.GUIDToAssetPath(assetGuid);
			return !Directory.Exists(path) ? null : path;
		}
	}
}