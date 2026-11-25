[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('\A[0-9]+\.[0-9]+\.[0-9]+\z')]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$output = New-Item -ItemType Directory -Path $OutputDirectory -Force
$staging = [IO.Directory]::CreateTempSubdirectory('onec-outpost-agent-')
$tempRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))
if ($staging.Parent.FullName -ne $tempRoot) {
    throw 'The staging folder must be inside the temporary directory.'
}
$programDirectory = Join-Path $staging.FullName 'programs'
$repository = Split-Path -Parent $PSScriptRoot

Push-Location -LiteralPath $PSScriptRoot
try {
    $publishOptions = @(
        '--configuration', 'Release'
        '--self-contained'
        "-p:Version=$Version"
        '-p:IncludeSourceRevisionInInformationalVersion=false'
    )

    # The console publishes the agent through its project reference. Both use the same x64 runtime.
    dotnet publish Moltaro.OnecOutpost.Console @publishOptions --runtime win-x64 --output $programDirectory
    foreach ($architecture in 'x64', 'x86') {
        $workerDirectory = Join-Path $programDirectory $architecture
        dotnet publish Moltaro.OnecOutpost.ComWorker @publishOptions --runtime "win-$architecture" --output $workerDirectory
    }

    $programs = [ordered]@{
        'Moltaro.OnecOutpost.Agent' = 'x64'
        'Moltaro.OnecOutpost.Console' = 'x64'
        'x64/Moltaro.OnecOutpost.ComWorker' = 'x64'
        'x86/Moltaro.OnecOutpost.ComWorker' = 'x86'
    }
    $runtimePacks = @{}
    foreach ($program in $programs.Keys) {
        $programPath = Join-Path $programDirectory $program
        $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo("$programPath.exe").ProductVersion
        if ($productVersion -ne $Version) {
            throw "Expected $program.exe to have product version $Version, found '$productVersion'."
        }

        $configuration = Get-Content -LiteralPath "$programPath.runtimeconfig.json" -Raw | ConvertFrom-Json
        $runtime = $configuration.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App'
        if (-not $runtime) {
            throw "Expected $program.exe to include the .NET runtime."
        }

        $architecture = $programs[$program]
        $runtimeVersion = $runtime.version
        $runtimePacks["dotnet-$architecture-$runtimeVersion"] = "microsoft.netcore.app.runtime.win-$architecture/$runtimeVersion"
    }

    Copy-Item -LiteralPath "$PSScriptRoot/Executor/OutpostExecutor.epf", "$repository/LICENSE", "$repository/THIRD-PARTY-NOTICES.md" -Destination $programDirectory

    $assets = Get-Content -LiteralPath 'Moltaro.OnecOutpost.Console/obj/project.assets.json' -Raw | ConvertFrom-Json -AsHashtable
    $packagesDirectory = $assets.project.restore.packagesPath
    $licensesDirectory = New-Item -ItemType Directory -Path (Join-Path $programDirectory 'licenses')
    foreach ($runtimePack in $runtimePacks.GetEnumerator()) {
        $packageDirectory = Join-Path $packagesDirectory $runtimePack.Value
        $runtimeLicenses = New-Item -ItemType Directory -Path (Join-Path $licensesDirectory.FullName $runtimePack.Key)
        Copy-Item -LiteralPath "$packageDirectory/LICENSE.TXT", "$packageDirectory/THIRD-PARTY-NOTICES.TXT" -Destination $runtimeLicenses.FullName
    }

    # The Spectre packages name the source commit in their manifests and carry only the MIT license expression.
    # Spectre.Console.Ansi comes from the same repository as Spectre.Console.
    foreach ($spectrePackage in 'spectre.console', 'spectre.console.cli') {
        $library = $assets.libraries.Values | Where-Object path -Like "$spectrePackage/*"
        $manifestPath = Join-Path $packagesDirectory "$($library.path)/$spectrePackage.nuspec"
        [xml] $manifest = Get-Content -LiteralPath $manifestPath -Raw
        $commit = $manifest.package.metadata.repository.commit
        $license = Join-Path $licensesDirectory.FullName "$spectrePackage-LICENSE.md"
        Invoke-WebRequest -Uri "https://raw.githubusercontent.com/spectreconsole/$spectrePackage/$commit/LICENSE.md" -OutFile $license
    }

    $archiveName = "Moltaro.OnecOutpost-$Version-win.zip"
    $archivePath = Join-Path $output.FullName $archiveName
    [IO.Compression.ZipFile]::CreateFromDirectory($programDirectory, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $false)

    $checksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$archivePath.sha256", "$checksum  $archiveName`n")
}
finally {
    Pop-Location
    $staging.Delete($true)
}
