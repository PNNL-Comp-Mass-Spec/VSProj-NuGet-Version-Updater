using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// E-mail: matthew.monroe@pnnl.gov or matt@alchemistmatt.com
    /// Website: http://panomics.pnnl.gov/ or http://omics.pnl.gov or http://www.sysbio.org/resources/staff/
    /// </remarks>
    internal static class Program
    {
        public const string PROGRAM_DATE = "April 7, 2017";

        private struct udtPackageUpdateOptions
        {
            public string NuGetPackageName;
            public string NuGetPackageVersion;
            public bool Rollback;
            public bool Preview;

            public Version NewPackageVersion { get; set; }
        }

        private static string mSearchFolderPath;
        private static bool mRecurse;

        private static udtPackageUpdateOptions mUpdateOptions;

        private static bool mVerboseLogging;

        private static DateTime mLastProgressTime;

        private static bool mProgressNewlineRequired;


        static int Main(string[] args)
        {
            var objParseCommandLine = new clsParseCommandLine();

            mSearchFolderPath = ".";
            mRecurse = false;

            mUpdateOptions = new udtPackageUpdateOptions
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

                if (objParseCommandLine.ParseCommandLine())
                {
                    if (SetOptionsUsingCommandLineParameters(objParseCommandLine))
                        success = true;
                }

                if (!success ||
                    objParseCommandLine.NeedToShowHelp)
                {
                    ShowProgramHelp();
                    return -1;
                }

                if (string.IsNullOrWhiteSpace(mSearchFolderPath))
                {
                    mSearchFolderPath = ".";
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

                success = SearchForProjectFiles(mSearchFolderPath, mRecurse, mUpdateOptions);

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

        private static void ProcessProjectFile(FileInfo projectFile, string baseFolderPath, udtPackageUpdateOptions updateOptions)
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
                        ShowProcessingFileMessage(projectFile, baseFolderPath);

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

                using (var writer = new XmlTextWriter(projectFile.FullName, Encoding.UTF8))
                {
                    writer.Formatting = Formatting.Indented;
                    doc.Save(writer);
                }

            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error processing " + projectFile.FullName + ": " + ex.Message);
            }
        }

        private static bool SearchForProjectFiles(
            string searchFolderPath,
            bool recurse,
            udtPackageUpdateOptions updateOptions)
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
                if (string.IsNullOrWhiteSpace(searchFolderPath))
                    searchFolderPath = ".";

                var searchFolder = new DirectoryInfo(searchFolderPath);

                Console.WriteLine("Searching for Visual Studio projects referencing {0} to assure each uses version {1}",
                    updateOptions.NuGetPackageName, updateOptions.NuGetPackageVersion);

                if (recurse)
                    Console.WriteLine("Searching {0} and subdirectories", searchFolder);
                else
                    Console.WriteLine("Only search {0} -- to recurse, use /S", searchFolder);

                var baseFolderPath = searchFolder.Parent?.FullName ?? string.Empty;
                mLastProgressTime = DateTime.Now;
                mProgressNewlineRequired = false;

                var success = SearchForProjectFiles(searchFolder, baseFolderPath, recurse, updateOptions);

                return success;
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error instantiating a DirectoryInfo object for " + searchFolderPath + ": " + ex.Message);
                return false;
            }
        }

        private static bool SearchForProjectFiles(
            DirectoryInfo searchFolder,
            string baseFolderPath,
            bool recurse,
            udtPackageUpdateOptions updateOptions)
        {

            try
            {

                var projectFiles = searchFolder.GetFiles("*.csproj").ToList();
                projectFiles.AddRange(searchFolder.GetFiles("*.vbproj"));

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
                        ShowProcessingFileMessage(projectFile, baseFolderPath);

                    ProcessProjectFile(projectFile, baseFolderPath, updateOptions);
                }

                if (!recurse)
                    return true;

                var successOverall = true;

                foreach (var subDirectory in searchFolder.GetDirectories())
                {
                    var success = SearchForProjectFiles(subDirectory, baseFolderPath, true, updateOptions);

                    if (success)
                        continue;

                    if (mProgressNewlineRequired)
                    {
                        Console.WriteLine();
                        mProgressNewlineRequired = false;
                    }
                    ShowWarning("Error processing " + subDirectory.FullName + "; will continue searching");
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

        private static bool SetOptionsUsingCommandLineParameters(clsParseCommandLine objParseCommandLine)
        {
            // Returns True if no problems; otherwise, returns false
            var lstValidParameters = new List<string> {
                "I", "P", "Package", "V", "Version", "Apply", "Rollback", "S", "Verbose"};

            try
            {
                // Make sure no invalid parameters are present
                if (objParseCommandLine.InvalidParametersPresent(lstValidParameters))
                {
                    var badArguments = new List<string>();
                    foreach (var item in objParseCommandLine.InvalidParameters(lstValidParameters))
                    {
                        badArguments.Add("/" + item);
                    }

                    ShowErrorMessage("Invalid commmand line parameters", badArguments);

                    return false;
                }

                // Query objParseCommandLine to see if various parameters are present
                if (objParseCommandLine.NonSwitchParameterCount > 0)
                {
                    mSearchFolderPath = objParseCommandLine.RetrieveNonSwitchParameter(0);
                }


                string paramValue;

                if (objParseCommandLine.RetrieveValueForParameter("I", out paramValue))
                {
                    mSearchFolderPath = string.Copy(paramValue);
                }

                if (objParseCommandLine.RetrieveValueForParameter("Package", out paramValue))
                {
                    mUpdateOptions.NuGetPackageName = paramValue;
                }
                else if (objParseCommandLine.RetrieveValueForParameter("P", out paramValue))
                {
                    mUpdateOptions.NuGetPackageName = paramValue;
                }

                if (objParseCommandLine.RetrieveValueForParameter("Version", out paramValue))
                {
                    mUpdateOptions.NuGetPackageVersion = paramValue;
                }
                else if (objParseCommandLine.RetrieveValueForParameter("V", out paramValue))
                {
                    mUpdateOptions.NuGetPackageVersion = paramValue;
                }

                if (objParseCommandLine.IsParameterPresent("Apply"))
                    mUpdateOptions.Preview = false;

                if (objParseCommandLine.IsParameterPresent("Rollback"))
                    mUpdateOptions.Rollback = true;

                if (objParseCommandLine.IsParameterPresent("S"))
                    mRecurse = true;

                if (objParseCommandLine.IsParameterPresent("Verbose"))
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
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        private static void ShowErrorMessage(string message, Exception ex = null)
        {

            const string separator = "------------------------------------------------------------------------------";

            Console.WriteLine();
            Console.WriteLine(separator);

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);

            if (ex != null)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(PRISM.clsStackTraceFormatter.GetExceptionStackTraceMultiLine(ex));
            }

            Console.ResetColor();
            Console.WriteLine(separator);
            Console.WriteLine();

            WriteToErrorStream(message);
        }

        private static void ShowErrorMessage(string title, IEnumerable<string> items)
        {
            const string separator = "------------------------------------------------------------------------------";

            Console.WriteLine();
            Console.WriteLine(separator);

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(title);
            var message = title + ":";

            foreach (var item in items)
            {
                Console.WriteLine("   " + item);
                message += " " + item;
            }

            Console.ResetColor();
            Console.WriteLine(separator);
            Console.WriteLine();

            WriteToErrorStream(message);
        }

        private static void ShowProcessingFileMessage(FileInfo projectFile, string baseFolderPath)
        {

            string projectFilePath;

            if (!string.IsNullOrWhiteSpace(baseFolderPath))
            {
                projectFilePath = projectFile.FullName.Substring(baseFolderPath.Length).TrimStart('\\');
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
                Console.WriteLine("This program searches for Visual Studio project files (.csproj and.vsproj) " +
                                  "that reference a specific NuGet package and updates the referenced version " +
                                  "to a newer version if necessary");
                Console.WriteLine();
                Console.WriteLine("Program syntax:" + Environment.NewLine + exeName);
                Console.WriteLine(" FolderPath /Package:PackageName /Version:PackageVersion");
                Console.WriteLine(" [/S] [/Apply] [/Rollback] [/Verbose]");
                Console.WriteLine();
                Console.WriteLine("FolderPath is the path to the folder to search for Visual Studio project files");
                Console.WriteLine("If FolderPath is not specified, the current folder is used");
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

                Console.WriteLine("E-mail: matthew.monroe@pnnl.gov or matt@alchemistmatt.com");
                Console.WriteLine("Website: http://panomics.pnnl.gov/ or http://omics.pnl.gov or http://www.sysbio.org/resources/staff/");
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

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        private static void WriteToErrorStream(string errorMessage)
        {
            try
            {
                using (var swErrorStream = new System.IO.StreamWriter(Console.OpenStandardError()))
                {
                    swErrorStream.WriteLine(errorMessage);
                }
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch
            {
                // Ignore errors here
            }
        }

    }
}
