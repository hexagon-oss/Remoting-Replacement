using System;
using System.Collections.Generic;
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

[CheckBuildProjectConfigurations]
[ShutdownDotNetAfterServerBuild]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

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
			    .SetFramework("net6.0-windows")
		    );
	    });

    Target Pack => _ => _
	    .DependsOn(UnitTest)
	    .DependsOn(PublishExecutable)
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

			//DotNetPack(s => s
			//    .SetNoBuild(true)
			//	.SetAssemblyVersion(GitVersion.AssemblySemVer)
			//    .SetFileVersion(GitVersion.AssemblySemFileVer)
			//    .SetVersion(GitVersion.SemVer)
			//	.SetProject(SourceDirectory / "RemotingServer" / "RemotingServer.csproj")
			//    .SetConfiguration(Configuration)
			//    .SetProperty("NuspecBasePath", publishDir)
			//    .SetProperty("PackageVersion", GitVersion.SemVer)
		    // );
	    });
}
