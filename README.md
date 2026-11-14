# CPAQuota

Windows 原生任务栏额度工具，通过 CPA 查看 Codex、Claude、Gemini 等账号的实时额度，可选连接 CPAMP 查看历史 Token 与请求量。

## 下载与运行

从 [GitHub Releases](https://github.com/songxff/CPAQuota/releases/latest) 选择：

| 版本 | 使用方式 | 配置位置 |
| --- | --- | --- |
| 安装版（Setup-x86.msi） | 双击安装，之后从开始菜单启动 CPAQuota；可从 Windows 应用列表卸载 | 当前用户的 %LOCALAPPDATA%\CPAQuota\data |
| 便携版（Portable-x86.zip） | 解压到可写文件夹，直接双击 CPAQuota.exe | EXE 同目录的 data |

需要 Windows 和 .NET Framework 4.8。x86 程序也可在常见的 x64 Windows 上运行。
**无需启动或停止 CMD。** 退出时右键任务栏面板 → 退出；再次运行 EXE 可打开现有实例的设置。

便携版请保留 portable.flag。安装版卸载时保留用户配置。旧版的同目录 data/desktop-settings.json 会继续识别。两个发行包均不包含预置服务地址、账号或密钥。

首次启动，在原生设置中填写自己的 CPA 服务地址与管理密钥；CPAMP 历史统计按需启用。CPA 管理密钥不同于模型 API Key。密钥使用当前 Windows 用户的 DPAPI 加密；换用户或换机器后需要重新输入。

![显示示例（非真实用量）](docs/taskbar-preview.png)

## 功能

- 显示五小时、周额度或渠道提供的模型额度，支持选择、切换和轮播账号。
- 百分比默认表示剩余，也可改为已用。
- 可选显示各周期的具体重置时间或倒计时；本地自动更新，不增加查询请求。
- 可选按已用比例改变背景、百分比数字或两者，颜色与阈值可配置。
- 点击查看完整额度、重置时间及历史统计；右键设置、刷新或退出。
- 支持主题、字号、单双行、开机启动；宽度和左移偏移按实际屏幕像素应用。
- 不需要浏览器、Node.js 或本地 HTTP 服务。

主要验证环境为 Windows 10 横向任务栏；其他任务栏结构使用托盘左侧定位方式，Windows 11 尚未完成实机验证。任务栏已有工具栏时保留已有布局，可使用左移偏移选择合适位置。

## 额度与时间显示

**账号与指标 → 额度重置时间**可选不显示、具体时间、倒计时。具体时间跟随 Windows 时区，支持当天、明天、周日期及更远日期。未知或过期显示“—”，到期显示“等待刷新”，等待下一次正常查询获取新周期。此项与额度查询刷新间隔互相独立。

开启时间后，双行宽度可参考 260–320 像素；单行通常需要更宽。预览同步显示，完整信息可通过悬停或详情查看。

**额度配色**提供默认、仅背景、仅数字、背景与数字四种模式。默认按已用比例：低于 50% 绿色、50% 至低于 90% 黄色、90% 及以上红色。剩余 28% 对应已用 72%，背景填充 72% 且为黄色；各周期独立计算。

CPAMP 历史按北京时间自然日/月统计，仅包含其收集并保留的请求，不等同于订阅额度或网络流量。

## 项目结构

    CPAQuota.sln
    src/CPAQuota/              应用源码、项目文件和 Assets
    tests/CPAQuota.Tests/      独立测试程序，不随发行包发布
    installer/                MSI 定义和两种发行版说明
    scripts/                  构建、测试、打包脚本
    docs/                     展示文档
    artifacts/                本机构建、测试和发布产物（不提交）

## 构建、测试与打包

编译应用与独立测试程序：

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1

运行测试：

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1

应用输出为 artifacts/build/CPAQuota.exe，测试报告在 artifacts/tests/。32 项测试覆盖解析、缓存、故障隔离、DPAPI、配色、时间、实际像素及安装版/便携版存储选择。

打包需要 WiX Toolset 5：

    dotnet tool install --global wix --version 5.0.2
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package.ps1

打包脚本会准备匹配的 WiX UI 扩展，先构建并通过测试，然后在 artifacts/releases/ 生成安装版、便携版和 SHA256SUMS.txt。首次准备打包依赖需要联网。发布包通过明确的文件清单组装，不包含测试程序或运行时配置。

## 内存与后台开销

采用 x86 构建、详情窗口按需创建、直接复制配置、字体复用和按显示变化重绘。缓存未过期的轮询不访问接口，也不通知界面刷新。

上一版本在相同配置、重启后 60 秒的对比中，工作集约 53 MB 降至 36 MB，私有提交内存约 39 MB 降至 30 MB，未使用强制清空工作集。实际数值随环境、账号数量、窗口与运行时长变化。

## 配置与发布

个人 data/、日志、构建产物及备份均已忽略。转交便携文件夹前，应移除自己的 data 和 logs。不要上传个人地址、密钥或 DPAPI 密文；公开测试只使用 example.test 域名与测试字符串。

发行文件提供 SHA-256 校验值，当前版本未进行商业代码签名。
