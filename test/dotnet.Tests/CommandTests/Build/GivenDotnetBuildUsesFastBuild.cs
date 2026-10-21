// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.Cli.Build.Tests;

[TestClass]
public class GivenDotnetBuildUsesFastBuild : FastBuildTestBase
{
    [TestMethod]
    [DataRow("implicit", false)]
    [DataRow("project", false)]
    [DataRow("directory", false)]
    [DataRow("sln", false)]
    [DataRow("slnx", false)]
    [DataRow("project", true)]
    public void BuildsOnceUsingTheSelectedProjectOrSolution(string selection, bool noRestore)
    {
        TestAsset asset = CreateTestAsset();
        string selectedFile = Path.Combine(asset.Path, "MSBuildTestApp.csproj");
        if (selection is "sln" or "slnx")
        {
            new DotnetCommand(Log).WithWorkingDirectory(asset.Path)
                .Execute("new", "sln", "--name", "MSBuildTestApp", "--format", selection)
                .Should().Pass();
            selectedFile = Path.Combine(asset.Path, $"MSBuildTestApp.{selection}");
            new DotnetCommand(Log).WithWorkingDirectory(asset.Path)
                .Execute("sln", selectedFile, "add", "MSBuildTestApp.csproj")
                .Should().Pass();
        }

        if (noRestore)
        {
            new DotnetRestoreCommand(Log).WithWorkingDirectory(asset.Path).Execute().Should().Pass();
        }

        string toolDirectory = CreateFastBuildTool();
        string argumentsFile = Path.Combine(asset.Path, "fastbuild-arguments.txt");
        string markerFile = Path.Combine(asset.Path, "fastbuild.done");
        File.WriteAllText(Path.Combine(asset.Path, "Directory.Build.targets"),
            """
            <Project>
              <Target Name="RejectSecondBuild" BeforeTargets="Build">
                <Error Condition="Exists('$(MSBuildProjectDirectory)/fastbuild.done')" Text="Unexpected second build." />
              </Target>
            </Project>
            """);
        string[] paths = selection switch
        {
            "implicit" => [],
            "directory" => [asset.Path],
            _ => [selectedFile]
        };

        var result = new DotnetBuildCommand(Log)
            .WithWorkingDirectory(asset.Path)
            .WithEnvironmentVariable("PATH", toolDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"))
            .WithEnvironmentVariable("FASTBUILD_TEST_ARGUMENTS", argumentsFile)
            .WithEnvironmentVariable("FASTBUILD_TEST_MARKER", markerFile)
            .Execute([.. paths, "--fastbuild", "-c", "Release", "-p:FastBuildCustomProperty=hello world",
                .. noRestore ? new[] { "--no-restore" } : []]);

        result.Should().Pass().And.HaveStdOutContaining("FastBuild test tool invoked");
        File.Exists(markerFile).Should().BeTrue();
        string[] arguments = File.ReadAllLines(argumentsFile);
        arguments[0].Should().Be(selectedFile);
        arguments.Should().Contain("-p:Configuration=Release");
        arguments.Should().Contain("-p:FastBuildCustomProperty=hello world");
        arguments.Should().NotContain("--fastbuild");
        arguments.Contains("--no-restore").Should().Be(noRestore);

        string output = Path.Combine(
            OutputPathCalculator.FromProject(asset.Path).GetOutputDirectory(configuration: "Release"),
            "MSBuildTestApp.dll");
        new DotnetCommand(Log).Execute(output).Should().Pass().And.HaveStdOutContaining("Hello World");
    }

    [TestMethod]
    public void ReturnsTheFastBuildFailureWithoutFallingBack()
    {
        TestAsset asset = CreateTestAsset();
        string toolDirectory = CreateFastBuildTool();
        string argumentsFile = Path.Combine(asset.Path, "fastbuild-arguments.txt");

        var result = new DotnetBuildCommand(Log)
            .WithWorkingDirectory(asset.Path)
            .WithEnvironmentVariable("PATH", toolDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"))
            .WithEnvironmentVariable("FASTBUILD_TEST_ARGUMENTS", argumentsFile)
            .WithEnvironmentVariable("FASTBUILD_TEST_EXIT_CODE", "42")
            .Execute("--fastbuild");

        result.ExitCode.Should().Be(42);
        result.StdOut.Should().Contain("FastBuild test tool invoked");
        Directory.Exists(Path.Combine(asset.Path, "obj")).Should().BeFalse();
    }

    [TestMethod]
    public void MissingToolReportsInstallationInstructions()
    {
        TestAsset asset = CreateTestAsset();

        var result = new DotnetBuildCommand(Log)
            .WithWorkingDirectory(asset.Path)
            .WithEnvironmentVariable("PATH", string.Empty)
            .WithEnvironmentVariable("DOTNET_CLI_HOME", Path.Combine(asset.Path, "isolated-home"))
            .WithEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", "0")
            .WithEnvironmentVariable("DOTNET_GENERATE_ASPNET_CERTIFICATE", "false")
            .Execute("--fastbuild");

        result.Should().Fail();
        if (!SdkTestContext.IsLocalized())
        {
            result.StdErr.Should().Contain("dotnet tool install --global Microsoft.FastBuild");
        }
    }

    [TestMethod]
    public void DoesNotInvokeFastBuildWithoutTheSwitch()
    {
        TestAsset asset = CreateTestAsset();
        string toolDirectory = CreateFastBuildTool();
        string argumentsFile = Path.Combine(asset.Path, "fastbuild-arguments.txt");

        var result = new DotnetBuildCommand(Log)
            .WithWorkingDirectory(asset.Path)
            .WithEnvironmentVariable("PATH", toolDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"))
            .WithEnvironmentVariable("FASTBUILD_TEST_ARGUMENTS", argumentsFile)
            .Execute();

        result.Should().Pass();
        result.StdOut.Should().NotContain("FastBuild test tool invoked");
        File.Exists(argumentsFile).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HelpDocumentsTheSwitch(bool enableAot)
    {
        TestAsset asset = CreateTestAsset();
        new DotnetCommand(Log)
            .WithWorkingDirectory(asset.Path)
            .WithEnvironmentVariable("DOTNET_CLI_ENABLEAOT", enableAot ? "true" : "false")
            .Execute("build", "--help")
            .Should().Pass().And.HaveStdOutContaining("--fastbuild");
    }

    private TestAsset CreateTestAsset()
        => TestAssetsManager.CopyTestAsset("MSBuildTestApp", identifier: Guid.NewGuid().ToString()).WithSource();
}
