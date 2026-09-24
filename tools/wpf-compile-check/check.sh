#!/bin/sh
# Compile-checks the WPF app on a machine that cannot build WPF (Linux, no NuGet):
# builds a stand-in reference assembly from dotnet/wpf's public ref sources, then
# compiles src/FdDraft.App against it. Catches every C# and API error; it does not
# run the app. On Windows just build FD-Draft.sln instead.
set -e
HERE=$(cd "$(dirname "$0")" && pwd); ROOT=$(cd "$HERE/../.." && pwd); WORK=${WORK:-/tmp/fdd-wpfcheck}
mkdir -p "$WORK/feed"
if [ ! -d "$WORK/wpf" ]; then
  git clone -q --depth 1 --filter=blob:none --sparse https://github.com/dotnet/wpf.git "$WORK/wpf"
  (cd "$WORK/wpf" && git sparse-checkout set --no-cone '**/ref/*.cs')
fi
W="$WORK/wpf/src/Microsoft.DotNet.Wpf/src"
mkdir -p "$WORK/ref" "$WORK/app"
cat > "$WORK/ref/ref.csproj" <<P
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>WpfRef</AssemblyName><Nullable>disable</Nullable>
<NoWarn>\$(NoWarn);CS0618;CS0067;CS0108;CS0114;CS0809;CS3021;CS1591;CS0672;SYSLIB0003;SYSLIB0050;SYSLIB0051;WPF0001</NoWarn><AllowUnsafeBlocks>true</AllowUnsafeBlocks>
<EnableDefaultCompileItems>false</EnableDefaultCompileItems><GenerateAssemblyInfo>false</GenerateAssemblyInfo></PropertyGroup><ItemGroup>
<Compile Include="$W/WindowsBase/ref/WindowsBase.cs" /><Compile Include="$W/System.Xaml/ref/System.Xaml.cs" />
<Compile Include="$W/UIAutomation/UIAutomationTypes/ref/UIAutomationTypes.cs" /><Compile Include="$W/UIAutomation/UIAutomationProvider/ref/UIAutomationProvider.cs" />
<Compile Include="$W/PresentationCore/ref/PresentationCore.cs" /><Compile Include="$W/ReachFramework/ref/ReachFramework.cs" />
<Compile Include="$W/System.Printing/ref/System.Printing.cs" /><Compile Include="$W/PresentationFramework/ref/PresentationFramework.cs" />
<Compile Include="$W/System.Windows.Input.Manipulations/ref/System.Windows.Input.Manipulations.cs" /><Compile Include="$HERE/Stubs.cs" />
</ItemGroup></Project>
P
dotnet build "$WORK/ref/ref.csproj" -v q --source "$WORK/feed"
B="$ROOT/tests/FdDraft.Tests/bin/Debug/net8.0"
cat > "$WORK/app/app.csproj" <<P
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
<ImplicitUsings>disable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors><NoWarn>\$(NoWarn);WPF0001</NoWarn></PropertyGroup><ItemGroup>
<Compile Include="$ROOT/src/FdDraft.App/*.cs" /><Reference Include="WpfRef"><HintPath>$WORK/ref/bin/Debug/net10.0/WpfRef.dll</HintPath></Reference>
<Reference Include="FdDraft.Core"><HintPath>$B/FdDraft.Core.dll</HintPath></Reference><Reference Include="FdDraft.Cad"><HintPath>$B/FdDraft.Cad.dll</HintPath></Reference>
<Reference Include="FdDraft.View"><HintPath>$B/FdDraft.View.dll</HintPath></Reference><Reference Include="ACadSharp"><HintPath>$B/ACadSharp.dll</HintPath></Reference>
</ItemGroup></Project>
P
dotnet build "$WORK/app/app.csproj" -v q --source "$WORK/feed"
