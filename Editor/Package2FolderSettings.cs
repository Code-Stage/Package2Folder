using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CodeStage.PackageToFolder
{
	/// <summary>
	/// Per-user Package2Folder settings (EditorPrefs-backed) and their Preferences UI.
	/// </summary>
	internal static class Package2FolderSettings
	{
		/// <summary>
		/// EditorPrefs key holding the default import folder.
		/// </summary>
		internal const string DefaultFolderPrefsKey = "CodeStage.Package2Folder.DefaultFolder";

		private static string invalidFolderMessage;

		/// <summary>
		/// Project-relative folder ("Assets" or below) applied to new import dialogs by default.
		/// Empty string when unset; setting an empty or null value clears it; setting an
		/// invalid path throws ArgumentException. A stored invalid value reads as unset.
		/// </summary>
		internal static string DefaultFolder
		{
			get
			{
				var stored = NormalizeFolderPath(EditorPrefs.GetString(DefaultFolderPrefsKey, string.Empty));
				return IsValidProjectFolder(stored) ? stored : string.Empty;
			}
			set
			{
				var normalized = NormalizeFolderPath(value);
				if (normalized.Length == 0)
				{
					EditorPrefs.DeleteKey(DefaultFolderPrefsKey);
					return;
				}

				if (!IsValidProjectFolder(normalized))
					throw new ArgumentException("Default folder must be 'Assets' or a folder under it", "value");

				EditorPrefs.SetString(DefaultFolderPrefsKey, normalized);
			}
		}

		/// <summary>
		/// True when a valid default folder is configured.
		/// </summary>
		internal static bool HasDefaultFolder
		{
			get { return DefaultFolder.Length > 0; }
		}

		/// <summary>
		/// Converts backslashes to slashes and trims whitespace and trailing slashes; null becomes an empty string.
		/// </summary>
		internal static string NormalizeFolderPath(string rawPath)
		{
			if (string.IsNullOrEmpty(rawPath)) return string.Empty;

			var normalized = rawPath.Replace('\\', '/').Trim();
			while (normalized.EndsWith("/"))
				normalized = normalized.Substring(0, normalized.Length - 1);

			return normalized;
		}

		/// <summary>
		/// True only for "Assets" itself or a path under "Assets/" (case-sensitive).
		/// </summary>
		internal static bool IsValidProjectFolder(string folderPath)
		{
			if (string.IsNullOrEmpty(folderPath)) return false;
			return folderPath == "Assets" || folderPath.StartsWith("Assets/", StringComparison.Ordinal);
		}

		/// <summary>
		/// Converts an absolute path inside the current project's Assets directory
		/// to its "Assets/..." form; returns false for paths outside it.
		/// </summary>
		internal static bool TryGetProjectRelativeFolder(string absolutePath, out string projectRelativePath)
		{
			projectRelativePath = null;
			if (string.IsNullOrEmpty(absolutePath)) return false;

			var normalized = absolutePath.Replace('\\', '/');
			var dataPath = Application.dataPath.Replace('\\', '/');

			if (normalized == dataPath)
			{
				projectRelativePath = "Assets";
				return true;
			}

			if (normalized.StartsWith(dataPath + "/", StringComparison.Ordinal))
			{
				projectRelativePath = "Assets" + normalized.Substring(dataPath.Length);
				return true;
			}

			return false;
		}

		///////////////////////////////////////////////////////////////
		// Preferences UI
		///////////////////////////////////////////////////////////////

		[SettingsProvider]
		private static SettingsProvider CreateSettingsProvider()
		{
			return new SettingsProvider("Preferences/Package2Folder", SettingsScope.User)
			{
				guiHandler = context => DrawPreferencesGUI(),
				keywords = new HashSet<string> { "package", "import", "folder", "default", "Package2Folder" }
			};
		}

		private static void DrawPreferencesGUI()
		{
			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal();

			var current = DefaultFolder;
			var entered = EditorGUILayout.DelayedTextField(new GUIContent("Default import folder",
				"Folder to pre-select in the package import dialog. Use 'Assets' or a folder under it, like Assets/ThirdParty."),
				current);

			var browseClicked = GUILayout.Button("Browse...", GUILayout.Width(70));
			var clearClicked = GUILayout.Button("Clear", GUILayout.Width(50));

			EditorGUILayout.EndHorizontal();

			// Modal dialogs must not open while a layout group is still on the stack.
			if (browseClicked)
			{
				BrowseForDefaultFolder();
			}
			else if (clearClicked)
			{
				DefaultFolder = null;
				invalidFolderMessage = null;
				GUI.FocusControl(null);
			}
			else if (entered != current)
			{
				TrySetDefaultFolder(entered);
			}

			if (!string.IsNullOrEmpty(invalidFolderMessage))
				EditorGUILayout.HelpBox(invalidFolderMessage, MessageType.Warning);

			if (HasDefaultFolder)
				EditorGUILayout.HelpBox("New package import dialogs will target '" + DefaultFolder +
					"'. To undo it for one import, use the Restore Original Paths button in the companion window.",
					MessageType.Info);
		}

		private static void TrySetDefaultFolder(string rawValue)
		{
			var normalized = NormalizeFolderPath(rawValue);
			if (normalized.Length == 0)
			{
				DefaultFolder = null;
				invalidFolderMessage = null;
				return;
			}

			if (!IsValidProjectFolder(normalized))
			{
				invalidFolderMessage = "The folder must be 'Assets' or a path under it, like Assets/ThirdParty.";
				return;
			}

			DefaultFolder = normalized;
			invalidFolderMessage = null;
		}

		private static void BrowseForDefaultFolder()
		{
			var startFolder = HasDefaultFolder ? DefaultFolder : "Assets";
			var absolutePath = EditorUtility.OpenFolderPanel("Select default import folder", startFolder, "");
			if (string.IsNullOrEmpty(absolutePath)) return;

			string relativePath;
			if (TryGetProjectRelativeFolder(absolutePath, out relativePath))
			{
				DefaultFolder = relativePath;
				invalidFolderMessage = null;
				GUI.FocusControl(null);
			}
			else
			{
				invalidFolderMessage = "This folder is outside the project. Pick one inside the Assets directory.";
			}
		}
	}
}
