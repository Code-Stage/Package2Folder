#if HAS_TEST_FRAMEWORK
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace CodeStage.PackageToFolder.Tests
{
    public class ImportPackageToFolderTests
    {
        private const string MenuPath = "Assets/Import Package/Here...";
        private const string TestAssetName = "TestDummyAsset.txt";
        private const string TestAssetContent = "This is a test asset for Package2Folder testing";
        private const string TestFolderPath = "Assets/Package2FolderTest";
        private const string ImportTargetPath = "Assets/ImportedAssets";
        private const double ImportTimeoutSeconds = 60;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type ImportWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.PackageImport");
        private static readonly Type CompanionType = typeof(Package2Folder).Assembly.GetType("CodeStage.PackageToFolder.Package2FolderCompanion");
        private static readonly FieldInfo PickerField = typeof(Package2Folder).GetField("selectPackage", PrivateStatic);
        private string tempPackagePath;
        private string exportedGuid;
        private UnityEngine.Object previousSelection;
        private object previousPicker;
        private EditorWindow importWindow;

        private static string TestAssetPath => TestFolderPath + "/Nested/" + TestAssetName;
        private static string ExpectedPath => ImportTargetPath + "/Package2FolderTest/Nested/" + TestAssetName;
        private static object[] ImportItems(EditorWindow window) => (object[])ImportWindowType
            .GetField("m_ImportPackageItems", PrivateInstance).GetValue(window);

        [SetUp]
        public void SetUp()
        {
            previousSelection = Selection.activeObject;
            previousPicker = PickerField.GetValue(null);
            CleanupTestArtifacts();
        }

        [TearDown]
        public void TearDown()
        {
            PickerField.SetValue(null, previousPicker);
            Selection.activeObject = previousSelection;
            foreach (var window in Resources.FindObjectsOfTypeAll(ImportWindowType))
                ((EditorWindow)window).Close();
            foreach (var window in Resources.FindObjectsOfTypeAll(CompanionType))
                ((EditorWindow)window).Close();
            if (tempPackagePath != null && File.Exists(tempPackagePath)) File.Delete(tempPackagePath);
            CleanupTestArtifacts();
        }

        [UnityTest]
        public IEnumerator TestSilentImportPackageToFolder()
        {
            yield return PrepareExternalPackage();
            Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, false);
            yield return ValidateImport();
            yield return WaitForCompanionToClose();
        }

        [UnityTest]
        public IEnumerator SilentImportPreservesUnrelatedAssetAtOriginalPath()
        {
            yield return PrepareExternalPackage();
            yield return CreateUnrelatedAsset();
            var unrelatedGuid = AssetDatabase.AssetPathToGUID(TestAssetPath);
            Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, false);
            yield return ValidateImport();
            AssertUnrelatedAsset(unrelatedGuid);
        }

        [UnityTest]
        public IEnumerator ContextMenuImportsExternalPackageAfterNativeConfirmation()
        {
            yield return PrepareExternalPackage();
            yield return CreateUnrelatedAsset();
            var unrelatedGuid = AssetDatabase.AssetPathToGUID(TestAssetPath);
            var pickerCalled = false;
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ImportTargetPath);
            Assert.IsTrue(IsMenuEnabled());
            // Only the OS picker is replaced; execute Unity's registered menu and native confirmation handler.
            PickerField.SetValue(null, new Func<string>(() =>
            {
                pickerCalled = true;
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<DefaultAsset>(TestFolderPath);
                return tempPackagePath;
            }));
            Assert.IsTrue(EditorApplication.ExecuteMenuItem(MenuPath));
            Assert.IsTrue(pickerCalled, "Registered menu did not request a package");
            yield return WaitForImportWindow();
            AssertPreparedTarget(ExpectedPath);
            Assert.AreEqual("Unrelated local content", File.ReadAllText(TestAssetPath));
            ConfirmNativeImport();
            yield return ValidateImport();
            AssertUnrelatedAsset(unrelatedGuid);
            yield return WaitForCompanionToClose();
        }

        [UnityTest]
        public IEnumerator DamagedArchiveDoesNotImportAndAllowsRetry()
        {
            yield return PrepareExternalPackage();
            var validArchive = File.ReadAllBytes(tempPackagePath);
            var damagedArchive = new byte[validArchive.Length / 2];
            Array.Copy(validArchive, damagedArchive, damagedArchive.Length);
            File.WriteAllBytes(tempPackagePath, damagedArchive);
            string failure = null;
            var previousIgnore = LogAssert.ignoreFailingMessages;
            Application.LogCallback recordFailure = (message, stack, type) =>
            {
                if ((type == LogType.Error || type == LogType.Exception) &&
                    message.IndexOf("decompress", StringComparison.OrdinalIgnoreCase) >= 0) failure = message;
            };
            void RecordNativeFailure(string name, string error)
            {
                if (Path.GetFileName(name) == Path.GetFileNameWithoutExtension(tempPackagePath)) failure = error;
            }
            Application.logMessageReceived += recordFailure;
            AssetDatabase.importPackageFailed += RecordNativeFailure;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                try { Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, false); }
                catch (Exception exception) { failure = exception.ToString(); }
                yield return WaitFor(() => failure != null && !NativeImportPending(),
                    "Unity did not reject the damaged archive and release the pending import");
                Assert.IsFalse(File.Exists(ExpectedPath));
                Assert.IsFalse(File.Exists(TestAssetPath));
                Assert.IsEmpty(Resources.FindObjectsOfTypeAll(ImportWindowType));
                Debug.Log("Damaged native archive rejection: " + failure);
            }
            finally
            {
                Application.logMessageReceived -= recordFailure;
                AssetDatabase.importPackageFailed -= RecordNativeFailure;
                LogAssert.ignoreFailingMessages = previousIgnore;
                File.WriteAllBytes(tempPackagePath, validArchive);
            }
            Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, false);
            yield return ValidateImport();
            yield return WaitForCompanionToClose();
        }

