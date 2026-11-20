# ETDisplay 1.0.0.2-rc2 · 公开测试版

首次公开测试候选，Build为settings-ui-rc2，适用Windows x64 / Dalamud API15。不是游戏官方插件，也未加入默认社区插件库。

## 安装

在卫月的自定义插件仓库中添加并启用：

https://raw.githubusercontent.com/Catacatata/ETDisplay/main/repo.json

刷新插件安装器，搜索ETDisplay，安装后输入 `/etd`。曾使用开发加载的用户请先停用同名开发实例，避免重复加载。

## 本版功能

- ET聊天时间识别、高清文字浮层和倒计时。
- Windows系统语音、独立音量、末10秒读秒与SS优先级。
- 系统消息SS侦察提示及固定冷却，休息区自动清空。
- 常规/语音/外观/测试/高级五页设置，独立静音样式预览。

## 验证与限制

Debug与Release各283项测试通过，完整安装包的依赖、版本、API、x64与哈希已经检查。国服开发验证使用Dalamud15.0.3.9；国际服、其他语言与不同电脑仍需反馈验证。

本机系统语音不同可能导致声音不同或不可用，不附带中文语音包、不调用云TTS。语音不可用不影响文字及倒计时。SS仅支持当前实现的中英文系统文本候选。第三方插件并非游戏官方功能，请了解相关规定和风险。

## 下载

- `ETDisplay-1.0.0.2-rc2.zip`：完整插件安装包，不要只复制主DLL。
- `ETDisplay-1.0.0.2-rc2-source.zip`：对应公开源码，不是插件安装包。
- `SHA256SUMS.txt`：两个ZIP的SHA256。

项目与反馈：https://github.com/Catacatata/ETDisplay

保留插件原署名“该死之人 奇里格”。项目遵循仓库GNU AGPL v3许可证，第三方依赖许可随包提供。
