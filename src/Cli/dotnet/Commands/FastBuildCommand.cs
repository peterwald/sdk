// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Cli.CommandFactory;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.FileBasedPrograms;

namespace Microsoft.DotNet.Cli.Commands;

internal sealed class FastBuildCommand(MSBuildArgs buildArgs, bool noRestore, CommandServices services) : CommandBase(services)
{
    public override int Execute() => Run(GetArguments());

    internal string[] GetArguments()
    {
        string[] paths = [.. buildArgs.OtherMSBuildArgs.Where(argument =>
            !argument.StartsWith('-') &&
            (!argument.StartsWith('/') || File.Exists(argument) || Directory.Exists(argument)))];
        if (paths.Length > 1)
        {
            throw new GracefulException(CliCommandStrings.FastBuildRequiresProject);
        }

        string filePath = paths.SingleOrDefault() ?? Directory.GetCurrentDirectory();
        if (Directory.Exists(filePath))
        {
            var (found, message) = ProjectOrSolutionFileResolver.TryGetProjectOrSolutionFilePath(filePath, out filePath, out _);
            if (!found)
            {
                throw new GracefulException(message);
            }
        }

        string[] arguments = CreateArguments(
            filePath,
            buildArgs.CloneWithExplicitArgs([.. buildArgs.OtherMSBuildArgs.Except(paths)]),
            noRestore);
        if (!File.Exists(filePath))
        {
            throw new GracefulException(CliCommandStrings.CmdNonExistentFileErrorDescription, Path.GetFullPath(filePath));
        }

        return arguments;
    }

    internal static string[] CreateArguments(string filePath, MSBuildArgs buildArgs, bool noRestore, string? requiredTarget = null)
    {
        string extension = Path.GetExtension(filePath);
        if (VirtualProjectBuilder.IsValidEntryPointPath(filePath, requireFileToExist: false) ||
            (!extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase) &&
             !extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) &&
             !extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) &&
             !extension.Equals(".slnf", StringComparison.OrdinalIgnoreCase)))
        {
            throw new GracefulException(CliCommandStrings.FastBuildRequiresProject);
        }

        List<string> arguments = [Path.GetFullPath(filePath), "--msbuild-engine", "dotnet", "--no-server"];
        if (noRestore)
        {
            arguments.Add("--no-restore");
        }

        if (buildArgs.Verbosity is { } verbosity)
        {
            arguments.AddRange(["--verbosity", verbosity.ToString().ToLowerInvariant()]);
        }

        foreach (var (name, value) in buildArgs.GlobalProperties?.AsEnumerable() ?? [])
        {
            arguments.Add($"-p:{name}={value}");
        }

        if (buildArgs.RestoreGlobalProperties is { Count: > 0 })
        {
            throw UnsupportedArgument("--restoreProperty");
        }

        if (buildArgs.GetProperty is { Length: > 0 })
        {
            throw UnsupportedArgument("--getProperty");
        }

        if (buildArgs.GetItem is { Length: > 0 })
        {
            throw UnsupportedArgument("--getItem");
        }

        if (buildArgs.GetTargetResult is { Length: > 0 })
        {
            throw UnsupportedArgument("--getTargetResult");
        }

        if (buildArgs.GetResultOutputFile is { Length: > 0 })
        {
            throw UnsupportedArgument("--getResultOutputFile");
        }

        foreach (string target in buildArgs.RequestedTargets ?? [])
        {
            if (!target.Equals(requiredTarget, StringComparison.OrdinalIgnoreCase))
            {
                throw UnsupportedArgument($"--target:{target}");
            }
        }

        foreach (string argument in buildArgs.OtherMSBuildArgs)
        {
            if (noRestore && argument.Equals("-restore:false", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (argument.Equals("-bl", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("/bl", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--binaryLogger", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("-binaryLogger", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("/binaryLogger", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("--binlog");
            }
            else
            {
                throw UnsupportedArgument(argument);
            }
        }

        return [.. arguments];
    }

    internal static int Run(
        string filePath,
        MSBuildArgs buildArgs,
        bool noRestore,
        string? requiredTarget = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
        => Run(CreateArguments(filePath, buildArgs, noRestore, requiredTarget), environmentVariables);

    private static int Run(string[] arguments, IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        Utils.Command command;
        try
        {
            command = CommandFactoryUsingResolver.Create("dotnet-fastbuild", arguments);
        }
        catch (CommandUnknownException)
        {
            throw new GracefulException(CliCommandStrings.FastBuildToolNotFound);
        }

        foreach (var (name, value) in environmentVariables?.AsEnumerable() ?? [])
        {
            command.EnvironmentVariable(name, value);
        }

        // FastBuild also invokes dotnet through PATH for cold builds and restore.
        string muxerPath = new Muxer().MuxerPath;
        string? path = environmentVariables is not null && environmentVariables.TryGetValue("PATH", out string? explicitPath)
            ? explicitPath
            : Environment.GetEnvironmentVariable("PATH");
        return command
            .EnvironmentVariable("FASTBUILD_SDK_PATH", SdkPaths.SdkDirectory)
            .EnvironmentVariable("DOTNET_HOST_PATH", muxerPath)
            .EnvironmentVariable("PATH", Path.GetDirectoryName(muxerPath) + Path.PathSeparator + path)
            .Execute().ExitCode;
    }

    private static GracefulException UnsupportedArgument(string argument)
        => new(CliCommandStrings.FastBuildUnsupportedArgument, argument);
}
