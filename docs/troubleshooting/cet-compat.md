# .NET 9 / .NET 10 CET (控制流强制技术) 兼容性错误深度分析与全方案指南

## 一、故障现象与典型特征

### 1. 现象描述
在 Windows 系统（特别是 Windows 10 较早版本，如 Build 19044 / 21H2）上运行基于 **.NET 9** 或 **.NET 10** 构建发布的应用程序（如 FModel、解包工具或现代化 .NET 客户端）时，双击程序**没有任何窗口弹出，瞬间闪退退出**。

### 2. 底层错误信息与日志指标
- **控制台直接运行/捕获输出**：
  ```text
  Fatal error.
  Your Windows doesn't fully support CET. Please install all available Windows updates.
  ```
- **进程退出状态码（Exit Status）**：
  `-2146233082`（对应 16 进制：`0x80131506`，即 .NET 运行时的 `COR_E_EXECUTIONENGINE` 致命执行引擎故障 / FailFast）。
- **Windows 事件查看器（Application 日志，来源 `.NET Runtime`）**：
  ```text
  Application: FModel.exe
  CoreCLR Version: 10.0.x
  Description: The process was terminated due to an internal error in the .NET Runtime with exit code 0x80131506.
  ```
- **Process Monitor 捕获特征**：
  进程在加载 `coreclr.dll` 后，尝试查找本地化错误字符串资源（`zh-CN\mscorrc.dll` 未找到，回退到 `mscorrc.dll`），随后直接执行 `Process Exit`，期间未加载任何托管 DLL 或 UI 框架（WPF/WinForms）模块。

---

## 二、根本原因深度剖析

### 1. 什么是 CET（Control-flow Enforcement Technology）？
CET 是 Intel（11 代酷睿 Tiger Lake 及以上）与 AMD（Zen 3 / 锐龙 5000 及以上）在 CPU 硬件层面引入的现代安全防御技术。其核心包含两项机制：
- **用户影子堆栈（User-mode Shadow Stack）**：CPU 在硬件隔离区额外维护一份只记录返回地址的独立堆栈，每次函数 `call` 和 `ret` 时进行双向对比，以彻底杜绝黑客利用缓冲区溢出实施的 **ROP（Return-Oriented Programming，返回导向编程）** 劫持攻击。
- **间接分支跟踪（IBT）**：防止非法跳转攻击（JOP）。

### 2. .NET 9 / .NET 10 的策略变更
从 .NET 9 开始，微软在默认的应用宿主二进制（`apphost` / `singlefilehost`）编译选项中加入了 `/CETCOMPAT` 标记，强制要求在支持 CET 的 CPU 平台上运行影子堆栈安全检查。

### 3. 为什么会在当前系统触发 Fatal Error？
- 触发该问题的关键在于操作系统内核对 CET 的支持状态处于 **“部分支持（Incomplete / Partial）”**：
  - 计算机的 CPU 硬件已具备 CET 功能；
  - 但当前安装的操作系统（如 Windows 10 21H2 Build 19044）内核未能完整搭载运行用户影子堆栈所需的所有 Windows API 与系统累积补丁；
- .NET 运行时（CoreCLR）启动初始化时检测到系统环境无法完整保障 CET 规范，为了遵循安全基线，CoreCLR 会直接调用底层的 `EEPOLICY_HANDLE_FATAL_ERROR` 主动终止进程，而不是冒着未定义行为的风险降级静默执行。

---

## 三、解决方案汇总（按不同需求场景）

### 方案 1：针对该程序单进程禁用 CET（推荐使用者采用，立竿见影）
若您只想让特定的程序（如 `FModel.exe`）正常运行，而不变动系统全局策略：

1. 以 **管理员身份** 打开 **PowerShell**；
2. 执行以下命令：
   ```powershell
   Set-ProcessMitigation -Name "FModel.exe" -Disable UserShadowStack
   ```
3. 再次双击程序，即可正常启动。
> **恢复方法**：若日后想恢复该程序的默认保护，执行 `Set-ProcessMitigation -Name "FModel.exe" -Reset`。

