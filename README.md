# 需求协作平台

面向单项目团队的需求管理系统，提供需求列表、看板、迭代、个人工作台和基础报表。

当前发布版本：`v1.15.1`，包含成员搜索、父子需求创建及界面一致性改进。生产升级需要在服务器执行升级脚本并通过健康检查。

## v1.15.1 显示修复

- 修复普通需求前的占位元素在鼠标悬停时误显示蓝框；保留父需求展开按钮及树状连线。

## v1.15 筛选与显示优化

- 需求列表和看板不再默认筛选当前迭代，保留手动筛选。
- 列表筛选后隐藏未命中的父需求，匹配子需求独立显示；可开启“显示父子结构”查看关联父需求。
- 移除列表中的子需求数量文字，保持简洁。
- 下拉搜索框先定位再聚焦，修复左上角瞬间闪动。
- 新建附件删除按钮使用白底黑字，“待上传”文字与按钮字号一致。

## v1.14 成员搜索与父子需求创建

- 成员选择支持姓名或账号包含检索，不区分大小写；处理人保持多选，验收人保持单选。覆盖新建、详情、批量表格、默认字段配置及处理人筛选。
- 下拉菜单统一字体、主题和滚动样式，根据窗口空间选择上下展开；评论 @ 列表在编辑器内展开，避免被边界裁切。
- 详情页处理人、验收人及关系选择控件统一尺寸；父需求与子需求上下相邻。父子需求候选项展示单号、标题和描述摘要，列表以浮层展开，点击外部自动收起，不增加页面长度。
- 新建页按“需求描述、父需求、子需求、图片附件”排列，与详情页保持一致。子需求绑定兼容空的成功响应，避免已生效却提示 JSON 解析失败。
- 当前页面写入地址栏的 `page` 参数，刷新后保留页面位置及已打开的需求详情；仅含需求编号的旧链接仍进入需求页。
- 新建需求可同时选择多个已有需求及填写多个新子需求，总计最多 100 条。已有父需求的任务可见但禁止选择，包含子需求的任务也不可选。
- 新子需求行默认带入父需求当前的迭代、模块、处理人、验收人、优先级、类型和期望日期，状态为未开始。标题、描述需分别填写，之后修改父需求不覆盖已填写行。
- 父需求、新子需求和已有需求绑定在一个事务中提交，任一校验失败全部撤销；同一提交标识重试不会重复创建。后台通知随事务保存，成功提交后按已有规则发送。图片附件仍在需求创建后上传，失败可单独重试。
- 详情页支持搜索多选后批量绑定，整批校验，并对重复绑定请求保持幂等。不支持在同一表单嵌套创建孙需求。
- 复用已有批次记录，不新增数据库表。生产 HTTP 场景使用兼容的随机 UUID 生成方式。

可使用下方“隔离本地预览”命令验证功能。生产更新使用保留数据升级脚本。

## v1.13 协作体验迭代

1. 从看板新建需求后，留在看板并展开新需求详情，保留筛选条件和浏览位置。
2. 创建成功提示在 3 秒后自动关闭，仍可手动关闭或点击查看。
3. 状态、处理人、优先级、迭代、需求单类型的筛选下拉支持全选，再逐项取消；列表和看板同步支持。
4. 需求描述默认完整展示并保留换行，编辑时提供保存和取消。
5. 评论支持 Enter 换行、Ctrl/Cmd+Enter 发送、多选或粘贴图片、纯图片评论，以及从成员列表选择 @ 对象。只有已启用且已绑定企微的成员可被 @。图片归属于对应评论，不混入需求附件。已发布的评论图片不能独立删除。
6. 需求列表新增“任务状态”排序：未开始 → 进行中 → 待验收 → 暂停 → 需求池 → 验收完成（`completed`）→ 已关闭。同状态按优先级从高到低、单号数字从小到大排列。父子层级保留，子需求跟随父需求；其他自定义状态排在最后。
7. 看板每列按极高 → 高 → 中 → 低排列，同优先级按单号数字从小到大排列。
8. 列表和看板默认勾选当前迭代。默认选择会跟随周切换，手动选择或清空后则保留用户选择；两个页面共享当前会话的筛选条件。

评论发布和 @ 通知在同一事务中保存。客户端重试使用同一提交标识，避免重复发布；后台每 30 秒检查通知，首次失败后最多再尝试 3 次。成员在发送前停用、解绑或更换企微身份时取消通知，不在重新绑定后补发。链接可直接定位到对应评论。企微服务存在“已接收消息但本地尚未记录成功”的极端断电窗口，因此不能保证外部消息绝对不重复。

