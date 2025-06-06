# build the frontend
FROM node:18-bullseye AS frontend-build

WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ .
RUN npm run build

# build the .NET backend (and integrate the frontend)
FROM mcr.microsoft.com/dotnet/sdk:9.0-jammy AS backend-build

WORKDIR /src
# Copy solution and project files
COPY SearchEngine.sln ./
COPY SearchEngine.Core/SearchEngine.Core.csproj SearchEngine.Core/
COPY SearchEngine.Services/SearchEngine.Services.csproj SearchEngine.Services/
COPY SearchEngine.Persistence/SearchEngine.Persistence.csproj SearchEngine.Persistence/
COPY SearchEngine.Analysis/SearchEngine.Analysis.csproj SearchEngine.Analysis/
COPY SearchEngine/SearchEngine.csproj SearchEngine/

# restore all projects
RUN dotnet restore

# copy source code
COPY SearchEngine.Core/ SearchEngine.Core/
COPY SearchEngine.Services/ SearchEngine.Services/
COPY SearchEngine.Persistence/ SearchEngine.Persistence/
COPY SearchEngine.Analysis/ SearchEngine.Analysis/
COPY SearchEngine/ SearchEngine/

# copy frontend build output into wwwroot
COPY --from=frontend-build /src/frontend/dist/ SearchEngine/wwwroot/

# publish the backend in Release mode
WORKDIR /src/SearchEngine
RUN dotnet publish -c Release -o /app/publish

# runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0-jammy AS runtime
WORKDIR /app
COPY --from=backend-build /app/publish .

# expose default port (matches the app’s launch URL)
EXPOSE 5268

# use an entrypoint that allows passing a filename (if provided) to the app
ENTRYPOINT ["dotnet", "SearchEngine.dll"]

