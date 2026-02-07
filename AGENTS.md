# Flux - Agent Guide

> This file contains essential information for AI agents working on the Flux codebase.

## Project Overview

**Flux** is a high-performance, modular automation and testing platform built with .NET 8.0. designed for web automation, penetration testing, and credential stuffing research.

- **Current Version**: 0.3.2
- **License**: MIT (Copyright 2024)
- **Primary Language**: C# (.NET 8.0)
- **Frontend**: Angular 17 with TypeScript

## Architecture

### Project Structure

```
Flux/
├── Flux.sln                    # Main solution file
├── Flux.Core/                  # Core domain layer (entities, repositories, services)
├── Flux.Web/                   # ASP.NET Core Web API application
├── Flux.Native/                # Native/desktop-specific components
├── Flux.Native.Updater/        # Auto-updater for native components
├── Flux.Shared/                # Shared models and contracts
├── Flux.Shared.Tests/          # Unit tests for shared components
├── flux-web-client/            # Angular 17 frontend application
├── RuriLib/                    # Core automation library (blocks, scripting, providers)
├── RuriLib.Http/               # HTTP client abstractions
├── RuriLib.Http.Tests/         # HTTP library tests
├── RuriLib.Parallelization/    # Parallel execution engine
├── RuriLib.Proxies/            # Proxy management
└── Libraries/TlsClient.NET/    # TLS fingerprinting library
```

### Key Technologies

#### Backend
- **.NET 8.0** - Target framework
- **ASP.NET Core** - Web API with SignalR for real-time communication
- **Entity Framework Core 8.0** - ORM with SQLite provider
- **MediatR 12.2** - CQRS and mediator pattern
- **AutoMapper** - Object mapping
- **FluentValidation** - Input validation
- **Serilog** - Structured logging
- **JWT** - Authentication tokens
- **Swagger/OpenAPI** - API documentation

