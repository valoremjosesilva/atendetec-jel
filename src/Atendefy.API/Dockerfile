FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Atendefy.API/Atendefy.API.csproj", "src/Atendefy.API/"]
RUN dotnet restore "src/Atendefy.API/Atendefy.API.csproj"
COPY . .
WORKDIR "/src/src/Atendefy.API"
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Atendefy.API.dll"]
