FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["UserLoginService/UserLoginService.csproj", "UserLoginService/"]
RUN dotnet restore "UserLoginService/UserLoginService.csproj"
COPY . .
WORKDIR "/src/UserLoginService"
RUN dotnet build "UserLoginService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "UserLoginService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UserLoginService.dll"]
