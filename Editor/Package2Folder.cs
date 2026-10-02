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
#if UNITY_6000_4_OR_NEWER
using ImportWindowId = UnityEngine.EntityId;
#else
using ImportWindowId = System.Int32;
#endif

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
		private static bool nativeImportPending;
		private static Type PackageUtilityType
		{
			get
			{
				if (packageUtilityType == null)
					packageUtilityType = typeof(MenuItem).Assembly.GetType("UnityEditor.AssetPackage.Utility") ??
						typeof(MenuItem).Assembly.GetType("UnityEditor.PackageUtility");
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
					var importPackageItem = typeof(MenuItem).Assembly.GetType("UnityEditor.AssetPackage.ImportPackageItem") ??
						typeof(MenuItem).Assembly.GetType("UnityEditor.ImportPackageItem");
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
			if (nativeImportPending) return;
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
		/// <remarks>
		/// On Unity 6.5+, native preparation briefly opens an import window even for non-interactive imports.
		/// These imports require a graphics device and complete asynchronously after this method returns.
		/// </remarks>
		/// <param name="packagePath">Native path to the package.</param>
		/// <param name="selectedFolderPath">Path to the target folder where you wish to import package into.
		/// Relative to the project folder (should start with 'Assets')</param>
		/// <param name="interactive">If true - imports using standard import window, otherwise does this silently.</param>
		/// <param name="assetOrigin">An optional UnityEditor.AssetOrigin object which Unity from version 2023+ uses internally to store the source of the imported asset inside the meta file.</param>
		public static void ImportPackageToFolder(string packagePath, string selectedFolderPath, bool interactive, object assetOrigin = null)
		{
			selectedFolderPath = ValidateTargetFolder(selectedFolderPath);
			if (nativeImportPending || Resources.FindObjectsOfTypeAll(PackageImportType).Length != 0)
				throw new InvalidOperationException("Finish or cancel the current package import before starting another.");

			if (PackageUtilityType.GetMethod("ExtractAndPrepareAssetList") == null)
			{
				ImportUsingNativePreparation(packagePath, selectedFolderPath, interactive, assetOrigin);
				return;
			}

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
			selectedFolderPath = ValidateTargetFolder(selectedFolderPath);

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
			if (ShowImportPackageMethodInfo.GetParameters()[1].ParameterType == typeof(IntPtr))
			{
				var wizard = GetImportWizard();
				var origin = assetOrigin ?? Activator.CreateInstance(typeof(MenuItem).Assembly.GetType("UnityEditor.AssetOrigin"));
				var startImport = wizard.GetType().GetMethod("StartImport");
				var arguments = new List<object> { path, array, packageIconPath, origin, GetExtractedPackagePath(array) };
				if (startImport.GetParameters().Length == 6)
				{
					var preparedPath = (string)wizard.GetType().GetField("m_PackagePath", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(wizard);
					if (string.IsNullOrEmpty(preparedPath) || Path.GetFullPath(preparedPath) != Path.GetFullPath(path))
						throw new InvalidOperationException("Prepare this package with Unity before opening its import window.");
					arguments.Add(wizard.GetType().GetProperty("assetPackageInfo").GetValue(wizard));
				}
				startImport.Invoke(wizard, arguments.ToArray());
				return;
			}
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
			if (ImportPackageAssetsMethodInfo.GetParameters().Length == 4)
			{
				var extractedPath = GetExtractedPackagePath(assetsItems);
				if (assetOrigin != null)
					ImportPackageAssetsWithOriginMethodInfo.Invoke(null, new[] { assetOrigin, assetsItems, extractedPath, false });
				else
					ImportPackageAssetsMethodInfo.Invoke(null, new object[] { packageName, assetsItems, extractedPath, false });
				return;
			}
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

		private static string ValidateTargetFolder(string folder)
		{
			var normalized = folder?.Replace('\\', '/').TrimEnd('/');
			if (string.IsNullOrEmpty(normalized) ||
				(normalized != "Assets" && !normalized.StartsWith("Assets/", StringComparison.Ordinal)) ||
				Array.IndexOf(normalized.Split('/'), "..") >= 0)
				throw new ArgumentException("The target folder must be inside Assets.", nameof(folder));
			return normalized;
		}

		private static object GetImportWizard()
		{
			var type = typeof(MenuItem).Assembly.GetType("UnityEditor.PackageImportWizard");
			return type.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
		}

		private static string GetExtractedPackagePath(object[] items)
		{
			if (items == null || items.Length == 0)
				throw new ArgumentException("At least one prepared import item is required.", nameof(items));
			var source = (string)items[0].GetType().GetField("sourceFolder").GetValue(items[0]);
			return Path.GetDirectoryName(source);
		}

		private static void ImportUsingNativePreparation(string packagePath, string folder, bool interactive, object origin)
		{
			if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
				throw new NotSupportedException("Native package preparation requires a graphics device. Run Unity without -nographics.");
			packagePath = Path.GetFullPath(packagePath);
			if (!File.Exists(packagePath)) throw new FileNotFoundException("Package not found.", packagePath);
			nativeImportPending = true;
			EditorApplication.update += RedirectPreparedImport;
			AssetDatabase.importPackageFailed += ImportFailed;
			AssetDatabase.importPackageCancelled += ImportCancelled;
			AssetDatabase.importPackageCompleted += ImportCancelled;
			try { AssetDatabase.ImportPackage(packagePath, true); }
			catch { StopWaiting(); throw; }

			void StopWaiting()
			{
				nativeImportPending = false;
				EditorApplication.update -= RedirectPreparedImport;
				AssetDatabase.importPackageFailed -= ImportFailed;
				AssetDatabase.importPackageCancelled -= ImportCancelled;
				AssetDatabase.importPackageCompleted -= ImportCancelled;
			}

			void ImportFailed(string name, string error) { ImportCancelled(name); }
			void ImportCancelled(string name)
			{
				if (name == Path.GetFileNameWithoutExtension(packagePath)) StopWaiting();
			}

			void RedirectPreparedImport()
			{
				EditorWindow ownedWindow = null;
				try
				{
					var wizard = GetImportWizard();
					var type = wizard.GetType();
					const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
					var path = (string)type.GetField("m_PackagePath", fields).GetValue(wizard);
					var window = type.GetField("m_ImportWindow", fields).GetValue(wizard) as EditorWindow;
					if (window == null || string.IsNullOrEmpty(path) || Path.GetFullPath(path) != packagePath)
						return;
					ownedWindow = window;

					var items = (object[])type.GetField("m_InitialImportItems", fields).GetValue(wizard);
					if (items == null) return;
					StopWaiting();
					if (interactive) Package2FolderCompanion.ShowForImportWindow(window, folder);
					foreach (var item in items) ChangeAssetItemPath(item, folder);
					if (interactive)
					{
						if (origin != null) type.GetField("m_AssetOrigin", fields).SetValue(wizard, origin);
						TreeFieldInfo.SetValue(window, null);
						window.Repaint();
					}
					else
					{
						ImportPackageSilently(Path.GetFileNameWithoutExtension(packagePath), items, origin);
						window.Close();
						type.GetMethod("ClearImportData", fields).Invoke(wizard, null);
					}
				}
				catch (Exception exception)
				{
					StopWaiting();
					if (ownedWindow != null) ownedWindow.Close();
					Debug.LogException(exception);
				}
			}
		}

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
		private static readonly Dictionary<ImportWindowId, Package2FolderCompanion> activeCompanions = new Dictionary<ImportWindowId, Package2FolderCompanion>();
		private static readonly HashSet<ImportWindowId> dismissedImportWindows = new HashSet<ImportWindowId>();

		[SerializeField] private EditorWindow importWindow;
		[SerializeField] private string[] originalPaths;
		[SerializeField] private string selectedFolder;

		internal static void ShowForImportWindow(EditorWindow importWindow, string selectedFolder = null)
		{
			var id = GetImportWindowId(importWindow);

			if (dismissedImportWindows.Contains(id))
				return;

			ClearStaleEntries();

			Package2FolderCompanion existing;
			if (activeCompanions.TryGetValue(id, out existing) && existing != null)
				return;

			var companion = CreateInstance<Package2FolderCompanion>();
			companion.importWindow = importWindow;
			companion.selectedFolder = selectedFolder;
			companion.titleContent = new GUIContent("Package2Folder");
			companion.CacheOriginalPaths();
			companion.ShowUtility();
			companion.PositionNearImportWindow();
			activeCompanions[id] = companion;
		}

		private static void ClearStaleEntries()
		{
			var staleKeys = new List<ImportWindowId>();
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

		private static ImportWindowId GetImportWindowId(EditorWindow window)
		{
#if UNITY_6000_4_OR_NEWER
			return window.GetEntityId();
#else
			return window.GetInstanceID();
#endif
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
				activeCompanions[GetImportWindowId(importWindow)] = this;
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
				var id = GetImportWindowId(importWindow);
				activeCompanions.Remove(id);
				// Import window still alive means user dismissed companion manually
				dismissedImportWindows.Add(id);
			}
		}
	}
}
