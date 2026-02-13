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
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

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
					showImportPackageMethodInfo = PackageImportType.GetMethod("ShowImportPackage");
				}

				return showImportPackageMethodInfo;
			}
		}

		private static Type packageImportType;
		private static Type PackageImportType
		{
			get
			{
				if (packageImportType == null)
					packageImportType = typeof(MenuItem).Assembly.GetType("UnityEditor.PackageImport");
				return packageImportType;
			}
		}

		private static FieldInfo importPackageItemsFieldInfo;
		private static FieldInfo ImportPackageItemsFieldInfo
		{
			get
			{
				if (importPackageItemsFieldInfo == null)
					importPackageItemsFieldInfo = PackageImportType.GetField("m_ImportPackageItems", BindingFlags.NonPublic | BindingFlags.Instance);
				return importPackageItemsFieldInfo;
			}
		}

		private static FieldInfo treeFieldInfo;
		private static FieldInfo TreeFieldInfo
		{
			get
			{
				if (treeFieldInfo == null)
					treeFieldInfo = PackageImportType.GetField("m_Tree", BindingFlags.NonPublic | BindingFlags.Instance);
				return treeFieldInfo;
			}
		}

		#endregion reflection stuff

		///////////////////////////////////////////////////////////////
		// PackageImport window watcher
		///////////////////////////////////////////////////////////////

		[InitializeOnLoadMethod]
		private static void SetupPackageImportWatcher()
		{
			EditorApplication.update -= WatchForPackageImportWindows;
			EditorApplication.update += WatchForPackageImportWindows;
		}

		private static double nextWatchTime;

		private static void WatchForPackageImportWindows()
		{
			if (EditorApplication.timeSinceStartup < nextWatchTime) return;
			nextWatchTime = EditorApplication.timeSinceStartup + 0.25;

			var windows = Resources.FindObjectsOfTypeAll(PackageImportType);
			if (windows == null || windows.Length == 0) return;

			foreach (var window in windows)
			{
				var editorWindow = window as EditorWindow;
				if (editorWindow != null)
					Package2FolderCompanion.ShowForImportWindow(editorWindow);
			}
		}

		///////////////////////////////////////////////////////////////
		// Unity Editor menus integration
		///////////////////////////////////////////////////////////////

		[MenuItem("Assets/Import Package/Here...", true)]
		private static bool IsImportToFolderCheck()
		{
			var selectedFolderPath = GetSelectedFolderPath();
			return !string.IsNullOrEmpty(selectedFolderPath);
		}

		[MenuItem("Assets/Import Package/Here...", false)]
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

		public static void ChangeAssetItemPath(object assetItem, string selectedFolderPath)
		{
			if (string.IsNullOrEmpty(selectedFolderPath) || !selectedFolderPath.StartsWith("Assets"))
				throw new ArgumentException("selectedFolderPath must start with 'Assets'", "selectedFolderPath");

			string destinationPath = (string)DestinationAssetPathFieldInfo.GetValue(assetItem);
			if (destinationPath.StartsWith("Packages/")) return;

			int firstSlashIndex = destinationPath.IndexOf('/');
			if (firstSlashIndex >= 0)
			{
				string relativePath = destinationPath.Substring(firstSlashIndex);
				destinationPath = selectedFolderPath + relativePath;
			}
			else
			{
				destinationPath = selectedFolderPath + "/" + destinationPath;
			}

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
		// PackageImport window helpers
		///////////////////////////////////////////////////////////////

		internal static object[] GetImportPackageItems(EditorWindow importWindow)
		{
			return ImportPackageItemsFieldInfo.GetValue(importWindow) as object[];
		}

		internal static string[] GetImportItemPaths(EditorWindow importWindow)
		{
			var items = GetImportPackageItems(importWindow);
			if (items == null) return null;

			var paths = new string[items.Length];
			for (int i = 0; i < items.Length; i++)
			{
				paths[i] = (string)DestinationAssetPathFieldInfo.GetValue(items[i]);
			}
			return paths;
		}

		internal static void SetImportWindowFolder(EditorWindow importWindow, string selectedFolderPath, string[] originalPaths)
		{
			var items = GetImportPackageItems(importWindow);
			if (items == null) return;

			// Restore original paths first to avoid stacking folder prefixes
			if (originalPaths != null)
			{
				for (int i = 0; i < items.Length && i < originalPaths.Length; i++)
				{
					DestinationAssetPathFieldInfo.SetValue(items[i], originalPaths[i]);
				}
			}

			// Apply new folder
			foreach (var item in items)
			{
				ChangeAssetItemPath(item, selectedFolderPath);
			}

			// Reset tree view to force rebuild
			TreeFieldInfo.SetValue(importWindow, null);
			importWindow.Repaint();
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

	internal class Package2FolderCompanion : EditorWindow
	{
		private static readonly Dictionary<int, Package2FolderCompanion> activeCompanions = new Dictionary<int, Package2FolderCompanion>();
		private static readonly HashSet<int> dismissedImportWindows = new HashSet<int>();

		[SerializeField] private EditorWindow importWindow;
		[SerializeField] private string[] originalPaths;
		[SerializeField] private string selectedFolder;

		internal static void ShowForImportWindow(EditorWindow importWindow)
		{
			var id = importWindow.GetInstanceID();

			if (dismissedImportWindows.Contains(id))
				return;

			ClearStaleEntries();

			Package2FolderCompanion existing;
			if (activeCompanions.TryGetValue(id, out existing) && existing != null)
				return;

			var companion = CreateInstance<Package2FolderCompanion>();
			companion.importWindow = importWindow;
			companion.titleContent = new GUIContent("Package2Folder");
			companion.CacheOriginalPaths();
			companion.ShowUtility();
			companion.PositionNearImportWindow();
			activeCompanions[id] = companion;
		}

		private static void ClearStaleEntries()
		{
			var staleKeys = new List<int>();
			foreach (var kvp in activeCompanions)
			{
				if (kvp.Value == null || kvp.Value.importWindow == null)
					staleKeys.Add(kvp.Key);
			}
			foreach (var key in staleKeys)
			{
				activeCompanions.Remove(key);
				dismissedImportWindows.Remove(key);
			}
		}

		private void CacheOriginalPaths()
		{
			originalPaths = Package2Folder.GetImportItemPaths(importWindow);
		}

		private void PositionNearImportWindow()
		{
			if (importWindow == null) return;

			var importPos = importWindow.position;
			position = new Rect(
				importPos.x + importPos.width + 10,
				importPos.y,
				220,
				60
			);
		}

		private void OnEnable()
		{
			if (importWindow != null)
				activeCompanions[importWindow.GetInstanceID()] = this;
		}

		private void Update()
		{
			if (importWindow == null)
			{
				Close();
			}
		}

		private void OnGUI()
		{
			if (GUILayout.Button("Import to Folder...", GUILayout.Height(30)))
			{
				SelectFolderAndModifyPaths();
			}

			if (!string.IsNullOrEmpty(selectedFolder))
			{
				EditorGUILayout.LabelField("Target: " + selectedFolder, EditorStyles.miniLabel);
			}
		}

		private void SelectFolderAndModifyPaths()
		{
			var absolutePath = EditorUtility.OpenFolderPanel("Select target folder", "Assets", "");
			if (string.IsNullOrEmpty(absolutePath)) return;
			if (importWindow == null) return;

			absolutePath = absolutePath.Replace('\\', '/');
			var dataPath = Application.dataPath.Replace('\\', '/');

			string relativePath;
			if (absolutePath == dataPath)
			{
				relativePath = "Assets";
			}
			else if (absolutePath.StartsWith(dataPath + "/"))
			{
				relativePath = "Assets" + absolutePath.Substring(dataPath.Length);
			}
			else
			{
				EditorUtility.DisplayDialog("Invalid Folder",
					"Please select a folder inside the Assets directory.", "OK");
				return;
			}

			selectedFolder = relativePath;
			Package2Folder.SetImportWindowFolder(importWindow, selectedFolder, originalPaths);
			Repaint();
		}

		private void OnDestroy()
		{
			if (importWindow != null)
			{
				var id = importWindow.GetInstanceID();
				activeCompanions.Remove(id);
				// Import window still alive means user dismissed companion manually
				dismissedImportWindows.Add(id);
			}
		}
	}
}
