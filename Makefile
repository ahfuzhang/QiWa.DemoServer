
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
	  -http1.port=8081 \
	  -http2.port=8082 \
	  -cores=1

show_metrics:
	curl --compressed -G "http://127.0.0.1:8081/metrics" -v

send_login:
	curl --compressed -X POST \
	  -H "Content-Type: application/json" \
	  -d '{"user_name":"ahfu"}' \
	  "http://127.0.0.1:8081/Demo/Login" -v

send_login_h2c:
	curl --compressed -X POST --http2-prior-knowledge \
	  -H "Content-Type: application/json" \
	  -d '{"user_name":"ahfu"}' \
	  "http://127.0.0.1:8082/Demo/Login" -v
