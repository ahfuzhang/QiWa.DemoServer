.PHONY: build clean run

build:
	dotnet build -c Release -o bin/

clean:
	dotnet clean

run:
	dotnet run -- -http1.port=8071 -http2.port=8072 -cores=1
