// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Cli.CommandFactory;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.FileBasedPrograms;

namespace Microsoft.DotNet.Cli.Commands.Test;

internal static class FastBuildCommand
{
    internal static string[] CreateArguments(string filePath, MSBuildArgs buildArgs, bool noRestore)
    {
        if (VirtualProjectBuilder.IsValidEntryPointPath(filePath, requireFileToExist: false))
        {
            throw new GracefulException(CliCommandStrings.TestFastBuildRequiresProject);
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

        foreach (string target in buildArgs.RequestedTargets ?? [])
        {
            if (!target.Equals("VSTest", StringComparison.OrdinalIgnoreCase) &&
                !target.Equals(TestCommandDefinition.MicrosoftTestingPlatform.BuildTargetName, StringComparison.OrdinalIgnoreCase))
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
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        string[] arguments = CreateArguments(filePath, buildArgs, noRestore);
        Utils.Command command;
        try
        {
            command = CommandFactoryUsingResolver.Create("dotnet-fastbuild", arguments);
        }
        catch (CommandUnknownException)
        {
            throw new GracefulException(CliCommandStrings.TestFastBuildToolNotFound);
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
        => new(CliCommandStrings.TestFastBuildUnsupportedArgument, argument);
}
