#requires -Version 5.1
[CmdletBinding()]
param(
    [string] $AndroidSdkDirectory,
    [string] $JavaSdkDirectory,
    [switch] $CleanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workspaceRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$solutionPath = Join-Path $workspaceRoot 'src\SSSMovementLogger.sln'
$apkDirectory = Join-Path $workspaceRoot 'src\SSSMovementLogger\bin\Release\net9.0-android35.0'
$OutputDirectory = [IO.Path]::GetFullPath((Join-Path $workspaceRoot 'out'))

if ($CleanOnly) {
    $rootPrefix = $workspaceRoot.TrimEnd('\') + '\'
    $cleanTargets = @(
        $OutputDirectory,
        (Join-Path $workspaceRoot 'src\.vs'),
        (Join-Path $workspaceRoot 'src\SSSMovementLogger\bin'),
        (Join-Path $workspaceRoot 'src\SSSMovementLogger\obj')
    )
    # Validate every target and its ancestors before deleting anything.
    foreach ($target in $cleanTargets) {
        $targetPath = [IO.Path]::GetFullPath($target)
        if (-not $targetPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Clean target must be inside the workspace: $targetPath"
        }
        $ancestor = $targetPath
        while (-not [string]::Equals($ancestor, $workspaceRoot, [StringComparison]::OrdinalIgnoreCase)) {
            if (Test-Path -LiteralPath $ancestor) {
                $item = Get-Item -LiteralPath $ancestor -Force
                if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                    throw "Clean refused a link or file: $ancestor"
                }
            }
            $ancestor = Split-Path -Parent $ancestor
        }
        if (Test-Path -LiteralPath $targetPath) {
            $folders = [Collections.Generic.Stack[string]]::new()
            $folders.Push($targetPath)
            while ($folders.Count -gt 0) {
                foreach ($item in Get-ChildItem -LiteralPath $folders.Pop() -Force) {
                    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                        throw "Clean refused a link: $($item.FullName)"
                    }
                    if ($item.PSIsContainer) { $folders.Push($item.FullName) }
                }
            }
        }
    }
    foreach ($target in $cleanTargets) {
        if (Test-Path -LiteralPath $target) {
            foreach ($item in Get-ChildItem -LiteralPath $target -Force) {
                Remove-Item -LiteralPath $item.FullName -Recurse -Force
            }
        }
    }
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    Write-Host "Clean completed: $($cleanTargets -join ', ')"
    return
}

