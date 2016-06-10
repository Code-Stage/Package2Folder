using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using Object = UnityEngine.Object;

namespace CodeStage.PackageToFolder
{
	public class Package2Folder
	{

#if UNITY_4_6
		delegate AssetsItem[] ImportPackageStep1Delegate(string packagePath, out string packageIconPath);
#else
		delegate object[] ExtractAndPrepareAssetListDel(string packagePath, out string packageIconPath, out bool allowReInstall);
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

		[MenuItem("Assets/Import Package/Custom to this folder...", true, 10)]
		private static bool IsImportToFolderPossible()
		{
			string selectedFolderPath = GetSelectedFolderPath();
			return !string.IsNullOrEmpty(selectedFolderPath);
		}

		[MenuItem("Assets/Import Package/Custom to this folder...", false, 10)]
		private static void ImportToFolderCallback()
		{
			string packagePath = EditorUtility.OpenFilePanel("Import package ...", "",  "unitypackage");
			if (string.IsNullOrEmpty(packagePath)) return;
			if (!File.Exists(packagePath)) return;

			string selectedFolderPath = GetSelectedFolderPath();

			ImportPackageToFolder(packagePath, selectedFolderPath, true);
		}

		private static string GetSelectedFolderPath()
		{
			Object obj = Selection.activeObject;
			if (obj == null) return null;

			string path = AssetDatabase.GetAssetPath(obj.GetInstanceID());
			return !Directory.Exists(path) ? null : path;
		}

		private static void ImportPackageToFolder(string path, string selectedFolderPath, bool interactive)
		{
			string packageIconPath;
			bool allowReInstall;

			object[] assetsItems = ExtractAssetsFromPackage(path, out packageIconPath, out allowReInstall);

			if (assetsItems == null) return;

			foreach (object item in assetsItems)
			{
				ChangeAssetItemPath(item, selectedFolderPath);
			}

			if (interactive)
			{
				ShowImportPackageWindow(path, assetsItems, packageIconPath, allowReInstall);
			}
			else
			{
				ImportPackageSilently(assetsItems);
			}
		}

		private static void ChangeAssetItemPath(object assetItem, string selectedFolderPath)
		{
#if UNITY_4_6
			AssetsItem item = (AssetsItem)assetItem;
			item.exportedAssetPath = selectedFolderPath + item.exportedAssetPath.Remove(0, 6);
			item.pathName = selectedFolderPath + item.pathName.Remove(0, 6);
#else
			Type importPackageItem = typeof(MenuItem).Assembly.GetType("UnityEditor.ImportPackageItem");
			FieldInfo destinationAssetPath = importPackageItem.GetField("destinationAssetPath");
			object item = array[i];
			string destinationPath = (string)destinationAssetPath.GetValue(item);
			destinationPath = selectedFolderPath + destinationPath.Remove(0, 6);
			destinationAssetPath.SetValue(item, destinationPath);
#endif
		}

		public static object[] ExtractAssetsFromPackage(string path, out string packageIconPath, out bool allowReInstall)
		{
#if UNITY_4_6
			AssetsItem[] array = ImportPackageStep1(path, out packageIconPath);
			allowReInstall = false;
			return array;
#else
			Type packageUtility = typeof(MenuItem).Assembly.GetType("UnityEditor.PackageUtility");

			MethodInfo extractAndPrepareAssetList = packageUtility.GetMethod("ExtractAndPrepareAssetList");
			ExtractAndPrepareAssetListDel extractAndPrepareAssetListDel = (ExtractAndPrepareAssetListDel)Delegate.CreateDelegate(
				typeof(ExtractAndPrepareAssetListDel),
				null,
				extractAndPrepareAssetList);

			object[] array = extractAndPrepareAssetListDel(path, out packageIconPath, out allowReInstall);
			return array;
#endif
		}

		public static void ShowImportPackageWindow(string path, object[] array, string packageIconPath, bool allowReInstall)
		{
#if UNITY_4_6
			ShowImportPackageMethodInfo.Invoke(null, new object[] { path, array, packageIconPath });
#else
			ShowImportPackageMethodInfo.Invoke(null, new object[] { path, array, packageIconPath, allowReInstall });
#endif
		}



		public static void ImportPackageSilently(object[] assetsItems)
		{
#if UNITY_4_6
			ImportPackageStep2MethodInfo.Invoke(null, new object[] { assetsItems, false });
#else
			Type packageUtility = typeof(MenuItem).Assembly.GetType("UnityEditor.PackageUtility");
			MethodInfo importPackageAssets = packageUtility.GetMethod("ImportPackageAssets");
			importPackageAssets.Invoke(null, new object[] { assetsItems, false });
#endif
		}
	}
}