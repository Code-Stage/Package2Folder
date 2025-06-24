# Package2Folder

Unity Editor extension that allows you to import custom packages into the selected Project folder, avoiding your project's root bloating.

## Description

This Unity Editor extension allows you to import custom package into the selected Project folder.
It also has public API to let you run package import to folder from your scripts:

```csharp
CodeStage.PackageToFolder.Package2Folder.ImportPackageToFolder();
```

See detailed API description in code.

## Installation

### Option 1: Unity Package Manager (Git URL)

1. Open the Package Manager in Unity (`Window > Package Manager`)
2. Click the `+` button in the top-left corner
3. Select `Add package from git URL...`
4. Enter: `https://github.com/Code-Stage/Package2Folder.git`
5. Click `Add`

### Option 2: Unity Package (Legacy)

Download the latest `.unitypackage` file from the [Releases](https://github.com/Code-Stage/Package2Folder/releases) section and import it into your project.

## How to use

1. Import the package to your project using one of the installation methods above
2. In the Project window, select the folder where you want to import a package
3. Use the menu item: `Assets > Import Package > Here...`
4. Select the `.unitypackage` file you want to import
5. The package will be imported into the selected folder instead of the project root

## Public API

The package exposes a public API that allows you to import packages programmatically:

```csharp
using CodeStage.PackageToFolder;

// Import package interactively (shows import dialog)
Package2Folder.ImportPackageToFolder(packagePath, targetFolderPath, true);

// Import package silently
Package2Folder.ImportPackageToFolder(packagePath, targetFolderPath, false);
```

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for details about changes in each version.

## License

This plugin is licensed under [Mozilla Public License Version 2.0](LICENSE).

## Contributing

Please report any bugs and suggestions via [GitHub's issues](https://github.com/Code-Stage/Package2Folder/issues).
Pull requests are welcome!

Have fun!