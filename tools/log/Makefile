
test:
	docker run --rm -it \
		--network="host" \
		--cpuset-cpus="3" \
		-m 512m \
		-v "./vector.toml:/etc/vector/vector.toml:ro" \
		timberio/vector:latest-alpine \
		-c /etc/vector/vector.toml

write:
	head -n 1000 ../../log.txt | docker run --rm -i \
		--network="host" \
		--cpuset-cpus="3" \
		-m 512m \
		-v "./vector.toml:/etc/vector/vector.toml:ro" \
		-e VECTOR_LOG=debug \
		timberio/vector:latest-alpine \
		-c /etc/vector/vector.toml

test2:
	echo '{"Timestamp":"2024-05-28T10:00:00Z","Level":"INFO","Message":"This is a test log message."}' | docker run --rm -i \
		--network="host" \
		--cpuset-cpus="3" \
		-m 512m \
		-v "./vector.toml:/etc/vector/vector.toml:ro" \
		-e VECTOR_LOG=debug \
		timberio/vector:latest-alpine \
		-c /etc/vector/vector.toml

vlogs:
	# todo: modify time
	curl -X POST \
  'http://192.168.1.248:9428/insert/jsonline?_time_field=Timestamp&_msg_field=Properties.Message&_stream_fields=Level&ignore_fields=&decolorize_fields=&AccountID=0&ProjectID=0&debug=false&extra_fields=' \
  -H 'Content-Type: application/stream+json' \
  --data-binary $'{"Timestamp":"2026-04-24T14:30:00+08:00","Level":"Information","Properties":{"Message":"hello VictoriaLogs from curl"},"TraceId":"abc-001","UserId":123}\n{"Timestamp":"2026-04-24T14:30:01+08:00","Level":"Error","Properties":{"Message":"something failed"},"TraceId":"abc-002","Exception":"demo exception"}\n'

