$ErrorActionPreference = 'Stop'
$state = 'C:\ProgramData\RinDeploy'
$log = Join-Path $state 'keepalive.log'
$qq = 'C:\Program Files\Tencent\QQNT\QQ.exe'
$launcher = 'C:\DeepSeek-QQ\apps\MaiBot-OneKey\MaiBot OneKey.exe'
$core = 'C:\DeepSeek-QQ\data\local-lab\modules\MaiBot\.onekey\environments\<your-environment-id>\python.exe'
$sessionId = (Get-Process -Id $PID).SessionId

function Write-Action($message) {
    if ((Test-Path -LiteralPath $log) -and (Get-Item -LiteralPath $log).Length -gt 512KB) {
        Move-Item -LiteralPath $log -Destination (Join-Path $state 'keepalive.previous.log') -Force
    }
    Add-Content -LiteralPath $log -Value "$(Get-Date -Format o) $message" -Encoding UTF8
}

function Test-MainProcess($file) {
    $name = [IO.Path]::GetFileName($file)
    $processes = Get-CimInstance Win32_Process -Filter "Name='$name' AND SessionId=$sessionId"
    return [bool]($processes | Where-Object {
        $_.ExecutablePath -eq $file -and $_.CommandLine -notmatch '(?:^|\s)--type='
    } | Select-Object -First 1)
}

function Get-CommitHeadroom {
    $memory = Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory
    return ([long]$memory.CommitLimit - [long]$memory.CommittedBytes)
}

function Test-CoreHttp {
    $request = [Net.HttpWebRequest]::Create('http://127.0.0.1:8001/')
    $request.Proxy = $null
    $request.Timeout = 4000
    $request.ReadWriteTimeout = 4000
    $request.AllowAutoRedirect = $false
    try {
        $response = $request.GetResponse()
        $response.Close()
        return $true
    } catch [Net.WebException] {
        if ($_.Exception.Response) {
            $_.Exception.Response.Close()
            return $true
        }
        return $false
    }
}

function Test-RunningCore {
    # Main-loop/status recovery is owned by auto_repair.py.
    try {
        $lease=Get-Content 'C:\DeepSeek-QQ\data\auto-repair\gui-repair.json' -Raw -ErrorAction Stop | ConvertFrom-Json
        $age=([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())-[double]$lease.time
        if($lease.enabled -and $age -ge 0 -and $age -lt 90){return}
    } catch { }
    $owners = @(Get-NetTCPConnection -State Listen -LocalAddress '127.0.0.1' -LocalPort 8001 -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
    if ($owners.Count -ne 1) { return }
    $target = Get-CimInstance Win32_Process -Filter "ProcessId=$($owners[0]) AND SessionId=$sessionId"
    if (!$target -or $target.ExecutablePath -ne $core) { return }
    $parent = Get-CimInstance Win32_Process -Filter "ProcessId=$($target.ParentProcessId)"
    if ($parent.ExecutablePath -ne $core -or $parent.CommandLine -notmatch 'src\.cli\.tool.*run') { return }

    $healthFile = Join-Path $state 'keepalive-core.json'
    $last = if (Test-Path -LiteralPath $healthFile) {
        Get-Content -LiteralPath $healthFile -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    $identity = "$($target.ProcessId)@$($target.CreationDate.ToString('o'))"
    $healthy = Test-CoreHttp
    $sameProcess = $last -and $last.Identity -eq $identity
    $failed = if ($healthy -or !$sameProcess) { 0 } else { 1 + [int]$last.Failures }
    $previouslyHealthy = $healthy -or ($sameProcess -and $last.PreviouslyHealthy)
    $lastRestart = $last.LastRestart
    if (!$healthy -and $previouslyHealthy -and $failed -ge 3 -and
        (!$lastRestart -or (Get-Date) -gt ([datetime]$lastRestart).AddMinutes(10)) -and
        (Get-CommitHeadroom) -ge 384MB) {
        $current = Get-CimInstance Win32_Process -Filter "ProcessId=$($target.ProcessId) AND SessionId=$sessionId"
        if ($current -and $current.CreationDate -eq $target.CreationDate -and $current.ExecutablePath -eq $core) {
            $runners = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($current.ProcessId) AND SessionId=$sessionId" |
                Where-Object {$_.ExecutablePath -eq $core -and $_.CommandLine -match 'src\.plugin_runtime\.runner\.runner_main'})
            Stop-Process -Id $current.ProcessId -ErrorAction Stop
            foreach ($runner in $runners) {
                $remaining = Get-CimInstance Win32_Process -Filter "ProcessId=$($runner.ProcessId) AND SessionId=$sessionId"
                if ($remaining -and $remaining.CreationDate -eq $runner.CreationDate -and $remaining.ExecutablePath -eq $core) {
                    Stop-Process -Id $remaining.ProcessId -ErrorAction SilentlyContinue
                }
            }
            $lastRestart = (Get-Date).ToString('o')
            Write-Action "MaiBot core PID=$($current.ProcessId) stopped after consecutive HTTP timeouts; OneKey will restart it"
        }
    }
    @{ Identity = $identity; Failures = $failed; PreviouslyHealthy = $previouslyHealthy; LastRestart = $lastRestart } |
        ConvertTo-Json -Compress | Set-Content -LiteralPath $healthFile -Encoding UTF8
}

try {
    if ($sessionId -eq 0 -or (Test-Path -LiteralPath (Join-Path $state 'keepalive.paused'))) { exit 0 }
    foreach ($program in @(
        @{Id = 'qq'; Path = $qq},
        @{Id = 'onekey'; Path = $launcher}
    )) {
        if (Test-MainProcess $program.Path) { continue }
        $lastAttempt = Join-Path $state ('keepalive-' + $program.Id + '.stamp')
        if ((Test-Path -LiteralPath $lastAttempt) -and
            (Get-Date) -lt (Get-Item -LiteralPath $lastAttempt).LastWriteTime.AddMinutes(2)) { exit 0 }
        Set-Content -LiteralPath $lastAttempt -Value (Get-Date -Format o) -Encoding ASCII
        if ((Get-CommitHeadroom) -lt 384MB) {
            Write-Action "$($program.Id) restart deferred: commit headroom below 384 MiB"
            exit 0
        }
        if (!(Test-Path -LiteralPath $program.Path -PathType Leaf)) {
            Write-Action "$($program.Id) executable is missing"
            exit 1
        }
        # Launch in the signed-in desktop, one application per run.
        if (!(Test-MainProcess $program.Path)) {
            $child = Start-Process -FilePath $program.Path -WorkingDirectory (Split-Path -Parent $program.Path) -PassThru
            Write-Action "$($program.Id) restarted in session $sessionId, PID=$($child.Id)"
        }
        exit 0
    }
    Test-RunningCore
} catch {
    Write-Action ("watchdog check failed: " + $_.Exception.GetType().Name)
    exit 1
}
