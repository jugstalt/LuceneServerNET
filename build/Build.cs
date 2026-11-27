using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

// Build and release of LuceneServer.NET (same structure as MessageQueueNET)
//
//   .\build.cmd                           => Test
//   .\build.cmd PrintVersion              => version from the git tags (MinVer)
//   .\build.cmd Test Pack Zip             => NuGet packages and ZIP downloads in artifacts/
//   .\build.cmd DockerBuild               => images ghcr.io/jugstalt/luceneserver-net[-admin]
//   .\build.cmd DockerBuild --container-cli wslc
//   .\build.cmd Docs                      => MkDocs site in artifacts/docs/site
//   .\build.cmd DocsPublish               => copy the site to ../docs-gviewonline/app/other/luceneserver-net
//
// Releases: push a tag v<version>; .github/workflows/release.yml pushes packages and images and creates the GitHub release.
class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Test);

    [Parameter("Configuration to build - Default is 'Release'")]
    readonly string Configuration = "Release";

    [Parameter("Runtime identifiers for the ZIP downloads - Default is win-x64 and linux-x64")]
    readonly string[] Runtimes = ["win-x64", "linux-x64"];

    [Parameter("Container registry including owner - Default is 'ghcr.io/jugstalt'")]
    readonly string ContainerRegistry = "ghcr.io/jugstalt";

    // also read from the environment variable NUKE_CONTAINER_CLI (e.g. set it to 'wslc' on your machine once)
    [Parameter("Container CLI with docker compatible build/push commands (e.g. 'wslc', 'podman') - Default is 'docker'")]
    readonly string ContainerCli = "docker";

    [Parameter("NuGet feed to push packages to - Default is nuget.org")]
    readonly string NuGetSource = "https://api.nuget.org/v3/index.json";

    [Parameter("NuGet API key (in CI a short-lived key from NuGet trusted publishing)"), Secret]
    readonly string NuGetApiKey;

    [Parameter("Checkout of docs-gviewonline for DocsPublish - Default is the folder docs-gviewonline next to this repository")]
    readonly string DocsRepository;

    const string DocsFolder = "luceneserver-net";
    const string RepositoryUrl = "https://github.com/jugstalt/LuceneServerNET";

    AbsolutePath SolutionFile => RootDirectory / "LuceneServerNET.sln";
    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath DeployDirectory => RootDirectory / "deploy";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath PackagesDirectory => ArtifactsDirectory / "packages";
    AbsolutePath ZipDirectory => ArtifactsDirectory / "zip";
    AbsolutePath DockerDirectory => ArtifactsDirectory / "docker";
    AbsolutePath TestResultsDirectory => ArtifactsDirectory / "test-results";
    AbsolutePath ReleaseNotesFile => ArtifactsDirectory / "release-notes.md";

    AbsolutePath ServerProject => SourceDirectory / "LuceneServerNET" / "LuceneServerNET.csproj";
    AbsolutePath AdminProject => SourceDirectory / "LuceneServerNET.Admin" / "LuceneServerNET.Admin.csproj";

    // any project that gets MinVer through Directory.Build.props
    AbsolutePath VersionProject => SourceDirectory / "LuceneServerNET.Core" / "LuceneServerNET.Core.csproj";

    // Folder = path inside {Version}/ of the ZIP
    (AbsolutePath Project, string Folder)[] ZipApps =>
    [
        (ServerProject, "luceneserver"),
        (AdminProject, "admin"),
    ];

    // Name = folder below deploy/docker (Dockerfile + override/), Image = image name without registry
    const string DockerRuntime = "linux-musl-x64";

    (string Name, AbsolutePath Project, string Image)[] DockerImages =>
    [
        ("server", ServerProject, "luceneserver-net"),
        ("admin", AdminProject, "luceneserver-net-admin"),
    ];

    Tool _containerTool;
    Tool Docker => _containerTool ??= ToolResolver.GetPathTool(ContainerCli);

    string _version;

    // single source of truth: MinVer (git tag v*), the same value the projects are built with
    string Version => _version ??= DotNet(
            $"msbuild {VersionProject} -t:MinVer -getProperty:MinVerVersion -p:Configuration={Configuration} -nologo",
            logOutput: false)
        .StdToText()
        .Trim();

    bool IsPreRelease => Version.Contains('-');

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(SolutionFile));
        });

    Target PrintVersion => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            Log.Information("Version: {Version}", Version);
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(SolutionFile)
                .SetConfiguration(Configuration)
                .EnableNoRestore());
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Produces(TestResultsDirectory / "*.trx")
        .Executes(() =>
        {
            DotNetTest(s => s
                .SetProjectFile(SolutionFile)
                .SetConfiguration(Configuration)
                .EnableNoRestore()
                .EnableNoBuild()
                .SetResultsDirectory(TestResultsDirectory)
                .AddLoggers("trx"));
        });

    // Section "## [x.y.z]" of CHANGELOG.md for the current version (pre-releases use x.y.z).
    // Used for the GitHub release and as PackageReleaseNotes (see Directory.Build.props).
    // No section => no file; the release workflow then falls back to --generate-notes.
    Target ReleaseNotes => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            ReleaseNotesFile.DeleteFile();

            var changelogFile = RootDirectory / "CHANGELOG.md";
            if (!changelogFile.FileExists())
            {
                Log.Warning("No CHANGELOG.md, no release notes");
                return;
            }

            var releaseVersion = Version.Split('-')[0];
            var match = Regex.Match(
                changelogFile.ReadAllText(),
                $@"^## \[{Regex.Escape(releaseVersion)}\][^\n]*\n(?<notes>.*?)(?=^## |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline);

            if (!match.Success || string.IsNullOrWhiteSpace(match.Groups["notes"].Value))
            {
                Log.Warning("CHANGELOG.md has no section [{Version}], no release notes", releaseVersion);
                return;
            }

            ArtifactsDirectory.CreateDirectory();
            ReleaseNotesFile.WriteAllText(match.Groups["notes"].Value.Trim() + Environment.NewLine);
        });

    Target Pack => _ => _
        .DependsOn(Compile, ReleaseNotes)
        .After(Test)
        .Produces(PackagesDirectory / "*.nupkg", PackagesDirectory / "*.snupkg")
        .Executes(() =>
        {
            PackagesDirectory.CreateOrCleanDirectory();

            DotNetPack(s => s
                .SetProject(SolutionFile)
                .SetConfiguration(Configuration)
                .EnableNoRestore()
                .EnableNoBuild()
                .SetOutputDirectory(PackagesDirectory));
        });

    Target NuGetPush => _ => _
        .DependsOn(Pack)
        .After(Test)
        .Requires(() => NuGetApiKey)
        .Executes(() =>
        {
            foreach (var package in PackagesDirectory.GlobFiles("*.nupkg"))
            {
                // the matching .snupkg next to the .nupkg is pushed automatically
                DotNetNuGetPush(s => s
                    .SetTargetPath(package)
                    .SetSource(NuGetSource)
                    .SetApiKey(NuGetApiKey)
                    .EnableSkipDuplicate());
            }
        });

    // ZIP structure: {Version}/luceneserver/ (server without _config/luceneserver.json: the server creates it on
    // the first start, see Setup.cs), {Version}/admin/ (admin UI) and the start scripts from deploy/zip.
    Target Zip => _ => _
        .DependsOn(Restore)
        .After(Test)
        .Produces(ZipDirectory / "*.zip")
        .Executes(() =>
        {
            ZipDirectory.CreateOrCleanDirectory();

            foreach (var runtime in Runtimes)
            {
                var stagingDirectory = ArtifactsDirectory / "zip-staging" / runtime;
                var root = stagingDirectory / Version;
                stagingDirectory.CreateOrCleanDirectory();

                foreach (var (project, folder) in ZipApps)
                {
                    PublishWebApp(project, runtime, root / folder);
                }

                // start scripts: deploy/zip/*.bat for Windows, *.sh for Linux
                var startScripts = runtime.StartsWith("win") ? "*.bat" : "*.sh";
                if ((DeployDirectory / "zip").DirectoryExists())
                {
                    foreach (var script in (DeployDirectory / "zip").GlobFiles(startScripts))
                    {
                        script.CopyToDirectory(root);
                    }
                }

                stagingDirectory.ZipTo(ZipDirectory / $"luceneserver_net_{Version}_{runtime}.zip");
                stagingDirectory.DeleteDirectory();
            }

            (ArtifactsDirectory / "zip-staging").DeleteDirectory();
        });

    IEnumerable<string> ImageTags(string image)
    {
        var repository = $"{ContainerRegistry.TrimEnd('/')}/{image}";

        yield return $"{repository}:{Version}";

        if (!IsPreRelease)
        {
            var majorMinor = string.Join('.', Version.Split('.').Take(2));
            yield return $"{repository}:{majorMinor}";
            yield return $"{repository}:latest";
        }
    }

    Target DockerBuild => _ => _
        .DependsOn(Restore)
        .After(Test)
        .Executes(() =>
        {
            foreach (var (name, project, image) in DockerImages)
            {
                var context = DockerDirectory / name;
                context.CreateOrCleanDirectory();

                CopyDirectory(DeployDirectory / "docker" / name, context);
                (context / "override").CreateDirectory();
                PublishWebApp(project, DockerRuntime, context / "app");
                (RootDirectory / "LICENSE").CopyToDirectory(context / "app", ExistsPolicy.FileOverwrite);

                var tags = ImageTags(image).ToList();
                // local pre-release builds also get 'latest' so docker run/compose finds them; never pushed
                var latest = $"{ContainerRegistry.TrimEnd('/')}/{image}:latest";
                if (IsLocalBuild && !tags.Contains(latest))
                {
                    tags.Add(latest);
                }

                var tagArgs = string.Join(' ', tags.Select(t => $"--tag {t}"));
                // image.source links the package on ghcr.io to the repository (shown in the repository's sidebar)
                Docker($"build --pull --label org.opencontainers.image.source={RepositoryUrl} --label org.opencontainers.image.version={Version} {tagArgs:nq} {context}", logger: LogContainerOutput);
                Log.Information("Built {Tags}", tags);
            }
        });

    Target DockerPush => _ => _
        .DependsOn(DockerBuild)
        .Executes(() =>
        {
            foreach (var tag in DockerImages.SelectMany(i => ImageTags(i.Image)))
            {
                Docker($"push {tag}", logger: LogContainerOutput);
            }
        });

    // docker/wslc write build progress to stderr; real failures are reported via the exit code
    static void LogContainerOutput(OutputType type, string text) => Log.Information(text);

    // Static documentation site (MkDocs Material, see mkdocs.yml) in artifacts/docs/site.
    // Needs Python; without it the target is skipped with a warning.
    Target Docs => _ => _
        .Executes(() =>
        {
            // never publish the site of an earlier build when this one is skipped or fails
            (ArtifactsDirectory / "docs").DeleteDirectory();

            // MkDocs in its own virtual environment .venv-docs, created on the first run
            var venv = RootDirectory / ".venv-docs";
            var python = OperatingSystem.IsWindows() ? venv / "Scripts" / "python.exe" : venv / "bin" / "python";
            if (!python.FileExists())
            {
                var check = ProcessTasks.StartProcess("python", $"-m venv \"{venv}\"", workingDirectory: RootDirectory, logger: (_, _) => { }, timeout: 120000);
                check.WaitForExit();
                if (check.ExitCode != 0 || !python.FileExists())
                {
                    Log.Warning("Docs skipped: Python not found (needed for MkDocs)");
                    return;
                }
            }

            ProcessTasks.StartProcess(python, "-m pip install --quiet --disable-pip-version-check -r docs/requirements.txt",
                    workingDirectory: RootDirectory,
                    logger: (_, text) => Log.Debug(text))
                .AssertZeroExitCode();

            // strict: broken links fail the build
            ProcessTasks.StartProcess(python, "-m mkdocs build --strict --clean",
                    workingDirectory: RootDirectory,
                    logger: (_, text) => Log.Information(text))
                .AssertZeroExitCode();

            Log.Information("Docs site: {Site}", ArtifactsDirectory / "docs" / "site");
        });

    // Mirrors the site into app/other/luceneserver-net of the docs-gviewonline repository, whose app folder
    // a GitHub action deploys as it is. Nothing is committed or pushed here; do that in docs-gviewonline.
    //   .\build.cmd DocsPublish
    //   .\build.cmd DocsPublish --docs-repository D:\github\docs-gviewonline
    Target DocsPublish => _ => _
        .DependsOn(Docs)
        .Executes(() =>
        {
            var site = ArtifactsDirectory / "docs" / "site";
            Assert.DirectoryExists(site, "The Docs target made no site (is Python installed?)");

            var repository = DocsRepository is null ? RootDirectory.Parent / "docs-gviewonline" : (AbsolutePath)DocsRepository;
            if (!(repository / "app").DirectoryExists())
            {
                Log.Warning("{Repository} is no checkout of docs-gviewonline (no app folder); set --docs-repository. Docs not copied.", repository);
                return;
            }

            var target = repository / "app" / "other" / DocsFolder;

            // mirror, not merge: renamed or removed pages must not stay behind
            target.DeleteDirectory();
            site.Copy(target, ExistsPolicy.MergeAndOverwrite);

            Log.Information("Docs copied to {Target}. Commit and push it in {Repository} to publish.", target, repository);
        });

    void PublishWebApp(AbsolutePath project, string runtime, AbsolutePath output)
    {
        DotNetPublish(s => s
            .SetProject(project)
            .SetConfiguration(Configuration)
            .SetRuntime(runtime)
            .DisableSelfContained()
            .SetPublishReadyToRun(true)
            .SetOutput(output));

        // never ship the developer configuration (subfolders like _config/proto are kept):
        // the server creates _config/luceneserver.json on the first start
        if ((output / "_config").DirectoryExists())
        {
            (output / "_config").GlobFiles("*").DeleteFiles();
        }
        (output / "appsettings.Development.json").DeleteFile();

        // only our own debug symbols
        foreach (var file in output.GlobFiles("**/*.pdb"))
        {
            if (!file.Name.StartsWith("LuceneServerNET", StringComparison.OrdinalIgnoreCase))
            {
                file.DeleteFile();
            }
        }
    }

    static void CopyDirectory(AbsolutePath source, AbsolutePath target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = target / Path.GetRelativePath(source, file);
            destination.Parent.CreateDirectory();
            File.Copy(file, destination, overwrite: true);
        }
    }
}