此轮仅新增评论元数据和图片归属字段，SQLite 启动时增量补齐，PostgreSQL 提供增量迁移。旧评论、普通附件和状态配置保持原样；不会重新创建已删除的状态，也不修改自动迭代顺延规则。未发布的评论图片由后台在上传满 24 小时后清理，已发布图片保留。

升级时保留旧版带哈希文件名的前端资源，兼容浏览器缓存的入口页面，避免旧脚本返回 404 导致白屏。升级后刷新页面即可使用最新界面。

### 隔离本地预览

以下方式使用专用的 `server/artifacts/local-preview/Data/preview.db` 和附件目录，关闭后台任务与真实企微调用，不读取或覆盖生产数据。首次启动创建的演示账号为 `admin`，密码为 `G43-Preview-Only!`，该密码仅用于此隔离预览。重复启动保留已有预览数据。

```powershell
# 终端一：后端，默认 5081
./server/run-local-preview.ps1

# 终端二：前端，默认 5180
$env:VITE_DATA_MODE = 'api'
$env:G43_DEV_API_TARGET = 'http://127.0.0.1:5081'
pnpm --dir client dev --host 127.0.0.1 --port 5180 --strictPort
```

访问 `http://127.0.0.1:5180/`。预览中的演示绑定成员仅用于测试选择和通知入队；通知发送、失败重试和取消由模拟企微发送器回归验证，不向真实成员发送消息。后台任务关闭期间，演示迭代不会自动切换。

## 功能

- 需求创建、编辑、父子关系、状态流转、优先级和截止日期管理。
- 多位处理人共同协作，单一验收人负责验收。
- 需求列表与看板共享多选筛选条件，支持“我的任务”。
- 表格导入导出、批量快速新建；多人账号使用英文分号分隔。
- 图片附件多选、追加、粘贴上传、预览及删除。
- 周迭代自动切换，未完成需求顺延并保留业务信息。
- 可选的企业消息集成：身份绑定、新建/指派通知、验收通知及每日提醒。
- 管理员配置成员、状态、模块、需求类型及默认值。

## 技术栈

| 层级 | 技术 |
| --- | --- |
| 前端 | React、TypeScript、Vite |
| 后端 | ASP.NET Core / .NET 10、Entity Framework Core |
| 数据库 | SQLite；另提供 PostgreSQL 配置与迁移支持 |
| 文件存储 | 独立附件目录 |
| 测试 | Vitest、Testing Library、xUnit |

## 目录

```text
client/                         前端源码与测试
server/src/Ground43.Api/         后端 API、数据模型及后台任务
server/tests/                   后端测试
server/deploy/                  打包、部署与备份工具
```

## 本地开发

安装 .NET 10 SDK（`server/global.json` 固定为 10.0.400）、Node.js 22.12+ 和 pnpm。在仓库根目录运行以下 PowerShell 命令。

后端使用独立开发数据库，并关闭消息发送等后台任务：

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:GROUND43_Jobs__Enabled = 'false'
$env:GROUND43_Bootstrap__AdminPassword = '<设置本地开发密码>'
dotnet run --no-launch-profile --project server/src/Ground43.Api --urls http://127.0.0.1:5080
```

在另一个终端启动前端：

```powershell
pnpm --dir client install --frozen-lockfile
$env:VITE_DATA_MODE = 'api'
pnpm --dir client dev --host 127.0.0.1 --port 5179
```

访问 `http://127.0.0.1:5179/`。首次初始化开发库时会创建演示成员，可使用 `admin` 和上面设置的开发密码登录。初始化密码不会重置已有数据库中的账号密码。开发演示数据不得用于正式上线。

## 测试与构建

```powershell
pnpm --dir client test
pnpm --dir client lint
pnpm --dir client build
dotnet test server/tests/Ground43.Api.Tests
```

Windows 自包含发布包可通过 `server/deploy/package.ps1` 构建；该脚本自动以 API 模式构建前端。直接执行前端 build 时应自行设置 `VITE_DATA_MODE=api`，否则用于纯前端演示。打包输出不应加入 Git。

现有 SQLite Windows 服务升级使用 `server/deploy/update-service.ps1 -SkipBuild`。脚本停服后备份程序、生产配置和完整 `Data`，并备份配置指定的额外附件目录；数据备份逐文件校验 SHA256，通过后才替换程序。生产配置、数据库、附件及 `Backups` 不被发布包覆盖。升级失败时尝试恢复应用文件，保留数据备份供核查；不要用开发数据库回滚生产数据。

