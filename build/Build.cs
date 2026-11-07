using System;
using System.IO.Compression;
using System.Text.RegularExpressions;

using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;

using Serilog;

// Deploy build for LuceneServer.NET (same structure as gview-pipe)
//
//   .\build.cmd                         => Test, Compile, Zip, Docker
//   .\build.cmd Zip                     => only the ZIP files (no docker)
//   .\build.cmd Zip --platform win64
//   .\build.cmd Deploy --skip Test
//   .\build.cmd Docker                  => image luceneserver-net:{version} and :latest (+ ghcr.io/jugstalt tags)
//   .\build.cmd DockerPush              => also push to ghcr.io/jugstalt (docker login ghcr.io first)
//   .\build.cmd Docs                    => MkDocs site in artifacts/docs/site
//   .\build.cmd DocsPublish             => copy the site to ../docs-gviewonline/app/other/luceneserver-net
//
// Output (DeployRoot, default c:\deploy\luceneserver-net):
//   download\luceneserver-net-{win64|linux64}-{version}.zip   framework dependent (.NET 10 runtime required)
//
// ZIP structure: {version}/luceneserver/... (the published server without _config/luceneserver.json:
// the server creates it on the first start, see Setup.cs)
class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Deploy);

    [Parameter("Configuration to build - Default is 'Release'")]
    readonly string Configuration = "Release";

    [Parameter("Version - Default is the <Version> of src/LuceneServerNET.Core")]
    string Version;

    [Parameter("Platform to build - win64, linux64 (default: both)")]
    readonly string[] Platform = { "win64", "linux64" };

    [Parameter("Deploy root - Default is c:\\deploy\\luceneserver-net")]
    readonly AbsolutePath DeployRoot = (AbsolutePath)@"c:\deploy\luceneserver-net";

    [Parameter("Checkout of docs-gviewonline for DocsPublish - Default is the folder docs-gviewonline next to this repository")]
    readonly string DocsRepository;

    [Parameter("Docker exe file name")]
    readonly string DockerExe = "docker";

    [Parameter("Docker registry for DockerPush")]
    readonly string DockerRegistry = "ghcr.io/jugstalt";

    const string AppName = "luceneserver";
    const string ZipName = "luceneserver-net";
    const string DockerImage = "luceneserver-net";
    const string DocsFolder = "luceneserver-net";

    static readonly RelativePath ServerProject = (RelativePath)"src/LuceneServerNET/LuceneServerNET.csproj";
    static readonly RelativePath VersionProject = (RelativePath)"src/LuceneServerNET.Core/LuceneServerNET.Core.csproj";

    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath DownloadDirectory => DeployRoot / "download";

    AbsolutePath Artifacts(string platform) => ArtifactsDirectory / AppName / platform;

    static string RuntimeIdentifier(string platform) => platform switch
    {
        "win64" => "win-x64",
        "linux64" => "linux-x64",
        _ => throw new ArgumentException($"Unknown platform '{platform}'")
    };

    protected override void OnBuildInitialized()
    {
        if (String.IsNullOrEmpty(Version))
        {
            var project = (RootDirectory / VersionProject).ReadAllText();
            var match = Regex.Match(project, @"<Version>\s*([^<\s]+)\s*</Version>");
            Assert.True(match.Success, $"Can't read <Version> from {VersionProject}");
            Version = match.Groups[1].Value;
        }

        foreach (var platform in Platform)
        {
            Assert.True(platform is "win64" or "linux64", $"Unknown platform '{platform}'");
        }

        Log.Information("LuceneServer.NET {Version} for {Platform}", Version, String.Join(", ", Platform));
    }

    Target Test => _ => _
        .Before(Compile, Docker)
        .Executes(() =>
        {
            DotNetTasks.DotNetTest(s => s
                .SetProjectFile(RootDirectory / "LuceneServerNET.sln")
                .SetConfiguration(Configuration));
        });

    Target Compile => _ => _
        .DependsOn(Test)
        .Executes(() =>
        {
            foreach (var platform in Platform)
            {
                var artifacts = Artifacts(platform);

                Log.Information("Compile {App} {Platform}", AppName, platform);

                artifacts.CreateOrCleanDirectory();
                DotNetTasks.DotNetPublish(s => s
                    .SetProject(RootDirectory / ServerProject)
                    .SetConfiguration(Configuration)
                    .SetRuntime(RuntimeIdentifier(platform))
                    .SetSelfContained(false)
                    .SetProperty("GeneratePackageOnBuild", "false")
                    .SetOutput(artifacts));

                // never deploy developer settings: the server creates _config/luceneserver.json on the first start
                (artifacts / "_config" / "luceneserver.json").DeleteFile();
                (artifacts / "appsettings.Development.json").DeleteFile();

                // third party pdb files
                foreach (var file in artifacts.GlobFiles("**/*.pdb"))
                {
                    if (!file.Name.StartsWith("LuceneServerNET", StringComparison.OrdinalIgnoreCase))
                    {
                        file.DeleteFile();
                    }
                }
            }
        });

    Target Zip => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DownloadDirectory.CreateDirectory();

            foreach (var platform in Platform)
            {
                var zipFile = DownloadDirectory / $"{ZipName}-{platform}-{Version}.zip";
                zipFile.DeleteFile();

                var source = Artifacts(platform);
                using (var zip = ZipFile.Open(zipFile, ZipArchiveMode.Create))
                {
                    foreach (var file in source.GlobFiles("**/*"))
                    {
                        var relative = source.GetRelativePathTo(file).ToString().Replace('\\', '/');
                        zip.CreateEntryFromFile(file, $"{Version}/{AppName}/{relative}", CompressionLevel.Optimal);
                    }
                }

                Log.Information("Zip {ZipFile}", zipFile);
            }
        });

    // multi-stage Dockerfile in the repository root: builds from source, needs no Compile
    Target Docker => _ => _
        .After(Zip)
        .Executes(() =>
        {
            Log.Information("Build docker image {Image}:{Version}", DockerImage, Version);

            RunDocker($"build -t {DockerImage}:{Version} --label org.opencontainers.image.version={Version} -f Dockerfile .", RootDirectory);
            RunDocker($"tag {DockerImage}:{Version} {DockerImage}:latest");
            RunDocker($"tag {DockerImage}:{Version} {DockerRegistry}/{DockerImage}:{Version}");
            RunDocker($"tag {DockerImage}:{Version} {DockerRegistry}/{DockerImage}:latest");
        });

    // needs "docker login ghcr.io" with a token that has the write:packages scope
    Target DockerPush => _ => _
        .DependsOn(Docker)
        .Executes(() =>
        {
            RunDocker($"push {DockerRegistry}/{DockerImage}:{Version}");
            RunDocker($"push {DockerRegistry}/{DockerImage}:latest");
        });

    // Static documentation site (MkDocs Material, see mkdocs.yml) in artifacts/docs/site.
    // Needs Python; without it the target is skipped with a warning.
    Target Docs => _ => _
        .Executes(() =>
        {
            // never publish the site of an earlier build when this one is skipped or fails
            (ArtifactsDirectory / "docs").DeleteDirectory();

            // MkDocs in its own virtual environment .venv-docs (same as build-docs.ps1), created on the first run
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

    Target Deploy => _ => _
        .DependsOn(Zip, Docker);

    void RunDocker(string arguments, AbsolutePath workingDirectory = null)
    {
        ProcessTasks.StartProcess(DockerExe, arguments,
                workingDirectory: workingDirectory,
                logger: (_, text) => Log.Information(text))
            .AssertZeroExitCode();
    }
}
