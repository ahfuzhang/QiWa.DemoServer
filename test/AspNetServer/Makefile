.PHONY: build clean run

build:
	dotnet build -c Release -o bin/

clean:
	dotnet clean

run:
	dotnet run -- -http1.port=8071 -http2.port=8072 -cores=1

PRJ=AspNetServer
BUILD_DIR=./build/

# build_linux:
# 	dotnet restore $(PRJ).csproj -r linux-x64
# 	dotnet clean $(PRJ).csproj -r linux-x64 -c Release
# 	dotnet publish $(PRJ).csproj \
# 	  -r linux-x64 \
# 	  -p:DefineConstants=UNIX -p:AllowUnsafeBlocks=true \
# 	  -p:PublishAot=true \
# 	  -p:StripSymbols=false \
# 	  -p:StaticLinkedRuntime=true \
# 	  -p:StaticExecutable=true \
# 	  -p:PositionIndependentExecutable=false \
# 	  -p:InvariantGlobalization=true \
# 	  -p:OptimizationPreference=Size \
# 	  -p:CppCompilerAndLinker=clang \
# 	  --self-contained true \
# 	  -c Release -o $(BUILD_DIR)

# build_dll:
# 	dotnet build $(PRJ).csproj \
# 		-c Release -r linux-x64 \
# 		-p:PublishAot=true \
# 		-p:PublishSingleFile=true \
# 		-p:IncludeNativeLibrariesForSelfExtract=true \
# 		-p:EnableCompressionInSingleFile=true \
# 		-p:GenerateDocumentationFile=false \
# 		-p:StripSymbols=false \
# 		-p:DebugType=portable \
# 		-p:DebugSymbols=true \
# 		-p:EmbedUntrackedSources=true \
# 		-p:EmbedAllSources=true \
# 		-p:ContinuousIntegrationBuild=true \
# 		-p:Optimize=true

build_no_aot:
	dotnet build $(PRJ).csproj \
		-c Release -r linux-x64 \
		-p:PublishAot=false \
		-p:PublishSingleFile=true \
		-p:IncludeNativeLibrariesForSelfExtract=true \
		-p:EnableCompressionInSingleFile=true \
		-p:GenerateDocumentationFile=false \
		-p:StripSymbols=false \
		-p:DebugType=portable \
		-p:DebugSymbols=true \
		-p:EmbedUntrackedSources=true \
		-p:EmbedAllSources=true \
		-p:ContinuousIntegrationBuild=true \
		-p:Optimize=true

# docker_build:
# 	docker build --platform linux/amd64 -t ahfuzhang/asp.net.server .

docker_build_aspnet:
	docker build --platform linux/amd64 -f Dockerfile_aspnet -t ahfuzhang/asp.net.server:aspnet6 .

# run-in-docker-linux-amd64-with-log:
# 	docker run --rm \
# 	    --platform=linux/amd64 \
# 		--cpuset-cpus="19" \
# 		-m 128m \
# 		--network host \
# 		ahfuzhang/asp.net.server:latest \
# 			-http1.port=8091 \
# 			-http2.port=8092 \
# 			-cores=1 2>&1 | \
# 	docker run --rm -i \
# 		--network="host" \
# 		--cpuset-cpus="18" \
# 		-m 512m \
# 		-v "./tools/log/vector.toml:/etc/vector/vector.toml:ro" \
# 		timberio/vector:latest-alpine \
# 		-c /etc/vector/vector.toml

# run-in-docker-linux-amd64:
# 	docker run -it --rm \
# 	    --platform=linux/amd64 \
# 		--cpuset-cpus="19" \
# 		-m 128m \
# 		--network host \
# 		ahfuzhang/asp.net.server:latest \
# 			-http1.port=8091 \
# 			-http2.port=8092 \
# 			-cores=1 2>&1

run-in-docker_aspnet6:
	docker run -it --rm \
	    --platform=linux/amd64 \
		--cpuset-cpus="19" \
		-m 128m \
		--network host \
		ahfuzhang/asp.net.server:aspnet6 \
			-http1.port=8091 \
			-http2.port=8092 \
			-cores=1 2>&1

run-in-docker_aspnet6-with-log:
	docker run --rm \
	    --platform=linux/amd64 \
		--cpuset-cpus="19" \
		-m 128m \
		--network host \
		ahfuzhang/asp.net.server:aspnet6 \
			-http1.port=8091 \
			-http2.port=8092 \
			-cores=1 2>&1 | \
	docker run --rm -i \
		--network="host" \
		--cpuset-cpus="18" \
		-m 512m \
		-v "./build/vector.toml:/etc/vector/vector.toml:ro" \
		timberio/vector:latest-alpine \
		-c /etc/vector/vector.toml