#### Frontend
- **Angular 17** - SPA framework
- **TypeScript 5.4** - Primary language
- **Bootstrap 4.6** - UI framework
- **PrimeNG 17** - Component library
- **RxJS 7.8** - Reactive programming
- **Monaco Editor** - Code editor (VS Code's editor)
- **SignalR Client** - Real-time communication

#### Automation & Scripting
- **Selenium.WebDriver 4.35** - Browser automation
- **Playwright 1.41** - Modern browser automation
- **PuppeteerSharp 20.2** - Headless Chrome automation
- **Appium.WebDriver 8.0** - Mobile automation
- **Jint 4.4** - JavaScript engine for .NET
- **IronPython 3.4** - Python scripting support
- **Microsoft.CodeAnalysis.CSharp** - C# scripting (Roslyn)

#### Additional Libraries
- **AngleSharp** - HTML parsing
- **HtmlAgilityPack** - HTML manipulation
- **CaptchaSharp** - CAPTCHA solving integration
- **MailKit** - Email functionality
- **SSH.NET** - SSH/SFTP support
- **FluentFTP** - FTP client
- **SixLabors.ImageSharp** - Image processing
- **MaxMind.GeoIP2** - GeoIP lookups

## Build Configuration

### Requirements
- **.NET SDK 8.0.401** (specified in `global.json`)
- **Node.js 20.9.0+** (for frontend)

### Build Optimizations
The project includes aggressive build optimizations in `Directory.Build.props`:

- Parallel builds using all CPU cores
- Package lock files for reproducible builds
- Shared compilation enabled
- Reduced debug info in Release mode
- Size optimizations (trimming, compression)
- Satellite resource languages limited to English

### Build Commands

```bash
# Restore packages
dotnet restore Flux.sln

# Build solution
dotnet build Flux.sln -c Release

# Build web project
dotnet publish Flux.Web -c Release -o ./publish

# Frontend build (from flux-web-client/)
cd flux-web-client
npm install
npm run build
```

### Configurations
- **Debug** - Development with full debugging support
- **Release** - Optimized for production
- **testing** - Test-specific configuration

## Key Concepts

### LoliCode
LoliCode is the domain-specific language (DSL) used for writing automation configs. Key aspects:

- Transpiles to C# for execution
- Supports blocks (reusable automation steps)
- Variables using `[[variable]]` syntax
- Functions for common operations

### Blocks System (RuriLib.Blocks)
Modular automation components organized by category:
- **Requests** - HTTP/HTTPS requests
- **Parsing** - HTML/JSON/XML parsing
- **Selenium** - Browser automation via Selenium
- **Playwright** - Modern browser automation
- **Puppeteer** - Chrome DevTools Protocol automation
- **Android** - Mobile automation via Appium
- **Captchas** - CAPTCHA solving
- **Conditions** - Control flow (IF/ELSE)
- **Functions** - Reusable code blocks
- **Interop** - External interop (Python, JS, etc.)

### Job System
The application supports multiple job types:
- **MultiRunJob** - Execute configs against wordlists
- **ProxyCheckJob** - Validate proxy servers
- **ConfigDebugger** - Debug configs step-by-step

### Scripting Support
Multiple scripting languages are supported for extensibility:
- **C# Scripts** - Full Roslyn scripting support
- **JavaScript** - Via Jint engine
- **Python** - Via IronPython
- **Node.js** - Via Jering.Javascript.NodeJS

## Configuration

### appsettings.json
Key configuration sections:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source={DataDir}/flux.db"
  },
  "Jwt": {
    "Issuer": "Flux",
    "Audience": "Flux.Client",
    "AccessTokenExpiryMinutes": 120
  },
  "Frontend": {
    "Origins": ["http://localhost:5173"]
  }
}
```

### NuGet Configuration
- Local package cache in `./packages` directory
- Parallel downloads enabled (degree: 8)
- Signature validation disabled for faster installs

## Testing

### Test Projects
- **Flux.Shared.Tests** - Unit tests for shared components
- **Flux.Web.Api.Tests** - API integration tests
- **RuriLib.Http.Tests** - HTTP library tests
- **TlsClient.NET Tests** - TLS client tests

### Running Tests
```bash
dotnet test Flux.sln
```

## Docker Support

### Dockerfiles
- **Dockerfile** - Full production build (backend + frontend)
- **Dockerfile.build** - Build-only environment
- **Dockerfile.remote** - Remote/headless environment

### Docker Compose
```bash
docker-compose up -d
```

## Development Guidelines

### Code Style
- Follow existing code patterns in each project
- Use file-scoped namespaces (C# 10+)
- Prefer `var` when type is obvious
- Use nullable reference types
- Async/await pattern for I/O operations

### API Design
- RESTful API with versioning (v1.0 currently)
- Lowercase URLs enforced
- JWT authentication for protected endpoints
- API Key support for admin endpoints
- SignalR hubs for real-time updates

### Adding New Blocks
1. Create block descriptor in `RuriLib/Blocks/`
2. Inherit from appropriate base class
3. Implement `ExecuteAsync` method
4. Add to block factory if needed
5. Update TypeScript types if adding UI support

### Database Migrations
```bash
dotnet ef migrations add MigrationName --project Flux.Core --startup-project Flux.Web
dotnet ef database update --project Flux.Core --startup-project Flux.Web
```

## Important Notes

### Performance Considerations
- Heavy use of async/await throughout
- Parallelization engine for multi-threaded execution
- Persistent script caching for faster startup
- Direct LoliCode-to-C# transpilation (no intermediate stack for non-debug mode)
- Pool configuration for thread and connection limits

### Security Considerations
- BCrypt for password hashing
- JWT tokens with configurable expiry
- API key authentication for admin endpoints
- Input validation via FluentValidation
- GeoIP blocking/country detection support

### Known Limitations
- Cannot use `PublishSingleFile` due to CSharpScript API limitations
- SQLite only (no other database providers currently supported)
- Windows-focused (some features may not work cross-platform)

## File Locations

### Important Paths
- **UserData/** - User configs, wordlists, hits (excluded from git)
- **Changelog/** - Version history markdown files
- **packages/** - Local NuGet package cache
- **.agent/ .claude/ .roo/ .trae/** - AI assistant configuration directories

### Version Info
- Version stored in `version.txt`
- Changelog entries in `Changelog/{version}.md`

## Common Tasks

### Adding a New Controller
1. Create controller in `Flux.Web/Controllers/`
2. Inherit from `ApiController`
3. Add `[ApiVersion("1.0")]` attribute
4. Implement actions with proper HTTP verbs
5. Add XML docs for Swagger generation

### Adding a New Service
1. Define interface in `Flux.Web/Interfaces/`
2. Implement in `Flux.Web/Services/`
3. Register in `Program.cs` DI container
4. Inject via constructor where needed

### Adding Frontend Components
1. Component files in `flux-web-client/src/app/`
2. Services in `flux-web-client/src/app/services/`
3. Models in `flux-web-client/src/app/models/`
4. Update Angular module declarations as needed

## Resources

- **OpenAPI/Swagger**: Available at `/swagger` when running in Development mode
- **API Documentation**: Auto-generated from XML comments
- **Changelog**: See `Changelog/` directory for version history

---

*Last updated: 2026-02-07*
