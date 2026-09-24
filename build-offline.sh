#!/bin/sh
# Build and test with no NuGet access (the cloud build box). Needs the .NET 10 SDK.
# On a normal Windows machine just use:  dotnet build FD-Draft.sln  /  dotnet run --project tests/FdDraft.Tests
set -e
mkdir -p .offline-feed
ARGS="-p:FD_OFFLINE=1 -p:TargetFrameworks=net8.0 -p:GeneratePackageOnBuild=false --source $(pwd)/.offline-feed -v q"
dotnet build tools/FdDraft.Cli $ARGS
dotnet build tests/FdDraft.Tests $ARGS
dotnet tests/FdDraft.Tests/bin/Debug/net8.0/FdDraft.Tests.dll
