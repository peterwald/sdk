// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using Microsoft.DotNet.Cli.CommandLine;
using Microsoft.DotNet.Cli.Commands.Restore;
using Microsoft.DotNet.Cli.Commands.Run;
using Microsoft.DotNet.Cli.Extensions;
using Microsoft.DotNet.Cli.Utils;

namespace Microsoft.DotNet.Cli.Commands.Build;

public static class BuildCommand
{
    public static CommandBase FromArgs(string[] args, string? msbuildPath, CommandServices services)
    {
        var parseResult = Parser.Parse(["dotnet", "build", .. args]);
        return FromParseResult(parseResult, msbuildPath, services);
    }

    public static CommandBase FromParseResult(ParseResult parseResult, string? msbuildPath, CommandServices services)
    {
        var definition = (BuildCommandDefinition)parseResult.CommandResult.Command;

        parseResult.ShowHelpOrErrorIfAppropriate();

        CommonOptions.ValidateSelfContainedOptions(
            parseResult.HasOption(definition.SelfContainedOption),
            parseResult.HasOption(definition.NoSelfContainedOption));

        bool noRestore = parseResult.HasOption(definition.NoRestoreOption);
        bool useFastBuild = parseResult.GetValue(definition.FastBuildOption);

        return DotNetCommandFactory.CreateVirtualOrPhysicalCommand(
            definition,
            definition.SlnOrProjectOrFileArgument,
            createVirtualCommand: (msbuildArgs, appFilePath) =>
            {
                if (useFastBuild)
                {
                    throw new GracefulException(CliCommandStrings.FastBuildRequiresProject);
                }

                return new VirtualProjectBuildingCommand(
                    entryPointFileFullPath: Path.GetFullPath(appFilePath),
                    msbuildArgs: msbuildArgs,
                    services: services)
                {
                    NoRestore = noRestore,
                    NoCache = true,
                };
            },
            createPhysicalCommand: (msbuildArgs, msbuildPath) =>
            {
                if (useFastBuild)
                {
                    if (msbuildPath is not null)
                    {
                        throw new GracefulException(CliCommandStrings.FastBuildUnsupportedArgument, msbuildPath);
                    }

                    return new FastBuildCommand(msbuildArgs, noRestore, services);
                }

                return new RestoringCommand(
                    msbuildArgs: msbuildArgs.CloneWithAdditionalArgs("-consoleloggerparameters:Summary"),
                    noRestore: noRestore,
                    msbuildPath: msbuildPath,
                    userProfileDir: null,
                    advertiseWorkloadUpdates: null,
                    services: services);
            },
            optionsToUseWhenParsingMSBuildFlags:
            [
                CommonOptions.CreatePropertyOption(),
                CommonOptions.CreateRestorePropertyOption(),
                CommonOptions.CreateMSBuildTargetOption(),
                CommonOptions.CreateVerbosityOption(),
                CommonOptions.CreateNoLogoOption()
            ],
            parseResult,
            msbuildPath
        );
    }

    public static int Run(ParseResult parseResult)
    {
        parseResult.HandleDebugSwitch();

        return FromParseResult(parseResult, msbuildPath: null, services: CommandServices.CreateDefault()).Execute();
    }
}
