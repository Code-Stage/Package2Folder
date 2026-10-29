#if HAS_TEST_FRAMEWORK
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
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
#if UNITY_6000_6_OR_NEWER
        private const string SignedContent = "Package signature regression fixture.\n";
        private const string SignedAssetGuid = "fd6f0ab538eb419a9e1973aa46d712b5";
        private const string SignedFolderGuid = "31b886f879a44530a491ead1f17dd37d";
        private const string SignedArchiveHash = "a7eb62db306d812de004e73e7eaaed7ce24796216fc8354c0b78c5032eb5e409";
        private const string TamperedArchiveHash = "dad0d695d44d4cb19b110fd1f365afb66d5627e57680468c04d61ce563ae4a6e";
        private const string SignedTargetPath = ImportTargetPath + "/OwnedFixture/provenance.txt";
#endif
        private static readonly Type ImportWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.PackageImport");
        private static readonly Type CompanionType = typeof(Package2Folder).Assembly.GetType("CodeStage.PackageToFolder.Package2FolderCompanion");
        private static readonly FieldInfo PickerField = typeof(Package2Folder).GetField("selectPackage", PrivateStatic);
        private string tempPackagePath;
        private string exportedGuid;
        private UnityEngine.Object previousSelection;
        private object previousPicker;
        private EditorWindow importWindow;
#if UNITY_6000_6_OR_NEWER
        private bool ownsSignedArtifacts;
