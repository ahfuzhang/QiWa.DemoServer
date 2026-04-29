
QiWa.rpc:
	hulu tu \
	  -src=./proto/Demo.proto \
	  -csharp_out=./generated/Demo/ \
	  -csharp_out.with.test \
	  -src.csharp_template.dir=./templates/QiWa.rpc/ \
	  -dst.csharp_template.out_dir=./generated/Demo/

build:
	dotnet build

run:
	dotnet run	-- -log.level=info \
	  -log.flush.interval.ms=1000 \
	  -log.buffer.size=4kb \
	  -log.global.tags=server=DemoServer \
	  -http1.port=8091 \
	  -http2.port=8092 \
	  -cores=1

show_metrics:
	curl --compressed -G "http://127.0.0.1:8091/metrics" -v

send_login:
	curl --compressed -X POST \
	  -H "Content-Type: application/json" \
	  -d '{"user_name":"ahfu"}' \
	  "http://127.0.0.1:8091/Demo/Login" -v

send_login_h2c:
	curl --compressed -X POST --http2-prior-knowledge \
	  -H "Content-Type: application/json" \
	  -d '{"user_name":"ahfu"}' \
	  "http://127.0.0.1:8092/Demo/Login" -v

PRJ=QiWa.DemoServer
BUILD_DIR=./build/Release/linux/amd64/

# -p:PublishAot=true
# -p:PublishTrimmed=false
build_linux:
	dotnet restore $(PRJ).csproj -r linux-x64
	dotnet clean $(PRJ).csproj -r linux-x64 -c Release
	dotnet publish $(PRJ).csproj \
	  -r linux-x64 \
	  -p:DefineConstants=UNIX -p:AllowUnsafeBlocks=true \
	  -p:PublishAot=true \
	  -p:StripSymbols=false \
	  -p:StaticLinkedRuntime=true \
	  -p:StaticExecutable=true \
	  -p:PositionIndependentExecutable=false \
	  -p:InvariantGlobalization=true \
	  -p:OptimizationPreference=Size \
	  -p:CppCompilerAndLinker=clang \
	  --self-contained true \
	  -c Release -o $(BUILD_DIR)
	  rm -rf $(BUILD_DIR)/generated $(BUILD_DIR)/build

docker_build:
	docker build --platform linux/amd64 -t ahfuzhang/qiwa.demoserver .

run-in-docker-linux-amd64:
	docker run -it --rm \
	    --platform=linux/amd64 \
		-p 8091:8091 \
		-p 8092:8092 \
		--cpuset-cpus="19" \
		-m 128m \
		--network host \
		ahfuzhang/qiwa.demoserver:latest \
			-log.level=info \
			-log.flush.interval.ms=1000 \
			-log.buffer.size=4kb \
			-log.global.tags="server=DemoServer&pod=demo-server-123456&namespace=asia" \
			-http1.port=8091 \
			-http2.port=8092 \
			-cores=1

run-in-docker-linux-amd64-with-log:
	docker run --rm \
	    --platform=linux/amd64 \
		--cpuset-cpus="19" \
		-m 128m \
		--network host \
		ahfuzhang/qiwa.demoserver:latest \
			-log.level=info \
			-log.flush.interval.ms=1000 \
			-log.buffer.size=4kb \
			-log.global.tags="server=DemoServer&pod=demo-server-123456&namespace=asia" \
			-http1.port=8091 \
			-http2.port=8092 \
			-cores=1 2>&1 | \
	docker run --rm -i \
		--network="host" \
		--cpuset-cpus="18" \
		-m 512m \
		-v "./tools/log/vector.toml:/etc/vector/vector.toml:ro" \
		timberio/vector:latest-alpine \
		-c /etc/vector/vector.toml

run-in-docker-linux-amd64-with-log-push:
	docker run --rm \
	    --platform=linux/amd64 \
		--cpuset-cpus="19" \
		-m 128m \
		--network host \
		ahfuzhang/qiwa.demoserver:latest \
			-log.level=info \
			-log.flush.interval.ms=2000 \
			-log.buffer.size=64kb \
			-log.global.tags="server=DemoServer&pod=demo-server-123456&namespace=asia" \
			-log.push.addr="http://192.168.1.248:9428/insert/jsonline?_time_field=Timestamp&_msg_field=Properties.Message&_stream_fields=Level&ignore_fields=&decolorize_fields=&AccountID=0&ProjectID=0&debug=false&extra_fields=" \
			-http1.port=8091 \
			-http2.port=8092 \
			-cores=1 2>&1
