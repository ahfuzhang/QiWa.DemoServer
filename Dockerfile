FROM alpine:latest

RUN apk add --no-cache libc6-compat

WORKDIR /app
COPY --chmod=0755 build/Release/linux/amd64/QiWa.DemoServer /app/

# Disable config file watching to avoid inotify instance limit in containers
ENV DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false

ENTRYPOINT ["/app/QiWa.DemoServer"]
