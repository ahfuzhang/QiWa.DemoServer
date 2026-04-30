
push:
	curl -X POST 'http://192.168.1.248:8428/api/v1/import/prometheus?extra_label=env=prod&extra_label=app=demo' \
  -H "Content-Type: text/plain" -v \
  --data-binary @- <<'EOF'
http_requests_total{method="GET",status="200"} 1027
http_requests_total{method="POST",status="200"} 342
response_time_seconds{handler="/api/v1/query"} 0.042
EOF
