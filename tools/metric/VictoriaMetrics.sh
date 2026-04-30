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

