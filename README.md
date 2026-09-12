# 模型规范检查器 · ModelChecker

> 一个 Unity 编辑器扩展：一键扫描当前场景中的模型资产，找出**面数超标、材质冗余、命名随意、轴心跑飞、UV 拉伸**等美术规范问题，输出问题清单与修复建议，并导出 Excel 报告交付美术返修。

![Unity](https://img.shields.io/badge/Unity-2020.3-black?logo=unity&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS-4a90d9)
![Editor Only](https://img.shields.io/badge/Scope-Editor%20Only-2ea44f)
![Version](https://img.shields.io/badge/Version-V0.3-orange)

---

## 为什么需要它

美术从 Maya / 3ds Max / Blender 导出的 FBX，进 Unity 后常常带着一堆"看不见的坑"：面数超标、一个模型挂七八个材质球、层级里全是 `pCube1` / `default`、轴心点飞到模型外几米远、贴图 8192 还不是 2 的幂……

单看模型发现不了，等打包上机才炸：**DrawCall 飙升、内存膨胀、光照烘焙出黑块**。

这个工具把这套资产规范自动扫一遍，让问题在提交前就暴露出来。

---

## 功能特性

- 🔍 **19 类问题自动检测** — 网格 / 材质 / 贴图 / UV / 命名 / 变换 / 骨骼 / 轴心全覆盖
- 📊 **场景级效率分析** — 材质复用度统计 + DrawCall 预算反推
- 🎯 **一键定位** — 点「定位」直接选中并高亮 Hierarchy 中出问题的对象
- 🔎 **双维筛选** — 严重度（高/中/低/建议）× 问题类型，整行点击即筛
- 📗 **Excel 报告导出** — 总览统计 + 每种问题一张明细表，类型名带超链接跳转
- 🌓 **明暗主题自适应** — Unity Personal / Pro 皮肤下均可正常显示
- ⚡ **纯编辑器扩展** — 代码位于 `Editor/`，不会打进玩家包体

---

## 快速开始

**1. 安装**

把 `ModelChecker-V0.3` 文件夹整个拷进工程的 `Assets/` 目录：

```
你的工程/Assets/ModelChecker-V0.3/
├── EPPlus.dll          # Excel 生成库
└── Editor/             # 全部代码，自动编入 Assembly-CSharp-Editor
```

或直接 clone 本仓库后拷贝 `Assets/ModelChecker-V0.3`。

**2. 打开**

Unity 菜单栏 → `工具` → `模型规范检查器`

**3. 检查**

点 **[开始检查]** → 左侧摘要栏查看分布 → 点「定位」跳转对象 → 点 **[导出报告]** 导出 Excel。

---

## 界面预览

<!-- 把截图放到 docs/preview.png，然后取消下面这行的注释 -->
<!-- ![界面预览](docs/preview.png) -->

> 完整界面说明（五大区域逐一讲解）见 [使用说明书 · 第 5 节](使用说明书.md#5-界面总览)。

---

## 检查项一览

| 分类 | 检查项 | 默认阈值 |
|---|---|---|
| **网格与结构** | 三角面数 | > 10000 |
| | 网格拓扑 | 顶点数为 0 / 索引数非 3 的倍数 |
| | 命名规范 | 命中默认名关键词黑名单 |
| | 变换设置 | `localScale ≠ (1,1,1)` |
| | 空节点 | 有子节点但仅挂 `Transform` |
| | 导入设置 | `Read/Write Enable` 开启 |
| | 骨骼数量 | > 60 |
| | 骨骼丢失 | `bones` 中存在 `null` |
| **材质与贴图** | 材质数量 | > 2 |
| | 材质丢失 | 材质数组存在 `null` |
| | 贴图尺寸 | 宽或高 > 4096 |
| | 贴图规格 | 非 2 的幂 |
| | 贴图导入 | 法线贴图未设为 Normal Map |
| | 材质使用效率（场景级） | 见说明书 7.2 |
| **UV 与轴心** | UV映射 | 缺少 UV0 / UV 超出 0–1 |
| | 光照贴图UV | 缺少 UV2 |
| | UV拉伸 | 拉伸比 > 5 |
| | 轴心偏移 | 距包围盒 > 1 米 |

> 每项触发条件的精确算法、修复建议与分级逻辑，见 [使用说明书 · 第 7 节](使用说明书.md#7-检查项详解)。

---

## 默认命名关键词黑名单

```
pCube  Cube  default  GameObject  polySurface  pasted
pCylinder  pSphere  pCone  pTorus  pPlane  pDisc
```

---

## 文档

| 文档 | 内容 |
|---|---|
| 📘 [使用说明书](使用说明书.md) | 安装、界面总览、配置详解、19 项检查逐条说明、筛选分页、Excel 报告、FAQ、已知限制、推荐工作流 |

---

## 环境要求

| 项目 | 要求 |
|---|---|
| Unity | 2020.3（验证版本 `2020.3.9f1c1`）；基于标准 IMGUI 编辑器 API，2019.4+ 理论上通用 |
| 平台 | Windows / macOS |
| 依赖 | `EPPlus.dll`（已随包附带）；无需任何 Unity Package |

---

## 项目结构

```
Assets/ModelChecker-V0.3/
├── EPPlus.dll                    # Excel 读写库
└── Editor/
    ├── ModelStandardChecker.cs   # 主窗口 · 菜单入口 · 检查调度 · 导出流程
    ├── ModelChecker.cs           # 全部检测算法
    ├── UIDrawer.cs               # 界面绘制 · 显示名/配色映射
    ├── ReportExporter.cs         # Excel 报告生成
    └── DataModels.cs             # 配置类 · 问题模型 · 枚举
```

---

## 注意事项

- **配置不持久化**：阈值与开关仅保存在内存中，重开窗口会恢复默认值。团队统一规范建议直接修改 `Editor/DataModels.cs` 中 `ModelCheckConfig` 的默认值。
- **仅检查当前激活场景**：不扫描 Project 面板中的资源文件，也不支持多场景批量。
- **`EPPlus.dll` 位于 `Editor/` 之外**，Unity 会将其视为运行时插件并打进玩家包。如在意包体，请把它移入 `Editor/` 目录，或在 Inspector 的 Platform Settings 中只勾选 Editor。
- 更多限制见 [使用说明书 · 第 12 节](使用说明书.md#12-已知限制)。

---

## 作者

**阿灿**

> 使用中遇到问题或有检测项建议，欢迎提 Issue。
