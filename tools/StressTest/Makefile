PRJ=StressTest
BUILD_DIR=../../build/StressTest/

build:
	dotnet build $(PRJ).csproj

run:
	dotnet run --project $(PRJ).csproj -- \
	  -http1.addr=http://127.0.0.1:8091/api/v1/login \
	  -thread.count=8 \
	  -connection.per.thread=30 \
	  -task.per.connection=1 \
	  -compress="" \
	  -encode.type=json \
	  -stress.test.seconds=30

run_http1_pb:
	dotnet run --project $(PRJ).csproj -- \
	  -http1.addr=http://127.0.0.1:8091/Demo/Login \
	  -thread.count=8 \
	  -connection.per.thread=50 \
	  -task.per.connection=1 \
	  -compress="" \
	  -encode.type=protobuf \
	  -stress.test.seconds=30

run_h2:
	dotnet run --project $(PRJ).csproj -- \
	  -http2.addr=http://127.0.0.1:8092/api/v1/login \
	  -thread.count=8 \
	  -connection.per.thread=2 \
	  -task.per.connection=10 \
	  -compress="" \
	  -encode.type=json \
	  -stress.test.seconds=30

run_h2_pb:
	dotnet run --project $(PRJ).csproj -- \
	  -http2.addr=http://127.0.0.1:8092/Demo/Login \
	  -thread.count=8 \
	  -connection.per.thread=2 \
	  -task.per.connection=20 \
	  -compress="" \
	  -encode.type=json \
	  -stress.test.seconds=30

run_gzip:
	dotnet run --project $(PRJ).csproj -- \
	  -http1.addr=http://127.0.0.1:8091/Demo/Login \
	  -thread.count=1 \
	  -connection.per.thread=3 \
	  -compress=gzip \
	  -stress.test.seconds=10

run_zstd:
	dotnet run --project $(PRJ).csproj -- \
	  -http1.addr=http://127.0.0.1:8091/Demo/Login \
	  -thread.count=1 \
	  -connection.per.thread=3 \
	  -compress=zstd \
	  -stress.test.seconds=10

run_protobuf:
	dotnet run --project $(PRJ).csproj -- \
	  -http1.addr=http://127.0.0.1:8091/Demo/Login \
	  -thread.count=1 \
	  -connection.per.thread=3 \
	  -encode.type=protobuf \
	  -stress.test.seconds=10

build_linux:
	dotnet publish $(PRJ).csproj \
	  -r linux-x64 \
	  -c Release \
	  --self-contained true \
	  -o $(BUILD_DIR)
