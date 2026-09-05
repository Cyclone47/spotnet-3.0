# Spotnet 3.0 Build & Setup Instructions

This document provides complete instructions for building and testing Spotnet 3.0 from source.

---

## 1. Prerequisites & Environment

- **Operating System:** Windows 10 / 11 x64
- **SDK / Build Tools:**
  - .NET 10 SDK
  - Visual Studio / Build Tools compatible with .NET 10, with:
    - .NET desktop development workload
    - .NET Framework 4.7.2 targeting pack
- **Platform Architecture:** `x64` (the application, tests, SQLite interop, WebView2 loader, and LibVLC runtime are all 64-bit)
- **Runtime:** Microsoft Edge WebView2 Evergreen Runtime

---

## 2. Solution Structure

```
src/Spotnet/
├── Spotnet.sln                 # Visual Studio Solution file
├── lib/                        # Managed & Native third-party runtime dependencies
├── Spotnet.Enc/                # Managed yEnc Decoder Library
│   ├── Spotnet.Enc.csproj
│   └── SpotnetDecoder.cs
├── Spotnet/                    # Main WPF Application Project
│   ├── Spotnet.csproj
│   ├── App.xaml / App.xaml.cs
│   ├── Views/                  # Top-level WPF Windows
│   ├── Controls/               # Custom UI Controls & Views
│   ├── ViewModel/              # MVVM Light ViewModels
│   ├── Model/                  # Domain entities & Spot XML parser
│   ├── DAL/                    # SQLite database access layer & migrations
│   ├── Phuse/                  # NNTP protocol client & connection pool
│   ├── Downloader/             # Multi-part binary downloader & queue
│   ├── Browser/                # Chromium / WebBrowser spot rendering
│   ├── Data/                   # Filters, themes, categories definition files
│   └── Resources/              # Icons, fonts, localization, badwords
└── Spotnet.Tests/              # Automated Unit & Integration Tests (xUnit)
    ├── Spotnet.Tests.csproj
    ├── YEncDecoderTests.cs
    ├── SpotParserTests.cs
    ├── CategoryTaxonomyTests.cs
    └── SpotDbTests.cs
```

---

## 3. Build Commands

To restore and compile the complete solution via .NET CLI:

```powershell
# Build entire solution in Release configuration:
dotnet build d:\sourcecode\src\Spotnet\Spotnet.sln -c Release

# Or build in Debug configuration:
dotnet build d:\sourcecode\src\Spotnet\Spotnet.sln -c Debug
```

---

## 4. Running Automated Tests

To execute the automated test suite verifying yEnc decoding, Spot XML parsing, Category taxonomy resources, and SQLite database operations:

```powershell
dotnet test d:\sourcecode\src\Spotnet\Spotnet.sln
```

All 69 tests should pass. They cover yEnc decoding, Spot XML parsing, category resources,
SQLite durability and rebuild behavior, query generation and parameterization, RSA verifier
caching, WebView2 runtime probing, x64 assembly targeting, and traversal-safe ZIP extraction.

---

## 5. Output Binaries & Running the Application

The build process automatically deploys the x64 native dependencies (`SQLite.Interop.dll`,
`libvlc.dll`, `WebView2Loader.dll`) plus the child-process utilities (`UnRAR.exe`,
`phpar2.exe`, `7za.exe`), `Data/Filters.v2`, `Data/TabThemes`, and resources to the output folder:

- **Release Executable:** `d:\sourcecode\src\Spotnet\Spotnet\bin\Release\net472\Spotnet.exe`
- **Debug Executable:** `d:\sourcecode\src\Spotnet\Spotnet\bin\Debug\net472\Spotnet.exe`

To launch:
```powershell
& "d:\sourcecode\src\Spotnet\Spotnet\bin\Release\net472\Spotnet.exe"
```
