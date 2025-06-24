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
			// Set up paths
			testFolderPath = Path.Combine("Assets", TestFolderName);
			testAssetPath = Path.Combine(testFolderPath, TestAssetName);
			importTargetPath = Path.Combine("Assets", ImportTargetFolder);
			
			// Clean up any existing test artifacts
			CleanupTestArtifacts();
		}

		[TearDown]
		public void TearDown()
		{
			// Clean up test artifacts
			CleanupTestArtifacts();
		}

		[UnityTest]
		public IEnumerator TestSilentImportPackageToFolder()
		{
			// Step 1: Create dummy asset and let Unity import it
			yield return CreateTestAsset();
			
			// Step 2: Export asset as package to temporary directory
			yield return ExportAssetAsPackage();
			
			// Step 3: Delete original asset and let Unity catch up
			yield return DeleteOriginalAsset();
			
			// Step 4: Silently import package into different folder
			yield return ImportPackageToTargetFolder();
			
			// Step 5: Validate import was successful
			yield return ValidateImport();
			
			// Step 6: Clean up temporary package file
			CleanupTempPackage();
		}

		private IEnumerator CreateTestAsset()
		{
			// Create test folder
			if (!AssetDatabase.IsValidFolder(testFolderPath))
			{
				AssetDatabase.CreateFolder("Assets", TestFolderName);
			}
			
			// Create dummy text asset
			File.WriteAllText(testAssetPath, TestAssetContent);
			
			// Refresh to let Unity process the new asset
			AssetDatabase.Refresh();
			yield return null; // EditMode tests can only yield null
			
			// Verify asset was created with meta file
			Assert.IsTrue(File.Exists(testAssetPath), "Test asset was not created");
			Assert.IsTrue(File.Exists(testAssetPath + ".meta"), "Test asset meta file was not created");
		}

		private IEnumerator ExportAssetAsPackage()
		{
			// Get temporary path for package export
			string tempDir = Path.GetTempPath();
			string packageFileName = $"TestPackage_{System.DateTime.Now.Ticks}.unitypackage";
			tempPackagePath = Path.Combine(tempDir, packageFileName);
			
			// Export asset as package
			AssetDatabase.ExportPackage(testAssetPath, tempPackagePath, ExportPackageOptions.IncludeDependencies);
			yield return null; // EditMode tests can only yield null
			
			// Verify package was created
			Assert.IsTrue(File.Exists(tempPackagePath), $"Package was not exported to {tempPackagePath}");
		}

		private IEnumerator DeleteOriginalAsset()
		{
			// Delete the original asset and folder
			AssetDatabase.DeleteAsset(testFolderPath);
			AssetDatabase.Refresh();
			yield return null; // EditMode tests can only yield null
			
			// Verify asset was deleted
			Assert.IsFalse(File.Exists(testAssetPath), "Original test asset was not deleted");
			Assert.IsFalse(AssetDatabase.IsValidFolder(testFolderPath), "Original test folder was not deleted");
		}

		private IEnumerator ImportPackageToTargetFolder()
		{
			// Create target import folder
			if (!AssetDatabase.IsValidFolder(importTargetPath))
			{
				AssetDatabase.CreateFolder("Assets", ImportTargetFolder);
			}
			
			// Use Package2Folder API to silently import package
			Package2Folder.ImportPackageToFolder(tempPackagePath, importTargetPath, false);
			
			// Wait for import to complete
			AssetDatabase.Refresh();
			yield return null; // EditMode tests can only yield null
		}

		private IEnumerator ValidateImport()
		{
			// Expected path of imported asset
			string expectedImportedAssetPath = Path.Combine(importTargetPath, TestFolderName, TestAssetName);
			
			// Verify asset was imported to correct location
			Assert.IsTrue(File.Exists(expectedImportedAssetPath), 
				$"Asset was not imported to expected location: {expectedImportedAssetPath}");
			Assert.IsTrue(File.Exists(expectedImportedAssetPath + ".meta"), 
				$"Asset meta file was not imported: {expectedImportedAssetPath}.meta");
			
			// Verify content is correct
			string importedContent = File.ReadAllText(expectedImportedAssetPath);
			Assert.AreEqual(TestAssetContent, importedContent, "Imported asset content does not match original");
			
			// Verify asset is recognized by Unity
			var importedAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(expectedImportedAssetPath);
			Assert.IsNotNull(importedAsset, "Imported asset is not recognized by Unity AssetDatabase");
			
			yield return null;
		}

		private void CleanupTestArtifacts()
		{
			// Clean up test folder in Assets
			if (AssetDatabase.IsValidFolder(testFolderPath))
			{
				AssetDatabase.DeleteAsset(testFolderPath);
			}
			
			// Clean up import target folder
			if (AssetDatabase.IsValidFolder(importTargetPath))
			{
				AssetDatabase.DeleteAsset(importTargetPath);
			}
			
			AssetDatabase.Refresh();
		}

		private void CleanupTempPackage()
		{
			// Clean up temporary package file
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