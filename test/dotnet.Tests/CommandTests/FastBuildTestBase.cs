// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.Cli;

public abstract class FastBuildTestBase : SdkTest
{
    protected string CreateFastBuildTool()
    {
        var project = new TestProject
        {
            Name = "dotnet-fastbuild",
            IsExe = true
        };
        project.SourceFiles["Program.cs"] =
            """
            using System;
            using System.Diagnostics;
            using System.IO;

            Console.WriteLine("FastBuild test tool invoked");
            File.WriteAllLines(Environment.GetEnvironmentVariable("FASTBUILD_TEST_ARGUMENTS"), args);
            if (int.TryParse(Environment.GetEnvironmentVariable("FASTBUILD_TEST_EXIT_CODE"), out int exitCode))
            {
                return exitCode;
            }

            var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"))
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("build");
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--msbuild-engine")
                {
                    i++;
                }
                else if (args[i] == "--binlog")
                {
                    startInfo.ArgumentList.Add("-bl");
                }
                else if (args[i] != "--no-server")
                {
                    startInfo.ArgumentList.Add(args[i]);
                }
            }
            using var process = Process.Start(startInfo);
            process.WaitForExit();
            if (process.ExitCode == 0 &&
                Environment.GetEnvironmentVariable("FASTBUILD_TEST_MARKER") is string marker)
            {
                File.WriteAllText(
                    marker,
                    Environment.GetEnvironmentVariable("FastBuildUserEnvironment") ?? "built");
            }
            return process.ExitCode;
            """;

        TestAsset asset = TestAssetsManager.CreateTestProject(project, identifier: Guid.NewGuid().ToString());
        var build = new BuildCommand(asset);
        build.Execute().Should().Pass();
        return build.GetOutputDirectory(project.TargetFrameworks).FullName;
    }
}
