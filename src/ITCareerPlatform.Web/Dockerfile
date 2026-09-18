# ----- Build (.NET 10) -----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ITCareerPlatform.Web.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app

# ----- Runtime -----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
# P0-2: chỉ để dòng log của container đọc được theo giờ Việt Nam. Tính đúng đắn của dữ liệu
# KHÔNG phụ thuộc biến này: mọi mốc thời gian lưu ở UTC và chỉ quy đổi khi hiển thị
# (Ui.ToVietnamTime), nên gỡ dòng này ra thì log khó đọc hơn chứ nghiệp vụ vẫn đúng.
ENV TZ=Asia/Ho_Chi_Minh
ENTRYPOINT ["dotnet", "ITCareerPlatform.Web.dll"]
