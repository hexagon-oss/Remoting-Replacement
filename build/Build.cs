using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.CI.TeamCity;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Tools.NuGet;
using Nuke.Common.Tools.OpenCover;
using Nuke.Common.Tools.ReportGenerator;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.NuGet.NuGetTasks;

[ShutdownDotNetAfterServerBuild]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Debug (default) or Release")]
    readonly Configuration Configuration = Configuration.Debug;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion(Framework = "net6.0", NoFetch = true)]
    public GitVersion GitVersion;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
	
    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(ArtifactsDirectory);
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(GitVersion.AssemblySemVer)
                .SetFileVersion(GitVersion.AssemblySemFileVer)
                .SetInformationalVersion(GitVersion.InformationalVersion));
        });

    Target UnitTest => _ => _
	    .DependsOn(Compile)
	    .DependsOn(CopyOutputAround)
	    .Executes(() =>
	    {
		    var projectsToCheck = Solution.GetProjects("*UnitTest").OrderBy(x => x.Name).ToList();
		    var settings = new DotNetTestSettings()
			    .SetConfiguration(Configuration)
			    .SetProjectFile(Solution)
			    .EnableNoBuild()
			    .EnableNoRestore()
			    .SetResultsDirectory(RootDirectory /
			                         string.Concat("TestResult.UnitTest.", Platform, ".", Configuration, ".", "net6.0"));
		    DotNetTest(settings);

	    });

    Target PublishExecutable => _ => _
	    .DependsOn(Compile)
	    .Executes(() =>
	    {
		    DotNetPublish(s => s
			    .SetProject(SourceDirectory / "RemotingServer" / "RemotingServer.csproj")
				.SetAssemblyVersion(GitVersion.AssemblySemVer)
			    .SetFileVersion(GitVersion.AssemblySemFileVer)
			    .SetInformationalVersion(GitVersion.InformationalVersion)
			    .SetConfiguration(Configuration)
			    .SetFramework("net6.0-windows")
		    );
	    });

	/// <summary>
	/// Copy the build output around, so that unit tests and package building works
	/// </summary>
    Target CopyOutputAround => _ => _
	    .DependsOn(PublishExecutable)
	    .DependsOn(Compile)
	    .Executes(() =>
	    {
			// The code was originally in the post-build step of RemotingClient, but that was wrong, since that runs before the publish step
			string outDir = SourceDirectory / "RemotingClient" / "bin" / Configuration / "net6.0-windows";
			string solutionDir = SourceDirectory;
		    string[] commandsToExecute = new string[]
		    {
			    @$"xcopy /Y /D /I {outDir}\*.* {solutionDir}\RemotingServer\bin\{Configuration}\net6.0-windows",
			    @$"xcopy /Y /E /S /I {outDir}\runtimes {solutionDir}\RemotingServer\bin\{Configuration}\net6.0-windows\runtimes",
			    @$"xcopy /Y /D /I {solutionDir}\RemotingServer\bin\{Configuration}\net6.0-windows {solutionDir}\NewRemotingUnitTest\bin\{Configuration}\net6.0-windows",
			    @$"xcopy /Y /D /I {outDir}\*.* {solutionDir}\NewRemotingUnitTest\bin\{Configuration}\net6.0-windows",
			    @$"xcopy /Y /E /S /I {outDir}\runtimes {solutionDir}\NewRemotingUnitTest\bin\{Configuration}\net6.0-windows\runtimes"
		    };

			foreach (var cmd in commandsToExecute)
		    {
			    var process = ProcessTasks.StartShell(cmd, null, null, null, true, true);
			    process.WaitForExit();
		    }
	    });

    Target ShowOutput => _ => _
	    .DependsOn(CopyOutputAround)
	    .DependsOn(PublishExecutable)
	    .Executes(() =>
	    {
		    var process = ProcessTasks.StartShell("dir /s", null, null, null, true, true);
		    process.WaitForExit();
	    });

    Target Pack => _ => _
	    .DependsOn(UnitTest)
	    .DependsOn(PublishExecutable)
	    .DependsOn(ShowOutput)
	    .Executes(() =>
	    {
		    DotNetPack(s => s
			    .SetNoBuild(true)
			    .SetNoRestore(true)
			    .SetProject(SourceDirectory / "NewRemoting" / "NewRemoting.csproj")
			    .SetConfiguration(Configuration)
			    .SetAssemblyVersion(GitVersion.AssemblySemVer)
			    .SetFileVersion(GitVersion.AssemblySemFileVer)
			    .SetVersion(GitVersion.SemVer)
			    .SetInformationalVersion(GitVersion.InformationalVersion));

		    var publishDir = SourceDirectory / "RemotingServer" / "bin" / Configuration / "net6.0-windows" / "publish";

		    NuGetPack(s => s
				    .SetBasePath(publishDir)
				    .SetBuild(false)
				    .SetVersion(GitVersion.SemVer)
				    .SetConfiguration(Configuration)
				    .SetTargetPath(SourceDirectory / "RemotingServer" / "RemotingServer.nuspec")
				    .SetOutputDirectory(ArtifactsDirectory)
			    );
	    });

    Target Push => _ => _
	    .DependsOn(Pack)
	    .Executes(() =>
	    {
		    var key = Environment.GetEnvironmentVariable("NUGET_API_KEY");
		    if (string.IsNullOrWhiteSpace(key))
		    {
			    Serilog.Log.Error("Api key not set, cannot publish");
			    return; // Don't fail the build, because will be the default build target in CI
		    }

		    if (Configuration == Configuration.Debug)
		    {
				Serilog.Log.Warning("Skipping dotnet push - not a release build");
				return;
		    }

			Serilog.Log.Warning($"Checking {ArtifactsDirectory}...");
		    foreach (var p in Directory.GetFiles(ArtifactsDirectory, "*.nupkg", SearchOption.TopDirectoryOnly))
		    {
				// Only upload the current version
			    if (p.Contains(GitVersion.SemVer))
			    {
				    Serilog.Log.Information($"Uploading {p}...");
				    DotNetNuGetPush(s => s.SetApiKey(key)
					    .SetTargetPath(p)
					    .SetSource("https://api.nuget.org/v3/index.json"));
			    }
			    else
			    {
				    Serilog.Log.Warning($"Found package {p}, but this one is not of the expected version {GitVersion.SemVer}...");
                }
		    }
	    });
}
