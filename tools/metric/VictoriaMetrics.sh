mkdir -p /home/ahfu/VictoriaMetricsData/

# VictoriaMetrics single-node
docker run -d --rm --name victoriametrics \
  --network="host" \
  --cpuset-cpus="0,1,2,3" \
  -m 2048m \
  -v /home/ahfu/VictoriaMetricsData/:/storage/ \
  -e GOMAXPROCS=4 \
  victoriametrics/victoria-metrics:v1.115.0 \
  -storageDataPath=/storage/ \
  -inmemoryDataFlushInterval=30s \
  -memory.allowedPercent=80 \
  -retentionPeriod=2d \
  -httpListenAddr=:8428

# macos
docker run -d --rm --name victoriametrics \
  -p 8428:8428 \
  --cpuset-cpus="0" \
  -m 128m \
  -v /Users/ahfu/Downloads/temp/2026/VictoriaMetricsData/:/storage/ \
  -e GOMAXPROCS=1 \
  victoriametrics/victoria-metrics:v1.115.0 \
  -storageDataPath=/storage/ \
  -inmemoryDataFlushInterval=30s \
  -memory.allowedPercent=80 \
  -retentionPeriod=2d \
  -httpListenAddr=:8428


curl -X POST 'http://127.0.0.1:8428/api/v1/import/prometheus?extra_label=env=prod&extra_label=app=demo' \
  -H "Content-Type: text/plain" -v \
  --data-binary @- <<'EOF'
http_requests_total{method="GET",status="200"} 1027
http_requests_total{method="POST",status="200"} 342
response_time_seconds{handler="/api/v1/query"} 0.042
EOF

