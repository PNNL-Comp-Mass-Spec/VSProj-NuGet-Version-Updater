using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using PRISM;

namespace VSProjNuGetVersionUpdater
{
    /// <summary>
    /// This program searches for Visual Studio project files (.csproj and.vsproj)
    /// that reference a specific NuGet package and updates the referenced version
    /// to a newer version if necessary.
    /// It also updates packages.config files.
    /// </summary>
    /// <remarks>
    /// Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA)
    ///
    /// E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov
    /// Website: https://omics.pnl.gov/ or https://panomics.pnnl.gov/
    /// </remarks>
    internal static class Program
    {
        public const string PROGRAM_DATE = "February 21, 2019";

        private struct PackageUpdateOptions
        {
            public string NuGetPackageName;
            public string NuGetPackageVersion;
            public bool Rollback;
            public bool Preview;

            public Version NewPackageVersion { get; set; }
        }

        private static string mSearchDirectoryPath;
        private static bool mRecurse;

        private static PackageUpdateOptions mUpdateOptions;

        private static bool mVerboseLogging;

        private static DateTime mLastProgressTime;

        private static bool mProgressNewlineRequired;


        static int Main(string[] args)
        {
            var commandLineParse = new clsParseCommandLine();

            mSearchDirectoryPath = ".";
            mRecurse = false;

            mUpdateOptions = new PackageUpdateOptions
            {
                NuGetPackageName = "",
                NuGetPackageVersion = "",
                Rollback = false,
                Preview = true
            };

            mVerboseLogging = false;

            try
            {

                var success = false;

                if (commandLineParse.ParseCommandLine())
                {
                    if (SetOptionsUsingCommandLineParameters(commandLineParse))
                        success = true;
                }

                if (!success ||
                    commandLineParse.NeedToShowHelp)
                {
                    ShowProgramHelp();
                    return -1;
                }

                if (string.IsNullOrWhiteSpace(mSearchDirectoryPath))
                {
                    mSearchDirectoryPath = ".";
                }


                if (string.IsNullOrWhiteSpace(mUpdateOptions.NuGetPackageName))
                {
                    ShowErrorMessage("NuGet package must be defined using /P or /Package");
                    return -3;
                }

                if (string.IsNullOrWhiteSpace(mUpdateOptions.NuGetPackageVersion))
                {
                    ShowErrorMessage("NuGet package version must be defined using /V or /Version");
                    return -4;
                }

                success = SearchForProjectFiles(mSearchDirectoryPath, mRecurse, mUpdateOptions);

                if (!success)
                {
                    Thread.Sleep(1500);
                    return -1;
                }

                Console.WriteLine();
                Console.WriteLine("Search complete");

                Thread.Sleep(250);

            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error occurred in Program->Main", ex);
                Thread.Sleep(1500);
                return -1;
            }

            return 0;

        }

        private static bool IsUpdateRequired(string currentVersion, PackageUpdateOptions updateOptions, out Version parsedVersion)
        {
            parsedVersion = Version.Parse(currentVersion);

            if (parsedVersion < updateOptions.NewPackageVersion)
            {
                // Version in the project file is older; update it
                return true;
            }

            if (parsedVersion == updateOptions.NewPackageVersion)
            {
                // Already up-to-date
                ShowDebugMessage(string.Format("    version is already up-to-date: {0}", parsedVersion), 0);
                return false;
            }

            // Version in the project file is newer; only update if Rollback is enabled
            if (updateOptions.Rollback)
                return true;

            ShowWarning(string.Format("    referenced version {0} is newer than {1}; will not update",
                                      parsedVersion, updateOptions.NewPackageVersion), 0);
            return false;
        }

