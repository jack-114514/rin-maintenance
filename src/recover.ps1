param([string]$Root='C:\DeepSeek-QQ', [ValidateSet('core','snow')][string]$Kind)
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root).TrimEnd('\')
$module=Join-Path $Root 'data\local-lab\modules\MaiBot'
$runtimes=@(Get-ChildItem (Join-Path $module '.onekey\environments') -Directory | Where-Object {Test-Path (Join-Path $_.FullName 'python.exe')})
if($runtimes.Count -ne 1){throw 'Ambiguous Python runtime'}
$python=Join-Path $runtimes[0].FullName 'python.exe'
function Test-Identity($record){
    $current=Get-CimInstance Win32_Process -Filter "ProcessId=$($record.ProcessId)"
    return $current -and $current.CreationDate -eq $record.CreationDate -and $current.ExecutablePath -eq $record.ExecutablePath -and $current.ParentProcessId -eq $record.ParentProcessId
}
if($Kind -eq 'core'){
    $workers=@(Get-CimInstance Win32_Process -Filter "Name='python.exe'" | Where-Object {$_.ExecutablePath -eq $python -and $_.CommandLine -match ' -m src\.runtime$'})
    if($workers.Count -gt 1){throw 'Multiple core workers'}
    if($workers.Count -eq 0){
        $launchers=@(Get-CimInstance Win32_Process -Filter "Name='python.exe'" | Where-Object {$_.ExecutablePath -eq $python -and $_.CommandLine -match 'src\.cli\.tool.*run'})
        if($launchers.Count){throw 'Existing CLI supervisor is starting a worker'}
        Start-Process -FilePath $python -ArgumentList @('-I','-X','utf8','-m','src.cli.tool','run','--directory',('"'+$module+'"')) -WorkingDirectory $module -WindowStyle Hidden
        exit 0
    }
    $worker=$workers[0]
    $parent=Get-CimInstance Win32_Process -Filter "ProcessId=$($worker.ParentProcessId)"
    if($parent.ExecutablePath -ne $python -or $parent.CommandLine -notmatch 'src\.cli\.tool.*run'){throw 'Core supervisor mismatch'}
    try{
        $config=Get-Content (Join-Path $module 'data\webui.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $cookies=New-Object Microsoft.PowerShell.Commands.WebRequestSession
        $body=@{token=$config.access_token}|ConvertTo-Json -Compress
        $auth=Invoke-RestMethod 'http://127.0.0.1:8001/api/webui/auth/verify' -Method Post -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -WebSession $cookies -TimeoutSec 5
        if(!$auth.valid){throw 'Auth rejected'}
        $response=Invoke-RestMethod 'http://127.0.0.1:8001/api/webui/system/restart' -Method Post -WebSession $cookies -TimeoutSec 5
        if(!$response.success){throw 'Restart rejected'}
        for($attempt=0;$attempt -lt 10;$attempt++){
            Start-Sleep -Seconds 2
            if(!(Test-Identity $worker)){exit 0}
        }
    }catch{ }
    # A hung main loop can also hang the normal restart route. Limit fallback to
    # this exact worker and its exact plugin children; the website is unrelated.
    if(!(Test-Identity $worker)){exit 0}
    $children=@(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($worker.ProcessId)" | Where-Object {$_.ExecutablePath -eq $python -and $_.CommandLine -match ' -m src\.plugin_runtime\.runner\.runner_main$'})
    foreach($child in $children){if(Test-Identity $child){Stop-Process -Id $child.ProcessId -ErrorAction SilentlyContinue}}
    if(Test-Identity $worker){Stop-Process -Id $worker.ProcessId}
}else{
    $snow=Join-Path $Root 'data\local-lab\modules\SnowLuma'
    $node=Join-Path $snow 'node.exe'
    $processes=@(Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object {$_.ExecutablePath -eq $node})
    $targetEntry='\\\.maibot-target-runtime\.mjs"?(?:\s|$)'
    $workerEntry='\\(?:index|\.maibot-target-runtime)\.mjs"?(?:\s|$)'
    $supervisors=@($processes | Where-Object {
        $_.CommandLine -match $targetEntry -and $_.ParentProcessId -notin $processes.ProcessId
    })
    $workers=@($processes | Where-Object {
        $_.CommandLine -match $workerEntry -and $_.ParentProcessId -in $supervisors.ProcessId
    })
    if($workers.Count -gt 1){throw 'Multiple SnowLuma workers'}
    if(!$processes.Count){
        Start-Process -FilePath $node -ArgumentList ('"'+(Join-Path $snow '.maibot-target-runtime.mjs')+'"') -WorkingDirectory $snow -WindowStyle Hidden
        exit 0
    }
    if($workers.Count -ne 1){throw 'SnowLuma is starting or process layout changed'}
    $worker=$workers[0]
    $parent=Get-CimInstance Win32_Process -Filter "ProcessId=$($worker.ParentProcessId)"
    if($parent.ExecutablePath -ne $node -or $parent.CommandLine -notmatch $targetEntry){throw 'SnowLuma supervisor mismatch'}
    if(Test-Identity $worker){
        $result=Invoke-CimMethod -InputObject $worker -MethodName Terminate -Arguments @{Reason=[uint32]75}
        if($result.ReturnValue -ne 0){throw 'SnowLuma restart failed'}
    }
}
