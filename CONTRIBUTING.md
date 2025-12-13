# Contributing to Canopy

Thank you for your interest in contributing to Canopy! This document provides guidelines and information for contributors.

## Getting Started

### Prerequisites

- .NET 8.0 SDK
- Visual Studio 2022 or VS Code with C# extension
- Windows 10/11 for Windows development

### Setting Up Development Environment

1. Fork the repository
2. Clone your fork:
   ```bash
   git clone https://github.com/YOUR_USERNAME/canopy-sharp.git
   cd canopy-sharp
   ```
3. Restore dependencies:
   ```bash
   dotnet restore
   ```
4. Build the solution:
   ```bash
   dotnet build
   ```

## Project Structure

- **Canopy.Core**: Cross-platform shared code (no platform dependencies)
- **Canopy.Windows**: Windows WinUI 3 implementation
- **Canopy.Linux**: Linux implementation (planned)
- **Canopy.Mac**: macOS implementation (planned)

## Development Guidelines

### Code Style

- Follow the `.editorconfig` settings
- Use file-scoped namespaces
- Prefer `var` when the type is obvious
- Use meaningful names for variables and methods
- Add XML documentation for public APIs

### Architecture Principles

1. **Core First**: Implement shared logic in `Canopy.Core` whenever possible
2. **Interface Abstraction**: Use interfaces for platform-specific services
3. **Event-Driven**: Use the `AppCoordinator` for cross-cutting concerns
4. **Dependency Injection**: Register services in the DI container

### Adding New Features

1. **New Game Platform Support**:
   - Implement `IGameScanner` in `Canopy.Core/GameDetection/`
   - Register in the platform's DI configuration

2. **New IPC Message Type**:
   - Add constant to `IpcMessageTypes` class
   - Handle in appropriate service
   - Update README documentation

3. **New Platform Support**:
   - Create new project referencing `Canopy.Core`
   - Implement required interfaces:
     - `ISettingsService` (extend `SettingsServiceBase`)
     - `IHotkeyService`
     - `ITrayIconService`
   - Extend `IpcBridgeBase` for WebView communication

## Pull Request Process

1. Create a feature branch from `main`:
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. Make your changes with clear, descriptive commits

3. Ensure your code builds without warnings:
   ```bash
   dotnet build -warnaserror
   ```

4. Update documentation if needed

5. Push and create a Pull Request

### PR Checklist

- [ ] Code follows project style guidelines
- [ ] Self-reviewed my code
- [ ] Added/updated XML documentation
- [ ] Updated README if needed
- [ ] No new compiler warnings
- [ ] Changes work on intended platform(s)

## Reporting Issues

When reporting issues, please include:

- OS version and architecture
- .NET runtime version
- Steps to reproduce
- Expected vs actual behavior
- Relevant log output (if applicable)

## Questions?

Feel free to open a Discussion for questions about:

- How to implement a feature
- Architecture decisions
- Project roadmap

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
