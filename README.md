# 文明 VI 英文词典

当前版本：v0.4.1（生词本版，项目与数据库统一到 D 盘）。

双击“启动词典.cmd”或 app/Civ6EnglishDemo.exe 即可使用。请保留整个项目目录及 .civ6-project 标记文件。无需联网或安装 SDK 即可使用本地词典与原有笔记；在线翻译需要自行输入接口凭证。

| 目录 | 内容 |
| --- | --- |
| src | 当前完整源码、词库、OCR 模型、许可及 VS Code 配置 |
| app | 当前可运行版本，含 .NET 运行时 |
| data/learning.db | 已迁移的个人笔记、生词本与查询测试记录 |
| tools | 便携 .NET SDK、离线依赖包及构建缓存 |
| verification | 本次迁移与功能验证记录 |

开发时用 VS Code 打开 Civ6EnglishDemo.code-workspace，运行“编译 DEMO”或“运行 DEMO”任务。普通运行与 VS Code 构建均向本项目 data 目录保存数据；高级测试可用 CIV6_DEMO_DATA 指定独立路径。无项目标记时，单独分发 app 文件夹会改用程序旁的 user-data 目录。

Git 管理源码、随附词库、模型、许可、说明与配置；app、tools、个人 data 和构建缓存均忽略，避免提交学习数据或重复的程序文件。此仓库仅在本机，未设置远程仓库。复制整个项目可保留可运行软件和个人数据；仅克隆 Git 不包含个人数据库、已发布程序或便携 SDK。

数据库备份：退出词典后，复制 data 目录。接口密钥不持久化，也不提交到 Git。详细功能说明见 src/README.md。
