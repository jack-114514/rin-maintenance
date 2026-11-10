param([ValidateSet('check','keepalive')][string]$Mode='check')
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
if($Mode -eq 'keepalive'){
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File 'C:\ProgramData\RinDeploy\keepalive.ps1'
    if($LASTEXITCODE -ne 0){throw '旧保活脚本执行失败，请查看 keepalive.log'}
    '已执行一次原有进程保活检查。'
}
foreach($item in @(@{Name='QQ';Path='C:\Program Files\Tencent\QQNT\QQ.exe'},@{Name='麦麦启动器';Path='C:\DeepSeek-QQ\apps\MaiBot-OneKey\MaiBot OneKey.exe'})){
    $found=@(Get-CimInstance Win32_Process | Where-Object {$_.ExecutablePath -eq $item.Path -and $_.CommandLine -notmatch '(?:^|\s)--type='})
    ($item.Name+'：'+$(if($found.Count){'运行中'}else{'未运行'}))
}
'静默计划任务 RinBot-KeepAlive：已停用，由可见窗口控制。'
