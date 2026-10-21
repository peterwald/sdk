// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.Cli.Commands;

internal static class ProjectOrSolutionFileResolver
{
    public static (bool SolutionOrProjectFileFound, string Message) TryGetProjectOrSolutionFilePath(string directory, out string projectOrSolutionFilePath, out bool isSolution)
    {
        projectOrSolutionFilePath = string.Empty;
        isSolution = false;

        if (!Directory.Exists(directory))
        {
            return (false, string.Format(CliCommandStrings.CmdNonExistentDirectoryErrorDescription, directory));
        }

        var actualSolutionFiles = GetSolutionFilePaths(directory);
        var solutionFilterFiles = GetSolutionFilterFilePaths(directory);
        var actualProjectFiles = Directory.GetFiles(directory, "*.*proj", SearchOption.TopDirectoryOnly);

        // Match MSBuild's implicit project/solution selection.
        if (actualProjectFiles.Length == 1 && actualSolutionFiles.Length == 1)
        {
            string solutionName = Path.GetFileNameWithoutExtension(actualSolutionFiles[0]);
            string projectName = Path.GetFileNameWithoutExtension(actualProjectFiles[0]);

            if (!string.Equals(solutionName, projectName))
            {
                return (false, CliCommandStrings.CmdMultipleProjectOrSolutionFilesErrorDescription);
            }

            projectOrSolutionFilePath = actualSolutionFiles[0];
            isSolution = true;
        }
        else if (actualSolutionFiles.Length > 1)
        {
            return (false, string.Format(CliStrings.MoreThanOneSolutionInDirectory, directory));
        }
        else if (actualProjectFiles.Length > 1)
        {
            bool isAmbiguousProject = true;

            if (actualProjectFiles.Length == 2)
            {
                string firstPotentialProjectExtension = Path.GetExtension(actualProjectFiles[0]);
                string secondPotentialProjectExtension = Path.GetExtension(actualProjectFiles[1]);

                if (!string.Equals(firstPotentialProjectExtension, secondPotentialProjectExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(firstPotentialProjectExtension, ".proj", StringComparison.OrdinalIgnoreCase))
                    {
                        projectOrSolutionFilePath = actualProjectFiles[0];
                        isAmbiguousProject = false;
                    }
                    else if (string.Equals(secondPotentialProjectExtension, ".proj", StringComparison.OrdinalIgnoreCase))
                    {
                        projectOrSolutionFilePath = actualProjectFiles[1];
                        isAmbiguousProject = false;
                    }
                }
            }

            if (isAmbiguousProject)
            {
                return (false, string.Format(CliStrings.MoreThanOneProjectInDirectory, directory));
            }
        }
        else if (actualProjectFiles.Length == 0 &&
                 actualSolutionFiles.Length == 0 &&
                 solutionFilterFiles.Length == 0)
        {
            return (false, CliCommandStrings.CmdNoProjectOrSolutionFileErrorDescription);
        }
        else
        {
            projectOrSolutionFilePath = actualSolutionFiles.Length == 1 ? actualSolutionFiles[0] : actualProjectFiles.Length == 1 ? actualProjectFiles[0] : solutionFilterFiles[0];
            isSolution = actualSolutionFiles.Length == 1 || (actualProjectFiles.Length != 1 && solutionFilterFiles.Length == 1);
            if (actualSolutionFiles.Length != 1 &&
                actualProjectFiles.Length != 1 &&
                solutionFilterFiles.Length != 1)
            {
                return (false, CliCommandStrings.CmdMultipleProjectOrSolutionFilesErrorDescription);
            }
        }

        return (true, string.Empty);
    }

    internal static string[] GetSolutionFilePaths(string directory)
        => GetFilesWithExtensions(directory, ".sln", ".slnx");

    internal static string[] GetSolutionFilterFilePaths(string directory)
        => GetFilesWithExtensions(directory, ".slnf");

    private static string[] GetFilesWithExtensions(string directory, params string[] extensions)
        => [.. Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))];
}
