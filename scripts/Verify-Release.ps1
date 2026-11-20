[CmdletBinding()]
param([string]$DalamudHome)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$oldDalamudHome = $env:DALAMUD_HOME
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$run = Join-Path $root "artifacts\verification\$runId"
$src = Join-Path $run 'src'
$logs = Join-Path $run 'logs'
$results = Join-Path $run 'test-results'
$release = $null
$successReport = $null
New-Item -ItemType Directory -Path $src, $logs, $results -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Write-Utf8([string]$Path, [string]$Content) {
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}
function Invoke-DotNet([string]$LogName, [string[]]$Arguments, [switch]$ReturnOutput) {
    $quoted = foreach ($argument in $Arguments) {
        if ($argument.Contains('"')) { throw 'Embedded argument quotes are not supported.' }
        if ($argument -match '\s') { '"' + ($argument -replace '(\\+)$', '$1$1') + '"' } else { $argument }
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo.FileName = (Get-Command dotnet -CommandType Application).Source
    $process.StartInfo.Arguments = $quoted -join ' '
    $process.StartInfo.WorkingDirectory = (Get-Location).Path
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.EnvironmentVariables['DOTNET_CLI_UI_LANGUAGE'] = 'en-US'
    try {
        if (!$process.Start()) { throw 'Failed to start dotnet.' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $stdout.GetAwaiter().GetResult()
        $errorOutput = $stderr.GetAwaiter().GetResult()
        Write-Utf8 (Join-Path $logs $LogName) ($output + "`n[stderr]`n" + $errorOutput)
        if ($output) { Write-Host $output }
        if ($errorOutput) { Write-Host $errorOutput }
        if ($process.ExitCode -ne 0) { throw "dotnet failed ($($process.ExitCode)): $($Arguments -join ' ')" }
        if ($ReturnOutput) { return $output }
    } finally { $process.Dispose() }
}
function Stream-Hash($Stream) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($Stream)).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}
function Entry-Hash($Entry) {
    $stream = $Entry.Open()
    try { return Stream-Hash $stream } finally { $stream.Dispose() }
}
function Assert-Set($Actual, $Expected, [string]$Label) {
    if (((@($Actual) | Sort-Object) -join "`n") -cne ((@($Expected) | Sort-Object) -join "`n")) { throw "Unexpected set: $Label" }
}
function Source-Files([string]$Directory) {
    foreach ($item in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Source reparse point is not allowed: $($item.FullName)" }
        if ($item.PSIsContainer) {
            if ($item.Name -notin 'bin','obj','.vs','.git','artifacts','TestResults') { Source-Files $item.FullName }
        } else { $item }
    }
}
function Entry-Text($Entry) {
    $reader = [IO.StreamReader]::new($Entry.Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

Push-Location $root
try {
    if ($DalamudHome) {
        $resolved = (Resolve-Path $DalamudHome).Path
        if (!(Test-Path (Join-Path $resolved 'Dalamud.dll'))) { throw 'DalamudHome must contain Dalamud.dll.' }
        $env:DALAMUD_HOME = $resolved.TrimEnd([char[]]'\/') + [IO.Path]::DirectorySeparatorChar
    } elseif (Test-Path (Join-Path $root 'Directory.Build.local.props')) {
        throw 'Local override detected: pass -DalamudHome explicitly. Local machine props are never exported.'
    }
    foreach ($name in @('OutputPath','OutDir','BaseOutputPath','IntermediateOutputPath','BaseIntermediateOutputPath','MSBuildProjectExtensionsPath','ArtifactsPath','UseArtifactsOutput','DirectoryBuildPropsPath','DirectoryBuildTargetsPath','MSBuildSDKsPath','CustomBeforeMicrosoftCommonProps','CustomAfterMicrosoftCommonProps','CustomBeforeMicrosoftCommonTargets','CustomAfterMicrosoftCommonTargets','DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR')) {
        if ([Environment]::GetEnvironmentVariable($name)) { throw "Redirecting build environment property is not allowed: $name" }
    }
    $sdk = (Invoke-DotNet 'sdk.log' @('--version') -ReturnOutput).Trim()
    if ($sdk -notmatch '^10\.0\.') { throw 'A stable .NET 10.0 SDK is required (see global.json).' }
    if (!$env:DALAMUD_HOME) { $env:DALAMUD_HOME = Join-Path $env:APPDATA 'XIVLauncherCN\addon\Hooks\dev\' }
    $dalamudDll = Join-Path $env:DALAMUD_HOME 'Dalamud.dll'
    $dalamudIdentity = [Reflection.AssemblyName]::GetAssemblyName($dalamudDll)
    if ($dalamudIdentity.Name -ne 'Dalamud' -or $dalamudIdentity.Version.Major -ne 15) { throw 'Expected a Dalamud 15 reference assembly.' }
    $dalamudReference = [ordered]@{ Path = $dalamudDll; Version = $dalamudIdentity.Version.ToString(); Sha256 = (Get-FileHash $dalamudDll -Algorithm SHA256).Hash.ToLowerInvariant() }

    # Positive root allowlist. Never export local props, NuGet credentials, IDE state, or old build products.
    $files = [Collections.Generic.List[IO.FileInfo]]::new()
    foreach ($name in @('ETDisplay.slnx', 'README.md', 'CHANGELOG.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', '.gitignore', 'global.json', 'Directory.Build.props', 'Directory.Build.targets')) {
        $path = Join-Path $root $name
        if (Test-Path $path -PathType Leaf) { $files.Add((Get-Item $path)) }
    }
    foreach ($name in @('ETDisplay', 'ETDisplay.Core', 'ETDisplay.Core.Tests', 'scripts', 'docs', 'licenses')) {
        $sourceDirectory = Get-Item (Join-Path $root $name)
        if ($sourceDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source directory cannot be a reparse point.' }
        foreach ($file in Source-Files $sourceDirectory.FullName) {
            $relative = $file.FullName.Substring($root.Length + 1)
            if ($relative -match '(^|[\\/])(bin|obj|\.vs|\.git|artifacts|TestResults)([\\/]|$)') { continue }
            if ($file.Name -in 'Directory.Build.local.props', 'NuGet.config') { continue }
            $extension = $file.Extension.ToLowerInvariant()
            $include = switch ($name) {
                'scripts' { $extension -eq '.ps1' }
                'docs' { $extension -eq '.md' }
                'licenses' { $extension -eq '.txt' }
                default { $extension -in '.cs', '.csproj', '.png', '.resx', '.props', '.targets' -or $file.Name -eq 'packages.lock.json' -or $relative -eq 'ETDisplay\ETDisplay.json' }
            }
            if (!$include) { continue }
            $files.Add($file)
        }
    }
    $snapshot = @()
    foreach ($file in $files) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source file cannot be a reparse point.' }
        $relative = $file.FullName.Substring($root.Length + 1)
        $destination = Join-Path $src $relative
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        $snapshot += [pscustomobject]@{ Path = $relative; Hash = (Get-FileHash $destination -Algorithm SHA256).Hash }
    }
    [xml]$project = Get-Content (Join-Path $src 'ETDisplay\ETDisplay.csproj') -Raw -Encoding UTF8
    $version = [string]$project.Project.PropertyGroup.Version
    $pluginSource = Get-Content (Join-Path $src 'ETDisplay\Plugin.cs') -Raw -Encoding UTF8
    $buildMatches = [regex]::Matches($pluginSource, '(?m)^\s*public const string BuildId\s*=\s*"([A-Za-z0-9._-]+)"\s*;')
    if ($buildMatches.Count -ne 1 -or $version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid/ambiguous numeric version or BuildId.' }
    $buildId = $buildMatches[0].Groups[1].Value
    $informationVersion = [string]$project.Project.PropertyGroup.InformationalVersion
    $rcMatch = [regex]::Match($buildId, '-(rc\d+)$')
    if (!$rcMatch.Success -or $informationVersion -ne ($version + '-' + $rcMatch.Groups[1].Value)) { throw 'Candidate informational version and BuildId disagree.' }
    $configSource = Get-Content (Join-Path $src 'ETDisplay\Configuration.cs') -Raw -Encoding UTF8
    $configMatch = [regex]::Match($configSource, 'public int Version\s*\{[^}]+\}\s*=\s*(\d+)')
    if (!$configMatch.Success) { throw 'Cannot determine configuration schema version.' }
    $configVersion = [int]$configMatch.Groups[1].Value
    $release = Join-Path $root "artifacts\releases\$version-$buildId\$runId"

    $isolation = @("-p:DirectoryBuildPropsPath=$(Join-Path $src 'Directory.Build.props')", "-p:DirectoryBuildTargetsPath=$(Join-Path $src 'Directory.Build.targets')")
    Push-Location $src
    try {
        Invoke-DotNet 'restore.log' (@('restore', 'ETDisplay.slnx', '--locked-mode', '-p:TreatWarningsAsErrors=true') + $isolation)
        $testRuns = @()
        foreach ($configuration in @('Debug', 'Release')) {
            foreach ($projectPath in @('ETDisplay\ETDisplay.csproj','ETDisplay.Core\ETDisplay.Core.csproj','ETDisplay.Core.Tests\ETDisplay.Core.Tests.csproj')) {
                $query = Invoke-DotNet ("paths-$configuration-" + [IO.Path]::GetFileNameWithoutExtension($projectPath) + '.log') (@('msbuild', $projectPath, "-p:Configuration=$configuration", '-getProperty:TargetPath,IntermediateOutputPath,MSBuildProjectExtensionsPath') + $isolation) -ReturnOutput
                $paths = ($query | ConvertFrom-Json).Properties
                foreach ($property in $paths.PSObject.Properties) {
                    $projectDir = Split-Path (Join-Path $src $projectPath)
                    $fullPath = if ([IO.Path]::IsPathRooted($property.Value)) { [IO.Path]::GetFullPath($property.Value) } else { [IO.Path]::GetFullPath((Join-Path $projectDir $property.Value)) }
                    if (!$fullPath.StartsWith($src + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Output escaped isolated snapshot: $fullPath" }
                }
            }
            Invoke-DotNet "build-$configuration.log" (@('build', 'ETDisplay.slnx', '-c', $configuration, '--no-restore', '-warnaserror', '-v:minimal') + $isolation)
            $trxName = "phase5-$configuration.trx"
            Invoke-DotNet "test-$configuration.log" (@('test', 'ETDisplay.Core.Tests\ETDisplay.Core.Tests.csproj', '-c', $configuration, '--no-build', '--no-restore', '-v:minimal', '--logger', "trx;LogFileName=$trxName", '--results-directory', $results) + $isolation)
            [xml]$trx = Get-Content (Join-Path $results $trxName) -Raw -Encoding UTF8
            $counters = $trx.TestRun.ResultSummary.Counters
            if ([int]$counters.total -lt 200 -or [int]$counters.failed -ne 0 -or [int]$counters.passed -ne [int]$counters.total) { throw "Incomplete/failed $configuration tests." }
            $testRuns += [pscustomobject]@{ Configuration = $configuration; Total = [int]$counters.total; Passed = [int]$counters.passed; Failed = [int]$counters.failed }
        }
    } finally { Pop-Location }

    $output = Join-Path $src 'ETDisplay\bin\x64\Release'
    $sdkZip = Join-Path $output 'ETDisplay\latest.zip'
    $required = @('ETDisplay.dll', 'ETDisplay.Core.dll', 'ETDisplay.json', 'ETDisplay.deps.json', 'System.Speech.dll', 'runtimes/win/lib/net10.0/System.Speech.dll')
    $allowed = $required + @('ETDisplay.pdb', 'ETDisplay.Core.pdb')
    $zip = [IO.Compression.ZipFile]::OpenRead($sdkZip)
    try {
        $entries = [Collections.Generic.Dictionary[string, IO.Compression.ZipArchiveEntry]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name -notin $allowed -or $entries.ContainsKey($name)) { throw "Unexpected/duplicate runtime payload: $name" }
            $entries.Add($name, $entry)
        }
        foreach ($name in $required) { if (!$entries.ContainsKey($name)) { throw "Missing runtime dependency: $name" } }
        $manifest = (Entry-Text $entries['ETDisplay.json']) | ConvertFrom-Json
        if ($manifest.InternalName -ne 'ETDisplay' -or [int]$manifest.DalamudApiLevel -ne 15 -or $manifest.AssemblyVersion -ne $version) { throw 'Manifest identity/API/version mismatch.' }
        $deps = (Entry-Text $entries['ETDisplay.deps.json']) | ConvertFrom-Json
        $libraryNames = @("ETDisplay/$version", 'System.Speech/10.0.7', 'ETDisplay.Core/1.0.0')
        Assert-Set $deps.libraries.PSObject.Properties.Name $libraryNames 'runtime libraries'
        Assert-Set $deps.targets.PSObject.Properties.Name @('.NETCoreApp,Version=v10.0') 'target frameworks'
        if ($deps.runtimeTarget.name -ne '.NETCoreApp,Version=v10.0') { throw 'Runtime TFM mismatch.' }
        $target = $deps.targets.PSObject.Properties['.NETCoreApp,Version=v10.0'].Value
        Assert-Set $target.PSObject.Properties.Name $libraryNames 'target libraries'
        $app = $target.PSObject.Properties["ETDisplay/$version"].Value
        $core = $target.PSObject.Properties['ETDisplay.Core/1.0.0'].Value
        $speech = $target.PSObject.Properties['System.Speech/10.0.7'].Value
        Assert-Set $app.PSObject.Properties.Name @('dependencies','runtime') 'plugin target fields'
        Assert-Set $app.dependencies.PSObject.Properties.Name @('ETDisplay.Core','System.Speech') 'plugin dependencies'
        if ($app.dependencies.'ETDisplay.Core' -ne '1.0.0' -or $app.dependencies.'System.Speech' -ne '10.0.7') { throw 'Runtime dependency version mismatch.' }
        Assert-Set $app.runtime.PSObject.Properties.Name @('ETDisplay.dll') 'plugin runtime assets'
        Assert-Set $core.PSObject.Properties.Name @('runtime') 'core target fields'
        Assert-Set $core.runtime.PSObject.Properties.Name @('ETDisplay.Core.dll') 'core runtime assets'
        Assert-Set $speech.PSObject.Properties.Name @('runtime','runtimeTargets') 'speech target fields'
        Assert-Set $speech.runtime.PSObject.Properties.Name @('lib/net10.0/System.Speech.dll') 'speech compile/runtime assets'
        Assert-Set $speech.runtimeTargets.PSObject.Properties.Name @('runtimes/win/lib/net10.0/System.Speech.dll') 'speech Windows assets'
        $winAsset = $speech.runtimeTargets.PSObject.Properties['runtimes/win/lib/net10.0/System.Speech.dll'].Value
        if ($winAsset.rid -ne 'win' -or $winAsset.assetType -ne 'runtime') { throw 'Speech runtime asset identity mismatch.' }
        $assemblyIdentity = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $output 'ETDisplay.dll'))
        $actualInformation = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $output 'ETDisplay.dll')).ProductVersion
        if ($assemblyIdentity.Name -ne 'ETDisplay' -or $assemblyIdentity.Version.ToString() -ne $version -or $actualInformation -ne $informationVersion) { throw 'Compiled assembly/informational version mismatch.' }
        $lock = Get-Content (Join-Path $src 'ETDisplay\packages.lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $lockHash = $null
        foreach ($framework in $lock.dependencies.PSObject.Properties) {
            $package = $framework.Value.PSObject.Properties['System.Speech']
            if ($package) { $lockHash = $package.Value.contentHash }
        }
        if (!$lockHash -or $deps.libraries.'System.Speech/10.0.7'.sha512 -ne ('sha512-' + $lockHash)) { throw 'Speech lock/deps content hash mismatch.' }
        $assets = Get-Content (Join-Path $src 'ETDisplay\obj\project.assets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $packageDirectory = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $candidateDirectory = Join-Path $folder 'system.speech\10.0.7'
            if (Test-Path (Join-Path $candidateDirectory 'system.speech.10.0.7.nupkg')) { $packageDirectory = $candidateDirectory; break }
        }
        if (!$packageDirectory) { throw 'Cannot locate the restored Speech NuGet package.' }
        # NuGet contentHash is signature-aware, not SHA512 of the signed ZIP bytes.
        $signatureOutput = Invoke-DotNet 'speech-signature.log' @('nuget', 'verify', (Join-Path $packageDirectory 'system.speech.10.0.7.nupkg'), '--all', '--verbosity', 'normal') -ReturnOutput
        $contentHashMatch = [regex]::Match($signatureOutput, '(?m)^Content hash:\s*(\S+)')
        if (!$contentHashMatch.Success -or $contentHashMatch.Groups[1].Value -cne $lockHash) { throw 'Verified NuGet content hash does not match lock file.' }
        $nuget = [IO.Compression.ZipFile]::OpenRead((Join-Path $packageDirectory 'system.speech.10.0.7.nupkg'))
        try {
            foreach ($mapping in @(@('System.Speech.dll','lib/net10.0/System.Speech.dll'), @('runtimes/win/lib/net10.0/System.Speech.dll','runtimes/win/lib/net10.0/System.Speech.dll'))) {
                $speechDisk = Join-Path $output $mapping[0]
                $speechIdentity = [Reflection.AssemblyName]::GetAssemblyName($speechDisk)
                if ($speechIdentity.Name -ne 'System.Speech' -or $speechIdentity.Version.ToString() -ne '10.0.0.7') { throw 'Speech assembly identity mismatch.' }
                if ((Entry-Hash $entries[$mapping[0]]) -cne (Entry-Hash $nuget.GetEntry($mapping[1]))) { throw 'Packaged Speech bytes differ from locked NuGet assets.' }
            }
        } finally { $nuget.Dispose() }
        $payload = @()
        foreach ($name in $entries.Keys | Sort-Object) {
            $entry = $entries[$name]
            $hash = Entry-Hash $entry
            $diskFile = Join-Path $output ($name.Replace('/', '\'))
            if (!(Test-Path $diskFile) -or $hash -ne (Get-FileHash $diskFile -Algorithm SHA256).Hash.ToLowerInvariant()) { throw "Archive/build payload mismatch: $name" }
            $payload += [pscustomobject]@{ Path = $name; Sha256 = $hash; Bytes = $entry.Length }
        }
        $pe = [IO.File]::ReadAllBytes((Join-Path $output 'ETDisplay.dll'))
        $peOffset = [BitConverter]::ToInt32($pe, 0x3c)
        if ([BitConverter]::ToUInt16($pe, $peOffset + 4) -ne 0x8664) { throw 'Plugin PE architecture is not x64.' }
        foreach ($item in $snapshot) {
            if ((Get-FileHash (Join-Path $src $item.Path) -Algorithm SHA256).Hash -ne $item.Hash) { throw "Build mutated source: $($item.Path)" }
        }
        New-Item -ItemType Directory -Path $release -Force | Out-Null
        $base = "ETDisplay-$version-$buildId"
        $runtimePath = Join-Path $release "$base.zip"
        $candidate = [IO.Compression.ZipFile]::Open($runtimePath, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($name in $entries.Keys | Sort-Object) {
                $input = $entries[$name].Open()
                $target = $candidate.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal).Open()
                try { $input.CopyTo($target) } finally { $input.Dispose(); $target.Dispose() }
            }
            foreach ($name in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'licenses/System.Speech-LICENSE.txt', 'licenses/System.Speech-THIRD-PARTY-NOTICES.TXT', 'docs/INSTALL.md')) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($candidate, (Join-Path $src $name), $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        } finally { $candidate.Dispose() }
    } finally { $zip.Dispose() }

    $sourcePath = Join-Path $release "$base-source.zip"
    $sourceZip = [IO.Compression.ZipFile]::Open($sourcePath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in $snapshot | Sort-Object Path) {
            $entryName = 'ETDisplay/' + $item.Path.Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceZip, (Join-Path $src $item.Path), $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $sourceZip.Dispose() }
    $readBack = [IO.Compression.ZipFile]::OpenRead($runtimePath)
    try {
        if ($readBack.Entries.Count -ne ($payload.Count + 5)) { throw 'Final runtime archive entry count mismatch.' }
        foreach ($item in $payload) {
            if ((Entry-Hash $readBack.GetEntry($item.Path)) -cne $item.Sha256) { throw 'Final runtime archive read-back failed.' }
        }
    } finally { $readBack.Dispose() }
    $readBack = [IO.Compression.ZipFile]::OpenRead($sourcePath)
    try {
        if ($readBack.Entries.Count -ne $snapshot.Count) { throw 'Final source archive entry count mismatch.' }
        foreach ($item in $snapshot) {
            $entryName = 'ETDisplay/' + $item.Path.Replace('\', '/')
            if ((Entry-Hash $readBack.GetEntry($entryName)) -cne $item.Hash.ToLowerInvariant()) { throw 'Final source archive read-back failed.' }
        }
    } finally { $readBack.Dispose() }
    $checksums = @()
    foreach ($path in @($runtimePath, $sourcePath)) {
        $checksums += (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($path)
    }
    Write-Utf8 (Join-Path $release 'SHA256SUMS.txt') (($checksums -join "`n") + "`n")
    $report = [ordered]@{
        Status = 'local-verification-passed'; ReleaseStatus = 'candidate-not-published'
        BuildId = $buildId; AssemblyVersion = $version; ConfigurationVersion = $configVersion
        UtcCreated = [DateTimeOffset]::UtcNow.ToString('o'); DotNetSdk = $sdk
        CleanSourceBuild = $true; Tests = $testRuns; RuntimePayload = $payload
        SourceFileCount = $snapshot.Count; SourceArchive = [IO.Path]::GetFileName($sourcePath)
        RuntimeArchive = [IO.Path]::GetFileName($runtimePath); Checksums = $checksums
        VerificationDirectory = $run
        ManualChecks = 'not-verified-by-this-script'
        InformationalVersion = $informationVersion; DalamudReference = $dalamudReference
        SpeechNuGetSignatureVerified = $true; SpeechNuGetContentHash = $lockHash
    }
    Copy-Item (Join-Path $results '*.trx') -Destination $release
    $successReport = Join-Path $release 'verification-report.json'
    $temporaryReport = $successReport + '.tmp'
    Write-Utf8 $temporaryReport ($report | ConvertTo-Json -Depth 8)
    Move-Item -LiteralPath $temporaryReport -Destination $successReport
    Write-Host "VERIFIED_RELEASE=$release"
    Write-Host 'Local candidate only. No upload, game operation, audio playback, or development output replacement.'
} catch {
    if ($successReport -and (Test-Path $successReport)) { Remove-Item -LiteralPath $successReport }
    Write-Utf8 (Join-Path $run 'FAILED.txt') ($_ | Out-String)
    throw
} finally {
    $env:DALAMUD_HOME = $oldDalamudHome
    Pop-Location
}