# Prefer the same MSBuild as Visual Studio 2022; otherwise use the .NET CLI.
$msbuildPath = $null
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path -LiteralPath $vswherePath) {
    $candidates = @(& $vswherePath -latest -products '*' -version '[17.0,18.0)' `
        -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\MSBuild.exe')
    if ($LASTEXITCODE -ne 0) {
        throw 'Visual Studio 2022 の検索に失敗しました。'
    }
    if ($candidates.Count -gt 0) {
        $msbuildPath = $candidates[0]
    }
}

if ($msbuildPath) {
    $buildCommand = $msbuildPath
    $buildArguments = @($solutionPath, '/restore', '/t:Build', '/p:Configuration=Release',
        '/p:AndroidPackageFormats=apk', '/nologo', '/verbosity:minimal')
} else {
    $buildCommand = (Get-Command dotnet -ErrorAction Stop).Source
    $buildArguments = @('build', $solutionPath, '--configuration', 'Release', '--nologo',
        '--verbosity', 'minimal', '-p:AndroidPackageFormats=apk')
}

function Find-SdkDirectory {
    param([string[]] $Candidates, [string] $Marker)
    foreach ($candidate in $Candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and
            (Test-Path -LiteralPath (Join-Path $candidate $Marker) -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).ProviderPath
        }
    }
    return $null
}

$installDependencies = $false
if ([string]::IsNullOrWhiteSpace($AndroidSdkDirectory)) {
    $AndroidSdkDirectory = Find-SdkDirectory -Marker 'platforms\android-35\android.jar' -Candidates @(
        $env:ANDROID_SDK_ROOT,
        $env:ANDROID_HOME,
        (Join-Path ${env:ProgramFiles(x86)} 'Android\android-sdk'),
        (Join-Path $env:LOCALAPPDATA 'Android\Sdk'),
        (Join-Path $workspaceRoot '.build-tools\android-sdk')
    )
    if (-not $AndroidSdkDirectory) {
        $AndroidSdkDirectory = Join-Path $workspaceRoot '.build-tools\android-sdk'
        $installDependencies = $true
    }
}
if ([string]::IsNullOrWhiteSpace($JavaSdkDirectory)) {
    $jdkCandidates = @($env:JAVA_HOME)
    $microsoftDirectory = Join-Path $env:ProgramFiles 'Microsoft'
    if (Test-Path -LiteralPath $microsoftDirectory -PathType Container) {
        $jdkCandidates += @(Get-ChildItem -LiteralPath $microsoftDirectory -Directory -Filter 'jdk-17*' |
            Sort-Object Name -Descending | Select-Object -ExpandProperty FullName)
    }
    $jdkCandidates += Join-Path $workspaceRoot '.build-tools\jdk'
    $JavaSdkDirectory = Find-SdkDirectory -Marker 'bin\javac.exe' -Candidates $jdkCandidates
    if (-not $JavaSdkDirectory) {
        $JavaSdkDirectory = Join-Path $workspaceRoot '.build-tools\jdk'
        $installDependencies = $true
    }
}
if ($installDependencies) {
    Write-Host 'SDK / JDK が不足しているため、必要な依存ツールを取得します（初回はインターネット接続が必要です）。'
    $dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
    $androidPackDirectory = Join-Path (Split-Path -Parent $dotnetCommand) 'packs\Microsoft.Android.Sdk.Windows'
    if (-not (Test-Path -LiteralPath $androidPackDirectory -PathType Container)) {
        & $dotnetCommand workload install android --skip-manifest-update
        if ($LASTEXITCODE -ne 0) {
            throw 'Android ワークロードの導入に失敗しました。ネットワーク接続と導入権限を確認してください。'
        }
    }
    $setupArguments = @('build', (Join-Path $workspaceRoot 'src\SSSMovementLogger\SSSMovementLogger.csproj'),
        '-t:InstallLoggerBuildTools', '-f:net9.0-android35.0',
        "-p:AndroidSdkDirectory=$AndroidSdkDirectory", "-p:JavaSdkDirectory=$JavaSdkDirectory",
        '-p:AcceptAndroidSDKLicenses=True', '--nologo', '--verbosity', 'minimal')
    & $dotnetCommand @setupArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Android SDK / JDK の取得に失敗しました。ネットワーク接続を確認して再実行してください。'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $AndroidSdkDirectory 'platforms\android-35\android.jar')) -or
        -not (Test-Path -LiteralPath (Join-Path $JavaSdkDirectory 'bin\javac.exe'))) {
        throw '依存ツールの取得後も SDK / JDK が不足しています。取得処理のログを確認してください。'
    }
}
Write-Host "Android SDK: $AndroidSdkDirectory"
Write-Host "Java SDK: $JavaSdkDirectory"

foreach ($sdk in @(
    @{ Name = 'AndroidSdkDirectory'; Value = $AndroidSdkDirectory },
    @{ Name = 'JavaSdkDirectory'; Value = $JavaSdkDirectory }
)) {
    if (-not [string]::IsNullOrWhiteSpace($sdk.Value)) {
        $sdkPath = (Resolve-Path -LiteralPath $sdk.Value -ErrorAction Stop).ProviderPath
        if (-not (Test-Path -LiteralPath $sdkPath -PathType Container)) {
            throw "SDK の指定先がフォルダではありません: $sdkPath"
        }
        $buildArguments += "-p:$($sdk.Name)=$sdkPath"
    }
}

Write-Host 'Release APK をビルドしています。'
& $buildCommand @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "Release ビルドに失敗しました（終了コード $LASTEXITCODE）。APK はコピーしていません。"
}

$signedApks = @(Get-ChildItem -LiteralPath $apkDirectory -Filter 'SSSMovementLogger-Signed.apk' -File)
if ($signedApks.Count -ne 1) {
    throw "署名済み APK が1つに確定できません（$($signedApks.Count) 件）。"
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputPath = Join-Path $OutputDirectory $signedApks[0].Name
if (-not [string]::Equals($signedApks[0].FullName, $outputPath, [StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $signedApks[0].FullName -Destination $outputPath -Force
}
Write-Host "生成完了: $outputPath"
