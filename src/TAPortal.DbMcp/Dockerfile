FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/TAPortal.DbMcp/TAPortal.DbMcp.csproj", "src/TAPortal.DbMcp/"]
RUN dotnet restore "src/TAPortal.DbMcp/TAPortal.DbMcp.csproj"
COPY . .
RUN dotnet publish "src/TAPortal.DbMcp/TAPortal.DbMcp.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TAPortal.DbMcp.dll"]