## 首次部署（Windows / SQLite）

克隆仓库后先安装前端依赖，再打包：

```powershell
pnpm --dir client install --frozen-lockfile
powershell -ExecutionPolicy Bypass -File server/deploy/package.ps1
```

输出位于 `server/artifacts/publish`。正式使用前创建一份全新的空库，仅含管理员和系统字典，不导入演示任务。以下命令在源码根目录执行；目标文件必须不存在，工具拒绝覆盖已有数据库：

```powershell
# 先构建，避免密码输入被构建进程消费。
dotnet build server/tools/Ground43.PrepareCleanDatabase -c Release
dotnet server/tools/Ground43.PrepareCleanDatabase/bin/Release/net10.0/Ground43.PrepareCleanDatabase.dll --output C:\Deploy\TaskPlatform\Data\ground43-lan.db
# 程序等待一行密码输入；输入自定义强密码并回车（控制台可能回显，请勿录屏）。
```

以管理员 PowerShell 安装服务（不会立即启动）：

```powershell
powershell -ExecutionPolicy Bypass -File server/deploy/install-service.ps1 -PackagePath server/artifacts/publish -InstallPath C:\Deploy\TaskPlatform -Urls http://0.0.0.0:4433
```

编辑安装目录中的 `appsettings.Production.json`：模板默认 SQLite；将 `ConnectionStrings.Default` 设为 `Data Source=C:/Deploy/TaskPlatform/Data/ground43-lan.db`，`Storage.RootPath` 设为 `C:/Deploy/TaskPlatform/Data`，`Platform.PublicBaseUrl` 设为使用者可访问的实际地址。不要原样使用回环地址发送通知。已创建空库时无需再配置初始化密码。

```powershell
Start-Service Ground43.Api
Invoke-RestMethod http://127.0.0.1:4433/api/health
```

使用 `admin` 和创建空库时输入的密码登录，再添加自己的成员、模块和任务。服务账号须有数据目录读写权限；防火墙放行所需端口，对外服务应由 HTTPS 反向代理保护。安装脚本仅用于首次安装，升级使用 `update-service.ps1`，不要覆盖生产数据库和配置。

默认自动初始化会产生通用演示成员和任务，**正式部署请使用上述空库工具**。所有演示成员共享开发初始化密码，不能将演示库直接上线。现有 `update-service.ps1` 适用于安装目录下的 `Data/ground43-lan.db` 和 4433 端口；使用其他布局需先调整并验证脚本。PostgreSQL 为另一种部署选项，需自行配置连接串及数据库权限；上述空库工具仅适用于 SQLite，`backup.ps1`/`restore.ps1` 则针对 PostgreSQL。工作日历须按实际年份和团队规则维护。

## 配置与凭据

生产配置应由部署环境单独提供。支持使用 `GROUND43_` 前缀环境变量，层级之间使用双下划线，例如：

```text
GROUND43_Database__Provider
GROUND43_ConnectionStrings__Default
GROUND43_Storage__RootPath
GROUND43_Platform__PublicBaseUrl
GROUND43_Bootstrap__AdminPassword
GROUND43_Jobs__Enabled
```

企业消息集成是可选项。企业标识、应用标识、密钥及回调凭据仅在部署环境配置，不在本说明中提供任何实际值。不使用该集成时，无需配置这些信息。

不要将密码放入前端环境变量、源代码、截图、日志或发布说明。前端构建变量会进入浏览器可读取的资源。

## 数据备份与升级

- 数据库保存需求、成员、关系和历史记录，图片文件位于独立附件目录。
- 备份必须覆盖数据库、附件及生产配置；SQLite 运行中请使用一致性备份，或停服后复制数据库及相关日志文件。
- 更新前备份，保留生产数据和配置，只替换应用文件并执行必要的增量迁移。
- 不要将开发数据库、演示数据或本地预览目录覆盖到服务器。
- 更新后检查 `/api/health`，核对业务数据与附件；涉及处理人模型变更时刷新旧客户端。
- 回滚前确认数据库结构兼容，不能仅凭旧程序能启动就认定回滚成功。

## 提交安全

仓库提交范围应限于通用源码、测试、依赖锁文件、无敏感值的配置模板及公共使用说明。

以下内容不应提交：真实业务数据、内部需求文档、成员信息表、数据库及备份、附件、生产配置、密钥、企业应用标识、内网拓扑和内部截图。

`.gitignore` 仅约束未跟踪文件，不能清除旧提交。如果敏感内容曾进入 Git，应单独处理历史；已泄露的凭据还需要在对应系统撤销或轮换。
