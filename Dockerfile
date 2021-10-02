FROM mcr.microsoft.com/dotnet/sdk:5.0-alpine AS build
WORKDIR /src
COPY ./ /src
RUN dotnet restore -r linux-musl-x64 && dotnet publish -c release -r linux-musl-x64 --no-restore

FROM alpine:latest
RUN apk upgrade --no-cache --available && apk add --no-cache openssl libstdc++
COPY --from=build /src/bin/release/net5.0/linux-musl-x64/publish /src
COPY ./res /src/res

ENTRYPOINT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 /src/CurseWatcher