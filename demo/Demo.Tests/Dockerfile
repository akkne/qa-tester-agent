FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src
COPY . .
ENTRYPOINT ["dotnet", "test", "demo/Demo.Tests/Demo.Tests.csproj", "-c", "Release"]