        private static void ProcessPackageConfigFile(FileSystemInfo packageConfigFile, string baseDirectoryPath, PackageUpdateOptions updateOptions)
        {
            try
            {
                // Open the packages.config file and look for XML like this:
                //
                //  <package id="PRISM-Library" version="2.5.10" targetFramework="net451" />

                var saveRequired = false;

                var doc = XDocument.Load(packageConfigFile.FullName);

                foreach (var packageRef in doc.Descendants().Where(p => p.Name.LocalName == "package"))
                {
                    if (!packageRef.HasAttributes)
                        continue;

                    var refName = (string)packageRef.Attribute("id");
                    if (refName == null)
                    {
                        // The package element does not have attribute id
                        continue;
                    }

                    if (!string.Equals(refName, updateOptions.NuGetPackageName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!mVerboseLogging)
                        ShowProcessingFileMessage(packageConfigFile, baseDirectoryPath);

                    // Examine the version
                    var versionAttribute = packageRef.Attribute("version");
                    if (versionAttribute == null)
                    {
                        // The package element does not have attribute version
                        ConsoleMsgUtils.ShowWarning(string.Format(
                                                        "package element has id=\"{0}\" but does not have version=\"x.y.z\": {1}",
                                                        updateOptions.NuGetPackageName, packageConfigFile.FullName));
                        continue;
                    }

                    // Found XML like this:
                    // <package id="PRISM-Library" version="2.5.10" targetFramework="net451" />

                    saveRequired = UpdateVersionAttributeIfRequired(versionAttribute, updateOptions);
                }

                if (updateOptions.Preview)
                    return;

                if (!saveRequired)
                    return;

                WriteXmlFile(packageConfigFile, doc);
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error processing file " + packageConfigFile.FullName + ": " + ex.Message);
            }
        }

        private static void ProcessProjectFile(FileSystemInfo projectFile, string baseDirectoryPath, PackageUpdateOptions updateOptions)
        {
            try
            {
                // Open the Visual Studio project file and look for XML like this:
                //
                //  <PackageReference Include="PRISM-Library">
                //     <Version>2.4.93</Version>
                //  </PackageReference>

                // Or like this
                //  <PackageReference Include="PRISM-Library" Version="2.4.93" />

                var saveRequired = false;

                var doc = XDocument.Load(projectFile.FullName);

                foreach (var packageRef in doc.Descendants().Where(p => p.Name.LocalName == "PackageReference"))
                {
                    if (!packageRef.HasAttributes)
                        continue;

                    var refName = (string)packageRef.Attribute("Include");
                    if (refName == null)
                    {
                        // The PackageReference element does not have attribute Include
                        continue;
                    }

                    if (!string.Equals(refName, updateOptions.NuGetPackageName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!mVerboseLogging)
                        ShowProcessingFileMessage(projectFile, baseDirectoryPath);

                    // Examine the version
                    var versionElementFound = false;

                    foreach (var element in packageRef.Elements())
                    {
                        if (element.Name.LocalName != "Version")
                            continue;

                        // Found XML like this:
                        // <PackageReference Include="PRISM-Library">
                        //   <Version>2.5.2</Version>
                        // </PackageReference>

                        saveRequired = UpdateVersionElementIfRequired(element, updateOptions);

                        versionElementFound = true;
                    }

                    if (versionElementFound || !packageRef.HasAttributes)
                        continue;

                    var versionAttribute = packageRef.Attribute("Version");
                    if (versionAttribute != null)
                    {

                        // Found XML like this:
                        // <PackageReference Include="PRISM-Library" Version="2.4.93" />

                        saveRequired = UpdateVersionAttributeIfRequired(versionAttribute, updateOptions);
                    }
                }

                if (updateOptions.Preview)
                    return;

                if (!saveRequired)
                    return;

                WriteXmlFile(projectFile, doc);
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error processing file " + projectFile.FullName + ": " + ex.Message);
            }
        }

        private static void ShowUpdateInfo(PackageUpdateOptions updateOptions, Version parsedVersion)
        {
            string updateVerb;
            if (updateOptions.Preview)
                updateVerb = "would update";
            else
                updateVerb = "updating";

            ShowWarning(string.Format("      {0} version from {1} to {2} for {3}",
                                      updateVerb,
                                      parsedVersion,
                                      updateOptions.NewPackageVersion,
                                      updateOptions.NuGetPackageName), 0);
        }

        /// <summary>
        /// Update formatting of XML tags to match the formatting that Visual Studio uses
        /// </summary>
        /// <param name="xmlFile"></param>
        private static void UpdateEmptyXMLTagFormatting(FileSystemInfo xmlFile)
        {

            try
            {
                // Reopen the file and add back line feeds to pairs of XML tags
                // that the XmlWriter puts on one line yet Visual Studio puts on two lines
                // For example, change from
                //    <FileUpgradeFlags></FileUpgradeFlags>
                // to
                //    <FileUpgradeFlags>
                //    </FileUpgradeFlags>

                var tempFile = new FileInfo(Path.Combine(Path.GetTempPath(), xmlFile.Name + ".tmp"));

                if (tempFile.Exists)
                    tempFile.Delete();

                var reEmptyNodePair = new Regex(@"^(?<Whitespace>\W*)<(?<OpenTag>[^>]+)></(?<CloseTag>[^>]+)>", RegexOptions.Compiled);

                var updateRequired = false;

                using (var reader = new StreamReader(new FileStream(xmlFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                using (var writer = new StreamWriter(new FileStream(tempFile.FullName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)))
                {
                    while (!reader.EndOfStream)
                    {
                        var lineIn = reader.ReadLine();
                        if (lineIn == null)
                            continue;

                        var match = reEmptyNodePair.Match(lineIn);
                        if (!match.Success)
                        {
                            writer.WriteLine(lineIn);
                            continue;
                        }

                        if (!string.Equals(match.Groups["OpenTag"].Value, match.Groups["CloseTag"].Value))
                        {
                            ShowWarning("Unbalanced XML tag pair: " + lineIn);
                            continue;
                        }

                        writer.WriteLine(match.Groups["Whitespace"].Value + "<" + match.Groups["OpenTag"].Value + ">");
                        writer.WriteLine(match.Groups["Whitespace"].Value + "</"+ match.Groups["CloseTag"].Value + ">");

                        updateRequired = true;
                    }
                }

                if (updateRequired)
                {
                    xmlFile.Delete();
                    tempFile.MoveTo(xmlFile.FullName);
                }
                else
                {
                    tempFile.Delete();
                }

            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error updating XML tag formatting in file " + xmlFile.FullName + ": " + ex.Message);
            }

        }

        private static bool UpdateVersionAttributeIfRequired(XAttribute versionAttribute, PackageUpdateOptions updateOptions)
        {
            var currentVersion = versionAttribute.Value;

            var updateVersion = IsUpdateRequired(currentVersion, updateOptions, out var parsedVersion);

            if (!updateVersion)
                return false;

            versionAttribute.Value = updateOptions.NewPackageVersion.ToString();

            ShowUpdateInfo(updateOptions, parsedVersion);

            return true;
        }

        private static bool UpdateVersionElementIfRequired(XElement element, PackageUpdateOptions updateOptions)
        {
            var currentVersion = element.Value;

            var updateVersion = IsUpdateRequired(currentVersion, updateOptions, out var parsedVersion);

            if (!updateVersion)
                return false;

            element.Value = updateOptions.NewPackageVersion.ToString();

            ShowUpdateInfo(updateOptions, parsedVersion);

            return true;
        }

        private static bool SearchForProjectFiles(
            string searchDirectoryPath,
            bool recurse,
            PackageUpdateOptions updateOptions)
        {

            try
            {
                var newPackageVersion = Version.Parse(updateOptions.NuGetPackageVersion);

                updateOptions.NewPackageVersion = newPackageVersion;
            }
            catch (Exception ex)
            {
                ShowErrorMessage(string.Format("Error parsing the NuGet Package Version '{0}', {1}", updateOptions.NuGetPackageVersion, ex.Message));
                return false;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(searchDirectoryPath))
                    searchDirectoryPath = ".";

                var searchDirectory = new DirectoryInfo(searchDirectoryPath);

                Console.WriteLine("Searching for Visual Studio projects referencing {0} to assure each uses version {1}",
                    updateOptions.NuGetPackageName, updateOptions.NuGetPackageVersion);

                if (recurse)
                    Console.WriteLine("Searching {0} and subdirectories", searchDirectory);
                else
                    Console.WriteLine("Only search {0} -- to recurse, use /S", searchDirectory);

                var baseDirectoryPath = searchDirectory.Parent?.FullName ?? string.Empty;
                mLastProgressTime = DateTime.Now;
                mProgressNewlineRequired = false;

                var success = SearchForProjectFiles(searchDirectory, baseDirectoryPath, recurse, updateOptions);

                return success;
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error instantiating a DirectoryInfo object for " + searchDirectoryPath + ": " + ex.Message);
                return false;
            }
        }

        private static bool SearchForProjectFiles(
            DirectoryInfo searchDirectory,
            string baseDirectoryPath,
            bool recurse,
            PackageUpdateOptions updateOptions)
        {

            try
            {

                var projectFiles = searchDirectory.GetFiles("*.csproj").ToList();
                projectFiles.AddRange(searchDirectory.GetFiles("*.vbproj"));

                if (recurse && DateTime.Now.Subtract(mLastProgressTime).TotalMilliseconds > 200)
                {
                    mLastProgressTime = DateTime.Now;
                    Console.Write(".");
                    mProgressNewlineRequired = true;
                }

                foreach (var projectFile in projectFiles)
                {
                    if (mProgressNewlineRequired)
                    {
                        Console.WriteLine();
                        mProgressNewlineRequired = false;
                    }

                    mLastProgressTime = DateTime.Now;

                    if (mVerboseLogging)
                        ShowProcessingFileMessage(projectFile, baseDirectoryPath);

                    ProcessProjectFile(projectFile, baseDirectoryPath, updateOptions);
                }

                var packageConfigFiles = searchDirectory.GetFiles("packages.config").ToList();
                foreach (var configFile in packageConfigFiles)
                {
                    if (mProgressNewlineRequired)
                    {
                        Console.WriteLine();
                        mProgressNewlineRequired = false;
                    }

                    mLastProgressTime = DateTime.Now;

                    if (mVerboseLogging)
                        ShowProcessingFileMessage(configFile, baseDirectoryPath);

                    ProcessPackageConfigFile(configFile, baseDirectoryPath, updateOptions);
                }

                if (!recurse)
                    return true;

                var successOverall = true;

                foreach (var subDirectory in searchDirectory.GetDirectories())
                {
                    var success = SearchForProjectFiles(subDirectory, baseDirectoryPath, true, updateOptions);

                    if (success)
                        continue;

                    if (mProgressNewlineRequired)
                    {
                        Console.WriteLine();
                        mProgressNewlineRequired = false;
                    }
                    ShowWarning("Error processing directory " + subDirectory.FullName + "; will continue searching");
                    successOverall = false;
                }

                return successOverall;
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error in SearchForProjectFiles: " + ex.Message, ex);
                return false;
            }
        }

        private static void WriteXmlFile(FileSystemInfo xmlFile, XDocument doc)
        {
            var settings = new XmlWriterSettings
            {
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace,
                Encoding = Encoding.UTF8,
                Indent = true,
                IndentChars = "  "
            };

            using (var writer = XmlWriter.Create(xmlFile.FullName, settings))
            {
                doc.Save(writer);
            }

            // Reopen the file and add back line feeds to pairs of XML tags
            // that the XmlWriter puts on one line yet Visual Studio puts on two lines

            UpdateEmptyXMLTagFormatting(xmlFile);
        }

        private static string GetAppVersion()
        {
            return PRISM.FileProcessor.ProcessFilesOrDirectoriesBase.GetAppVersion(PROGRAM_DATE);
        }

        private static bool SetOptionsUsingCommandLineParameters(clsParseCommandLine commandLineParse)
        {
            // Returns True if no problems; otherwise, returns false
            var lstValidParameters = new List<string> {
                "I", "P", "Package", "V", "Version", "Preview", "Apply", "Rollback", "S", "Verbose"};

            try
            {
                // Make sure no invalid parameters are present
                if (commandLineParse.InvalidParametersPresent(lstValidParameters))
                {
                    var badArguments = new List<string>();
                    foreach (var item in commandLineParse.InvalidParameters(lstValidParameters))
                    {
                        badArguments.Add("/" + item);
                    }

                    ShowErrorMessage("Invalid command line parameters", badArguments);

                    return false;
                }

                // Query commandLineParse to see if various parameters are present
                if (commandLineParse.NonSwitchParameterCount > 0)
                {
                    mSearchDirectoryPath = commandLineParse.RetrieveNonSwitchParameter(0);
                }


                if (commandLineParse.RetrieveValueForParameter("I", out var paramValue))
                {
                    mSearchDirectoryPath = string.Copy(paramValue);
                }

                if (commandLineParse.RetrieveValueForParameter("Package", out paramValue))
                {
                    mUpdateOptions.NuGetPackageName = paramValue;
                }
                else if (commandLineParse.RetrieveValueForParameter("P", out paramValue))
                {
                    mUpdateOptions.NuGetPackageName = paramValue;
                }

                if (commandLineParse.RetrieveValueForParameter("Version", out paramValue))
                {
                    mUpdateOptions.NuGetPackageVersion = paramValue;
                }
                else if (commandLineParse.RetrieveValueForParameter("V", out paramValue))
                {
                    mUpdateOptions.NuGetPackageVersion = paramValue;
                }

                if (commandLineParse.IsParameterPresent("Apply"))
                    mUpdateOptions.Preview = false;

                if (commandLineParse.IsParameterPresent("Preview"))
                    mUpdateOptions.Preview = true;

                if (commandLineParse.IsParameterPresent("Rollback"))
                    mUpdateOptions.Rollback = true;

                if (commandLineParse.IsParameterPresent("S"))
                    mRecurse = true;

                if (commandLineParse.IsParameterPresent("Verbose"))
                    mVerboseLogging = true;

                return true;
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error parsing the command line parameters", ex);
            }

            return false;
        }

        private static void ShowDebugMessage(string message, int emptyLinesBeforeMessage = 1)
        {
            ConsoleMsgUtils.ShowDebug(message, "  ", emptyLinesBeforeMessage);
        }

        private static void ShowErrorMessage(string message, Exception ex = null)
        {

            ConsoleMsgUtils.ShowError(message, ex);
        }

        private static void ShowErrorMessage(string title, IEnumerable<string> errorMessages)
        {
            ConsoleMsgUtils.ShowErrors(title, errorMessages);
        }

        private static void ShowProcessingFileMessage(FileSystemInfo projectFile, string baseDirectoryPath)
        {

            string projectFilePath;

            if (!string.IsNullOrWhiteSpace(baseDirectoryPath))
            {
                projectFilePath = projectFile.FullName.Substring(baseDirectoryPath.Length).TrimStart('\\');
            }
            else
            {
                projectFilePath = projectFile.FullName;
            }

            ShowDebugMessage("  processing " + projectFilePath);
        }

        private static void ShowProgramHelp()
        {
            var exeName = Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            try
            {
                Console.WriteLine();
                Console.WriteLine(ConsoleMsgUtils.WrapParagraph(
                                      "This program searches for Visual Studio project files (.csproj and.vsproj) " +
                                      "that reference a specific NuGet package and updates the referenced version " +
                                      "to a newer version if necessary.  It also updates packages.config files."));
                Console.WriteLine();
                Console.WriteLine("Program syntax:" + Environment.NewLine + exeName);
                Console.WriteLine(" DirectoryPath /Package:PackageName /Version:PackageVersion");
                Console.WriteLine(" [/S] [/Preview] [/Apply] [/Rollback] [/Verbose]");
                Console.WriteLine();
                Console.WriteLine(ConsoleMsgUtils.WrapParagraph(
                                      "DirectoryPath is the path to the directory to search for Visual Studio project files. " +
                                      "If DirectoryPath is not specified, the current directory is used"));
                Console.WriteLine();
                Console.WriteLine("Use /S to recurse subdirectories");
                Console.WriteLine();
                Console.WriteLine("Specify the NuGet package name using /Package or using /P");
                Console.WriteLine("Specify the NuGet package version using /Version or using /V");
                Console.WriteLine("");
                Console.WriteLine("By default will not update files (/Preview is implied)");
                Console.WriteLine("Use /Apply to save changes");
                Console.WriteLine();
                Console.WriteLine("Use /Rollback to downgrade versions if a newer version is found");
                Console.WriteLine();
                Console.WriteLine("Use /Verbose to see every Visual Studio project file processed");
                Console.WriteLine("Otherwise, only projects containing package PackageName will be shown");
                Console.WriteLine();
                Console.WriteLine("Program written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA) in 2017");
                Console.WriteLine("Version: " + GetAppVersion());
                Console.WriteLine();

                Console.WriteLine("E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov");
                Console.WriteLine("Website: https://omics.pnl.gov/ or https://panomics.pnnl.gov/");
                Console.WriteLine();

                // Delay for 750 msec in case the user double clicked this file from within Windows Explorer (or started the program via a shortcut)
                Thread.Sleep(750);

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error displaying the program syntax: " + ex.Message);
            }

        }

        private static void ShowWarning(string message, int emptyLinesBeforeMessage = 1)
        {

            ConsoleMsgUtils.ShowWarning(message, emptyLinesBeforeMessage);
        }

    }
}
