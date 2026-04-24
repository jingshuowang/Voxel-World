# NU1100: Unable to resolve Silk.NET

### The Problem
The error `NU1100` means the .NET build system **cannot reach the internet** (NuGet.org) to download the Silk.NET libraries and the .NET 8.0 core files.

### Why it happens:
- Your machine is currently **offline**.
- Or your firewall is blocking `api.nuget.org`.

### How to Fix (Step-by-Step):

1. **Connect to Internet**: This is mandatory for the first build. The libraries are NOT currently on your hard drive.
2. **Run Restore**: Open a terminal in the folder and run:
   ```powershell
   dotnet restore --interactive
   ```
3. **Verify NuGet Source**: If you are online but it still fails, run:
   ```powershell
   dotnet nuget list source
   ```
   If you don't see `nuget.org`, add it:
   ```powershell
   dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org
   ```
4. **Build Again**: Once the restore succeeds, `build.bat` will work perfectly.

### Minimalist Workaround:
If you cannot get online, you would need to manually copy the `.dll` files from another machine into a `lib/` folder, but using NuGet (Online) is the standard and easiest way for C# projects.
