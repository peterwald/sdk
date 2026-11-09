// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.Test.Utilities;

namespace Microsoft.DotNet.Cli.Test.Tests;

[TestClass]
public class GivenDotnetTestUsesFastBuild : FastBuildTestBase
{
    [TestMethod]
    [CombinatorialData]
    public void BuildsBeforeTestingWithoutBuildingAgain(
        [CombinatorialValues(false, true)] bool useMtp,
        [CombinatorialValues(false, true)] bool noRestore)
    {
        TestAsset asset = CreateTestAsset(useMtp);
        string projectDirectory = useMtp ? Path.Combine(asset.Path, "TestProject") : asset.Path;
        string toolDirectory = CreateFastBuildTool();
        string argumentsFile = Path.Combine(asset.Path, "fastbuild-arguments.txt");
        string markerFile = Path.Combine(projectDirectory, "fastbuild.done");
        File.WriteAllText(Path.Combine(projectDirectory, "Directory.Build.targets"),
            """
            <Project>
              <Target Name="RejectSecondBuild" BeforeTargets="Build">
                <Error Condition="Exists('$(MSBuildProjectDirectory)/fastbuild.done')" Text="Unexpected second build." />
              </Target>
            </Project>
            """);

        if (noRestore)
        {
            new DotnetRestoreCommand(Log)
                .WithWorkingDirectory(asset.Path)
                .Execute()
                .Should().Pass();
        }

        var command = new DotnetCommand(Log, "testx")
            .WithWorkingDirectory(asset.Path)
            .WithEnvironmentVariable("MSBuildEnableWorkloadResolver", "false")
            .WithEnvironmentVariable("DOTNET_TEST_RUNNER", useMtp ? "Microsoft.Testing.Platform" : "VSTest")
            .WithEnvironmentVariable("PATH", toolDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"))
            .WithEnvironmentVariable("FASTBUILD_TEST_ARGUMENTS", argumentsFile)
            .WithEnvironmentVariable("FASTBUILD_TEST_MARKER", markerFile);

        var result = command.Execute([
            "-c", "Release", "-p:FastBuildCustomProperty=hello world",
            "-e", "FastBuildUserEnvironment=user-value",
            .. noRestore ? new[] { "--no-restore" } : Array.Empty<string>()
        ]);

        // Both assets contain deliberately failing tests: test execution, not the build, must fail.
        result.ExitCode.Should().Be(useMtp ? 2 : 1);
        result.StdOut.Should().Contain("FastBuild test tool invoked");
        result.StdOut.Should().NotContain("Unexpected second build.");
        File.Exists(markerFile).Should().BeTrue();
        File.ReadAllText(markerFile).Should().Be(useMtp ? "built" : "user-value");
        string[] arguments = File.ReadAllLines(argumentsFile);
        arguments.Should().Contain("-p:Configuration=Release");
        arguments.Should().Contain("-p:FastBuildCustomProperty=hello world");
        arguments.Should().NotContain(argument => argument.StartsWith("-p:VSTest", StringComparison.OrdinalIgnoreCase));
        arguments.Contains("--no-restore").Should().Be(noRestore);
        result.StdOut.Should().Contain(useMtp ? "Test run summary:" : "VSTestFailTest");

        File.Delete(argumentsFile);
        var noBuildResult = command.Execute("--no-build", "-c", "Release");
        noBuildResult.ExitCode.Should().Be(useMtp ? 2 : 1);
        File.Exists(argumentsFile).Should().BeFalse();
        noBuildResult.StdOut.Should().NotContain("FastBuild test tool invoked");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void BuildFailurePreventsTestExecution(bool useMtp)
    {
        TestAsset asset = CreateTestAsset(useMtp);
        string toolDirectory = CreateFastBuildTool();
        string argumentsFile = Path.Combine(asset.Path, "fastbuild-arguments.txt");

        var result = new DotnetCommand(Log, "testx")
            .WithWorkingDirectory(asset.Path)
            .WithEnvironmentVariable("MSBuildEnableWorkloadResolver", "false")
            .WithEnvironmentVariable("DOTNET_TEST_RUNNER", useMtp ? "Microsoft.Testing.Platform" : "VSTest")
            .WithEnvironmentVariable("PATH", toolDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"))
            .WithEnvironmentVariable("FASTBUILD_TEST_ARGUMENTS", argumentsFile)
            .WithEnvironmentVariable("FASTBUILD_TEST_EXIT_CODE", "42")
            .Execute();

        result.ExitCode.Should().Be(useMtp ? 1 : 42);
        result.StdOut.Should().Contain("FastBuild test tool invoked");
        result.StdOut.Should().NotContain("Test run summary:");
        result.StdOut.Should().NotContain("Test run for ");
        File.Exists(argumentsFile).Should().BeTrue();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MissingToolReportsInstallationInstructions(bool useMtp)
    {
        TestAsset asset = CreateTestAsset(useMtp);

        var result = new DotnetCommand(Log, "testx")
            .WithWorkingDirectory(asset.Path)
            .WithEnvironmentVariable("MSBuildEnableWorkloadResolver", "false")
            .WithEnvironmentVariable("DOTNET_TEST_RUNNER", useMtp ? "Microsoft.Testing.Platform" : "VSTest")
            .WithEnvironmentVariable("PATH", string.Empty)
            .WithEnvironmentVariable("DOTNET_CLI_HOME", Path.Combine(asset.Path, "isolated-home"))
            .WithEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", "0")
            .WithEnvironmentVariable("DOTNET_GENERATE_ASPNET_CERTIFICATE", "false")
            .Execute();

        result.Should().Fail();
        if (!SdkTestContext.IsLocalized())
        {
            result.StdErr.Should().Contain("dotnet tool install --global Microsoft.DotNet.BuildX --version 11.11.34-preview");
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DirectAssembliesCannotRequestABuild(bool useMtp)
    {
        var directory = TestAssetsManager.CreateTestDirectory(identifier: Guid.NewGuid().ToString());
        string assembly = Path.Combine(directory.Path, "Tests.dll");
        File.WriteAllText(assembly, string.Empty);

        var result = new DotnetCommand(Log, "testx")
            .WithWorkingDirectory(directory.Path)
            .WithEnvironmentVariable("DOTNET_TEST_RUNNER", useMtp ? "Microsoft.Testing.Platform" : "VSTest")
            .Execute(useMtp
                ? ["--test-modules", assembly]
                : [assembly]);

        result.Should().Fail();
        if (!SdkTestContext.IsLocalized())
        {
            result.StdErr.Should().Contain("requires a project or solution");
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StandardTestDoesNotInvokeBuildX(bool useMtp)
    {
        TestAsset asset = CreateTestAsset(useMtp);
        string toolDirectory = CreateFastBuildTool();
        string argumentsFile = Path.Combine(asset.Path, "fastbuild-arguments.txt");

        var result = new DotnetTestCommand(Log, disableNewOutput: false)
            .WithWorkingDirectory(asset.Path)
            .WithEnvironmentVariable("DOTNET_TEST_RUNNER", useMtp ? "Microsoft.Testing.Platform" : "VSTest")
            .WithEnvironmentVariable("PATH", toolDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"))
            .WithEnvironmentVariable("FASTBUILD_TEST_ARGUMENTS", argumentsFile)
            .Execute();

        result.ExitCode.Should().Be(useMtp ? 2 : 1);
        result.StdOut.Should().Contain(useMtp ? "Test run summary:" : "VSTestFailTest");
        result.StdOut.Should().NotContain("FastBuild test tool invoked");
        File.Exists(argumentsFile).Should().BeFalse();
    }

    [TestMethod]
    public void HelpDocumentsTestXWithoutAFastBuildSwitch()
    {
        var result = new DotnetCommand(Log, "testx", "--help")
            .WithEnvironmentVariable("DOTNET_TEST_RUNNER", "VSTest")
            .Execute();

        result.Should().Pass();
        result.StdOut.Should().Contain("testx").And.NotContain("--fastbuild");
        if (!SdkTestContext.IsLocalized())
        {
            result.StdOut.Should().Contain("Microsoft.DotNet.BuildX");
        }
    }

    private TestAsset CreateTestAsset(bool useMtp)
        => TestAssetsManager.CopyTestAsset(
                useMtp ? "TestProjectSolutionWithTestsAndArtifacts" : "VSTestCore",
                identifier: Guid.NewGuid().ToString())
            .WithSource()
            .WithVersionVariables();
}
