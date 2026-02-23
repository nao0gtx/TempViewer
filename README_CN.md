# TempViewer (中文版)

TempViewer 是一款基于 WinUI 3 构建的高性能现代图像查看器，专为涉及 HDR 和 RAW 图像的专业工作流程而设计。本项目是 **Vibe coding** 的产物，利用 AI 辅助开发快速迭代并实现了复杂的底层功能。

## 🚀 核心功能

- **HDR 图像支持**: 全面支持高动态范围 (HDR) 图像，使用 PQ EOTF 和 BT.2020 色彩空间进行精确的色彩映射。
- **高级 RAW 渲染**: 集成 **LibRaw** 进行高质量 RAW 图像处理，并通过 **ComputeSharp** 实现 GPU 加速渲染。
- **现代 UI 界面**: 使用 **WinUI 3** 构建的简洁、透明且响应迅速的界面。
- **灵活的导航**: 支持多标签页查看和用于快速浏览的缩略图栏。
- **色彩管理**: 复杂的色彩管理流水线，确保在不同显示配置文件下色彩的准确性。
- **FFmpeg 集成**: 利用 FFmpeg 进行元数据提取和高级图像处理任务。

## 🛠 技术栈

- **框架**: WinUI 3 (Windows App SDK)
- **语言**: C# 12 / .NET 8
- **图形与 GPU**: ComputeSharp (HLSL), Win2D
- **核心库**: 
  - LibRaw (原生库) 用于 RAW 解码
  - FFmpeg 用于元数据和格式处理
- **样式**: 原生 XAML，具有自定义透明效果和现代审美

## 📂 项目结构

- `Controls/`: 自定义 UI 控件（如 ColorManagedImageControl）。
- `Helpers/`: 用于 FFmpeg、LibRaw 和 HDR 处理的工具类。
- `Shaders/`: 用于图像处理的 HLSL 计算着色器。
- `Services/`: 用于设置和导航的核心应用服务。
- `Views/`: 主要的应用视图和窗口。

## 🎨 Vibe Coding 声明

本项目采用了 "Vibe coding" 方法论构建。这意味着虽然核心架构决策和需求由开发者指导，但很大一部分实现、调试和功能扩展是通过与 AI 智能体协作交互完成的。这种方法允许快速原型设计并实现如 HDR 元数据流水线和 GPU 加速 RAW 渲染等复杂技术功能，同时保持代码库的高质量。

## 🏁 快速开始

### 开发要求
- Windows 10/11
- .NET 8.0 SDK
- Windows App SDK 运行时

### 安装步骤
1. 克隆仓库。
2. 在 Visual Studio 2022 中打开 `TransparentWinUI3.sln`。
3. 还原 NuGet 包。
4. 编译并运行。

---
*基于 🍕 和 AI 驱动开发。*
