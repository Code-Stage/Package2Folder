#if HAS_TEST_FRAMEWORK
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

namespace CodeStage.PackageToFolder.Tests
{
	public class DefaultFolderSettingsTests
	{
		private bool hadStoredValue;
		private string storedValue;

		[SetUp]
		public void SetUp()
		{
			hadStoredValue = EditorPrefs.HasKey(Package2FolderSettings.DefaultFolderPrefsKey);
			if (hadStoredValue)
				storedValue = EditorPrefs.GetString(Package2FolderSettings.DefaultFolderPrefsKey);

			EditorPrefs.DeleteKey(Package2FolderSettings.DefaultFolderPrefsKey);
		}

		[TearDown]
		public void TearDown()
		{
			if (hadStoredValue)
				EditorPrefs.SetString(Package2FolderSettings.DefaultFolderPrefsKey, storedValue);
			else
				EditorPrefs.DeleteKey(Package2FolderSettings.DefaultFolderPrefsKey);

			Package2Folder.ClearPendingImportState();
		}

		[Test]
		public void NormalizeFolderPath_ConvertsBackslashesToForwardSlashes()
		{
			Assert.AreEqual("Assets/Sub/Deep", Package2FolderSettings.NormalizeFolderPath("Assets\\Sub\\Deep"));
		}

		[Test]
		public void NormalizeFolderPath_TrimsWhitespaceAndTrailingSlashes()
		{
			Assert.AreEqual("Assets/Sub", Package2FolderSettings.NormalizeFolderPath("  Assets/Sub/  "));
			Assert.AreEqual("Assets", Package2FolderSettings.NormalizeFolderPath("Assets/"));
		}

		[Test]
		public void NormalizeFolderPath_NullOrEmpty_ReturnsEmpty()
		{
			Assert.AreEqual(string.Empty, Package2FolderSettings.NormalizeFolderPath(null));
			Assert.AreEqual(string.Empty, Package2FolderSettings.NormalizeFolderPath(""));
			Assert.AreEqual(string.Empty, Package2FolderSettings.NormalizeFolderPath("   "));
		}

		[TestCase("Assets", true)]
		[TestCase("Assets/ThirdParty", true)]
		[TestCase("Assets/Third Party/Sub", true)]
		[TestCase("", false)]
		[TestCase(null, false)]
		[TestCase("Packages/Foo", false)]
		[TestCase("AssetsFoo", false)]
		[TestCase("assets/foo", false)]
		public void IsValidProjectFolder_ChecksAssetsPrefix(string path, bool expected)
		{
			Assert.AreEqual(expected, Package2FolderSettings.IsValidProjectFolder(path));
		}

		[Test]
		public void DefaultFolder_SetAndGet_ReturnsNormalizedValue()
		{
			Package2FolderSettings.DefaultFolder = "Assets\\External\\";

			Assert.AreEqual("Assets/External", Package2FolderSettings.DefaultFolder);
			Assert.IsTrue(Package2FolderSettings.HasDefaultFolder);
			Assert.AreEqual("Assets/External", EditorPrefs.GetString(Package2FolderSettings.DefaultFolderPrefsKey));
		}

		[Test]
		public void DefaultFolder_SetEmpty_ClearsStoredValue()
		{
			Package2FolderSettings.DefaultFolder = "Assets/External";
			Package2FolderSettings.DefaultFolder = "";

			Assert.IsFalse(EditorPrefs.HasKey(Package2FolderSettings.DefaultFolderPrefsKey));
			Assert.IsFalse(Package2FolderSettings.HasDefaultFolder);
			Assert.AreEqual(string.Empty, Package2FolderSettings.DefaultFolder);
		}

		[Test]
		public void DefaultFolder_SetNull_ClearsStoredValue()
		{
			Package2FolderSettings.DefaultFolder = "Assets/External";
			Package2FolderSettings.DefaultFolder = null;

			Assert.IsFalse(EditorPrefs.HasKey(Package2FolderSettings.DefaultFolderPrefsKey));
			Assert.IsFalse(Package2FolderSettings.HasDefaultFolder);
		}

