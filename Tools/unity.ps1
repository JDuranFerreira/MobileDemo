#Requires -Version 5.1
<#
.SYNOPSIS
    Drives the Unity editor from the command line. See ARCHITECTURE.md §15 ("Process").

.DESCRIPTION
    Two jobs, because they are the only two this project has ever needed:

      -Tests            run the EditMode suite headless and report pass/fail.
      -Method <name>    run a static method, for the throwaway authoring and session
                        scripts §11 describes.

    It exists because neither invocation is guessable and both have a trap that fails
    *silently* -- five slices rediscovered them from the log rather than from a script.

.NOTES
    TRAP 1 -- `-runTests` must never be combined with `-quit`. The editor exits before the
    test runner starts and returns 0, so the run reports success having executed nothing.
    This script therefore never passes -quit, and judges a test run on the result XML
    rather than on the exit code alone.

    TRAP 2 -- do not write PlayerSettings.runInBackground from a batch-mode script. The
    write does not flush before the editor exits, which leaves ProjectSettings.asset dirty
    in git and had to be reverted by hand once. A session driver that needs it assigns
    Application.runInBackground at runtime instead.

    TRAP 3 -- Unity refuses to open a project that another editor already has open, with a
    lock error rather than a queue. Close the editor before running this.

.PARAMETER Tests
    Run the EditMode test suite. Results are written to Logs/test-results.xml.

.PARAMETER Method
    Fully qualified static method to run, e.g. MobileDemo.Editor.SliceSixAuthoring.Run.
    Implies -executeMethod. Combine with -KeepOpen for a method that enters play mode.

.PARAMETER KeepOpen
    Do not pass -quit. Required for a method that enters play mode, since the editor has
    to survive long enough to play; such a method is responsible for calling
    EditorApplication.Exit itself.

.PARAMETER UnityPath
    Override the editor executable. By default it is resolved from
    ProjectSettings/ProjectVersion.txt against the Unity Hub install location.

.PARAMETER TimeoutMinutes
    Kill the editor after this long. Default 15.

.EXAMPLE
    ./Tools/unity.ps1 -Tests

.EXAMPLE
    ./Tools/unity.ps1 -Method MobileDemo.Editor.SliceSixAuthoring.Run

.EXAMPLE
    ./Tools/unity.ps1 -Method MobileDemo.Editor.SliceSixSession.Run -KeepOpen
#>
[CmdletBinding(DefaultParameterSetName = 'Tests')]
param(
    [Parameter(ParameterSetName = 'Tests', Mandatory = $true)]
    [switch]$Tests,

    [Parameter(ParameterSetName = 'Method', Mandatory = $true)]
    [string]$Method,

    [Parameter(ParameterSetName = 'Method')]
    [switch]$KeepOpen,

    [string]$UnityPath,
    [int]$TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-UnityExecutable {
    param([string]$Override, [string]$RepoRoot)

    if ($Override) {
        if (-not (Test-Path $Override)) {
            throw "No Unity editor at -UnityPath '$Override'."
        }

        return $Override
    }

    # The project's own version, not whatever is newest: opening this project with a
    # different editor rewrites ProjectVersion.txt and can upgrade every asset silently.
    $versionFile = Join-Path $RepoRoot 'ProjectSettings/ProjectVersion.txt'
    $line = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(\S+)' |
        Select-Object -First 1
    if (-not $line) {
        throw "Could not read m_EditorVersion from $versionFile."
    }

    $version = $line.Matches[0].Groups[1].Value
    $candidate = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
    if (-not (Test-Path $candidate)) {
        throw "Project wants Unity $version but it is not installed at '$candidate'. Pass -UnityPath."
    }

    return $candidate
}

$unity = Resolve-UnityExecutable -Override $UnityPath -RepoRoot $repoRoot
$logDirectory = Join-Path $repoRoot 'Logs'
if (-not (Test-Path $logDirectory)) {
    New-Item -ItemType Directory -Path $logDirectory | Out-Null
}

$logFile = Join-Path $logDirectory 'cli.log'
$resultsFile = Join-Path $logDirectory 'test-results.xml'

# Paths are quoted individually, not left to Start-Process. It joins -ArgumentList with
# spaces and adds no quotes of its own, so an unquoted "D:\Unity Projects\MobileDemo"
# reaches Unity as two arguments and it reports a project path with the fragments
# concatenated -- a confusing error a long way from its cause.
function Format-Argument {
    param([string]$Value)
    return '"' + $Value + '"'
}

# -logFile to a real file rather than to stdout: batch mode interleaves the editor's own
# startup chatter with ours, and a file can be grepped after the fact.
$arguments = @(
    '-batchmode'
    '-nographics'
    '-projectPath', (Format-Argument $repoRoot)
    '-logFile', (Format-Argument $logFile)
)

if ($Tests) {
    if (Test-Path $resultsFile) {
        Remove-Item $resultsFile
    }

    # No -quit. See TRAP 1: the runner would never start.
    $arguments += @('-runTests', '-testPlatform', 'EditMode', '-testResults', (Format-Argument $resultsFile))
}
else {
    $arguments += @('-executeMethod', $Method)

    # -quit for a method that only touches assets; withheld for one that plays, because
    # the editor has to still be running when play mode starts.
    if (-not $KeepOpen) {
        $arguments += '-quit'
    }
}

Write-Host ''
Write-Host "Unity: $unity" -ForegroundColor Cyan
Write-Host "  $($arguments -join ' ')" -ForegroundColor DarkGray
Write-Host "  log: $logFile" -ForegroundColor DarkGray
Write-Host ''

$process = Start-Process -FilePath $unity -ArgumentList $arguments -PassThru -NoNewWindow

# Touching .Handle is not a no-op. Windows PowerShell 5.1 hands back a Process object whose
# native handle it has already closed, and ExitCode then reads as empty however long you
# wait; asking for the handle here is what makes .NET keep it open. Without this the wrapper
# reports "Editor exited ." and, because $null -ne 0, calls a clean run a failure.
$null = $process.Handle

if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    Write-Host "Timed out after $TimeoutMinutes minute(s) -- killing the editor." -ForegroundColor Red
    $process.Kill()
    exit 3
}

# The timed WaitForExit returns as soon as the process signals, before .NET has cached the
# exit code -- so ExitCode reads as empty without this second, unbounded wait. Reading it
# empty and treating that as failure is a fake red that costs a rerun.
$process.WaitForExit()
$exitCode = $process.ExitCode
Write-Host ''
Write-Host "Editor exited $exitCode." -ForegroundColor DarkGray

if ($Tests) {
    # Judged on the XML, not on the exit code, for TRAP 1's reason: an editor that exits
    # cleanly having run nothing is the failure this check exists to catch.
    if (-not (Test-Path $resultsFile)) {
        Write-Host 'No test results were written -- the runner did not start.' -ForegroundColor Red
        Write-Host "See $logFile."
        exit 1
    }

    [xml]$results = Get-Content $resultsFile
    $run = $results.'test-run'
    Write-Host ''
    Write-Host "Tests: total=$($run.total) passed=$($run.passed) failed=$($run.failed) skipped=$($run.skipped)" -ForegroundColor Cyan

    if ([int]$run.failed -gt 0 -or [int]$run.total -eq 0) {
        Write-Host 'Test run failed.' -ForegroundColor Red
        exit 1
    }

    Write-Host 'Tests green.' -ForegroundColor Green
    exit 0
}

if ($exitCode -ne 0) {
    Write-Host "See $logFile." -ForegroundColor Red
    exit 1
}

exit 0
