#if HAS_TEST_FRAMEWORK
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace CodeStage.PackageToFolder.Tests
{
	public class ImportPackageToFolderTests
	{
		private const string TestAssetName = "TestDummyAsset.txt";
		private const string TestAssetContent = "This is a test asset for Package2Folder testing";
		private const string TestFolderName = "Package2FolderTest";
		private const string ImportTargetFolder = "ImportedAssets";
		
		private string testAssetPath;
		private string testFolderPath;
		private string importTargetPath;
		private string tempPackagePath;

		[SetUp]
		public void SetUp()
		{
			testFolderPath = Path.Combine("Assets", TestFolderName);
			testAssetPath = Path.Combine(testFolderPath, TestAssetName);
			importTargetPath = Path.Combine("Assets", ImportTargetFolder);
			
			CleanupTestArtifacts();
		}

		[TearDown]
		public void TearDown()
		{
			CleanupTestArtifacts();
		}

		[UnityTest]
		public IEnumerator TestSilentImportPackageToFolder()
		{
			yield return CreateTestAsset();
			yield return ExportAssetAsPackage();
			yield return DeleteOriginalAsset();
			yield return ImportPackageToTargetFolder();
			yield return ValidateImport();
			
			CleanupTempPackage();
		}

		private IEnumerator CreateTestAsset()
		{
			if (!AssetDatabase.IsValidFolder(testFolderPath))
			{
				AssetDatabase.CreateFolder("Assets", TestFolderName);
			}
			
			File.WriteAllText(testAssetPath, TestAssetContent);
			AssetDatabase.Refresh();
			yield return null;
			
			Assert.IsTrue(File.Exists(testAssetPath), "Test asset was not created");
			Assert.IsTrue(File.Exists(testAssetPath + ".meta"), "Test asset meta file was not created");
		}

		private IEnumerator ExportAssetAsPackage()
		{
			string tempDir = Path.GetTempPath();
			string packageFileName = $"TestPackage_{System.DateTime.Now.Ticks}.unitypackage";
			tempPackagePath = Path.Combine(tempDir, packageFileName);
			
			AssetDatabase.ExportPackage(testAssetPath, tempPackagePath, ExportPackageOptions.IncludeDependencies);
			yield return null;
			
			Assert.IsTrue(File.Exists(tempPackagePath), $"Package was not exported to {tempPackagePath}");
		}

		private IEnumerator DeleteOriginalAsset()
		{
			AssetDatabase.DeleteAsset(testFolderPath);
			AssetDatabase.Refresh();
			yield return null;
			
			Assert.IsFalse(File.Exists(testAssetPath), "Original test asset was not deleted");
			Assert.IsFalse(AssetDatabase.IsValidFolder(testFolderPath), "Original test folder was not deleted");
		}

		private IEnumerator ImportPackageToTargetFolder()
		{
			if (!AssetDatabase.IsValidFolder(importTargetPath))
			{
				AssetDatabase.CreateFolder("Assets", ImportTargetFolder);
			}
			
			Package2Folder.ImportPackageToFolder(tempPackagePath, importTargetPath, false);
			
			AssetDatabase.Refresh();
			yield return null;
			yield return null;
		}

		private IEnumerator ValidateImport()
		{
			string expectedImportedAssetPath = Path.Combine(importTargetPath, TestFolderName, TestAssetName);
			
			bool assetExists = File.Exists(expectedImportedAssetPath);
			bool metaExists = File.Exists(expectedImportedAssetPath + ".meta");
			
			if (!assetExists)
			{
				string[] foundAssets = AssetDatabase.FindAssets($"t:TextAsset {Path.GetFileNameWithoutExtension(TestAssetName)}");
				foreach (string guid in foundAssets)
				{
					string assetPath = AssetDatabase.GUIDToAssetPath(guid);
					
					if (File.Exists(assetPath))
					{
						string content = File.ReadAllText(assetPath);
						if (content == TestAssetContent)
						{
							Assert.Fail($"Asset was imported to '{assetPath}' instead of expected location '{expectedImportedAssetPath}'. " +
								"This suggests the path manipulation logic needs to be updated for this Unity version.");
						}
					}
				}
			}
			
			Assert.IsTrue(assetExists, 
				$"Asset was not imported to expected location: {expectedImportedAssetPath}. Check if Package2Folder path manipulation is working correctly.");
			Assert.IsTrue(metaExists, 
				$"Asset meta file was not imported: {expectedImportedAssetPath}.meta");
			
			string importedContent = File.ReadAllText(expectedImportedAssetPath);
			Assert.AreEqual(TestAssetContent, importedContent, "Imported asset content does not match original");
			
			var importedAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(expectedImportedAssetPath);
			Assert.IsNotNull(importedAsset, "Imported asset is not recognized by Unity AssetDatabase");
			
			yield return null;
		}

		private void CleanupTestArtifacts()
		{
			if (AssetDatabase.IsValidFolder(testFolderPath))
			{
				AssetDatabase.DeleteAsset(testFolderPath);
			}
			
			if (AssetDatabase.IsValidFolder(importTargetPath))
			{
				AssetDatabase.DeleteAsset(importTargetPath);
			}
			
			AssetDatabase.Refresh();
		}

		private void CleanupTempPackage()
		{
			if (!string.IsNullOrEmpty(tempPackagePath) && File.Exists(tempPackagePath))
			{
				try
				{
					File.Delete(tempPackagePath);
				}
				catch (System.Exception e)
				{
					Debug.LogWarning($"Could not delete temp package file: {e.Message}");
				}
			}
		}
	}
}
#endif