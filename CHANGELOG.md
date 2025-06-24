# Changelog
Changelog format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

#### Types of changes  
- **Added** for new features.
- **Changed** for changes in existing functionality.
- **Deprecated** for soon-to-be removed features.
- **Removed** for now removed features.
- **Fixed** for any bug fixes.
- **Security** in case of vulnerabilities.

💡 _Always remove previous plugin version before updating_

## [1.2.0] - 2025-06-24

### Added
- Add assetOrigin argument support to ImportPackageToFolder API (@rwetzold)

### Changed
- Refactor asset into UPM package, can add to the project now with direct git link
- Improve paths handling

## [1.1.0] - 2022-12-20

### Added
- Add Unity 2023 support (at least for 2023 alpha)

### Removed
- Remove some legacy projects
- Remove legacy AssetStoreTools

## [1.0.6] - 2021-05-30

### Fixed
- Fix plugin did not work correctly with two-column project view (thx madc0der)

## [1.0.5]

### Fixed
- Fix exception when using ImportPackageToFolder with interactive == false (thx Vipul Tyagi)

## [1.0.4]

### Fixed
- Fix incorrect path processing in case package was exported not from the Assets folder (thx vestigialdev)

## [1.0.3]

### Fixed
- Fix reflection errors in Unity 2019.3+ (thx viktorkadza)

## [1.0.2]

### Fixed
- Fix reflection errors in Unity 2018.4 > 2018.4.3 (thx Aseemy)

## [1.0.1]

### Changed
- Update project to 2017.4 LTS
- Add Assembly Definition
- Remove obsolete code for pre-Unity 5.3

### Fixed
- Fix errors in Unity 2019.1.4+ (thx goldface)

## [1.0.0]

### Added
- Initial release 