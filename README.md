# TempViewer

TempViewer is a modern, high-performance image viewer built with WinUI 3, designed for professional workflows involving HDR and RAW imagery. This project is a product of **Vibe coding**, where AI-assisted development has been used to rapidly iterate and implement complex features.

## 🚀 Key Features

- **HDR Image Support**: Full support for High Dynamic Range (HDR) images with accurate color mapping using PQ EOTF and BT.2020 color space.
- **Advanced RAW Rendering**: Integrated with **LibRaw** for high-quality RAW image processing.
- **Modern UI**: A sleek, transparent, and responsive interface built using **WinUI 3**.
- **Flexible Navigation**: Supports multi-tab viewing and a thumbnail strip for quick browsing.
- **Color Management**: Sophisticated color management pipeline ensuring color accuracy across different display profiles.
- **FFmpeg Integration**: Leverages FFmpeg for metadata extraction and advanced image processing tasks.

## 🛠 Tech Stack

- **Framework**: WinUI 3 (Windows App SDK)
- **Language**: C# 12 / .NET 8
- **Graphics & GPU**: ComputeSharp (HLSL), Win2D
- **Libraries**: 
  - LibRaw (Native) for RAW decoding
  - FFmpeg for metadata and format handling
- **Styling**: Vanilla XAML with custom transparency and modern aesthetics

## 📂 Project Structure

- `Controls/`: Custom UI components (e.g., ColorManagedImageControl).
- `Helpers/`: Utility classes for FFmpeg, LibRaw, and HDR processing.
- `Shaders/`: HLSL compute shaders for image processing.
- `Services/`: Core application services for settings and navigation.
- `Views/`: Major application views and windows.

## 🎨 Vibe Coding Statement

This project was built using the "Vibe coding" methodology. This means that while the core architectural decisions and requirements were guided by the developer, a significant portion of the implementation, debugging, and feature expansion was performed through collaborative interaction with an AI agent. This approach allows for rapid prototyping and implementation of complex technical features like HDR metadata pipelines and GPU-accelerated RAW rendering while maintaining a high-quality codebase.

## 🏁 Getting Started

### Prerequisites
- Windows 10/11
- .NET 8.0 SDK
- Windows App SDK runtimes

### Installation
1. Clone the repository.
2. Open `TransparentWinUI3.sln` in Visual Studio 2022.
3. Restore NuGet packages.
4. Build and Run.

---
*Developed with 🍕 and AI.*
