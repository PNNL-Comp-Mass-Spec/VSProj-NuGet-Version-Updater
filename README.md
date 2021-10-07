VSProj NuGet Version Updater searches for Visual Studio project files (.csproj and.vsproj) 
that reference a specific NuGet package and updates the referenced version
to a newer version if necessary.

This is useful when you have several projects all referencing the same NuGet package,
and you want to quickly update all of the projects to use the latest release of a package.

### Example usage

```VSProjNuGetVersionUpdater.exe D:\Projects /Package:PRISM-Library /Version:1.0.2 /S /apply```

### Continuous Integration

The latest versions of the executable is available for six months on the [AppVeyor CI server](https://ci.appveyor.com/project/PNNLCompMassSpec/vsproj-nuget-version-updater/build/artifacts)

[![Build status](https://ci.appveyor.com/api/projects/status/076wixks3ywuc5l6?svg=true)](https://ci.appveyor.com/project/PNNLCompMassSpec/vsproj-nuget-version-updater)


## Program syntax

```VSProjNuGetVersionUpdater.exe
 FolderPath /Package:PackageName /Version:PackageVersion
 [/S] [/Apply] [/Rollback] [/Verbose]
 ```

* FolderPath is the path to the folder to search for Visual Studio project files
  *If FolderPath is not specified, the current folder is used
* Use /S to recurse subdirectories
* Specify the NuGet package name using /Package or using /P
* Specify the NuGet package version using /Version or using /V
* By default will not update files; use /Apply to save changes
* Use /Rollback to downgrade versions if a newer version is found
* Use /Verbose to see every Visual Studio project file processed
** Otherwise, only projects containing package PackageName will be shown

## Contacts

Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA) \
E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov \
Website: https://github.com/PNNL-Comp-Mass-Spec/ or https://panomics.pnnl.gov/ or https://www.pnnl.gov/integrative-omics

## License

Licensed under the Apache License, Version 2.0; you may not use this program except
in compliance with the License.  You may obtain a copy of the License at
http://www.apache.org/licenses/LICENSE-2.0
