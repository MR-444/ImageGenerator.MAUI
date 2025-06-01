# 🎨 Image Generator MAUI

A cross-platform image generation application built with .NET MAUI that leverages AI-powered image generation APIs. Currently supports integration with Replicate and OpenAI's image generation services, allowing users to create stunning AI-generated images through simple API calls.

## 🌟 Features

- Cross-platform support (Windows, Android, iOS, macOS)
- Modern UI with MAUI framework
- AI-powered image generation through REST APIs
- Support for multiple AI services:
  - Replicate API integration
  - OpenAI API integration
- Built with MVVM architecture
- Async/await best practices implementation
- Easy-to-use interface for generating AI images

## 🛠️ Technologies

- .NET MAUI 9.0
- CommunityToolkit.MVVM 8.4.0
- AsyncAwaitBestPractices.MVVM 9.0.0
- Refit 8.0.0 (for API integration)
- SixLabors.ImageSharp 3.1.8 (for image processing)

## 📋 Prerequisites

- .NET 9.0 SDK
- Visual Studio 2022 or later with MAUI workload
- For iOS development: macOS with Xcode
- For Android development: Android SDK

## 🚀 Getting Started

1. Clone the repository:
```bash
git clone https://github.com/yourusername/ImageGenerator.MAUI.git
```

2. Open the solution in Visual Studio:
```bash
cd ImageGenerator.MAUI
ImageGenerator.MAUI.sln
```

3. Restore NuGet packages and build the solution

4. Run the application on your preferred platform

## 🏗️ Project Structure

```
ImageGenerator.MAUI/
├── Converters/         # Value converters for UI binding
├── Models/            # Data models
├── Services/          # Business logic and services
├── ViewModels/        # MVVM view models
├── Views/             # MAUI XAML views
└── Resources/         # Application resources
```

## 🧪 Testing

The project includes a test project (`ImageGenerator.MAUI.Tests`) for unit testing. Run the tests using:

```bash
dotnet test
```

## 📱 Supported Platforms

- Windows 10.0.17763.0 and later
- Android 21.0 (API 21) and later
- iOS 15.0 and later
- macOS 15.0 and later

## 🤝 Contributing

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 👥 Authors

- Silmas - Initial work

## 🙏 Acknowledgments

- .NET MAUI team for the amazing framework
- All contributors who have helped shape this project 