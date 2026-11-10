# 小凛管家 · Rin Maintenance

**专为 Windows Server 上的麦麦（MaiBot）引擎和 QQ 机器人制作的桌面运维工具。**

小凛管家面向已经使用 MaiBot OneKey 和 SnowLuma 部署 QQ 机器人的用户。它在一个可见窗口里管理登录自启、QQ 和麦麦进程保活、消息链路巡检和故障恢复，方便通过远程桌面查看哪项维护在运行、刚做了什么。

## 功能

| 功能 | 行为 |
| --- | --- |
| 登录后自动启动 | 在窗口开启「开机自启」后，Windows 账户登录时打开小凛管家。在当前会话中启动尚未运行的 QQ 和 MaiBot OneKey，并开启自动修复与进程保活。 |
| 自动启动麦麦引擎 | 由 OneKey 的「自动启动服务」启动麦麦引擎和 SnowLuma。服务配置和日志仍可在 OneKey 中查看。 |
| 自动接入目标 QQ | QQ 登录后，由 SnowLuma 的目标账号监测接入 OneKey 配置的机器人账号。随项目提供的 SnowLuma 2.1.1 补丁修复了监督进程启动时丢失目标账号入口的问题。 |
| 故障修复 | 检查 OneBot 的 QQ 在线与健康状态，以及麦麦主循环心跳。常规自动恢复在连续 3 次检测失败后触发，恢复间隔至少 10 分钟，每小时最多 3 次。 |
| 主循环防卡 | 配套监测器记录麦麦主循环心跳。启动宽限期结束后，持续无响应会先保存线程栈，再由现有监督进程恢复。 |
| 进程保活 | 检查当前会话里的 QQ、OneKey 和本机麦麦 WebUI，按条件拉起缺失的程序或处理 WebUI 连续超时。故障修复开启时，WebUI 保活会跳过对同一核心的恢复。 |
| 校验与备份 | 自动修复使用部署方生成的 SHA-256 清单。仅对已知文件版本应用经过校验的本地修复载荷，遇到未知源码会记录路径。替换前保留备份。 |
| 可见的操作和日志 | 两项维护分别显示状态和日志，支持一键开启或暂停、单项开关、立即检查、手动修复麦麦和重连 SnowLuma。支持深浅色切换和 1.2 倍字号。 |

## 使用

1. 在 Windows Server 的交互式桌面登录 QQ。在 OneKey 的消息平台选择 SnowLuma，填入要接入的机器人 QQ 号，并开启 OneKey 的「自动启动服务」。
2. 按下方说明构建并配置本机部署。首次运行时先查看「立即检查」的结果，确认麦麦和 QQ 链路正常。
3. 手动打开管家时，两项自动维护默认暂停。点击「开启所有自动维护」，或在两个标签页分别启用。
4. 需要登录自启时，把窗口里的「开机自启」切换到「开」。启动后留出约 60 秒给麦麦和 SnowLuma 初始化，再开始自动巡检。这个选项使用当前 Windows 账户的交互式登录任务，需要该账户登录桌面会话。
5. 最小化后维护继续运行。关闭窗口会停止自动维护，QQ 和麦麦会继续运行。关闭提示可勾选「下次不再提醒」。

## 适用范围

- 验证过的组合是 Windows Server 上的 **MaiBot OneKey + SnowLuma 2.1.1 + QQNT**，使用单个机器人 QQ 账号和一套 MaiBot Python 环境。
- 本版本按原部署使用固定的目录结构和本机端口。其他目录、版本、协议端或多账号环境需适配后单独验证。
- 使用你自己安装的麦麦、OneKey、QQ、SnowLuma 和 Python 环境。仓库提供维护工具源码及补丁逻辑。修复清单、已修复的第三方文件、QQ 登录态和模型配置由部署方留在本机。

## 构建

在 64 位 Windows PowerShell 中进入仓库根目录，运行下列命令。构建使用系统的 .NET Framework C# 编译器。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

