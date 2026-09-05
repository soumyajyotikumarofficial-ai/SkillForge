FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY skillforge_phase1/backend/ .
RUN dotnet restore "SkillForge.API.csproj"
RUN dotnet publish "SkillForge.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 10000
ENV ASPNETCORE_URLS=http://0.0.0.0:10000

ENTRYPOINT ["dotnet", "SkillForge.API.dll"]