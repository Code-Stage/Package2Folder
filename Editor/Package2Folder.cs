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
			if (windows == null || windows.Length == 0)
			{
				// No import windows left: any not-yet-consumed explicit-import
				// state belongs to windows that never got a companion, and kept
				// dismiss marks could block a companion for a recycled window ID.
				ClearPendingImportState();
				Package2FolderCompanion.ClearDismissedWindows();
				return;
			}

			// Drop bookkeeping for registered windows that closed before a companion
			// consumed it, so a recycled instance ID can't inherit another window's state.
			if (HasPendingImportState)
			{
				var liveIds = new HashSet<int>();
				foreach (var window in windows)
				{
					liveIds.Add(window.GetInstanceID());
				}
				PruneStalePendingImportState(liveIds);
			}

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
		/// <param name="interactive">If true - imports using standard import window, otherwise does this silently.
		/// The companion window will not auto-apply the configured default folder to an import window
		/// opened this way, since the folder was chosen explicitly.</param>
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

			// Captured before re-pathing so the companion can restore the package's
			// true original paths instead of the already-prefixed ones.
			string[] pristinePaths = null;
			if (interactive)
				pristinePaths = GetItemDestinationPaths(assetsItems);

			foreach (object item in assetsItems)
			{
				ChangeAssetItemPath(item, selectedFolderPath);
			}

			if (interactive)
			{
				var knownWindowIds = GetPackageImportWindowIds();
#if CS_P2F_NEW_ARGUMENT_2
				ShowImportPackageWindow(packagePath, assetsItems, packageIconPath, assetOrigin);
#else
				ShowImportPackageWindow(packagePath, assetsItems, packageIconPath, allowReInstall);
#endif
				RegisterNewImportWindows(knownWindowIds, pristinePaths);
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
		// Explicit-import bookkeeping (default folder suppression)
		///////////////////////////////////////////////////////////////

		// State is keyed by PackageImport window instance ID so that concurrent
		// or aborted imports can't leak suppression onto unrelated windows.
		private static readonly HashSet<int> autoApplySuppressedWindows = new HashSet<int>();
		private static readonly Dictionary<int, string[]> pendingPristinePaths = new Dictionary<int, string[]>();

		/// <summary>
		/// Marks an import window as opened through an explicit ImportPackageToFolder call:
		/// the companion must not auto-apply the default folder to it, and should use
		/// <paramref name="pristinePaths"/> (captured before re-pathing) as the restore baseline.
		/// </summary>
		internal static void RegisterExplicitImportWindow(int windowInstanceId, string[] pristinePaths)
		{
			autoApplySuppressedWindows.Add(windowInstanceId);
			if (pristinePaths != null)
				pendingPristinePaths[windowInstanceId] = pristinePaths;
		}

		/// <summary>
		/// Returns true exactly once per window registered via RegisterExplicitImportWindow.
		/// </summary>
		internal static bool ConsumeAutoApplySuppression(int windowInstanceId)
		{
			return autoApplySuppressedWindows.Remove(windowInstanceId);
		}

		/// <summary>
		/// Hands out the pristine destination paths captured for the window, at most once.
		/// </summary>
		internal static bool TryTakePristinePaths(int windowInstanceId, out string[] pristinePaths)
		{
			if (pendingPristinePaths.TryGetValue(windowInstanceId, out pristinePaths))
			{
				pendingPristinePaths.Remove(windowInstanceId);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Drops all pending explicit-import state; called when no PackageImport windows exist.
		/// </summary>
		internal static void ClearPendingImportState()
		{
			autoApplySuppressedWindows.Clear();
			pendingPristinePaths.Clear();
		}

		internal static bool HasPendingImportState
		{
			get { return autoApplySuppressedWindows.Count > 0 || pendingPristinePaths.Count > 0; }
		}

		/// <summary>
		/// Removes registrations for windows no longer alive, so a later window with a
		/// recycled instance ID cannot pick up another window's suppression or paths.
		/// </summary>
		internal static void PruneStalePendingImportState(HashSet<int> liveWindowIds)
		{
			autoApplySuppressedWindows.RemoveWhere(id => !liveWindowIds.Contains(id));

			if (pendingPristinePaths.Count == 0) return;

			List<int> staleIds = null;
			foreach (var pending in pendingPristinePaths)
			{
				if (liveWindowIds.Contains(pending.Key)) continue;

				if (staleIds == null)
					staleIds = new List<int>();
				staleIds.Add(pending.Key);
			}

			if (staleIds == null) return;

			foreach (var id in staleIds)
			{
				pendingPristinePaths.Remove(id);
			}
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

			return GetItemDestinationPaths(items);
		}

		private static string[] GetItemDestinationPaths(object[] items)
		{
			var paths = new string[items.Length];
			for (int i = 0; i < items.Length; i++)
			{
				paths[i] = (string)DestinationAssetPathFieldInfo.GetValue(items[i]);
			}
			return paths;
		}

		private static void RestoreItemPaths(object[] items, string[] originalPaths)
		{
			for (int i = 0; i < items.Length && i < originalPaths.Length; i++)
			{
				DestinationAssetPathFieldInfo.SetValue(items[i], originalPaths[i]);
			}
		}

		private static HashSet<int> GetPackageImportWindowIds()
		{
			var ids = new HashSet<int>();
			var windows = Resources.FindObjectsOfTypeAll(PackageImportType);
			if (windows == null) return ids;

			foreach (var window in windows)
			{
				ids.Add(window.GetInstanceID());
			}
			return ids;
		}

		private static void RegisterNewImportWindows(HashSet<int> knownWindowIds, string[] pristinePaths)
		{
			var windows = Resources.FindObjectsOfTypeAll(PackageImportType);
			if (windows == null) return;

			foreach (var window in windows)
			{
				var id = window.GetInstanceID();
				if (!knownWindowIds.Contains(id))
					RegisterExplicitImportWindow(id, pristinePaths);
			}
		}

		/// <summary>
		/// Resets all import item destinations to <paramref name="originalPaths"/> and rebuilds the window's tree.
		/// </summary>
		internal static void RestoreImportWindowPaths(EditorWindow importWindow, string[] originalPaths)
		{
			var items = GetImportPackageItems(importWindow);
			if (items == null || originalPaths == null) return;

			RestoreItemPaths(items, originalPaths);

			TreeFieldInfo.SetValue(importWindow, null);
			importWindow.Repaint();
		}

		internal static void SetImportWindowFolder(EditorWindow importWindow, string selectedFolderPath, string[] originalPaths)
		{
			var items = GetImportPackageItems(importWindow);
			if (items == null) return;

			// Restore original paths first to avoid stacking folder prefixes
			if (originalPaths != null)
				RestoreItemPaths(items, originalPaths);

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

		/// <summary>
		/// Forgets manual dismissals; called when no PackageImport windows exist, since a kept
		/// mark could otherwise block the companion for an unrelated window with a recycled ID.
		/// </summary>
		internal static void ClearDismissedWindows()
		{
			dismissedImportWindows.Clear();
		}

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

			// For explicit ImportPackageToFolder imports the window's current paths already
			// carry the chosen folder, so the restore baseline comes from the pristine
			// paths captured before re-pathing.
			string[] pristinePaths;
			if (Package2Folder.TryTakePristinePaths(id, out pristinePaths))
				companion.originalPaths = pristinePaths;
			else
				companion.CacheOriginalPaths();

			companion.ShowUtility();
			companion.PositionNearImportWindow();
			activeCompanions[id] = companion;

			var suppressAutoApply = Package2Folder.ConsumeAutoApplySuppression(id);
			if (!suppressAutoApply && Package2FolderSettings.HasDefaultFolder)
				companion.ApplyFolder(Package2FolderSettings.DefaultFolder);
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

				if (GUILayout.Button("Restore Original Paths"))
				{
					RestoreOriginalPaths();
				}
			}
		}

		/// <summary>
		/// Redirects all import destinations to <paramref name="folderPath"/> (must be under Assets).
		/// </summary>
		internal void ApplyFolder(string folderPath)
		{
			if (importWindow == null) return;

			selectedFolder = folderPath;
			Package2Folder.SetImportWindowFolder(importWindow, selectedFolder, originalPaths);
			UpdateHeight();
			Repaint();
		}

		/// <summary>
		/// Puts all import destinations back to the paths cached when the companion attached.
		/// </summary>
		private void RestoreOriginalPaths()
		{
			if (importWindow == null) return;

			selectedFolder = null;
			Package2Folder.RestoreImportWindowPaths(importWindow, originalPaths);
			UpdateHeight();
			Repaint();
		}

		private void UpdateHeight()
		{
			var height = string.IsNullOrEmpty(selectedFolder) ? 60 : 92;
			position = new Rect(position.x, position.y, position.width, height);
		}

		private void SelectFolderAndModifyPaths()
		{
			var startFolder = Package2FolderSettings.HasDefaultFolder ? Package2FolderSettings.DefaultFolder : "Assets";
			var absolutePath = EditorUtility.OpenFolderPanel("Select target folder", startFolder, "");
			if (string.IsNullOrEmpty(absolutePath)) return;
			if (importWindow == null) return;

			string relativePath;
			if (!Package2FolderSettings.TryGetProjectRelativeFolder(absolutePath, out relativePath))
			{
				EditorUtility.DisplayDialog("Invalid Folder",
					"Please select a folder inside the Assets directory.", "OK");
				return;
			}

			selectedFolder = relativePath;
			Package2Folder.SetImportWindowFolder(importWindow, selectedFolder, originalPaths);
			UpdateHeight();
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
