FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "SkillForge.API.csproj"
RUN dotnet publish "SkillForge.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 5123
ENV ASPNETCORE_URLS=http://0.0.0.0:5123

ENTRYPOINT ["dotnet", "SkillForge.API.dll"]