#if UNITY_6000_5_OR_NEWER
        [UnityTest]
        public IEnumerator CancellationBeforeNativeWindowRedirectionClearsPendingAndAllowsRetry()
        {
            yield return PrepareExternalPackage();
            var previousUpdates = NativeUpdateHandlers();
            var cancelledWhilePending = false;
            Exception cancellationError = null;
            void CancelBeforeRedirection()
            {
                if (cancelledWhilePending || !NativeImportPending()) return;
                var wizard = ImportWizard();
                var path = (string)wizard.GetType().GetField("m_PackagePath", PrivateInstance).GetValue(wizard);
                var nativeWindow = wizard.GetType().GetField("m_ImportWindow", PrivateInstance).GetValue(wizard) as EditorWindow;
                if (nativeWindow == null || string.IsNullOrEmpty(path) || Path.GetFullPath(path) != Path.GetFullPath(tempPackagePath)) return;
                cancelledWhilePending = true;
                try { CancelNativeImport(); }
                catch (Exception exception) { cancellationError = exception; }
            }
            // Observe the real native window before Package2Folder's later update callback redirects it.
            EditorApplication.update += CancelBeforeRedirection;
            try
            {
                Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, true);
                yield return WaitFor(() => cancelledWhilePending && !NativeImportPending(),
                    "Native cancellation before redirection did not release the pending import.");
            }
            finally { EditorApplication.update -= CancelBeforeRedirection; }
            Assert.IsNull(cancellationError, cancellationError?.ToString());
            Assert.IsTrue(cancelledWhilePending);
            yield return WaitForCompanionToClose();
            CollectionAssert.AreEquivalent(previousUpdates, NativeUpdateHandlers(),
                "Pending cancellation left an Editor update handler subscribed.");
            Assert.IsFalse(File.Exists(ExpectedPath));
            Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, false);
            yield return ValidateImport();
            yield return WaitForCompanionToClose();
        }