生成文件为 **`dist\小凛管家.exe`**。运行时需要管理员权限，用于检查和恢复本机进程以及设置登录任务。构建完成后仍需准备下列运行依赖。

## 部署到已有 MaiBot OneKey 实例

| 项目 | 当前源码使用的位置 |
| --- | --- |
| 项目根目录 | `C:\DeepSeek-QQ` |
| QQNT | `C:\Program Files\Tencent\QQNT\QQ.exe` |
| MaiBot OneKey | `C:\DeepSeek-QQ\apps\MaiBot-OneKey\MaiBot OneKey.exe` |
| MaiBot 和 SnowLuma 模块 | `C:\DeepSeek-QQ\data\local-lab\modules` 下的 `MaiBot` 和 `SnowLuma` |
| MaiBot WebUI | `http://127.0.0.1:8001/` |
| SnowLuma OneBot WebSocket | `ws://127.0.0.1:7988/` |
| 维护后端脚本 | `C:\DeepSeek-QQ\extensions\snowluma-idle-health` |
| 进程保活脚本 | `C:\ProgramData\RinDeploy\keepalive.ps1` |

1. 先通过 OneKey 启动一次现有实例，确认 WebUI、本机 OneBot WebSocket 和目标 QQ 正常，并备份现有模块。确认 SnowLuma 已生成目标账号入口 `.maibot-target-runtime.mjs`。维护后端通过已安装的 MaiBot Python 环境访问本机 OneBot，需有 `aiohttp`。
2. 将 `src/auto_repair.py`、`src/recover.ps1`、`src/keepalive-console.ps1` 放到维护后端目录。将 `src/legacy/keepalive.ps1` 作为进程保活脚本部署到表中位置，并将脚本内的 Python 路径指向本机 OneKey 实例。中文 PowerShell 文件需保存为带 BOM 的 UTF-8，供 Windows PowerShell 5.1 读取。
3. 对照目标版本审查 [SnowLuma 补丁](src/patch.py) 和 [麦麦防卡安装脚本](src/install_guard.py)。两者都以项目根目录为参数。按需应用并重新启动对应服务。SnowLuma 需重启监督进程，才能加载修正后的入口。
4. 用 [清单生成模板](src/prepare_manifest.py) 记录本机已经验证的修复文件和原始备份。先将模板中的环境和备份路径改为本机路径，再生成 `data/auto-repair/manifest.json` 和对应的 `payload/`。这些文件留在本机。
5. 启动管家，先运行只读检查。核对日志后再启用自动维护。已有的静默巡检任务应保持停用，防止重复处理。

## 查看状态与排查

- 管家窗口底部会显示检查和恢复结果。`data/auto-repair/last_status.json` 记录最近一次故障修复巡检，`repair.log` 记录状态变化和恢复动作。
- 麦麦 WebUI 可打开但机器人无响应时，查看管家显示的主循环及 QQ 状态，同时确认 OneKey 里的 SnowLuma 配置仍指向目标账号。QQ 如果要求扫码或重新认证，请在服务器的 QQ 客户端完成。
- 升级 MaiBot 或 SnowLuma 后，先在测试环境验证补丁的适用性，再更新本地修复清单。已有修复文件的备份可用于回滚。
- 提交 Issue 时请附 Windows Server、MaiBot OneKey 和 SnowLuma 的版本、操作步骤和经过脱敏的日志片段。删除 QQ 号、服务器地址、访问令牌和聊天内容。

## 相关项目

- [麦麦 MaiBot](https://github.com/Mai-with-u/MaiBot)
- [MaiBot OneKey](https://github.com/Mai-with-u/MaiBotOneKey)
- [MaiBot SnowLuma 适配器](https://github.com/Mai-with-u/MaiBot-SnowLuma-Adapter)
- [SnowLuma 文档](https://snowluma.github.io/zh/docs/guide/configuration)

## 许可证

小凛管家的源码和文档采用 [MIT 许可证](LICENSE)。MaiBot、OneKey、SnowLuma 和 QQ 各自适用其上游许可或使用条款。