		[Test]
		public void DefaultFolder_SetInvalid_ThrowsAndKeepsPreviousValue()
		{
			Package2FolderSettings.DefaultFolder = "Assets/External";

			Assert.Throws<ArgumentException>(() => Package2FolderSettings.DefaultFolder = "Packages/Nope");
			Assert.AreEqual("Assets/External", Package2FolderSettings.DefaultFolder);
		}

		[Test]
		public void DefaultFolder_StoredInvalidValue_TreatedAsUnset()
		{
			EditorPrefs.SetString(Package2FolderSettings.DefaultFolderPrefsKey, "NotAssetsRelative");

			Assert.AreEqual(string.Empty, Package2FolderSettings.DefaultFolder);
			Assert.IsFalse(Package2FolderSettings.HasDefaultFolder);
		}

		[Test]
		public void AutoApplySuppression_ConsumedExactlyOncePerWindow()
		{
			Package2Folder.RegisterExplicitImportWindow(101, null);

			Assert.IsTrue(Package2Folder.ConsumeAutoApplySuppression(101));
			Assert.IsFalse(Package2Folder.ConsumeAutoApplySuppression(101));
		}

		[Test]
		public void AutoApplySuppression_ScopedToWindowId()
		{
			Package2Folder.RegisterExplicitImportWindow(201, null);

			Assert.IsFalse(Package2Folder.ConsumeAutoApplySuppression(202));
			Assert.IsTrue(Package2Folder.ConsumeAutoApplySuppression(201));
		}

		[Test]
		public void AutoApplySuppression_FalseWhenNotRequested()
		{
			Assert.IsFalse(Package2Folder.ConsumeAutoApplySuppression(999));
		}

		[Test]
		public void PristinePaths_TakenExactlyOnce()
		{
			var paths = new[] { "Assets/Sub/File.txt" };
			Package2Folder.RegisterExplicitImportWindow(301, paths);

			string[] taken;
			Assert.IsTrue(Package2Folder.TryTakePristinePaths(301, out taken));
			CollectionAssert.AreEqual(paths, taken);
			Assert.IsFalse(Package2Folder.TryTakePristinePaths(301, out taken));
		}

		[Test]
		public void PristinePaths_AbsentForUnknownWindow()
		{
			string[] taken;
			Assert.IsFalse(Package2Folder.TryTakePristinePaths(302, out taken));
			Assert.IsNull(taken);
		}

		[Test]
		public void ClearPendingImportState_DropsSuppressionAndPaths()
		{
			Package2Folder.RegisterExplicitImportWindow(401, new[] { "Assets/File.txt" });

			Package2Folder.ClearPendingImportState();

			string[] taken;
			Assert.IsFalse(Package2Folder.ConsumeAutoApplySuppression(401));
			Assert.IsFalse(Package2Folder.TryTakePristinePaths(401, out taken));
		}

		[Test]
		public void PruneStalePendingImportState_DropsOnlyClosedWindows()
		{
			Package2Folder.RegisterExplicitImportWindow(501, new[] { "Assets/A.txt" });
			Package2Folder.RegisterExplicitImportWindow(502, new[] { "Assets/B.txt" });

			Package2Folder.PruneStalePendingImportState(new HashSet<int> { 502 });

			string[] taken;
			Assert.IsFalse(Package2Folder.ConsumeAutoApplySuppression(501));
			Assert.IsFalse(Package2Folder.TryTakePristinePaths(501, out taken));
			Assert.IsTrue(Package2Folder.ConsumeAutoApplySuppression(502));
			Assert.IsTrue(Package2Folder.TryTakePristinePaths(502, out taken));
			CollectionAssert.AreEqual(new[] { "Assets/B.txt" }, taken);
		}
	}
}
#endif
