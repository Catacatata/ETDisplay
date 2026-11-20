# ETDisplay

FF14 / Dalamud 狩猎 ET 报时、倒计时与 SS 侦察提醒插件。

**当前为公开测试候选版：1.0.0.2-rc2（Build：settings-ui-rc2）。不是游戏官方插件，也未加入默认社区插件库。**

## 安装与订阅

在卫月/Dalamud设置的自定义插件仓库中添加以下地址，启用后刷新插件安装器，搜索 **ETDisplay** 并安装：

```text
https://raw.githubusercontent.com/Catacatata/ETDisplay/main/repo.json
```

添加仓库不会自动安装所有插件。以后同一个索引可以增加其他作品，已安装插件通过安装器更新。

需要正常访问 GitHub、raw.githubusercontent.com 及 GitHub Release 下载服务。不要将本仓库的 `.git` 地址或网页地址填入卫月。

> 首次安装前停用同名开发版/旧版，避免重复加载。不要只复制主DLL或把源码ZIP当成插件安装包。详细说明见 [安装与常见问题](docs/INSTALL.md)。

## 功能

- 识别 Say / Yell / Shout / Echo 正文中的ET时间，支持 `ET 15:30`、`ET 15.30`、`ET 1530` 等写法；多个候选取最后一个合法时间。
- 高清文字浮层，可调字号、ET/SS颜色、描边，支持拖动、锁定与鼠标穿透。
- 换算ET目标倒计时，支持真实秒校准与末10秒语音读秒。
- 系统侦察消息触发独立 `SS出现！` 提示；玩家转述和Echo复制不会触发。
- Windows系统语音、独立音量、总开关及读秒/SS细分开关。
- 系统休息区提示可自动清空提醒。
- 常规 / 语音 / 外观 / 测试 / 高级五页设置，外观静态实时预览不触发实际提醒或音频。

## 使用

- `/etd` 或 `/etdisplay`：打开设置。
- `/echo ET 12:34`：通过游戏本地Echo测试解析。
- `/etd clear`：清空提醒及语音。
- `/etd reset`：重置实际浮层位置。
- 设置页提供独立ET显示、倒计时、SS和语音测试。

## 环境与已知限制

- **Windows x64，Dalamud API15**；开发验证使用.NET SDK 10.0.401、国服Dalamud 15.0.3.9。
- 国服/国际服更新节奏不同，不承诺所有游戏版本可用；其他环境需要单独验证。
- 使用本机Windows语音，不附带中文语音包，不调用云TTS。声音名称与发音因电脑而异；无可用后端时仍可使用文字与倒计时。
- 打开设置不会初始化语音。首次实际播报或试听时才初始化；失败时正常语音页仅显示“不可用”，排错入口在高级页。
- SS提示只支持目前实现的中英文系统文本候选，其他语言或变动文本可能需要后续适配。
- 游戏/宿主更新可能导致暂时不兼容。第三方插件并非游戏官方功能，使用前请了解所在游戏及运行环境的相关规定和风险。

## 验证范围

Debug与Release各283项核心、配置和只读展示测试通过；运行时依赖、API、x64、版本、哈希及语音依赖包签名已检查。本机功能反馈为基本可用，但这不等于所有机器、语言与缩放环境均已验证。

问题请通过 [Issues](https://github.com/Catacatata/ETDisplay/issues) 反馈，并说明插件Build、宿主API版本、语言、缩放和复现步骤。请勿上传访问令牌、私人配置、完整聊天记录或他人信息。

## 从源码构建

需要稳定.NET 10 SDK、Windows及本机合法安装的Dalamud API15开发依赖，不在仓库中分发游戏/卫月DLL。

```powershell
$env:DALAMUD_HOME = '你的Dalamud API15依赖目录'
dotnet restore ETDisplay.slnx --locked-mode
dotnet build ETDisplay.slnx -c Release --no-restore
dotnet test ETDisplay.Core.Tests/ETDisplay.Core.Tests.csproj -c Release --no-build
```

纯核心和测试可独立构建；插件宿主行为需要游戏内测试。`scripts/Verify-Release.ps1`可执行隔离构建与本地候选校验，不会自动上传或发布。

## 署名与许可证

保留插件原清单署名：**该死之人 奇里格**。GitHub仓库维护账号为 **Catacatata**。

本项目遵循仓库中的 [GNU AGPL v3](LICENSE)，公开分发须遵守对应源码和许可要求。第三方组件许可见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) 与 `licenses/`。不随包分发系统字体、游戏或Dalamud本体。