#endif

        [UnityTest]
        public IEnumerator InteractiveImportRejectsSecondImportAndSupportsCancelThenRetry()
        {
            yield return PrepareExternalPackage();
            Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, true);
            yield return WaitForImportWindow();
            Assert.IsFalse(NativeImportPending(), "Prepared-window cancellation must run after native waiting ends.");
            AssertPreparedTarget(ExpectedPath);
            Assert.IsFalse(File.Exists(TestAssetPath), "Import wrote to the original path before confirmation");
            Assert.Throws<InvalidOperationException>(() =>
                Package2Folder.ImportPackageToFolder(tempPackagePath, "Assets", false));
            Assert.AreSame(importWindow, Resources.FindObjectsOfTypeAll(ImportWindowType)[0]);
            AssertPreparedTarget(ExpectedPath);
            CancelNativeImport();
            yield return WaitForCompanionToClose();
            Assert.IsFalse(File.Exists(ExpectedPath), "Cancelled import wrote an asset");
            Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, true);
            yield return WaitForImportWindow();
            AssertPreparedTarget(ExpectedPath);
            ConfirmNativeImport();
            yield return ValidateImport();
            yield return WaitForCompanionToClose();
        }

        [UnityTest]
        public IEnumerator CompanionChangesTargetWithoutStackingFolderPrefixes()
        {
            yield return PrepareExternalPackage();
            Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, true);
            yield return WaitForImportWindow();
            var companion = Resources.FindObjectsOfTypeAll(CompanionType)[0];
            Assert.AreEqual(ImportTargetPath, CompanionType.GetField("selectedFolder", PrivateInstance).GetValue(companion));
            var originalPaths = (string[])CompanionType.GetField("originalPaths", PrivateInstance).GetValue(companion);
            var setFolder = typeof(Package2Folder).GetMethod("SetImportWindowFolder", PrivateStatic);
            setFolder.Invoke(null, new object[] { importWindow, TestFolderPath, originalPaths });
            AssertPreparedTarget(TestFolderPath + "/Package2FolderTest/Nested/" + TestAssetName);
            setFolder.Invoke(null, new object[] { importWindow, ImportTargetPath, originalPaths });
            AssertPreparedTarget(ExpectedPath);
            ConfirmNativeImport();
            yield return ValidateImport();
            yield return WaitForCompanionToClose();
        }

        [UnityTest]
        public IEnumerator DismissedCompanionStaysClosedUntilNewImport()
        {
            yield return PrepareExternalPackage();
            Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, true);
            yield return WaitForImportWindow();
            ((EditorWindow)Resources.FindObjectsOfTypeAll(CompanionType)[0]).Close();
            typeof(Package2Folder).GetMethod("WatchForPackageImportWindows", PrivateStatic).Invoke(null, null);
            CompanionType.GetMethod("ShowForImportWindow", PrivateStatic).Invoke(null, new object[] { importWindow, null, null });
            Assert.IsEmpty(Resources.FindObjectsOfTypeAll(CompanionType), "Dismissed companion reopened");
            CancelNativeImport();
            Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, true);
            yield return WaitForImportWindow();
            Assert.AreEqual(1, Resources.FindObjectsOfTypeAll(CompanionType).Length);
            CancelNativeImport();
            yield return WaitForCompanionToClose();
        }

        [UnityTest]
        public IEnumerator ContextMenuPickerCancelDoesNotStartImport()
        {
            yield return PrepareExternalPackage();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ImportTargetPath);
            PickerField.SetValue(null, new Func<string>(() => string.Empty));
            Assert.IsTrue(EditorApplication.ExecuteMenuItem(MenuPath));
            yield return null;
            Assert.IsEmpty(Resources.FindObjectsOfTypeAll(ImportWindowType));
            Assert.IsFalse(File.Exists(ExpectedPath));
            Assert.IsFalse(File.Exists(TestAssetPath));
            Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, false);
            yield return ValidateImport();
        }

        [UnityTest]
        public IEnumerator ContextMenuRequiresAnAssetsFolder()
        {
            yield return CreateTestAsset();
            Selection.activeObject = null;
            Assert.IsFalse(IsMenuEnabled(), "Menu enabled with no selection");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<TextAsset>(TestAssetPath);
            Assert.IsFalse(IsMenuEnabled(), "Menu enabled for a file");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<DefaultAsset>(TestFolderPath);
            Assert.IsTrue(IsMenuEnabled(), "Menu disabled for an Assets folder");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Packages/net.codestage.package2folder");
            Assert.IsNotNull(Selection.activeObject, "Package folder was not available for validation");
            Assert.IsFalse(IsMenuEnabled(), "Menu enabled for a Packages folder");
        }

        [TestCase("AssetsOther")]
        [TestCase("Assets/../Outside")]
        public void RejectsTargetOutsideAssetsBeforeReadingPackage(string target)
        {
            Assert.Throws<ArgumentException>(() =>
                Package2Folder.ImportPackageToFolder("missing.unitypackage", target, false));
        }

        private static bool IsMenuEnabled() => (bool)typeof(Package2Folder)
            .GetMethod("IsImportToFolderCheck", PrivateStatic).Invoke(null, null);

        private static bool NativeImportPending() => (bool)typeof(Package2Folder)
            .GetField("nativeImportPending", PrivateStatic).GetValue(null);

        private static Delegate[] NativeUpdateHandlers() => Array.FindAll(
            EditorApplication.update?.GetInvocationList() ?? Array.Empty<Delegate>(), callback =>
                callback.Method.DeclaringType?.FullName?.StartsWith(typeof(Package2Folder).FullName + "+", StringComparison.Ordinal) == true);

        private IEnumerator PrepareExternalPackage()
        {
            yield return CreateTestAsset();
            exportedGuid = AssetDatabase.AssetPathToGUID(TestAssetPath);
            tempPackagePath = Path.Combine(Path.GetTempPath(), "TestPackage_" + Guid.NewGuid().ToString("N") + ".unitypackage");
            Assert.IsFalse(Path.GetFullPath(tempPackagePath).StartsWith(Path.GetFullPath(".") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), "Package must be outside the project");
            AssetDatabase.ExportPackage(TestAssetPath, tempPackagePath, ExportPackageOptions.IncludeDependencies);
            Assert.IsTrue(File.Exists(tempPackagePath));
            AssetDatabase.DeleteAsset(TestFolderPath);
            AssetDatabase.CreateFolder("Assets", "ImportedAssets");
            AssetDatabase.Refresh();
            yield return null;
            Assert.IsFalse(File.Exists(TestAssetPath));
        }

        private static IEnumerator CreateTestAsset()
        {
            if (!AssetDatabase.IsValidFolder(TestFolderPath)) AssetDatabase.CreateFolder("Assets", "Package2FolderTest");
            if (!AssetDatabase.IsValidFolder(TestFolderPath + "/Nested")) AssetDatabase.CreateFolder(TestFolderPath, "Nested");
            File.WriteAllText(TestAssetPath, TestAssetContent);
            AssetDatabase.Refresh();
            yield return null;
            Assert.IsTrue(File.Exists(TestAssetPath + ".meta"));
        }

        private static IEnumerator CreateUnrelatedAsset()
        {
            yield return CreateTestAsset();
            File.WriteAllText(TestAssetPath, "Unrelated local content");
            File.WriteAllText(TestAssetPath + ".meta", File.ReadAllText(TestAssetPath + ".meta")
                .Replace(AssetDatabase.AssetPathToGUID(TestAssetPath), Guid.NewGuid().ToString("N")));
            AssetDatabase.Refresh();
        }

        private IEnumerator WaitForImportWindow()
        {
            yield return WaitFor(() =>
            {
                var windows = Resources.FindObjectsOfTypeAll(ImportWindowType);
                if (windows.Length != 1) return false;
                importWindow = (EditorWindow)windows[0];
                var items = ImportItems(importWindow);
                return items != null && Array.Exists(items, item => ItemPath(item) == ExpectedPath) &&
                    Resources.FindObjectsOfTypeAll(CompanionType).Length == 1;
            }, "Native import dialog did not prepare the selected target and companion");
        }

        private static string ItemPath(object item) => (string)item.GetType().GetField("destinationAssetPath").GetValue(item);

        private void AssertPreparedTarget(string expected)
        {
            Assert.IsTrue(Array.Exists(ImportItems(importWindow), item => ItemPath(item) == expected),
                "Native import dialog has the wrong target: " + expected);
            Assert.IsFalse(File.Exists(expected), "Interactive import wrote before confirmation");
        }

        private static object ImportWizard()
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.PackageImportWizard");
            return type.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
        }

        private void ConfirmNativeImport()
        {
            var wizard = ImportWizard();
            var method = wizard.GetType().GetMethod("DoImportStep") ?? wizard.GetType().GetMethod("DoNextStep");
            Assert.IsNotNull(method, "Native Import button handler was not found");
            try { method.Invoke(wizard, new object[] { ImportItems(importWindow) }); }
            catch (TargetInvocationException exception) when (exception.InnerException is ExitGUIException) { }
        }

        private static void CancelNativeImport()
        {
            var wizard = ImportWizard();
            try { wizard.GetType().GetMethod("CancelImport").Invoke(wizard, null); }
            catch (TargetInvocationException exception) when (exception.InnerException is ExitGUIException) { }
        }

        private static IEnumerator WaitForCompanionToClose() => WaitFor(() =>
            Resources.FindObjectsOfTypeAll(ImportWindowType).Length == 0 &&
            Resources.FindObjectsOfTypeAll(CompanionType).Length == 0, "Import or companion window remained open");

        private static IEnumerator WaitFor(Func<bool> completed, string failure)
        {
            var deadline = EditorApplication.timeSinceStartup + ImportTimeoutSeconds;
            while (!completed() && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.IsTrue(completed(), failure);
        }

        private IEnumerator ValidateImport()
        {
            yield return WaitFor(() => File.Exists(ExpectedPath) && File.Exists(ExpectedPath + ".meta") &&
                AssetDatabase.LoadAssetAtPath<TextAsset>(ExpectedPath) != null, "Asset did not arrive at " + ExpectedPath);
            Assert.AreEqual(TestAssetContent, File.ReadAllText(ExpectedPath));
            Assert.AreEqual(exportedGuid, AssetDatabase.AssetPathToGUID(ExpectedPath), "Import changed the package asset GUID");
            Assert.AreEqual(1, AssetDatabase.FindAssets("t:TextAsset " + Path.GetFileNameWithoutExtension(TestAssetName),
                new[] { ImportTargetPath }).Length, "Import created duplicate assets");
        }

        private static void AssertUnrelatedAsset(string guid)
        {
            Assert.AreEqual("Unrelated local content", File.ReadAllText(TestAssetPath));
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(TestAssetPath));
            Assert.AreNotEqual(guid, AssetDatabase.AssetPathToGUID(ExpectedPath));
        }

        private static void CleanupTestArtifacts()
        {
            AssetDatabase.DeleteAsset(TestFolderPath);
            AssetDatabase.DeleteAsset(ImportTargetPath);
            AssetDatabase.Refresh();
        }
    }
}
#endif
