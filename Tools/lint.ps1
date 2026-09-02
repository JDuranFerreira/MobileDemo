#Requires -Version 5.1
<#
.SYNOPSIS
    Lints the project's C# against .editorconfig. See ARCHITECTURE.md §15 ("Process").

.DESCRIPTION
    Wraps `dotnet format` over the .csproj files Unity generates for this project's
    assemblies. Two passes, because they catch different things:

      whitespace  indentation, blank lines, trailing space, final newline. Needs only a
                  syntax tree, so it works even when the code does not compile.
      style       the IDE#### rules -- `this.` qualification, unused usings, naming,
                  modifier order. Needs a real compilation, so it needs Unity's generated
                  references to be present and current.

    Runs read-only by default and exits non-zero on the first violating pass, so it can
    gate a commit. Pass -Fix to rewrite the files instead.

    Why the generated .csproj files and not a committed one: Unity owns them (they are
    gitignored, and regenerated from the .asmdef files), and they carry the engine
    references and UNITY_* defines that the style pass needs in order to resolve types.
    The cost of that is the gotcha below.

.NOTES
    GOTCHA -- a new assembly is invisible to the style pass until Unity regenerates the
    project files. If you added an .asmdef or the first .cs file to an existing one, open
    the project in Unity (or Assets > Open C# Project) before trusting a clean run. The
    script reports which assemblies it saw so that a missing one is obvious rather than
    silent.

    EXPECTED NOISE -- "Warnings were encountered while loading the workspace" on
    MobileDemo.Tests.EditMode is benign. At diagnostic verbosity it reads "Found project
    reference without a matching metadata reference": Unity writes a ProjectReference to
    MobileDemo.Core without MSBuild ever having built it. The files still get analysed
    (confirmed at diagnostic verbosity), so the warning is left visible rather than
    suppressed -- hiding it would also hide a real workspace failure.

.PARAMETER Fix
    Apply fixes in place instead of only reporting them.

.PARAMETER WhitespaceOnly
    Skip the style pass. Useful when the project does not currently compile.

.EXAMPLE
    ./Tools/lint.ps1
    Report violations, change nothing, exit 1 if any are found.

.EXAMPLE
    ./Tools/lint.ps1 -Fix
    Fix what can be fixed automatically.
#>
[CmdletBinding()]
param(
    [switch]$Fix,
    [switch]$WhitespaceOnly
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Host 'dotnet SDK not found on PATH. Install the .NET SDK (8.0 or newer).' -ForegroundColor Red
        exit 2
    }

    # Only this project's assemblies. Unity also generates .csproj files for packages under
    # Packages/, and those are not ours to reformat.
    $projects = @(Get-ChildItem -Path $repoRoot -Filter '*.csproj' -File |
        Where-Object { $_.BaseName -like 'MobileDemo*' -or $_.BaseName -like 'Assembly-CSharp*' } |
        Sort-Object Name)

    if ($projects.Count -eq 0) {
        Write-Host 'No generated .csproj found.' -ForegroundColor Red
        Write-Host 'Open the project in Unity once (Assets > Open C# Project) to generate them.'
        exit 2
    }

    $passes = if ($WhitespaceOnly) { @('whitespace') } else { @('whitespace', 'style') }
    $mode = if ($Fix) { 'fixing' } else { 'checking' }

    Write-Host ''
    Write-Host "Lint ($mode) -- $($projects.Count) assembly/assemblies:" -ForegroundColor Cyan
    $projects | ForEach-Object { Write-Host "  $($_.BaseName)" -ForegroundColor DarkGray }
    Write-Host ''

    $failed = $false
    foreach ($pass in $passes) {
        foreach ($project in $projects) {
            $arguments = @('format', $pass, $project.Name, '--verbosity', 'minimal')
            if (-not $Fix) {
                $arguments += '--verify-no-changes'
            }

            Write-Host "dotnet $($arguments -join ' ')" -ForegroundColor DarkGray

            # 'Continue' around the native call on purpose. `dotnet format` writes its
            # workspace warning (see EXPECTED NOISE above) to stderr on every run, and under
            # 'Stop' Windows PowerShell 5.1 promotes a native command's stderr to a
            # terminating NativeCommandError as soon as output is redirected or piped -- so
            # the script would die on a warning it is meant to ignore, but only when run
            # non-interactively. Exit codes are what we actually judge on, checked below.
            $previous = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try {
                & dotnet @arguments
            }
            finally {
                $ErrorActionPreference = $previous
            }

            if ($LASTEXITCODE -ne 0) {
                $failed = $true
            }
        }
    }

    Write-Host ''
    if ($failed) {
        if ($Fix) {
            # `dotnet format --fix` reports what it could not fix automatically; naming and
            # most language-preference violations need a human.
            Write-Host 'Some violations could not be fixed automatically -- see above.' -ForegroundColor Yellow
        }
        else {
            Write-Host 'Lint failed. Run ./Tools/lint.ps1 -Fix to apply what is automatic.' -ForegroundColor Red
        }
        exit 1
    }

    Write-Host 'Lint clean.' -ForegroundColor Green
    exit 0
}
finally {
    Pop-Location
}
