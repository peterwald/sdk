// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Cli.CommandLine;
using Microsoft.DotNet.Cli.Commands;
using Microsoft.DotNet.Cli.Commands.Build;
using Microsoft.DotNet.Cli.Commands.Restore;
using Microsoft.DotNet.Cli.Utils;
using BuildCommand = Microsoft.DotNet.Cli.Commands.Build.BuildCommand;

namespace Microsoft.DotNet.Cli.Build.Tests;

[TestClass]
public class BuildFastBuildCommandTests : SdkTest
{
    [TestMethod]
    public void RecognizesFastBuildWithoutForwardingItToMSBuild()
    {
        var definition = new BuildCommandDefinition();
        definition.Parse([]).GetValue(definition.FastBuildOption).Should().BeFalse();
        var result = definition.Parse(["--fastbuild"]);

        result.Errors.Should().BeEmpty();
        result.UnmatchedTokens.Should().BeEmpty();
        result.GetValue(definition.FastBuildOption).Should().BeTrue();
        result.OptionValuesToBeForwarded(definition).Should().NotContain("--fastbuild");
    }

    [TestMethod]
    public void StandardBuildIsUnchangedWithoutTheSwitch()
    {
        var command = BuildCommand.FromArgs([], msbuildPath: null, CommandServices.CreateDefault());

        command.Should().BeOfType<RestoringCommand>();
        ((RestoringCommand)command).GetArgumentsToMSBuild().Should()
            .Contain("-restore")
            .And.Contain("-consoleloggerparameters:Summary")
            .And.NotContain("--fastbuild");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ForwardsBuildOptionsAndRestorePolicy(bool noRestore)
    {
        string directory = TestAssetsManager.CreateTestDirectory(identifier: Guid.NewGuid().ToString()).Path;
        string project = Path.Combine(directory, "App.csproj");
        File.WriteAllText(project, "<Project />");
        string output = Path.Combine(directory, "output with spaces");
        var command = BuildCommand.FromArgs([
            project, "--fastbuild", "-c", "Release", "-f", ToolsetInfo.CurrentTargetFramework,
            "-r", "linux-x64", "-o", output, "--no-self-contained", "--no-dependencies",
            "--version-suffix", "fastbuild", "-p:CustomProperty=hello world", "-v", "detailed", "-bl",
            .. noRestore ? new[] { "--no-restore" } : []
        ], msbuildPath: null, CommandServices.CreateDefault());

        string[] arguments = command.Should().BeOfType<FastBuildCommand>().Which.GetArguments();
        arguments[0].Should().Be(project);
        arguments.Should().Contain([
            "--msbuild-engine", "dotnet", "--no-server", "--verbosity", "detailed", "--binlog",
            "-p:Configuration=Release",
            $"-p:TargetFramework={ToolsetInfo.CurrentTargetFramework}",
            "-p:RuntimeIdentifier=linux-x64",
            $"-p:OutputPath={output}{Path.DirectorySeparatorChar}",
            "-p:SelfContained=false",
            "-p:BuildProjectReferences=false",
            "-p:VersionSuffix=fastbuild",
            "-p:CustomProperty=hello world"
        ]);
        arguments.Contains("--no-restore").Should().Be(noRestore);
        arguments.Should().NotContain("--fastbuild");
        arguments.Should().NotContain(argument => argument.StartsWith("-consoleloggerparameters"));
    }

    [TestMethod]
    [DataRow("--no-incremental")]
    [DataRow("-t:Build")]
    [DataRow("-t:Clean")]
    [DataRow("-bl:custom.binlog")]
    [DataRow("-m:1")]
    [DataRow("-rp:RestoreSources=custom")]
    [DataRow("--getProperty:Version")]
    [DataRow("--getItem:Compile")]
    [DataRow("--getTargetResult:Build")]
    [DataRow("--getResultOutputFile:result.json")]
    [DataRow("--disable-build-servers")]
    public void UnsupportedBuildArgumentsFailExplicitly(string argument)
    {
        string directory = TestAssetsManager.CreateTestDirectory(identifier: Guid.NewGuid().ToString()).Path;
        string project = Path.Combine(directory, "App.csproj");
        File.WriteAllText(project, "<Project />");
        var command = BuildCommand.FromArgs([project, "--fastbuild", argument], msbuildPath: null, CommandServices.CreateDefault());

        Action action = () => ((FastBuildCommand)command).GetArguments();

        action.Should().Throw<GracefulException>().WithMessage("*--fastbuild*");
    }

    [TestMethod]
    public void FileBasedAppsFailExplicitly()
    {
        string directory = TestAssetsManager.CreateTestDirectory().Path;
        string source = Path.Combine(directory, "App.cs");
        File.WriteAllText(source, "System.Console.WriteLine(\"Hello\");");

        Action action = () => BuildCommand.FromArgs([source, "--fastbuild"], msbuildPath: null, CommandServices.CreateDefault());

        action.Should().Throw<GracefulException>().WithMessage("*project or solution*");
    }

    [TestMethod]
    public void MultipleProjectPathsFailExplicitly()
    {
        var command = BuildCommand.FromArgs(["First.csproj", "Second.csproj", "--fastbuild"], msbuildPath: null, CommandServices.CreateDefault());

        Action action = () => ((FastBuildCommand)command).GetArguments();

        action.Should().Throw<GracefulException>().WithMessage("*single project or solution*");
    }

    [TestMethod]
    public void MissingProjectFailsExplicitly()
    {
        string missing = Path.Combine(TestAssetsManager.CreateTestDirectory().Path, "Missing.csproj");
        var command = BuildCommand.FromArgs([missing, "--fastbuild"], msbuildPath: null, CommandServices.CreateDefault());

        Action action = () => ((FastBuildCommand)command).GetArguments();

        action.Should().Throw<GracefulException>().WithMessage($"*{missing}*");
    }

    [TestMethod]
    public void CustomMSBuildPathsFailExplicitly()
    {
        Action action = () => BuildCommand.FromArgs(["--fastbuild"], "custom-msbuild.dll", CommandServices.CreateDefault());

        action.Should().Throw<GracefulException>().WithMessage("*custom-msbuild.dll*");
    }
}
