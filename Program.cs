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
    /// to a newer version if necessary
    /// </summary>
    /// <remarks>
    /// Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA)
    ///
    /// E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov
    /// Website: https://omics.pnl.gov/ or https://panomics.pnnl.gov/
    /// </remarks>
    internal static class Program
    {
        public const string PROGRAM_DATE = "October 17, 2017";

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
                Console.WriteLine("Error occurred in Program->Main: " + Environment.NewLine + ex.Message);
                Console.WriteLine(ex.StackTrace);
                Thread.Sleep(1500);
                return -1;
            }

            return 0;

        }

        private static void ProcessProjectFile(FileSystemInfo projectFile, string baseFolderPath, udtPackageUpdateOptions updateOptions)
        {
            try
            {
                // Open the Visual Studio project file and look for XML like this:
                //
                //  <PackageReference Include="PRISM-Library">
                //     <Version>1.0.2</Version>
                //  </PackageReference>

                var saveRequired = false;

                var doc = XDocument.Load(projectFile.FullName);

                foreach (var packageRef in doc.Descendants().Where(p => p.Name.LocalName == "PackageReference"))
                {
                    if (!packageRef.HasAttributes || !packageRef.HasElements)
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

                    foreach (var element in packageRef.Elements())
                    {
                        if (element.Name.LocalName != "Version")
                            continue;

                        var currentVersion = element.Value;
                        var parsedVersion = Version.Parse(currentVersion);
                        var updateVersion = false;

                        if (parsedVersion < updateOptions.NewPackageVersion)
                        {
                            // Version in the project file is older; update it
                            updateVersion = true;

                        }
                        else if (parsedVersion == updateOptions.NewPackageVersion)
                        {
                            // Already up-to-date
                            ShowDebugMessage(string.Format("    version is already up-to-date: {0}", parsedVersion));
                        }
                        else
                        {
                            // Version in the project file is newer; only update if Rollback is enabled
                            if (updateOptions.Rollback)
                                updateVersion = true;
                            else
                                ShowWarning(string.Format("    referenced version {0} is newer than {1}; will not update",
                                    parsedVersion, updateOptions.NewPackageVersion));
                        }

                        if (!updateVersion)
                            continue;

                        element.Value = updateOptions.NewPackageVersion.ToString();

                        string updateVerb;
                        if (updateOptions.Preview)
                            updateVerb = "would update";
                        else
                            updateVerb = "updating";

                        ShowWarning(string.Format("    {0} version from {1} to {2} for {3}",
                                                       updateVerb,
                                                       parsedVersion,
                                                       updateOptions.NewPackageVersion,
                                                       updateOptions.NuGetPackageName));

                        saveRequired = true;
                    }
                }

                if (updateOptions.Preview)
                    return;

                if (!saveRequired)
                    return;

                var settings = new XmlWriterSettings
                {
                    NewLineChars = "\r\n",
                    NewLineHandling = NewLineHandling.Replace,
                    Encoding = Encoding.UTF8,
                    Indent = true,
                    IndentChars = "  "
                };

                using (var writer = XmlWriter.Create(projectFile.FullName, settings))
                {
                    doc.Save(writer);
                }

                // Reopen the file and add back linefeeds to pairs of XML tags
                // that the XmlWriter puts on one line yet Visual Studio puts on two lines

                UpdateEmptyXMLTagFormatting(projectFile);

            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error processing file " + projectFile.FullName + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Update formatting of XML tags to match the formatting that Visual Studio uses
        /// </summary>
        /// <param name="projectFile"></param>
        private static void UpdateEmptyXMLTagFormatting(FileSystemInfo projectFile)
        {

            try
            {
                // Reopen the file and add back linefeeds to pairs of XML tags
                // that the XmlWriter puts on one line yet Visual Studio puts on two lines
                // For example, change from
                //    <FileUpgradeFlags></FileUpgradeFlags>
                // to
                //    <FileUpgradeFlags>
                //    </FileUpgradeFlags>

                var tempFile = new FileInfo(Path.Combine(Path.GetTempPath(), projectFile.Name + ".tmp"));

                if (tempFile.Exists)
                    tempFile.Delete();

                var reEmptyNodePair = new Regex(@"^(?<Whitespace>\W*)<(?<OpenTag>[^>]+)></(?<CloseTag>[^>]+)>", RegexOptions.Compiled);

                var updateRequired = false;

                using (var reader = new StreamReader(new FileStream(projectFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
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
                    projectFile.Delete();
                    tempFile.MoveTo(projectFile.FullName);
                }
                else
                {
                    tempFile.Delete();
                }

            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error updating XML tag formatting in file " + projectFile.FullName + ": " + ex.Message);
            }

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

        private static string GetAppVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + " (" + PROGRAM_DATE + ")";
        }

        private static bool SetOptionsUsingCommandLineParameters(clsParseCommandLine commandLineParse)
        {
            // Returns True if no problems; otherwise, returns false
            var lstValidParameters = new List<string> {
                "I", "P", "Package", "V", "Version", "Apply", "Rollback", "S", "Verbose"};

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
                ShowErrorMessage("Error parsing the command line parameters: " + Environment.NewLine + ex.Message);
            }

            return false;
        }

        private static void ShowDebugMessage(string message)
        {
            ConsoleMsgUtils.ShowDebug(message);
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
                                      "to a newer version if necessary"));
                Console.WriteLine();
                Console.WriteLine("Program syntax:" + Environment.NewLine + exeName);
                Console.WriteLine(" DirectoryPath /Package:PackageName /Version:PackageVersion");
                Console.WriteLine(" [/S] [/Apply] [/Rollback] [/Verbose]");
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
                Console.WriteLine("By default will not update files; use /Apply to save changes");
                Console.WriteLine("Use /Rollback to downgrade versions if a newer version is found");
                Console.WriteLine();
                Console.WriteLine("Use /Verbose to see every visual studio project file processed");
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

        private static void ShowWarning(string message)
        {

            ConsoleMsgUtils.ShowWarning(message);
        }


    }
}