#endif

        private static string TestAssetPath => TestFolderPath + "/Nested/" + TestAssetName;
        private static string ExpectedPath => ImportTargetPath + "/Package2FolderTest/Nested/" + TestAssetName;
        private static object[] ImportItems(EditorWindow window) => (object[])ImportWindowType
            .GetField("m_ImportPackageItems", PrivateInstance).GetValue(window);

        [SetUp]
        public void SetUp()
        {
            previousSelection = Selection.activeObject;
            previousPicker = PickerField.GetValue(null);
#if UNITY_6000_6_OR_NEWER
            ownsSignedArtifacts = false;
#endif
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
#if UNITY_6000_6_OR_NEWER
            if (ownsSignedArtifacts) AssetDatabase.DeleteAsset("Assets/OwnedFixture");
#endif
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

#if UNITY_6000_6_OR_NEWER
        [UnityTest]
        public IEnumerator SignedValidArchiveImportsSilentlyAndPreservesNativeSignature()
        {
            yield return PrepareSignedArchive("SignedValid.unitypackage", SignedArchiveHash);
            var updates = NativeUpdateHandlers();
            string signature = null, trust = null, error = null;
            var observedWhilePending = false;
            var observer = ObserveNativeSignature((status, level, code) =>
            {
                signature = status; trust = level; error = code;
                observedWhilePending = NativeImportPending();
            });
            EditorApplication.update += observer;
            try
            {
                Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, false);
                yield return WaitFor(() => File.Exists(SignedTargetPath), "Signed asset did not reach its target.");
            }
            finally { EditorApplication.update -= observer; }
            Assert.AreEqual("Valid", signature);
            Assert.IsNotEmpty(trust, "Native trust status was not observed.");
            Assert.AreNotEqual("Unchecked", trust);
            Assert.AreEqual("None", error);
            Assert.IsTrue(observedWhilePending, "Signature was not observed before redirection.");
            Debug.Log($"Native signed fixture: signature={signature}; trust={trust}; error={error}");
            yield return AssertSignedImport();
            Assert.AreEqual(SignedArchiveHash, ArchiveHash(tempPackagePath));
            Assert.IsFalse(NativeImportPending());
            CollectionAssert.AreEquivalent(updates, NativeUpdateHandlers());
            yield return WaitForCompanionToClose();
        }

        [UnityTest]
        public IEnumerator TamperedSignedArchiveIsRejectedAndAllowsValidRetry()
        {
            yield return PrepareSignedArchive("SignatureTampered.unitypackage", TamperedArchiveHash);
            var updates = NativeUpdateHandlers();
            string signature = null, trust = null;
            var rejected = false;
            var completed = false;
            var observer = ObserveNativeSignature((status, level, code) =>
            {
                signature = status; trust = level;
                Debug.Log($"Native tampered fixture: signature={status}; trust={level}; error={code}");
            });
            bool Owns(string name) => Path.GetFileName(name) == Path.GetFileNameWithoutExtension(tempPackagePath);
            void Cancelled(string name) { if (Owns(name)) rejected = true; }
            void Failed(string name, string message) { if (Owns(name)) rejected = true; }
            void Completed(string name) { if (Owns(name)) completed = true; }
            EditorApplication.update += observer;
            AssetDatabase.importPackageCancelled += Cancelled;
            AssetDatabase.importPackageFailed += Failed;
            AssetDatabase.importPackageCompleted += Completed;
            try
            {
                Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, false);
                yield return WaitFor(() => rejected || completed, "Invalid signature did not finish with a rejection.");
                Assert.AreEqual("Invalid", signature, "The archive must fail signature validation, not decompression.");
                Assert.IsNotEmpty(trust);
                Assert.IsFalse(completed, "An invalid signed archive was silently imported.");
                Assert.IsTrue(rejected);
                Assert.IsFalse(NativeImportPending());
                CollectionAssert.AreEquivalent(updates, NativeUpdateHandlers());
                Assert.IsFalse(File.Exists(SignedTargetPath));
                Assert.IsFalse(AssetDatabase.IsValidFolder(ImportTargetPath + "/OwnedFixture"));
                Assert.IsFalse(Directory.Exists("Assets/OwnedFixture"));
                Assert.IsEmpty(ExistingPathForGuid(SignedAssetGuid));
                Assert.IsEmpty(ExistingPathForGuid(SignedFolderGuid));
                Assert.AreEqual(TamperedArchiveHash, ArchiveHash(tempPackagePath));
                yield return WaitForCompanionToClose();
            }
            finally
            {
                EditorApplication.update -= observer;
                AssetDatabase.importPackageCancelled -= Cancelled;
                AssetDatabase.importPackageFailed -= Failed;
                AssetDatabase.importPackageCompleted -= Completed;
            }
            var previousTimestamp = File.GetLastWriteTimeUtc(tempPackagePath);
            File.Copy(SignedFixtureSource("SignedValid.unitypackage"), tempPackagePath, true);
            // Give the replacement a distinct timestamp so native validation does not reuse the previous result.
            File.SetLastWriteTimeUtc(tempPackagePath, previousTimestamp.AddSeconds(2));
            Assert.AreNotEqual(previousTimestamp, File.GetLastWriteTimeUtc(tempPackagePath));
            Assert.AreEqual(SignedArchiveHash, ArchiveHash(tempPackagePath));
            Package2Folder.ImportPackageToFolder(tempPackagePath, ImportTargetPath, false);
            yield return AssertSignedImport();
            Assert.IsFalse(NativeImportPending());
            CollectionAssert.AreEquivalent(updates, NativeUpdateHandlers());
            yield return WaitForCompanionToClose();
        }

        private IEnumerator PrepareSignedArchive(string name, string expectedHash)
        {
            Assert.IsFalse(Directory.Exists("Assets/OwnedFixture"), "A pre-existing fixture folder must not be changed.");
            Assert.IsEmpty(ExistingPathForGuid(SignedAssetGuid));
            Assert.IsEmpty(ExistingPathForGuid(SignedFolderGuid));
            ownsSignedArtifacts = true;
            var source = SignedFixtureSource(name);
            Assert.AreEqual(expectedHash, ArchiveHash(source));
            tempPackagePath = Path.Combine(Path.GetTempPath(), "SignedFixture_" + Guid.NewGuid().ToString("N") + ".unitypackage");
            Assert.IsFalse(Path.GetFullPath(tempPackagePath).StartsWith(Path.GetFullPath(".") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), "Package must be outside the project.");
            File.Copy(source, tempPackagePath);
            AssetDatabase.CreateFolder("Assets", "ImportedAssets");
            yield return null;
        }

        private static string ExistingPathForGuid(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var existingGuid = AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets);
            if (!string.IsNullOrEmpty(path) && string.IsNullOrEmpty(existingGuid))
            {
                Assert.IsFalse(File.Exists(path) || Directory.Exists(path));
                Debug.Log($"Signed fixture GUID lookup: cachedPath={path}; existingGuidEmpty=True");
            }
            return string.IsNullOrEmpty(existingGuid) ? string.Empty : path;
        }

        private static string SignedFixtureSource(string name)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(Package2Folder).Assembly);
            Assert.IsNotNull(package, "The signed fixture requires the testable local package.");
            var embedded = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages", package.name, "Tests/Fixtures~", name));
            if (File.Exists(embedded)) return embedded;
            return Path.Combine(package.resolvedPath, "Tests/Fixtures~", name);
        }

        private EditorApplication.CallbackFunction ObserveNativeSignature(Action<string, string, string> capture)
        {
            var captured = false;
            return () =>
            {
                if (captured) return;
                try
                {
                    var wizard = ImportWizard();
                    var path = (string)wizard.GetType().GetField("m_PackagePath", PrivateInstance).GetValue(wizard);
                    if (string.IsNullOrEmpty(path) || Path.GetFullPath(path) != Path.GetFullPath(tempPackagePath)) return;
                    var info = wizard.GetType().GetProperty("assetPackageInfo").GetValue(wizard);
                    var signature = info?.GetType().GetProperty("signature").GetValue(info);
                    if (signature == null) return;
                    captured = true;
                    capture(signature.GetType().GetProperty("status").GetValue(signature).ToString(),
                        info.GetType().GetProperty("trustLevel").GetValue(info).ToString(),
                        info.GetType().GetProperty("errorCode").GetValue(info).ToString());
                }
                catch (Exception exception) { captured = true; capture("OBSERVER_FAILED", exception.GetType().Name, "OBSERVER_FAILED"); }
            };
        }

        private static IEnumerator AssertSignedImport()
        {
            yield return WaitFor(() => AssetDatabase.LoadAssetAtPath<TextAsset>(SignedTargetPath) != null,
                "The signed text asset was not imported.");
            Assert.AreEqual(SignedContent, File.ReadAllText(SignedTargetPath));
            Assert.AreEqual(SignedAssetGuid, AssetDatabase.AssetPathToGUID(SignedTargetPath));
            Assert.AreEqual(SignedFolderGuid, AssetDatabase.AssetPathToGUID(ImportTargetPath + "/OwnedFixture"));
            Assert.AreEqual(SignedTargetPath, AssetDatabase.GUIDToAssetPath(SignedAssetGuid));
            Assert.IsFalse(Directory.Exists("Assets/OwnedFixture"));
        }

        private static string ArchiveHash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
#endif

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