---

### 方案 2：全局禁用系统的 UserShadowStack（一劳永逸）
如果您的开发环境经常需要测试不同团队用 .NET 9 / .NET 10 编译的工具，为了避免每个工具都手动添加一次例外，可直接在系统范围关闭此项缓解策略：

#### 命令行方式（最快捷）：
以 **管理员身份** 打开 **PowerShell**，执行：
```powershell
Set-ProcessMitigation -System -Disable UserShadowStack
```
> **恢复方法**：执行 `Set-ProcessMitigation -System -Reset` 即可还原全局默认策略。

#### 图形界面方式：
1. 打开 **“Windows 安全中心”**（Windows Security）；
2. 依次进入 **“应用和浏览器控制”** -> 底部 **“Exploit Protection 设置”** -> **“系统设置”**；
3. 找到 **“硬件强制实施的堆栈保护”**（Hardware-enforced Stack Protection），将其选项改为 **“默认关闭”**；
4. 重启计算机生效。

---

### 方案 3：作为开发者在工程代码中禁用 CET（发布免配置程序）
若您是该项目的维护者，希望编译打包出来的程序在广大普通玩家或旧版本 Windows 用户电脑上均能免配置双击运行：

#### 做法 A：在项目文件中关闭 CET 兼容校验（推荐保留 .NET 10 时使用）
打开主程序的 `.csproj` 文件（如 `FModel.csproj`），在 `<PropertyGroup>` 中添加：
```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows</TargetFramework>
  <CETCompat>false</CETCompat> <!-- 禁用 CET 兼容检查，允许在任意系统无报错运行 -->
</PropertyGroup>
```

#### 做法 B：将工程降级到 .NET 8（生态兼容性最佳）
.NET 8 不会强制要求 CET 检查，且属于微软 LTS 长期支持版本。将项目降级回 .NET 8 需要进行如下调整：
1. **修改目标框架**：
   - `FModel.csproj`: `<TargetFramework>net8.0-windows</TargetFramework>`
   - `CUE4Parse/Directory.Build.props`: `<TargetFramework>net8.0</TargetFramework>`
2. **回退 .NET 10 独有 API（共 3 处）**：
   - **异步 Zip 解压**（`FModel/ViewModels/ApplicationViewModel.cs`）：
     .NET 10 新增了 `ZipArchive.CreateAsync` 和 `ExtractToDirectoryAsync`，.NET 8 需改回经典的条目解压循环或同步 `ZipFile.ExtractToDirectory`。
   - **互斥锁对象**（`DetexHelper.cs` 和 `Aion2DatFileEncryption.cs`）：
     将 .NET 9+ 引入的 `private static readonly Lock _lock = new();` 类型改回 `private static readonly object _lock = new();`。

---

### 方案 4：更新补丁（系统层面彻底补全支持）
通过 Windows Update 安装对应系统的所有安全质量与累积更新包，或将系统升级至 **Windows 11** 或已完整打满补丁的 **Windows 10 22H2（Build 19045）**，操作系统将补全影子堆栈支持。

---

## 四、禁用 CET 的安全性与性能影响评估

| 评估维度 | 实际影响与结论 |
| :--- | :--- |
| **业务功能** | **完全无影响**。CET 纯属内核与硬件底层执行流保护，完全不干扰业务代码与资源解析导出逻辑。 |
| **运行性能** | **无损，甚至微幅减少开销**。CET 每次调用函数均需比对影子堆栈，关闭后省去了硬件级同步校验，无任何性能惩罚。 |
| **安全防护** | **回归主流基线标准**。禁用 CET 仅去除了对 ROP 攻击的防御；系统其他的核心安全防御（如 **DEP 数据执行保护**、**ASLR 地址空间随机化**、SEHOP 等）**依然处于完全开启生效状态**。目前所有 .NET 8 及更早版本的程序原本就未开启 CET，对于单机运行的本地解包工具而言，实际遭受内存攻击的风险极低。 |
