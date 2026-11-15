FROM node:24-alpine AS frontend
WORKDIR /frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY backend/AccessPanel.Api.csproj .
RUN dotnet restore
COPY backend/ .
COPY --from=frontend /frontend/dist/browser ./wwwroot
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /out .
COPY run.sh /run.sh
RUN chmod +x /run.sh
EXPOSE 8099
CMD ["/run.sh"]
