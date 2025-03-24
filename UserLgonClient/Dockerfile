FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["UserLoginClient/UserLoginClient.csproj", "UserLoginClient/"]
COPY ["UserLoginService/Protos/", "UserLoginService/Protos/"]
RUN dotnet restore "UserLoginClient/UserLoginClient.csproj"
COPY . .
WORKDIR "/src/UserLoginClient"
RUN dotnet build "UserLoginClient.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "UserLoginClient.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UserLoginClient.dll"]
