// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Cli.Commands;
using Microsoft.DotNet.Cli.Commands.Test;
using Microsoft.DotNet.Cli.CommandLine;
using Microsoft.DotNet.Cli.Utils;
using TestCommand = Microsoft.DotNet.Cli.Commands.Test.TestCommand;

namespace Microsoft.DotNet.Cli.Test.Tests;

[TestClass]
public class FastBuildCommandTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void BothRunnersUseBuildXOnlyForTestX(bool useMtp)
    {
        TestCommandDefinition definition = useMtp
            ? new TestCommandDefinition.MicrosoftTestingPlatform()
            : new TestCommandDefinition.VSTest();
        TestCommandDefinition testXDefinition = useMtp
            ? new TestCommandDefinition.MicrosoftTestingPlatform(useFastBuild: true)
            : new TestCommandDefinition.VSTest(useFastBuild: true);

        definition.Name.Should().Be("test");
        definition.UseFastBuild.Should().BeFalse();
        testXDefinition.Name.Should().Be("testx");
        testXDefinition.UseFastBuild.Should().BeTrue();
        definition.Options.Should().NotContain(option => option.Name == "--fastbuild");
        testXDefinition.Options.Should().NotContain(option => option.Name == "--fastbuild");

        var result = testXDefinition.Parse([]);
        result.Errors.Should().BeEmpty();
        result.UnmatchedTokens.Should().BeEmpty();
        result.OptionValuesToBeForwarded(testXDefinition).Should().NotContain("--fastbuild");
    }

    [TestMethod]
    [DataRow("VSTest", false)]
    [DataRow("VSTest", true)]
    [DataRow("Microsoft.Testing.Platform", false)]
    [DataRow("Microsoft.Testing.Platform", true)]
    public void CommandFactoryPreservesRunnerSelection(string runner, bool useFastBuild)
    {
        var definition = TestCommandDefinition.Create(Environment.CurrentDirectory, runner, useFastBuild);

        definition.UseFastBuild.Should().Be(useFastBuild);
        definition.Name.Should().Be(useFastBuild ? "testx" : "test");
        (definition is TestCommandDefinition.MicrosoftTestingPlatform)
            .Should().Be(runner == "Microsoft.Testing.Platform");
    }

    [TestMethod]
    public void TestXIsRegisteredSeparatelyFromTest()
    {
        var root = new DotNetCommandDefinition();

        root.Parse(["test"]).CommandResult.Command.Should().BeSameAs(root.TestCommand);
        root.Parse(["testx"]).CommandResult.Command.Should().BeSameAs(root.TestXCommand);
        root.TestCommand.UseFastBuild.Should().BeFalse();
        root.TestXCommand.UseFastBuild.Should().BeTrue();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NoBuildTakesPrecedenceOverFastBuild(bool noBuild)
    {
        var definition = new TestCommandDefinition.MicrosoftTestingPlatform(useFastBuild: true);
        var result = definition.Parse(noBuild ? ["--no-build"] : []);
        var options = TestCommandOptions.GetBuildOptions(result);

        options.UseFastBuild.Should().Be(!noBuild);
        options.TestApplicationArguments.Should().NotContain("--fastbuild");
        options.MSBuildArgs.Should().NotContain("--fastbuild");

        var vsDefinition = new TestCommandDefinition.VSTest(useFastBuild: true);
        var command = TestCommand.FromParseResult(
            vsDefinition.Parse(noBuild ? ["--no-build"] : []), [], "test-session", []);
        command.GetArgumentsToMSBuild().Should().Contain("-property:VSTestNoBuild=true");
        command.GetArgumentsToMSBuild().Should().NotContain("-restore");
        command.SeparateRestoreCommand.Should().BeNull();
    }

    [TestMethod]
    public void StandardBuildIsUnchangedWithoutTheSwitch()
    {
        var command = TestCommand.FromArgs([]);

        command.GetArgumentsToMSBuild().Should().NotContain("VSTestNoBuild");
        command.GetArgumentsToMSBuild().Should().Contain("-restore");
        var definition = new TestCommandDefinition.MicrosoftTestingPlatform();
        TestCommandOptions.GetBuildOptions(definition.Parse([])).UseFastBuild.Should().BeFalse();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FastBuildReceivesBuildPropertiesVerbosityAndRestorePolicy(bool noRestore)
    {
        MSBuildArgs buildArgs = Analyze(
            "-p:Configuration=Release",
            "-p:TargetFramework=net8.0",
            "-p:RuntimeIdentifier=linux-x64",
            "-p:OutputPath=output with spaces",
            "-p:BuildProjectReferences=false",
            "-v:detailed",
            "-t:_MTPBuild",
            "-bl");

        string[] arguments = FastBuildCommand.CreateArguments("Tests.csproj", buildArgs, noRestore, "_MTPBuild");

        arguments.Should().Equal(
            new[] { Path.GetFullPath("Tests.csproj"), "--msbuild-engine", "dotnet", "--no-server" }
                .Concat(noRestore ? ["--no-restore"] : Array.Empty<string>())
                .Concat([
                    "--verbosity", "detailed",
                    "-p:Configuration=Release",
                    "-p:TargetFramework=net8.0",
                    "-p:RuntimeIdentifier=linux-x64",
                    "-p:OutputPath=output with spaces",
                    "-p:BuildProjectReferences=false",
                    "--binlog"
                ]));
    }

    [TestMethod]
    [DataRow("-bl:custom.binlog")]
    [DataRow("-m:1")]
    [DataRow("-rp:RestoreSources=custom")]
    [DataRow("-t:Clean")]
    public void UnsupportedBuildArgumentsFailExplicitly(string argument)
    {
        Action action = () => FastBuildCommand.CreateArguments("Tests.csproj", Analyze(argument), noRestore: false);

        action.Should().Throw<GracefulException>().WithMessage("*--fastbuild*");
    }

    [TestMethod]
    public void FileBasedAppsFailExplicitly()
    {
        Action action = () => FastBuildCommand.CreateArguments("Tests.cs", Analyze(), noRestore: false);

        action.Should().Throw<GracefulException>().WithMessage("*project or solution*");
    }

    [TestMethod]
    public void NoRestoreConsumesTheCliForwardingToken()
    {
        string[] arguments = FastBuildCommand.CreateArguments("Tests.csproj", Analyze("-restore:false"), noRestore: true);

        arguments.Should().ContainSingle(argument => argument == "--no-restore");
        arguments.Should().NotContain("-restore:false");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TestModulesRequireNoBuildToIgnoreFastBuild(bool noBuild)
    {
        var definition = new TestCommandDefinition.MicrosoftTestingPlatform(useFastBuild: true);
        var result = definition.Parse(noBuild
            ? ["--test-modules", "*.dll", "--no-build"]
            : ["--test-modules", "*.dll"]);
        var options = TestCommandOptions.GetBuildOptions(result);
        Action action = () => ValidationUtility.ValidateMutuallyExclusiveOptions(result, options.PathOptions);

        if (noBuild)
        {
            action.Should().NotThrow();
        }
        else
        {
            action.Should().Throw<GracefulException>().WithMessage("*project or solution*");
        }
    }

    private static MSBuildArgs Analyze(params string[] arguments)
        => MSBuildArgs.AnalyzeMSBuildArguments(
            arguments,
            CommonOptions.CreatePropertyOption(),
            CommonOptions.CreateRestorePropertyOption(),
            CommonOptions.CreateMSBuildTargetOption(),
            CommonOptions.CreateVerbosityOption(),
            CommonOptions.CreateNoLogoOption());
}
