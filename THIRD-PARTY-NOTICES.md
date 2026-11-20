# 第三方组件说明

本项目保留根目录原有 `LICENSE`（GNU AGPL v3），未更改许可证或原清单作者。这里列出候选包的第三方运行时组件，不代替完整许可证或法律审查。

## 随候选包提供

- **System.Speech 10.0.7**：Microsoft；NuGet包声明MIT许可证。候选包包含入口及Windows实现DLL。
  - `licenses/System.Speech-LICENSE.txt`：按NuGet包MIT表达式整理的版权及许可正文。
  - `licenses/System.Speech-THIRD-PARTY-NOTICES.TXT`：从该版本原NuGet包原样复制的第三方声明。
  - NuGet：https://www.nuget.org/packages/System.Speech/10.0.7

## 不随候选包分发

- Dalamud/API15与游戏客户端：由用户现有环境提供，不打包Dalamud、游戏或FFXIVClientStructs DLL。
- Windows系统字体（Segoe UI、Arial、Microsoft YaHei、SimHei）：只在本机按需读取，不复制或分发字体文件。
- Dalamud.NET.Sdk、DalamudPackager、DotNet.ReproducibleBuilds：构建工具，不进入运行时包。
- xUnit、Microsoft.NET.Test.Sdk、测试用Newtonsoft.Json：仅测试依赖，不进入运行时包。

同版本源码通过本公开仓库和发布页提供，以便检查和重建。分发和修改时应保留项目与依赖的许可证、版权及第三方声明，并遵守相应许可要求。
