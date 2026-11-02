# 建置內容根目錄為 repo 根目錄（見 docker-compose.yml 的 build.context）。
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json QuotePlatform.slnx ./
COPY apps/api/QuotePlatform.Api/QuotePlatform.Api.csproj apps/api/QuotePlatform.Api/
RUN dotnet restore apps/api/QuotePlatform.Api/QuotePlatform.Api.csproj
COPY apps/api/QuotePlatform.Api/ apps/api/QuotePlatform.Api/
RUN dotnet publish apps/api/QuotePlatform.Api/QuotePlatform.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# 報價 PDF 的中文字型（Noto Sans CJK TC）；QuestPDF 從系統字型載入，不把字型檔放進 repo。
RUN apt-get update \
    && apt-get install -y --no-install-recommends fonts-noto-cjk fontconfig \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "QuotePlatform.Api.dll"]
