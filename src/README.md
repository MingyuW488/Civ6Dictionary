# 开发与验证说明

使用流程、功能、技术栈和翻译接口配置统一维护在[项目首页](../README.md)。本文补充源码入口与本地验证方式。

## 源码入口

| 文件 | 职责 |
| --- | --- |
| `MainWindow.xaml` / `MainWindow.xaml.cs` | 五个页面、截图 / 识别 / 查询流程、测速及笔记交互 |
| `MainWindow.Vocabulary.cs` | 收藏、笔记导入、延迟搜索、分页、详情和导出 |
| `DictionaryView.xaml` / `.cs` | 中英文释义、音标、词形、分节及发音按钮 |
| `LocalDictionary.cs` / `SyllableDictionary.cs` | 按索引查询两个只读 SQLite 词库 |
| `LocalStore.cs` | 数据路径、个人 SQLite 数据、参数化搜索及 CSV 导出 |
| `NoteDetailsWindow.xaml` / `.cs` | 长文本详情、编辑 / 取消和并发修改检测 |
| `Capture.cs` / `OcrService.cs` | Win32 截图、框选、预处理及 Tesseract OCR |
| `SpeechService.cs` | 独立 STA 线程中的 Windows SAPI 播放与释放 |
| `Translation.cs` | 签名、HTTPS 调用、响应解析、取消和统计 |
| `App.xaml.cs` | 普通启动、自检及窗口验证模式 |
| `SelfTest.cs` / `WindowSmoke.cs` / `VocabularySmoke.cs` | 离线逻辑与实际 WPF 窗口流程验证 |
| `StorageSmoke.cs` | 项目路径及已迁移数据库的专用验证 |

项目目标为 `net10.0-windows` / x64；包版本以 [Civ6EnglishDemo.csproj](Civ6EnglishDemo.csproj) 与 `packages.lock.json` 为准。

## 构建与运行

以下命令均在**仓库根目录**执行。请先安装 .NET 10 SDK（Windows x64）及 Visual C++ x64 运行库，或准备项目支持的便携 SDK。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\run.ps1
```

脚本优先使用 `tools/dotnet`，否则使用系统 `dotnet`；存在 `tools/nuget-feed` 时使用离线依赖源。`tools/` 不随 Git 仓库提供。VS Code 的运行和编译任务调用相同脚本。

使用系统 SDK 调试时，需删除或修改 `.vscode/launch.json` 中指向便携 SDK 的 `DOTNET_ROOT`。不要求安装完整 Visual Studio。

## 在独立目录验证

验证会生成测试数据库、日志或界面截图。每次运行用 `CIV6_DEMO_DATA` 指定新目录，与实际学习数据分开。以下示例使用系统 SDK；也可将 `dotnet` 替换为项目 `tools/dotnet/dotnet.exe` 的完整路径。

### 生词本专项验证

```powershell
$env:CIV6_DEMO_DATA = Join-Path (Get-Location) ('work/vocabulary-' + [guid]::NewGuid().ToString('N'))
dotnet run --project .\src\Civ6EnglishDemo.csproj -c Release -- --vocabulary-smoke
Get-Content (Join-Path $env:CIV6_DEMO_DATA 'vocabulary-smoke.txt')
Remove-Item Env:CIV6_DEMO_DATA
```

`--vocabulary-smoke` 要求全新的目录，覆盖旧库升级、超过 500 条笔记的导入、去重、英文 / 中文 / 原句 / 关联笔记搜索、编辑后的关联搜索、分页、长文本、离线查词、CSV 导出、移除及持久化。

### 其他入口

同样先设置独立的 `CIV6_DEMO_DATA`，再替换上述 `dotnet run` 命令的最后一个参数。运行后查看对应报告中的 `PASS` / `FAIL`。

| 参数 | 验证内容 | 报告 |
| --- | --- | --- |
| `--notes-smoke` | 笔记详情、长文本、编辑 / 取消、并发修改保护 | `notes-smoke.txt` |
| `--sentence-smoke` | 真实 OCR、整段 / 选句填入、模拟翻译请求 | `sentence-smoke.txt` |
| `--syllable-smoke` | 分节数据、顶部显示、换词清理、缺失降级 | `syllable-smoke.txt` |
| `--ui-smoke` | 综合窗口流程及本机语音组件 | `ui-smoke.txt` |
| `--self-test` | 签名、解析、词库、OCR、存储等逻辑及语音组件 | `self-test.txt` |

语音验证需要可用的 Windows 英文桌面语音和正常桌面会话。测试使用示例数据或模拟响应，不需要真实翻译密钥。历史验证记录位于 [verification](../verification/)。

也可在设置独立数据目录后执行 `build.ps1 -SelfTest`，脚本会读取该目录中的报告。不要直接对个人数据库运行验证模式。

`--storage-smoke` 是整理交付目录时使用的专用检查，需要已存在的个人数据库和 `app/` 发布布局，不属于全新克隆后的通用自检。

## 数据与查询约定

- 数据路径优先使用 `CIV6_DEMO_DATA`，其次使用祖先目录 `.civ6-project` 所在位置的 `data/`，最后回退到程序旁 `user-data/`。
- `notes` 保存原句与笔记，`vocabulary` 保存收藏及本地释义快照，`tests` 保存查询 / 测速元数据。
- 两个公开词库位于 `src/data/`，构建后复制到程序输出目录，与个人数据库用途不同。
- 生词本搜索使用参数化 SQLite 查询，`%` 和 `_` 按字面匹配；先筛选全部记录再分页，每页最多 100 条。
- 本地笔记列表显示最近 500 条；导出及生词本历史笔记导入覆盖全部笔记。
- 在线请求使用 `HttpClientHandler.UseProxy=false`，单次超时 8 秒，无自动重试；测速最多 100 条，每条不超过 2000 字符，请求间隔至少 1.2 秒。
- 在线译文不自动保存，API 凭证不持久化；新增日志时也应保持这一约定。

## 第三方数据

来源版本、校验值及记录数见 `data/dictionary-source.json`、`data/syllables-source.json`。分节数据采用书面断字边界，不是声学分节或重音词典。许可与授权信息见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
