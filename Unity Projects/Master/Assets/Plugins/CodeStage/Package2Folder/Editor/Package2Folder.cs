#define UNITY_5_3_PLUS

#if UNITY_4_5 || UNITY_4_6 || UNITY_4_7 || UNITY_4_8 || UNITY_4_9 || UNITY_5_0 || UNITY_5_1 || UNITY_5_2
#undef UNITY_5_3_PLUS
#endif

using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using Object = UnityEngine.Object;

namespace CodeStage.PackageToFolder
{
	public class Package2Folder
	{
		///////////////////////////////////////////////////////////////
		// Delegates and properties with caching for reflection stuff
		///////////////////////////////////////////////////////////////

		#region reflection stuff

#if !UNITY_5_3_PLUS
		private delegate AssetsItem[] ImportPackageStep1Delegate(string packagePath, out string packageIconPath);

		private static Type assetServerType;
		private static Type AssetServerType
		{
			get
			{
				if (assetServerType == null)
				{
					assetServerType = typeof(MenuItem).Assembly.GetType("UnityEditor.AssetServer");
				}

				return assetServerType;
			}
		}

		private static ImportPackageStep1Delegate importPackageStep1;
		private static ImportPackageStep1Delegate ImportPackageStep1
		{
			get
			{
				if (importPackageStep1 == null)
				{
					 importPackageStep1 = (ImportPackageStep1Delegate)Delegate.CreateDelegate(
						typeof(ImportPackageStep1Delegate),
						null,
						AssetServerType.GetMethod("ImportPackageStep1"));
				}

				return importPackageStep1;
			}
		}

		private static MethodInfo importPackageStep2MethodInfo;
		private static MethodInfo ImportPackageStep2MethodInfo
		{
			get
			{
				if (importPackageStep2MethodInfo == null)
				{
					importPackageStep2MethodInfo = AssetServerType.GetMethod("ImportPackageStep2");
				}

				return importPackageStep2MethodInfo;
			}
		}
#else
		private delegate object[] ExtractAndPrepareAssetListDelegate(string packagePath, out string packageIconPath, out bool allowReInstall);

		private static Type packageUtilityType;
		private static Type PackageUtilityType
		{
			get
			{
				if (packageUtilityType == null)
				{
					packageUtilityType = typeof(MenuItem).Assembly.GetType("UnityEditor.PackageUtility");
				}
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
					extractAndPrepareAssetList = (ExtractAndPrepareAssetListDelegate)Delegate.CreateDelegate(
					   typeof(ExtractAndPrepareAssetListDelegate),
					   null,
					   PackageUtilityType.GetMethod("ExtractAndPrepareAssetList"));
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
					Type importPackageItem = typeof(MenuItem).Assembly.GetType("UnityEditor.ImportPackageItem");
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
				{
					importPackageAssetsMethodInfo = PackageUtilityType.GetMethod("ImportPackageAssets");
				}

				return importPackageAssetsMethodInfo;
			}
		}
#endif

		private static MethodInfo showImportPackageMethodInfo;
		private static MethodInfo ShowImportPackageMethodInfo
		{
			get
			{
				if (showImportPackageMethodInfo == null)
				{
					Type packageImport = typeof(MenuItem).Assembly.GetType("UnityEditor.PackageImport");
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
			string selectedFolderPath = GetSelectedFolderPath();
			return !string.IsNullOrEmpty(selectedFolderPath);
		}

		[MenuItem("Assets/Import Package/Here...", false, 10)]
		private static void Package2FolderCommand()
		{
			string packagePath = EditorUtility.OpenFilePanel("Import package ...", "",  "unitypackage");
			if (string.IsNullOrEmpty(packagePath)) return;
			if (!File.Exists(packagePath)) return;
			
			string selectedFolderPath = GetSelectedFolderPath();
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
		public static void ImportPackageToFolder(string packagePath, string selectedFolderPath, bool interactive)
		{
			string packageIconPath;
			bool allowReInstall;

			object[] assetsItems = ExtractAssetsFromPackage(packagePath, out packageIconPath, out allowReInstall);

			if (assetsItems == null) return;

			foreach (object item in assetsItems)
			{
				ChangeAssetItemPath(item, selectedFolderPath);
			}

			if (interactive)
			{
				ShowImportPackageWindow(packagePath, assetsItems, packageIconPath, allowReInstall);
			}
			else
			{
				ImportPackageSilently(assetsItems);
			}
		}

		public static object[] ExtractAssetsFromPackage(string path, out string packageIconPath, out bool allowReInstall)
		{
#if !UNITY_5_3_PLUS
			AssetsItem[] array = ImportPackageStep1(path, out packageIconPath);
			allowReInstall = false;
			return array;
#else
			object[] array = ExtractAndPrepareAssetList(path, out packageIconPath, out allowReInstall);
			return array;
#endif
		}

		private static void ChangeAssetItemPath(object assetItem, string selectedFolderPath)
		{
#if !UNITY_5_3_PLUS
			AssetsItem item = (AssetsItem)assetItem;
			item.exportedAssetPath = selectedFolderPath + item.exportedAssetPath.Remove(0, 6);
			item.pathName = selectedFolderPath + item.pathName.Remove(0, 6);
#else
			string destinationPath = (string)DestinationAssetPathFieldInfo.GetValue(assetItem);
			destinationPath = selectedFolderPath + destinationPath.Remove(0, 6);
			DestinationAssetPathFieldInfo.SetValue(assetItem, destinationPath);
#endif
		}

		public static void ShowImportPackageWindow(string path, object[] array, string packageIconPath, bool allowReInstall)
		{
#if !UNITY_5_3_PLUS
			ShowImportPackageMethodInfo.Invoke(null, new object[] { path, array, packageIconPath });
#else
			ShowImportPackageMethodInfo.Invoke(null, new object[] { path, array, packageIconPath, allowReInstall });
#endif
		}

		public static void ImportPackageSilently(object[] assetsItems)
		{
#if !UNITY_5_3_PLUS
			ImportPackageStep2MethodInfo.Invoke(null, new object[] { assetsItems, false });
#else
			ImportPackageAssetsMethodInfo.Invoke(null, new object[] { assetsItems, false });
#endif
		}

		///////////////////////////////////////////////////////////////
		// Utility methods
		///////////////////////////////////////////////////////////////

		private static string GetSelectedFolderPath()
		{
			Object obj = Selection.activeObject;
			if (obj == null) return null;

			string path = AssetDatabase.GetAssetPath(obj.GetInstanceID());
			return !Directory.Exists(path) ? null : path;
		}
	}
}